using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI.Selection;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.Revit.Services
{
    /// <summary>
    /// 从 Revit 模型读取「空间(Space)」并转换为 Core 只读快照(需求 2.2.1 空间管理 / 2.2.3.1 公共区几何)。
    /// <list type="bullet">
    ///   <item>全部为<strong>只读</strong>操作:不建事务、不改模型,只读操作不需要 Transaction;</item>
    ///   <item>单位换算集中在本类:Revit 内部单位为英尺,Core/UI 一律 m / m² / m³
    ///         (2020 只有 <c>UnitUtils.ConvertFromInternalUnits(double, DisplayUnitType)</c> 重载);</item>
    ///   <item>同时读取<strong>已加载的链接模型</strong>一层:地铁工程里建筑空间通常挂在链接的建筑模型上,
    ///         只扫当前文档会读不到空间(嵌套链接不再递归,已足够覆盖常规出图结构)。</item>
    /// </list>
    /// </summary>
    internal static class RevitSpaceReader
    {
        /// <summary>
        /// 读取当前文档 + 已加载链接中的全部空间(合法空间 = 已放置,即面积 &gt; 0 的那些也会保留并标注)。
        /// </summary>
        public static IList<SpaceSnapshot> ReadAll(Document doc, out string note)
        {
            var result = new List<SpaceSnapshot>();
            if (doc == null)
            {
                note = "没有活动文档。";
                return result;
            }

            int hostCount = 0;
            int linkCount = 0;
            int linkDocCount = 0;

            foreach (var space in CollectSpaces(doc))
            {
                var snapshot = ToSnapshot(doc, space, Transform.Identity, false);
                if (snapshot == null) continue;
                result.Add(snapshot);
                hostCount++;
            }

            foreach (var link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                Document linkDoc;
                Transform transform;
                try
                {
                    linkDoc = link.GetLinkDocument();
                    transform = link.GetTotalTransform();
                }
                catch
                {
                    continue;   // 链接未加载/已卸载
                }

                if (linkDoc == null) continue;
                linkDocCount++;

                foreach (var space in CollectSpaces(linkDoc))
                {
                    var snapshot = ToSnapshot(linkDoc, space, transform, true);
                    if (snapshot == null) continue;
                    result.Add(snapshot);
                    linkCount++;
                }
            }

            note = "模型空间:当前项目 " + hostCount + " 个" +
                   (linkDocCount > 0
                       ? " + 链接模型 " + linkCount + " 个(共 " + linkDocCount + " 个已加载链接)"
                       : "") +
                   "。名称/编号/标高含「站厅」「站台」者参与自动识别。";

            return result;
        }

        /// <summary>
        /// 手动拾取空间(可多选,回车结束,Esc 取消)。
        /// <para>
        /// <strong>必须在没有模态窗口的 IExternalCommand 上下文里调用</strong>(见 PublicAreaWindow 注释);
        /// 只能拾取当前项目里的空间,链接模型的空间请用【自动识别全模型空间】。
        /// </para>
        /// </summary>
        /// <returns>拾取到的空间;用户 Esc 取消时返回 null。</returns>
        /// <remarks>
        /// 形参类型必须写成 <c>Autodesk.Revit.UI.UIDocument</c>:RevitAPIUI.dll 里还有一个
        /// <strong>全局命名空间下的 internal UIDocument</strong>,只写 <c>UIDocument</c> 会被它抢先命中,
        /// 报 CS0122「不可访问,因为它具有一定的保护级别」。
        /// </remarks>
        public static IList<SpaceSnapshot> PickMany(Autodesk.Revit.UI.UIDocument uidoc, PublicAreaTarget target, out string note)
        {
            string label = target == PublicAreaTarget.Hall ? "站厅" : "站台";
            return PickSpaces(uidoc, "请选择【" + label + "公共区】空间(可多选,回车结束,Esc 取消)", label, out note);
        }

        /// <summary>
        /// 手动拾取空间(通用,不限站厅/站台) —— 小系统"由用户依次选取模型空间"建房间列表用(需求 2.2.3.2)。
        /// <para>必须在没有模态窗口的 IExternalCommand 上下文里调用(见 PublicAreaWindow 注释)。</para>
        /// </summary>
        /// <returns>拾取到的空间快照;Esc 取消返回 null。</returns>
        public static IList<SpaceSnapshot> PickSpaces(Autodesk.Revit.UI.UIDocument uidoc, string prompt,
            string label, out string note)
        {
            note = "";
            if (uidoc == null)
            {
                note = "没有活动文档,无法拾取。";
                return null;
            }

            IList<Reference> references;
            try
            {
                references = uidoc.Selection.PickObjects(ObjectType.Element, new SpaceSelectionFilter(), prompt);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                note = "已取消";
                return null;
            }

            var result = new List<SpaceSnapshot>();
            var seen = new HashSet<int>();
            int skipped = 0;

            foreach (var reference in references)
            {
                var space = uidoc.Document.GetElement(reference) as Space;
                if (space == null) continue;
                if (!seen.Add(space.Id.IntegerValue)) continue;

                var snapshot = ToSnapshot(uidoc.Document, space, Transform.Identity, false);
                if (snapshot == null)
                {
                    skipped++;
                    continue;
                }

                result.Add(snapshot);
            }

            note = "拾取 " + result.Count + " 个" + label + "空间";
            if (skipped > 0) note += "(其中 " + skipped + " 个未放置已跳过)";
            return result;
        }

        // ------------------------------------------------------------------ 内部

        /// <summary>
        /// 收集文档中的全部空间(Space)。
        /// <para>
        /// <strong>不能用 <c>OfClass(typeof(Space))</c></strong>:<c>Mechanical.Space</c> 是"只存在于 API、
        /// 不在 Revit 原生对象模型里"的类型,那样写会在运行时抛 ArgumentException
        /// （"Input type(Autodesk.Revit.DB.Mechanical.Space) is of an element type that exists in the API,
        /// but not in Revit's native object model"）。
        /// Revit 自己的提示就是改用 <see cref="SpatialElement"/> 再后处理 —— 原生对象模型里空间/房间/面积
        /// 都归 <c>SpatialElement</c>(<c>Space.BaseType</c> 即 <c>SpatialElement</c>),故按它收集,再用
        /// <c>OfType&lt;Space&gt;</c> 过滤掉 Room/Area。
        /// </para>
        /// </summary>
        private static IEnumerable<Space> CollectSpaces(Document doc)
        {
            if (doc == null) return new List<Space>();

            return new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .OfType<Space>();
        }

        /// <summary>
        /// Space → 快照(英尺 → m)。
        /// 层高优先取「体积/面积」的有效平均高度(不受"空间上限标高设到上一层"影响),
        /// 取不到时回退 <see cref="Space.UnboundedHeight"/>。
        /// </summary>
        private static SpaceSnapshot ToSnapshot(Document doc, Space space, Transform transform, bool fromLink)
        {
            if (space == null) return null;

            try
            {
                double areaFt2 = space.Area;
                double volumeFt3 = space.Volume;

                var snapshot = new SpaceSnapshot
                {
                    ElementId = space.Id.IntegerValue,
                    Number = space.Number ?? "",
                    Name = space.Name ?? "",
                    LevelName = ResolveLevelName(doc, space),
                    FromLink = fromLink
                };

                snapshot.AreaM2 = ToSquareMeters(areaFt2);
                snapshot.VolumeM3 = ToCubicMeters(volumeFt3);

                if (areaFt2 > 1e-9 && volumeFt3 > 1e-9)
                {
                    snapshot.HeightM = ToMeters(volumeFt3 / areaFt2);
                }
                else if (space.UnboundedHeight > 1e-9)
                {
                    snapshot.HeightM = ToMeters(space.UnboundedHeight);
                }

                BoundingBoxXYZ box = space.get_BoundingBox(null);
                if (box != null)
                {
                    ApplyExtent(snapshot, box, transform);
                }

                // 未放置空间面积/体积均为 0:保留在列表里(界面会跳过并计数)
                return snapshot;
            }
            catch
            {
                return null;   // 单个空间取参数失败不该让整窗打不开
            }
        }

        private static void ApplyExtent(SpaceSnapshot snapshot, BoundingBoxXYZ box, Transform transform)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            for (int i = 0; i < 8; i++)
            {
                var corner = new XYZ(
                    (i & 1) == 0 ? box.Min.X : box.Max.X,
                    (i & 2) == 0 ? box.Min.Y : box.Max.Y,
                    (i & 4) == 0 ? box.Min.Z : box.Max.Z);

                XYZ p = transform == null || transform.IsIdentity ? corner : transform.OfPoint(corner);

                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }

            snapshot.MinXM = ToMeters(minX);
            snapshot.MinYM = ToMeters(minY);
            snapshot.MaxXM = ToMeters(maxX);
            snapshot.MaxYM = ToMeters(maxY);
        }

        private static string ResolveLevelName(Document doc, Space space)
        {
            try
            {
                Level level = space.Level;
                if (level != null) return level.Name ?? "";
            }
            catch
            {
                // 忽略:标高取不到不影响面积/层高
            }

            return "";
        }

        private static double ToMeters(double feet)
        {
            return UnitUtils.ConvertFromInternalUnits(feet, DisplayUnitType.DUT_METERS);
        }

        private static double ToSquareMeters(double squareFeet)
        {
            return UnitUtils.ConvertFromInternalUnits(squareFeet, DisplayUnitType.DUT_SQUARE_METERS);
        }

        private static double ToCubicMeters(double cubicFeet)
        {
            return UnitUtils.ConvertFromInternalUnits(cubicFeet, DisplayUnitType.DUT_CUBIC_METERS);
        }
    }

    /// <summary>选择过滤器:只允许空间(Space)。</summary>
    internal sealed class SpaceSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Space;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}

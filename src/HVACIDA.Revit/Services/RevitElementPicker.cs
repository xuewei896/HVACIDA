using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace HVACIDA.Revit.Services
{
    /// <summary>
    /// 手动拾取 Revit 实体(需求 2.2.3.2:小系统"与土壤接触外墙长度"由用户选取墙体、多选后取长度之和)。
    /// <para>
    /// <strong>必须在没有模态窗口的 IExternalCommand 上下文里调用</strong> —— 见 PublicAreaWindow 注释:
    /// WPF 模态窗会在 Win32 层禁用 Revit 主窗,故流程是"关窗 → 拾取 → 用同一 ViewModel 重开窗"。
    /// 只读操作,不需要 Transaction。
    /// </para>
    /// </summary>
    internal static class RevitElementPicker
    {
        /// <summary>
        /// 拾取多段墙体并返回**长度之和**(米)。
        /// 单元换算集中在这里:Revit 内部长度为英尺,对外统一 m。
        /// </summary>
        /// <returns>长度之和(米);用户 Esc 取消时返回 null。</returns>
        public static double? PickWallLengthMeters(Autodesk.Revit.UI.UIDocument uidoc, out string note)
        {
            note = "";
            if (uidoc == null)
            {
                note = "没有活动文档,无法拾取墙体。";
                return null;
            }

            IList<Reference> references;
            try
            {
                references = uidoc.Selection.PickObjects(ObjectType.Element, new WallSelectionFilter(),
                    "请选择与土壤接触的外墙(可多选,回车结束;Esc 取消)");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                note = "已取消";
                return null;
            }

            double totalFeet = 0;
            int counted = 0;
            int skipped = 0;
            var seen = new HashSet<int>();

            foreach (var reference in references)
            {
                var wall = uidoc.Document.GetElement(reference) as Wall;
                if (wall == null || !seen.Add(wall.Id.IntegerValue)) continue;

                double length = WallLengthFeet(wall);
                if (length <= 0)
                {
                    skipped++;
                    continue;
                }
                totalFeet += length;
                counted++;
            }

            note = "已选 " + counted + " 段墙体,合计长度 " + ToMeters(totalFeet).ToString("0.##") + " m" +
                   (skipped > 0 ? "(另有 " + skipped + " 段取不到长度,已跳过)" : "");
            return ToMeters(totalFeet);
        }

        /// <summary>取墙长(英尺):优先取定位线的曲线长度,退化到包围盒长边。</summary>
        private static double WallLengthFeet(Wall wall)
        {
            try
            {
                var location = wall.Location as LocationCurve;
                if (location != null && location.Curve != null)
                {
                    return location.Curve.Length;     // 英尺
                }

                BoundingBoxXYZ box = wall.get_BoundingBox(null);
                if (box != null)
                {
                    double dx = box.Max.X - box.Min.X;
                    double dy = box.Max.Y - box.Min.Y;
                    return Math.Max(dx, dy);
                }
            }
            catch
            {
                // 忽略:取不到长度的墙由调用方计数跳过
            }
            return 0;
        }

        private static double ToMeters(double feet)
        {
            return UnitUtils.ConvertFromInternalUnits(feet, DisplayUnitType.DUT_METERS);
        }
    }

    /// <summary>选择过滤器:只允许墙(及墙的实例)。</summary>
    internal sealed class WallSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Wall;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}

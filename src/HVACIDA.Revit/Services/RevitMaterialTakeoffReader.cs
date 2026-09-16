using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.Revit.Services
{
    /// <summary>
    /// 从 Revit 模型读取**材料表统计**所需的构件数据(需求 2.5)。
    /// <para>
    /// 只读操作(不开事务);把"怎么读的"写进每条的 <see cref="MaterialItem.Note"/> ——
    /// 长度取定位线曲线长度、件数按元素个数、保温按长度计(面积需按展开面另算),
    /// 读不到几何/参数的构件**计数并报出来**,不静默丢弃。
    /// </para>
    /// </summary>
    internal static class RevitMaterialTakeoffReader
    {
        /// <summary>统计的类别(与 Core 的 MaterialCategory 一一对应)。</summary>
        private static readonly BuiltInCategory[] Categories =
        {
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_DuctTerminal,
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_DuctInsulations,
            BuiltInCategory.OST_PipeInsulations
        };

        /// <summary>读全模型的构件清单。</summary>
        public static IList<MaterialItem> Read(Document doc, out string note)
        {
            var items = new List<MaterialItem>();
            note = "";
            if (doc == null)
            {
                note = "没有活动文档,无法统计。";
                return items;
            }

            int skippedNoGeometry = 0;
            int skippedNoType = 0;
            var byCategory = new Dictionary<MaterialCategory, int>();

            var filter = new ElementMulticategoryFilter(new List<BuiltInCategory>(Categories));
            var collector = new FilteredElementCollector(doc).WherePasses(filter).WhereElementIsNotElementType();

            foreach (var element in collector)
            {
                if (element == null) continue;
                var category = Map(element.Category);
                if (category == MaterialCategory.Other) continue;

                string family, typeName;
                if (!TryReadType(doc, element, out family, out typeName))
                {
                    skippedNoType++;
                    continue;
                }

                var item = new MaterialItem
                {
                    Category = category,
                    CategoryName = MaterialTakeoffService.CategoryName(category),
                    FamilyName = family,
                    TypeName = typeName,
                    ElementId = element.Id.IntegerValue,
                    SystemName = SafeSystemName(element)
                };

                // 计量口径:管道类取长度、保温取长度(面积需展开面另算)、其余按件数
                double lengthM = 0;
                if (IsLinear(category)) lengthM = CurveLengthMeters(element);
                if (lengthM > 0)
                {
                    item.Unit = "m";
                    item.Quantity = lengthM;
                    item.Count = 1;
                    item.Note = category == MaterialCategory.Insulation
                        ? "长度沿宿主管道量取(保温面积需按展开面另算)"
                        : "长度取定位线曲线长度";
                }
                else
                {
                    item.Unit = "个";
                    item.Quantity = 1;
                    item.Count = 1;
                    item.Note = IsLinear(category)
                        ? "未取到长度曲线,按 1 件计(请在模型里核对)"
                        : "按件数计";
                    if (IsLinear(category)) skippedNoGeometry++;
                }

                int count;
                byCategory.TryGetValue(category, out count);
                byCategory[category] = count + 1;
                items.Add(item);
            }

            var parts = new List<string>();
            parts.Add("已统计 " + items.Count + " 个构件");
            foreach (var pair in byCategory)
            {
                parts.Add(MaterialTakeoffService.CategoryName(pair.Key) + " " + pair.Value);
            }
            if (skippedNoGeometry > 0) parts.Add("有 " + skippedNoGeometry + " 个构件未取到长度曲线(已按 1 件计)");
            if (skippedNoType > 0) parts.Add("有 " + skippedNoType + " 个构件读不到族/类型(已跳过)");
            note = string.Join(",", parts.ToArray()) + "。";
            return items;
        }

        /// <summary>元素类别 → Core 的统计类别。</summary>
        private static MaterialCategory Map(Category category)
        {
            if (category == null) return MaterialCategory.Other;
            var id = (BuiltInCategory)category.Id.IntegerValue;
            switch (id)
            {
                case BuiltInCategory.OST_DuctCurves: return MaterialCategory.Duct;
                case BuiltInCategory.OST_DuctFitting: return MaterialCategory.DuctFitting;
                case BuiltInCategory.OST_DuctAccessory: return MaterialCategory.DuctAccessory;
                case BuiltInCategory.OST_DuctTerminal: return MaterialCategory.DuctTerminal;
                case BuiltInCategory.OST_PipeCurves: return MaterialCategory.Pipe;
                case BuiltInCategory.OST_PipeFitting: return MaterialCategory.PipeFitting;
                case BuiltInCategory.OST_PipeAccessory: return MaterialCategory.PipeAccessory;
                case BuiltInCategory.OST_MechanicalEquipment: return MaterialCategory.MechanicalEquipment;
                case BuiltInCategory.OST_PlumbingFixtures: return MaterialCategory.PlumbingFixture;
                case BuiltInCategory.OST_DuctInsulations:
                case BuiltInCategory.OST_PipeInsulations: return MaterialCategory.Insulation;
                default: return MaterialCategory.Other;
            }
        }

        /// <summary>管道类(按长度计量)。</summary>
        private static bool IsLinear(MaterialCategory category)
        {
            return category == MaterialCategory.Duct || category == MaterialCategory.Pipe ||
                   category == MaterialCategory.Insulation;
        }

        /// <summary>读族名与类型名(ElementType.FamilyName 对 MEP 曲线与族实例都有效)。</summary>
        private static bool TryReadType(Document doc, Element element, out string family, out string typeName)
        {
            family = "";
            typeName = "";
            try
            {
                var type = doc.GetElement(element.GetTypeId()) as ElementType;
                if (type == null) return false;
                family = type.FamilyName ?? "";
                typeName = type.Name ?? "";
                if (string.IsNullOrEmpty(typeName) && string.IsNullOrEmpty(family)) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>取长度(米):优先定位线曲线长度,退化到包围盒长边。</summary>
        private static double CurveLengthMeters(Element element)
        {
            try
            {
                var location = element.Location as LocationCurve;
                if (location != null && location.Curve != null)
                {
                    return UnitUtils.ConvertFromInternalUnits(location.Curve.Length, DisplayUnitType.DUT_METERS);
                }

                BoundingBoxXYZ box = element.get_BoundingBox(null);
                if (box != null)
                {
                    double dx = box.Max.X - box.Min.X;
                    double dy = box.Max.Y - box.Min.Y;
                    double dz = box.Max.Z - box.Min.Z;
                    double longest = Math.Max(dx, Math.Max(dy, dz));
                    if (longest > 0)
                        return UnitUtils.ConvertFromInternalUnits(longest, DisplayUnitType.DUT_METERS);
                }
            }
            catch
            {
                // 读不到长度由调用方计数并给出提示
            }
            return 0;
        }

        /// <summary>所属系统名(读不到给空,不影响统计)。</summary>
        private static string SafeSystemName(Element element)
        {
            try
            {
                var curve = element as MEPCurve;
                if (curve != null && curve.MEPSystem != null) return curve.MEPSystem.Name ?? "";
                var instance = element as FamilyInstance;
                if (instance != null && instance.MEPModel != null && instance.MEPModel.ConnectorManager != null)
                {
                    foreach (Connector connector in instance.MEPModel.ConnectorManager.Connectors)
                    {
                        if (connector != null && connector.MEPSystem != null) return connector.MEPSystem.Name ?? "";
                    }
                }
            }
            catch
            {
                // 系统名只是辅助信息
            }
            return "";
        }
    }
}

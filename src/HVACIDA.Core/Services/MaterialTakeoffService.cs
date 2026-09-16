using System;
using System.Collections.Generic;
using System.Globalization;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// **材料表统计**(需求 2.5):把从模型读到的构件归并成材料表。
    /// <para>
    /// 归并键 = **类别 + 族 + 类型 + 单位** —— 单位进了键,所以**不会**把"米"和"个"加在一起
    /// (那样得到的数字没有意义);类别小计同理只在该类别单位一致时给数量合计,单位不一致时只给件数与类型数。
    /// </para>
    /// <para>
    /// 与其它模块同一纪律:逐条构件由 Revit 读取器给出(**怎么读的写在每条 Note 里**),
    /// 本类只做归并与渲染,不重算、不估算、不补空缺。
    /// </para>
    /// </summary>
    public class MaterialTakeoffService
    {
        /// <summary>汇总构件清单 → 材料表结果。</summary>
        public MaterialTakeoffResult Summarize(IList<MaterialItem> items)
        {
            var result = new MaterialTakeoffResult();
            if (items == null || items.Count == 0)
            {
                result.Note = "没有读到任何构件:请确认当前模型里已建模风管 / 水管 / 管件 / 设备,或所选类别与项目实际一致。";
                return result;
            }

            result.ItemCount = items.Count;

            // ---- 归并:类别 + 族 + 类型 + 单位 ----
            var index = new Dictionary<string, MaterialTakeoffRow>();
            var order = new List<string>();
            foreach (var item in items)
            {
                if (item == null) continue;
                result.TotalCount += item.Count;

                string key = item.Category + "|" + (item.FamilyName ?? "") + "|" + (item.TypeName ?? "") + "|" + (item.Unit ?? "");
                MaterialTakeoffRow row;
                if (!index.TryGetValue(key, out row))
                {
                    row = new MaterialTakeoffRow
                    {
                        Category = item.Category,
                        CategoryName = string.IsNullOrEmpty(item.CategoryName) ? CategoryName(item.Category) : item.CategoryName,
                        FamilyName = item.FamilyName ?? "",
                        TypeName = item.TypeName ?? "",
                        Unit = item.Unit ?? "个",
                        Note = item.Note ?? ""
                    };
                    index[key] = row;
                    order.Add(key);
                }
                row.TotalQuantity += item.Quantity;
                row.TotalCount += item.Count;
            }

            foreach (var key in order) result.Rows.Add(index[key]);
            result.Rows.Sort(delegate (MaterialTakeoffRow a, MaterialTakeoffRow b)
            {
                int byCategory = a.Category.CompareTo(b.Category);
                if (byCategory != 0) return byCategory;
                int byQuantity = b.TotalQuantity.CompareTo(a.TotalQuantity);
                if (byQuantity != 0) return byQuantity;
                return string.Compare(a.TypeName, b.TypeName, StringComparison.Ordinal);
            });

            // ---- 类别小计:单位一致才累加数量,否则只给件数与类型数 ----
            var totals = new Dictionary<MaterialCategory, MaterialCategoryTotal>();
            var totalOrder = new List<MaterialCategory>();
            var mixedUnits = new Dictionary<MaterialCategory, bool>();
            foreach (var row in result.Rows)
            {
                MaterialCategoryTotal total;
                if (!totals.TryGetValue(row.Category, out total))
                {
                    total = new MaterialCategoryTotal
                    {
                        Category = row.Category,
                        CategoryName = row.CategoryName,
                        Unit = row.Unit
                    };
                    totals[row.Category] = total;
                    totalOrder.Add(row.Category);
                }
                if (!string.Equals(total.Unit, row.Unit, StringComparison.Ordinal))
                {
                    mixedUnits[row.Category] = true;
                }
                total.TotalQuantity += row.TotalQuantity;
                total.TotalCount += row.TotalCount;
                total.TypeCount++;
            }
            foreach (var category in totalOrder)
            {
                var total = totals[category];
                bool mixed;
                if (mixedUnits.TryGetValue(category, out mixed) && mixed)
                {
                    // 同一类别里出现不同计量单位(例如风管按 m、风管保温按 m²)→ 不累加数量
                    total.Unit = "混合单位(不累加)";
                    total.TotalQuantity = 0;
                }
                result.CategoryTotals.Add(total);
            }

            result.Note =
                "材料表口径:按「类别 + 族 + 类型 + 单位」归并,**单位进归并键** —— 长度(m)、面积(m²)、体积(m³)与" +
                "件数(个)不互相求和;数量一律取自模型(风管/水管取长度、保温取面积、设备与管件/附件/末端取件数),现场损耗与" +
                "接头/翻边等不计(需要时按项目规定另计损耗率)。";
            result.PendingNote = "";
            int skipped = 0;
            foreach (var item in items)
            {
                if (item == null) skipped++;
            }
            if (skipped > 0) result.PendingNote = "有 " + skipped + " 条构件数据为空,已跳过。";
            return result;
        }

        /// <summary>类别中文名(与 Revit 类别一一对应,便于回模型核对)。</summary>
        public static string CategoryName(MaterialCategory category)
        {
            switch (category)
            {
                case MaterialCategory.Duct: return "风管";
                case MaterialCategory.DuctFitting: return "风管管件";
                case MaterialCategory.DuctAccessory: return "风管附件";
                case MaterialCategory.DuctTerminal: return "风口 / 末端";
                case MaterialCategory.Pipe: return "水管";
                case MaterialCategory.PipeFitting: return "水管管件";
                case MaterialCategory.PipeAccessory: return "水管附件";
                case MaterialCategory.MechanicalEquipment: return "机械设备(风机/机组等)";
                case MaterialCategory.PlumbingFixture: return "卫生器具 / 末端";
                case MaterialCategory.Insulation: return "保温";
                default: return "其它";
            }
        }
    }

    /// <summary>
    /// 材料表的**表格渲染**:类别小计 + 逐类型明细(界面用 <see cref="ResultTable"/>,
    /// 逐类型明细是"一行一个类型"故走专用对象,与排烟/管段同理)。
    /// </summary>
    public static class MaterialTakeoffTable
    {
        /// <summary>类别小计 + 口径(交给 ResultTableView 渲染,与计算书/Excel 同源)。</summary>
        public static ResultTable ForSummary(MaterialTakeoffResult result)
        {
            var table = new ResultTable
            {
                Title = "材料表统计(需求 2.5)",
                Note = (result.Note ?? "") + (string.IsNullOrEmpty(result.PendingNote) ? "" : " " + result.PendingNote)
            };

            var counts = table.Section("一、统计概况");
            counts.Add("从模型读到的构件数", "", result.ItemCount, "件", 0);
            counts.Add("构件总件数", "", result.TotalCount, "件", 0);
            counts.Add("类型数(归并后)", "", result.Rows.Count, "种", 0);
            counts.Add("涉及类别数", "", result.CategoryTotals.Count, "类", 0);

            var totals = table.Section("二、按类别小计");
            foreach (var category in result.CategoryTotals)
            {
                if (string.IsNullOrEmpty(category.Unit) || category.Unit.StartsWith("混合"))
                {
                    totals.Add(category.CategoryName + "(件数)", "", category.TotalCount, "件", 0);
                    totals.AddText(category.CategoryName + " 计量",
                        "该类别下存在多种计量单位(混合单位),数量不累加 —— 见逐类型明细;件数仍可合计");
                }
                else
                {
                    totals.Add(category.CategoryName, "", category.TotalQuantity, category.Unit, category.Unit == "个" ? 0 : 1);
                }
            }
            return table;
        }

        /// <summary>逐类型明细的文本表(计算书用;界面用 DataGrid 直接绑 <see cref="MaterialTakeoffResult.Rows"/>)。</summary>
        public static string ToText(MaterialTakeoffResult result)
        {
            if (result == null || !result.HasRows) return "";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("—— 材料表(逐类型;按类别 + 族 + 类型 + 单位归并) ——");
            sb.AppendLine(Pad("类别", 22) + Pad("族", 24) + Pad("类型", 28) + Pad("数量", 14) + Pad("单位", 8) + "件数");
            foreach (var row in result.Rows)
            {
                sb.AppendLine(Pad(row.CategoryName, 22) + Pad(row.FamilyName, 24) + Pad(row.TypeName, 28) +
                              Pad(Num(row.TotalQuantity, row.Unit == "个" ? 0 : 2), 14) + Pad(row.Unit, 8) +
                              Num(row.TotalCount, 0));
            }
            return sb.ToString();
        }

        private static string Num(double value, int decimals)
        {
            return value.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        private static string Pad(string text, int width)
        {
            text = text ?? "";
            int w = 0;
            foreach (char c in text) w += c > 0x2E80 ? 2 : 1;
            return w >= width ? text + " " : text + new string(' ', width - w);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>图例表的一行(需求 2.6:图例,复用材料表统计结果)。</summary>
    public class LegendRow
    {
        /// <summary>图例编号(1、2、3…;按类别分组连续编号)。</summary>
        public int Index { get; set; }

        /// <summary>类别名。</summary>
        public string CategoryName { get; set; } = "";

        /// <summary>类型名(族 + 类型,与材料表同一归并口径)。</summary>
        public string TypeName { get; set; } = "";

        /// <summary>规格说明(族名 + 类型名 + 数量单位;作为图例说明文字)。</summary>
        public string Spec { get; set; } = "";

        /// <summary>数量。</summary>
        public double Quantity { get; set; }

        /// <summary>单位。</summary>
        public string Unit { get; set; } = "";

        /// <summary>件数。</summary>
        public double Count { get; set; }
    }

    /// <summary>
    /// **图例表生成**(需求 2.6:图例):直接复用材料表统计结果,不改口径、不重算数量。
    /// <para>
    /// 说明:插件**不自动在图纸上排版图例**(图例视图要放符号族、排版因图幅而异);
    /// 这里给出**编号 + 类型 + 数量**的图例表,可导出 Excel 供出图时贴入或据此创建 Revit 图例视图。
    /// 这一点在界面上如实写明,不假装已经自动排版。
    /// </para>
    /// </summary>
    public static class MaterialLegendBuilder
    {
        /// <summary>由材料表统计结果生成图例表(按类别分组、组内按数量从大到小)。</summary>
        public static IList<LegendRow> Build(MaterialTakeoffResult result)
        {
            var rows = new List<LegendRow>();
            if (result == null || !result.HasRows) return rows;

            int index = 0;
            var lastCategory = (MaterialCategory)(-1);
            foreach (var row in result.Rows)
            {
                if (row.Category != lastCategory)
                {
                    lastCategory = row.Category;
                    index = 0;      // 每个类别从 1 开始编号
                }
                index++;
                rows.Add(new LegendRow
                {
                    Index = index,
                    CategoryName = row.CategoryName,
                    TypeName = string.IsNullOrEmpty(row.FamilyName) ? row.TypeName : row.FamilyName + " · " + row.TypeName,
                    Spec = BuildSpec(row),
                    Quantity = row.TotalQuantity,
                    Unit = row.Unit,
                    Count = row.TotalCount
                });
            }
            return rows;
        }

        /// <summary>图例表渲染成结果表(界面与 Excel、计算书同源)。</summary>
        public static ResultTable ForLegend(IList<LegendRow> rows)
        {
            var table = new ResultTable
            {
                Title = "图例表(需求 2.6;由材料表统计生成)",
                Note = "图例口径:直接复用材料表统计结果(类别 + 族 + 类型 + 单位),**数量与材料表逐项一致**;" +
                       "插件不自动在图纸上排版图例(图例视图要放符号族、排版随图幅变化),本表用于出图时贴入或据此创建 Revit 图例视图。"
            };
            if (rows == null || rows.Count == 0)
            {
                table.Section("图例").AddText("图例项", "—(还没有材料表数据:请先到「出图 → 明细表」读取模型)");
                return table;
            }

            var section = table.Section("一、图例项(按类别分组编号)");
            foreach (var row in rows)
            {
                section.Add(row.CategoryName + " " + row.Index + " · " + row.TypeName, "",
                    row.Quantity, string.IsNullOrEmpty(row.Unit) ? "个" : row.Unit, row.Unit == "个" ? 0 : 1);
            }
            return table;
        }

        /// <summary>图例表的等宽文本(贴进图纸说明或交底文件)。</summary>
        public static string ToText(IList<LegendRow> rows)
        {
            if (rows == null || rows.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("—— 图例表 ——");
            sb.AppendLine(Pad("类别", 20) + Pad("编号", 8) + Pad("类型", 40) + Pad("数量", 14) + "单位");
            foreach (var row in rows)
            {
                sb.AppendLine(Pad(row.CategoryName, 20) + Pad(row.Index.ToString(), 8) + Pad(row.TypeName, 40) +
                              Pad(row.Quantity.ToString("N2", CultureInfo.InvariantCulture), 14) + row.Unit);
            }
            return sb.ToString();
        }

        private static string BuildSpec(MaterialTakeoffRow row)
        {
            string unit = string.IsNullOrEmpty(row.Unit) ? "个" : row.Unit;
            string quantity = row.TotalQuantity.ToString("N2", CultureInfo.InvariantCulture);
            return row.CategoryName + " " + (string.IsNullOrEmpty(row.FamilyName) ? "" : row.FamilyName + " ") +
                   row.TypeName + " — " + quantity + " " + unit + "(" + row.TotalCount.ToString("N0", CultureInfo.InvariantCulture) + " 件)";
        }

        private static string Pad(string text, int width)
        {
            text = text ?? "";
            int w = 0;
            foreach (char c in text) w += c > 0x2E80 ? 2 : 1;
            return w >= width ? text + " " : text + new string(' ', width - w);
        }
    }

    /// <summary>一个视图上的自动标注结果(需求 2.6)。</summary>
    public class AutoTagViewResult
    {
        public string ViewName { get; set; } = "";

        /// <summary>新增标注数。</summary>
        public int Added { get; set; }

        /// <summary>跳过数(已有标注 / 该视图没有空间)。</summary>
        public int Skipped { get; set; }

        /// <summary>失败数(标注创建失败,通常是没有合适的位置)。</summary>
        public int Failed { get; set; }

        /// <summary>说明(为什么跳过 / 失败).</summary>
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// **自动标注**结果(需求 2.6):逐视图统计新增 / 跳过 / 失败。
    /// <para>
    /// 口径:只做**有限范围**的标注 —— 在平面视图里给**空间**加「名称 + 编号」标注;
    /// 已经有同类标注的视图**跳过**(不重复堆标注);创建失败逐条报数,不静默。
    /// </para>
    /// </summary>
    public class AutoTagResult
    {
        public List<AutoTagViewResult> Views { get; set; } = new List<AutoTagViewResult>();

        public int AddedTotal { get; set; }
        public int SkippedTotal { get; set; }
        public int FailedTotal { get; set; }

        /// <summary>处理的视图数。</summary>
        public int ViewCount => Views.Count;

        /// <summary>口径说明。</summary>
        public string Note { get; set; } =
            "自动标注口径:在平面视图里为**空间**添加「名称 + 编号」标注;" +
            "**已有同类标注的视图跳过**(不重复堆标注);无空间的视图跳过;创建失败逐条计数。";

        /// <summary>待补 / 局限。</summary>
        public string PendingNote { get; set; } =
            "范围说明:只做空间名称/编号标注,**不做**风管/水管尺寸与设备编号的自动标注(需要规则库与位置算法,后续再做)。";

        /// <summary>是否有处理结果。</summary>
        public bool HasRows => Views.Count > 0;
    }
}

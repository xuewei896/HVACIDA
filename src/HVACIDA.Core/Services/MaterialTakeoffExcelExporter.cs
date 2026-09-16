using System;
using System.Collections.Generic;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// **材料表**的 Excel(.xlsx)导出(需求 2.5):类别小计 + 逐类型明细 + 口径与待补。
    /// 与其它模块一致 —— 汇总页渲染同一份 <see cref="ResultTable"/>,明细页从同一批结果对象逐行写。
    /// </summary>
    public static class MaterialTakeoffExcelExporter
    {
        /// <summary>材料表工作簿(3 页:类别小计 / 逐类型明细 / 口径与待补)。</summary>
        public static XlsxWorkbook Build(MaterialTakeoffResult result)
        {
            var workbook = new XlsxWorkbook();
            if (result == null) return workbook;

            ExcelReportBuilder.AddResultTable(workbook.AddSheet("类别小计"), MaterialTakeoffTable.ForSummary(result));

            var detail = workbook.AddSheet("逐类型明细");
            var rows = new List<object[]>();
            foreach (var row in result.Rows)
            {
                rows.Add(new object[]
                {
                    row.CategoryName, row.FamilyName, row.TypeName, row.TotalQuantity, row.Unit, row.TotalCount,
                    row.Note
                });
            }
            ExcelReportBuilder.AddTable(detail,
                new List<string> { "类别", "族", "类型", "数量", "单位", "件数", "备注" }, rows);

            // 图例表(需求 2.6):直接复用材料表结果生成,数量与明细页一致
            var legend = MaterialLegendBuilder.Build(result);
            ExcelReportBuilder.AddResultTable(workbook.AddSheet("图例表"), MaterialLegendBuilder.ForLegend(legend));

            ExcelReportBuilder.AddNoteSheet(workbook, "材料表统计", result.Note, result.PendingNote);
            return workbook;
        }
    }
}

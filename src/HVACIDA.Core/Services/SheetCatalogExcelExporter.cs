using System;
using System.Collections.Generic;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// **图纸清单 / 图框统计 / 批量出图记录**的 Excel(.xlsx)导出(需求 2.6)。
    /// 与其它模块一致:概况页渲染同一份 <see cref="ResultTable"/>,明细页从同一批对象逐行写。
    /// </summary>
    public static class SheetCatalogExcelExporter
    {
        /// <summary>图纸清单工作簿(4 页:概况与图框 / 逐张图纸 / 批量出图记录 / 口径与待补)。</summary>
        public static XlsxWorkbook Build(SheetCatalogResult result)
        {
            var workbook = new XlsxWorkbook();
            if (result == null) return workbook;

            ExcelReportBuilder.AddResultTable(workbook.AddSheet("概况与图框"), SheetCatalogTable.ForSummary(result));

            var sheets = workbook.AddSheet("逐张图纸");
            var sheetRows = new List<object[]>();
            foreach (var sheet in result.Sheets)
            {
                var names = new List<string>();
                foreach (var view in sheet.Views) names.Add(view.ViewName);
                sheetRows.Add(new object[]
                {
                    sheet.SheetNumber, sheet.SheetName, sheet.TitleBlockFamily, sheet.TitleBlockType,
                    sheet.WidthMm > 0 ? (object)sheet.WidthMm : "—",
                    sheet.HeightMm > 0 ? (object)sheet.HeightMm : "—",
                    sheet.ViewCount, string.Join("、", names.ToArray()), sheet.Note
                });
            }
            ExcelReportBuilder.AddTable(sheets,
                new List<string> { "图纸编号", "图纸名称", "图框族", "图框类型", "宽 mm", "高 mm", "视图数", "视图", "备注" },
                sheetRows);

            var exports = workbook.AddSheet("批量出图记录");
            var exportRows = new List<object[]>();
            foreach (var record in result.Exports)
            {
                exportRows.Add(new object[]
                {
                    record.SheetNumber, record.SheetName, record.Format, record.Succeeded ? "成功" : "失败",
                    record.OutputPath, record.Message
                });
            }
            if (exportRows.Count == 0)
            {
                exports.AddRow("(本次尚未执行批量出图)");
                exports.AddRow("输出目录", result.OutputDirectory ?? "");
            }
            else
            {
                ExcelReportBuilder.AddTable(exports,
                    new List<string> { "图纸编号", "图纸名称", "格式", "结果", "输出路径", "说明" }, exportRows);
            }

            ExcelReportBuilder.AddNoteSheet(workbook, "图纸清单与批量出图",
                result.Note + (string.IsNullOrEmpty(result.OutputDirectory) ? "" : " 输出目录:" + result.OutputDirectory),
                result.PendingNote);
            return workbook;
        }
    }
}

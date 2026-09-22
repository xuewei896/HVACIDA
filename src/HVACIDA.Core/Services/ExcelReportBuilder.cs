using System;
using System.Collections.Generic;
using System.IO;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// Excel 计算书的**公共渲染件**:把 Core 的结果对象写成工作表。
    /// <para>
    /// 纪律:所有导出都从**同一份 Core 数据**渲染 —— 汇总类页面直接走 <see cref="ResultTable"/>(与界面表格、
    /// 文本计算书同源),明细类页面(管段/房间/设备/分区)从同一批结果对象逐行写,
    /// 不在导出器里重算、也不手拼数字。
    /// </para>
    /// </summary>
    public static class ExcelReportBuilder
    {
        /// <summary>把一份结果表渲染成工作表(大标题 + 分区标题跨列合并 + 表头 + 项目/数值/单位三列 + 口径)。</summary>
        public static void AddResultTable(XlsxSheet sheet, ResultTable table)
        {
            if (sheet == null || table == null) return;
            sheet.SetColumnWidths(52, 18, 12);
            sheet.FreezeRows(1);
            sheet.AddTitle(table.Title);
            foreach (var section in table.Sections)
            {
                sheet.AddSectionTitle(section.Title, 3);
                sheet.AddHeader("项目", "数值", "单位");
                foreach (var row in section.Rows)
                {
                    if (row.Value.HasValue) sheet.AddRow(row.Label, row.Value.Value, row.Unit);
                    else sheet.AddRow(row.Label, row.Text, "");
                }
                sheet.AddBlankRow();
            }
            if (!string.IsNullOrEmpty(table.Note)) sheet.AddRow("口径 / 说明", table.Note);
        }

        /// <summary>加一张"表头 + 数据行"的明细表(数值保持数值单元格,便于在 Excel 里继续算)。</summary>
        public static void AddTable(XlsxSheet sheet, IList<string> headers, IList<object[]> rows)
        {
            if (sheet == null) return;
            if (headers != null && headers.Count > 0)
            {
                var widths = new double[headers.Count];
                for (int i = 0; i < widths.Length; i++) widths[i] = i == 0 ? 24 : 20;
                sheet.SetColumnWidths(widths);
                sheet.FreezeRows(1);

                var array = new string[headers.Count];
                for (int i = 0; i < headers.Count; i++) array[i] = headers[i];
                sheet.AddHeader(array);
            }
            if (rows == null) return;
            foreach (var row in rows)
            {
                if (row != null) sheet.AddRow(row);
            }
        }

        /// <summary>加一张"口径与待补"表:计算书必须自带口径,不看界面也知道数是怎么来的。</summary>
        public static void AddNoteSheet(XlsxWorkbook workbook, string title, string note, string pendingNote)
        {
            if (workbook == null) return;
            var sheet = workbook.AddSheet("口径与待补");
            sheet.SetColumnWidths(22, 90);
            sheet.FreezeRows(1);
            sheet.AddHeader("项", "内容");
            sheet.AddRow("计算书", title);
            sheet.AddRow("导出时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sheet.AddRow("口径说明", note ?? "");
            sheet.AddRow("待补 / 局限", string.IsNullOrEmpty(pendingNote) ? "(无)" : pendingNote);
        }

        /// <summary>按显示宽度给文本补空格(CJK 记 2 列;供需要"表格式文本"的页面用)。</summary>
        public static string Pad(string text, int width)
        {
            text = text ?? "";
            int w = 0;
            foreach (char c in text) w += c > 0x2E80 ? 2 : 1;
            return w >= width ? text + " " : text + new string(' ', width - w);
        }

        /// <summary>把"报告目录 + 标题"拼成完整文件路径(时间戳在 <see cref="XlsxWorkbook.SaveTo"/> 里加)。</summary>
        public static string BuildPath(string reportDirectory, string title)
        {
            string directory = string.IsNullOrEmpty(reportDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HVACIDA", "Reports")
                : reportDirectory;
            return Path.Combine(directory, title);
        }
    }
}

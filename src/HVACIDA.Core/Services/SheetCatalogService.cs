using System;
using System.Collections.Generic;
using System.Globalization;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// **图纸清单与批量出图**(需求 2.6)的 Core 侧:把读到的图纸整理成清单 + 图框统计 + 导出记录。
    /// <para>
    /// 纪律:图纸 / 图框 / 视图信息**一律从模型读**(Revit 侧),本类只排序、统计与渲染;
    /// 图幅取图框外框(读不到给「—」而不是编一个 A1 尺寸);**空图框单独计数**(没放视图的图纸
    /// 在批量出图时通常是废图,要在界面上看得见)。
    /// </para>
    /// </summary>
    public class SheetCatalogService
    {
        /// <summary>整理图纸清单(按图纸编号排序,空图框计数,图框类型小计)。</summary>
        public SheetCatalogResult Summarize(IList<SheetItem> sheets)
        {
            var result = new SheetCatalogResult();
            if (sheets == null || sheets.Count == 0)
            {
                result.Note = "没有读到图纸:当前模型里可能还没有创建图纸(视图 → 图纸),或图纸未放置图框。";
                return result;
            }

            foreach (var sheet in sheets)
            {
                if (sheet == null) continue;
                result.Sheets.Add(sheet);
                result.ViewCount += sheet.ViewCount;
                if (sheet.IsEmpty) result.EmptySheetCount++;
            }
            result.Sheets.Sort(delegate (SheetItem a, SheetItem b)
            {
                return string.Compare(a.SheetNumber ?? "", b.SheetNumber ?? "", StringComparison.Ordinal);
            });

            // 图框类型小计(同族同类型合并,便于核对图框数量)
            var index = new Dictionary<string, TitleBlockTotal>();
            var order = new List<string>();
            foreach (var sheet in result.Sheets)
            {
                string key = (sheet.TitleBlockFamily ?? "") + "|" + (sheet.TitleBlockType ?? "") + "|" + sheet.SizeText;
                TitleBlockTotal total;
                if (!index.TryGetValue(key, out total))
                {
                    total = new TitleBlockTotal
                    {
                        FamilyName = sheet.TitleBlockFamily ?? "",
                        TypeName = sheet.TitleBlockType ?? "",
                        SizeText = sheet.SizeText
                    };
                    index[key] = total;
                    order.Add(key);
                }
                total.SheetCount++;
                total.ViewCount += sheet.ViewCount;
            }
            foreach (var key in order) result.TitleBlocks.Add(index[key]);

            result.Note =
                "图纸清单口径:图纸编号 / 名称 / 图框族与类型 / 图幅均取自模型(图幅按图框外框量取);" +
                "**空图框**(未放置视图)单独计数 —— 批量出图前建议先处理;" +
                "批量出图导出的是**图纸**(Sheet),DWG/DXF 走 Revit 的导出接口(逐张导出、文件名取「图纸编号_图纸名称」),";
            result.PendingNote = "";
            if (result.EmptySheetCount > 0)
                result.PendingNote = "有 " + result.EmptySheetCount + " 张图纸未放置任何视图(空图框);";
            return result;
        }

        /// <summary>把一次批量导出的结果并入统计(命令层导出完一张调一次)。</summary>
        public static void AddExport(SheetCatalogResult result, SheetExportRecord record)
        {
            if (result == null || record == null) return;
            result.Exports.Add(record);
            if (record.Succeeded) result.ExportSucceeded++;
            else result.ExportFailed++;
        }
    }

    /// <summary>图纸清单的表格渲染(界面用 <see cref="ResultTable"/>;逐张明细走 DataGrid / 文本表)。</summary>
    public static class SheetCatalogTable
    {
        /// <summary>概况 + 图框统计(交给 ResultTableView;与计算书 / Excel 同源)。</summary>
        public static ResultTable ForSummary(SheetCatalogResult result)
        {
            var table = new ResultTable
            {
                Title = "图纸清单与批量出图(需求 2.6)",
                Note = (result.Note ?? "") + (string.IsNullOrEmpty(result.PendingNote) ? "" : " " + result.PendingNote)
            };

            var overview = table.Section("一、图纸概况");
            overview.Add("图纸总数", "", result.SheetCount, "张", 0);
            overview.Add("视图总数", "", result.ViewCount, "个", 0);
            overview.Add("空图框(未放视图)", "", result.EmptySheetCount, "张", 0);
            overview.Add("图框类型数", "", result.TitleBlocks.Count, "种", 0);
            if (result.Exports.Count > 0)
            {
                overview.Add("本次导出成功", "", result.ExportSucceeded, "张", 0);
                overview.AddTotal("本次导出失败", "", result.ExportFailed, "张", 0);
            }

            var blocks = table.Section("二、图框统计(核对图框数量与图幅)");
            foreach (var block in result.TitleBlocks)
            {
                string label = (string.IsNullOrEmpty(block.FamilyName) ? "(未读图框)" : block.FamilyName) +
                               (string.IsNullOrEmpty(block.TypeName) ? "" : " · " + block.TypeName);
                blocks.Add(label + "(" + block.SizeText + ")", "", block.SheetCount, "张", 0);
                blocks.Add("   " + label + " 视图数", "", block.ViewCount, "个", 0);
            }
            return table;
        }

        /// <summary>逐张图纸的文本清单(计算书用;界面用 DataGrid 绑 <see cref="SheetCatalogResult.Sheets"/>)。</summary>
        public static string ToText(SheetCatalogResult result)
        {
            if (result == null || !result.HasSheets) return "";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("—— 图纸清单 ——");
            sb.AppendLine(Pad("图纸编号", 16) + Pad("图纸名称", 30) + Pad("图框", 26) + Pad("图幅", 18) +
                          Pad("视图数", 10) + "视图");
            foreach (var sheet in result.Sheets)
            {
                var names = new List<string>();
                foreach (var view in sheet.Views) names.Add(view.ViewName);
                sb.AppendLine(Pad(sheet.SheetNumber, 16) + Pad(sheet.SheetName, 30) +
                              Pad((sheet.TitleBlockFamily + " " + sheet.TitleBlockType).Trim(), 26) +
                              Pad(sheet.SizeText, 18) + Pad(sheet.ViewCount.ToString(), 10) +
                              string.Join("、", names.ToArray()));
            }
            if (result.Exports.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("—— 本次批量出图 ——");
                foreach (var record in result.Exports)
                {
                    sb.AppendLine(Pad(record.SheetNumber, 16) + Pad(record.Format, 8) +
                                  Pad(record.Succeeded ? "成功" : "失败", 8) + record.Message);
                }
            }
            return sb.ToString();
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

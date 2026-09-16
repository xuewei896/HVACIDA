using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.Revit.Services
{
    /// <summary>
    /// **批量出图**(需求 2.6):把选中的图纸逐张导出为 DWG / DXF / PDF。
    /// <para>
    /// <strong>口径(必须如实写在界面上)</strong>:
    /// ① **DWG / DXF** 走 Revit 自带的导出接口(<see cref="DWGExportOptions"/>),
    /// 逐张图纸导出、文件名取「图纸编号_图纸名称」—— 这一条是可靠的;
    /// ② **PDF** 走系统打印机(<c>PrintManager</c> + 「Microsoft Print to PDF」这类虚拟打印机),
    /// **依赖本机装了 PDF 打印机驱动**;没有装就**明确报错并跳过**,绝不谎报成功;
    /// ③ 导出前先检查输出目录能否创建、图纸是否为空图框(**空图框默认跳过并记原因**)。
    /// </para>
    /// <para>导出会改变打印设置 / 导出选项,故调用方必须提供事务上下文(本类内部不开事务)。</para>
    /// </summary>
    internal static class RevitSheetExporter
    {
        /// <summary>本机可用的 PDF 打印机名(找不到返回空)。</summary>
        public static string FindPdfPrinter()
        {
            try
            {
                foreach (string printer in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                {
                    if (printer != null && printer.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0)
                        return printer;
                }
            }
            catch
            {
                // 读打印机列表失败按"没有 PDF 打印机"处理
            }
            return "";
        }

        /// <summary>导出若干图纸为 DWG 或 DXF(逐张;失败逐条记录原因)。</summary>
        public static void ExportCad(Document doc, IList<ViewSheet> sheets, string outputDirectory, bool dxf,
            SheetCatalogResult result)
        {
            if (doc == null || sheets == null || sheets.Count == 0) return;
            if (!EnsureDirectory(outputDirectory, result, dxf ? "DXF" : "DWG")) return;

            foreach (var sheet in sheets)
            {
                var record = new SheetExportRecord
                {
                    SheetNumber = sheet.SheetNumber ?? "",
                    SheetName = sheet.Name ?? "",
                    Format = dxf ? "DXF" : "DWG"
                };

                if (result != null && IsEmptySheet(sheet))
                {
                    record.Succeeded = false;
                    record.Message = "空图框(未放置视图),已跳过";
                    SheetCatalogService.AddExport(result, record);
                    continue;
                }

                string baseName = SafeFileName(record.SheetNumber + "_" + record.SheetName);
                try
                {
                    // 2020 的逐视图导出:用 Document.Export(folder, name, views, options) 重载
                    var views = new List<ElementId> { sheet.Id };
                    if (dxf)
                    {
                        var dxfOptions = new DXFExportOptions();
                        try { dxfOptions.FileVersion = ACADVersion.R2013; } catch { }
                        doc.Export(outputDirectory, baseName, views, dxfOptions);
                    }
                    else
                    {
                        var dwgOptions = new DWGExportOptions();
                        try { dwgOptions.FileVersion = ACADVersion.R2013; } catch { }
                        doc.Export(outputDirectory, baseName, views, dwgOptions);
                    }

                    record.Succeeded = true;
                    record.OutputPath = Path.Combine(outputDirectory, baseName + (dxf ? ".dxf" : ".dwg"));
                    record.Message = "已导出";
                }
                catch (Exception ex)
                {
                    record.Succeeded = false;
                    record.Message = "导出失败: " + ex.Message;
                }

                if (result != null) SheetCatalogService.AddExport(result, record);
            }
        }

        /// <summary>把若干图纸打印为 PDF(依赖本机 PDF 打印机;没有则逐条记失败原因)。</summary>
        public static void ExportPdf(Document doc, IList<ViewSheet> sheets, string outputDirectory,
            SheetCatalogResult result)
        {
            if (doc == null || sheets == null || sheets.Count == 0) return;
            if (!EnsureDirectory(outputDirectory, result, "PDF")) return;

            string printer = FindPdfPrinter();
            if (string.IsNullOrEmpty(printer))
            {
                foreach (var sheet in sheets)
                {
                    SheetCatalogService.AddExport(result, new SheetExportRecord
                    {
                        SheetNumber = sheet.SheetNumber ?? "",
                        SheetName = sheet.Name ?? "",
                        Format = "PDF",
                        Succeeded = false,
                        Message = "本机没有可用的 PDF 打印机(如「Microsoft Print to PDF」),已跳过;" +
                                  "请先安装 PDF 虚拟打印机,或改用 DWG/DXF 导出"
                    });
                }
                return;
            }

            foreach (var sheet in sheets)
            {
                var record = new SheetExportRecord
                {
                    SheetNumber = sheet.SheetNumber ?? "",
                    SheetName = sheet.Name ?? "",
                    Format = "PDF"
                };

                if (IsEmptySheet(sheet))
                {
                    record.Succeeded = false;
                    record.Message = "空图框(未放置视图),已跳过";
                    SheetCatalogService.AddExport(result, record);
                    continue;
                }

                string file = Path.Combine(outputDirectory, SafeFileName(record.SheetNumber + "_" + record.SheetName) + ".pdf");
                try
                {
                    var manager = doc.PrintManager;
                    manager.SelectNewPrintDriver(printer);
                    manager.PrintRange = PrintRange.Select;
                    manager.ViewSheetSetting.CurrentViewSheetSet.Views = new ViewSet();
                    manager.ViewSheetSetting.CurrentViewSheetSet.Views.Insert(sheet);
                    manager.PrintToFile = true;
                    manager.PrintToFileName = file;
                    manager.Apply();
                    manager.SubmitPrint(sheet);
                    record.Succeeded = true;
                    record.OutputPath = file;
                    record.Message = "已打印为 PDF(打印机:" + printer + ")";
                }
                catch (Exception ex)
                {
                    record.Succeeded = false;
                    record.Message = "PDF 打印失败: " + ex.Message;
                }

                SheetCatalogService.AddExport(result, record);
            }
        }

        private static bool EnsureDirectory(string directory, SheetCatalogResult result, string format)
        {
            try
            {
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                return true;
            }
            catch (Exception ex)
            {
                if (result != null)
                {
                    SheetCatalogService.AddExport(result, new SheetExportRecord
                    {
                        Format = format,
                        Succeeded = false,
                        Message = "输出目录不可用(" + directory + "):" + ex.Message
                    });
                }
                return false;
            }
        }

        private static bool IsEmptySheet(ViewSheet sheet)
        {
            try
            {
                foreach (var ignored in sheet.GetAllPlacedViews()) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Sheet";
            var chars = name.ToCharArray();
            var invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            }
            return new string(chars);
        }
    }
}

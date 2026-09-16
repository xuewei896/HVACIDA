using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HVACIDA.Core.Models;

namespace HVACIDA.Revit.Commands
{
    /// <summary>
    /// 出图 → 图框(**图纸清单与批量出图**,需求 2.6;2026-09-16 实装,此前为待实现说明窗)。
    /// <para>
    /// 打开窗先读一遍图纸清单;窗内【重新读取图纸】与【导出 DWG/DXF/PDF】都走
    /// "关窗 → 命令层执行 → 同一 ViewModel 重开窗"的闭环(导出要用 Revit 事务与导出接口)。
    /// </para>
    /// <para>
    /// 导出口径:**DWG/DXF 用 Revit 导出接口逐张导出**;**PDF 走系统打印机**(依赖本机 PDF 打印机驱动,
    /// 没有就逐张写明失败原因,不谎报成功);**空图框跳过并记录**。
    /// </para>
    /// </summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowTitleBlockCommand : IExternalCommand
    {
        private const int MaxRounds = 8;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData?.Application?.ActiveUIDocument;
                var doc = uidoc == null ? null : uidoc.Document;
                var viewModel = new UI.ViewModels.SheetCatalogViewModel();

                if (doc == null)
                {
                    viewModel.ApplyCatalog(null, "没有活动文档,无法读取图纸清单。");
                    Services.DialogService.ShowModal(new UI.Views.SheetCatalogWindow(viewModel));
                    return Result.Succeeded;
                }

                for (int round = 0; round < MaxRounds; round++)
                {
                    if (round == 0 || viewModel.ReloadRequested)
                    {
                        viewModel.ClearRequests();
                        string note;
                        var sheets = Services.RevitSheetReader.Read(doc, out note);
                        viewModel.ApplyCatalog(sheets, note);
                    }

                    var window = new UI.Views.SheetCatalogWindow(viewModel);
                    Services.DialogService.ShowModal(window);

                    if (window.ExportRequested)
                    {
                        string format = window.PendingFormat;
                        string outputDirectory = viewModel.OutputDirectory;
                        var records = ExportSheets(doc, viewModel.Result, format, outputDirectory);
                        viewModel.ClearRequests();
                        viewModel.ApplyExports(records, outputDirectory);
                        continue;
                    }

                    if (window.ReloadRequested) continue;
                    break;
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("HVACIDA 错误", ex.ToString());
                return Result.Failed;
            }
        }

        /// <summary>按格式批量出图(在事务里执行;返回逐张记录)。</summary>
        private static IList<SheetExportRecord> ExportSheets(Document doc, SheetCatalogResult catalog, string format,
            string outputDirectory)
        {
            var records = new List<SheetExportRecord>();
            if (catalog == null || !catalog.HasSheets) return records;

            // 收集图纸元素(按清单顺序)
            var sheets = new List<ViewSheet>();
            foreach (var item in catalog.Sheets)
            {
                var sheet = doc.GetElement(new ElementId(item.ElementId)) as ViewSheet;
                if (sheet != null) sheets.Add(sheet);
            }
            if (sheets.Count == 0) return records;

            // 导出前把结果对象里的记录清空,避免重复累计(记录由本方法返回后回注)
            catalog.Exports.Clear();

            using (var transaction = new Transaction(doc, "HVACIDA 批量出图(" + format + ")"))
            {
                transaction.Start();
                try
                {
                    if (string.Equals(format, "PDF", StringComparison.OrdinalIgnoreCase))
                        Services.RevitSheetExporter.ExportPdf(doc, sheets, outputDirectory, catalog);
                    else
                        Services.RevitSheetExporter.ExportCad(doc, sheets, outputDirectory,
                            string.Equals(format, "DXF", StringComparison.OrdinalIgnoreCase), catalog);
                    transaction.Commit();
                }
                catch
                {
                    if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
                    throw;
                }
            }

            records.AddRange(catalog.Exports);
            return records;
        }
    }
}

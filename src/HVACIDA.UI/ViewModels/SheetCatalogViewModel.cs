using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 出图 → 图框(**图纸清单与批量出图**,需求 2.6)窗 ViewModel。
    /// <para>
    /// 图纸清单由 Revit 命令层读取后注入(<see cref="ApplyCatalog"/>);批量出图由命令层执行
    /// (<see cref="ExportRequested"/> + <see cref="PendingFormat"/>,因为导出要用 Revit 事务与导出接口),
    /// 执行完把记录回注(<see cref="ApplyExports"/>)再重开窗。
    /// </para>
    /// <para>
    /// **PDF 明确依赖本机 PDF 打印机**:导出失败会逐张写明原因(如"本机没有可用的 PDF 打印机"),
    /// 不谎报成功;空图框默认跳过并记录。
    /// </para>
    /// </summary>
    public class SheetCatalogViewModel : ViewModelBase
    {
        private readonly ExcelReportGenerator _excel;

        private SheetCatalogResult _result = new SheetCatalogResult();
        private ResultTable _table;
        private string _sourceNote = "";
        private string _pendingNote = "";
        private string _status = "";
        private string _outputDirectory = "";
        private string _pendingFormat = "";
        private bool _reloadRequested;
        private bool _exportRequested;

        public SheetCatalogViewModel()
            : this(null)
        {
        }

        public SheetCatalogViewModel(string reportsDirectory)
        {
            _excel = new ExcelReportGenerator(reportsDirectory);
            _outputDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HVACIDA", "Export");

            ReloadCommand = new RelayCommand(() => ReloadRequested = true);
            TagCommand = new RelayCommand(() => TagRequested = true, () => _result.HasSheets);
            ExportDwgCommand = new RelayCommand(() => RequestExport("DWG"), () => _result.HasSheets);
            ExportDxfCommand = new RelayCommand(() => RequestExport("DXF"), () => _result.HasSheets);
            ExportPdfCommand = new RelayCommand(() => RequestExport("PDF"), () => _result.HasSheets);
            ExportExcelCommand = new RelayCommand(ExportExcel, () => _result.HasSheets);

            ApplyCatalog(null, "尚未读取图纸。");
        }

        public string WindowTitle => "出图 — 图框(图纸清单与批量出图) - HVACIDA";

        /// <summary>图纸清单与统计结果。</summary>
        public SheetCatalogResult Result
        {
            get => _result;
            private set => Set(ref _result, value);
        }

        /// <summary>逐张图纸(界面 DataGrid 绑定)。</summary>
        public IList<SheetItem> Sheets => _result == null ? new List<SheetItem>() : _result.Sheets;

        /// <summary>本次批量出图记录。</summary>
        public IList<SheetExportRecord> ExportRows => _result == null ? new List<SheetExportRecord>() : _result.Exports;

        /// <summary>概况与图框统计表(ResultTableView 绑定;与计算书 / Excel 同源)。</summary>
        public ResultTable Table
        {
            get => _table;
            private set => Set(ref _table, value);
        }

        /// <summary>输出目录(可改;导出前会尝试创建)。</summary>
        public string OutputDirectory
        {
            get => _outputDirectory;
            set => Set(ref _outputDirectory, value);
        }

        public string SourceNote
        {
            get => _sourceNote;
            private set => Set(ref _sourceNote, value);
        }

        public string PendingNote
        {
            get => _pendingNote;
            private set => Set(ref _pendingNote, value);
        }

        public string SummaryTitle => _result == null || !_result.HasSheets
            ? "图纸清单(尚无数据)"
            : "共 " + _result.SheetCount + " 张图纸 / " + _result.ViewCount + " 个视图 / " +
              _result.TitleBlocks.Count + " 种图框" +
              (_result.EmptySheetCount > 0 ? "(空图框 " + _result.EmptySheetCount + " 张)" : "");

        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        /// <summary>重新读取图纸(窗口关闭后由命令层读取并重开窗)。</summary>
        public ICommand ReloadCommand { get; }

        /// <summary>批量标注空间(平面视图里给空间加「名称 + 编号」标注;已有标注的视图跳过)。</summary>
        public ICommand TagCommand { get; }

        /// <summary>已请求批量标注(命令层据此在事务里执行)。</summary>
        public bool TagRequested
        {
            get => _tagRequested;
            private set => Set(ref _tagRequested, value);
        }

        /// <summary>本次自动标注的逐视图结果。</summary>
        public IList<AutoTagViewResult> TagRows => _tagResult == null ? new List<AutoTagViewResult>() : _tagResult.Views;

        /// <summary>自动标注的口径与范围说明(界面红字,必须可见)。</summary>
        public string TagNote => _tagResult == null || _tagResult.Views.Count == 0
            ? ""
            : _tagResult.Note + " " + _tagResult.PendingNote;

        /// <summary>回注自动标注结果(命令层执行后调用)。</summary>
        public void ApplyTagResult(AutoTagResult tagResult)
        {
            _tagResult = tagResult ?? new AutoTagResult();
            OnPropertyChanged(nameof(TagRows));
            OnPropertyChanged(nameof(TagNote));
            Status = "自动标注完成:处理 " + _tagResult.ViewCount + " 个视图,新增标注 " + _tagResult.AddedTotal +
                     " 个 / 跳过 " + _tagResult.SkippedTotal + " 个 / 失败 " + _tagResult.FailedTotal +
                     " 个(逐视图见「自动标注结果」表)。";
        }

        private AutoTagResult _tagResult;
        private bool _tagRequested;

        /// <summary>批量导出 DWG(逐张图纸,输出到 <see cref="OutputDirectory"/>)。</summary>
        public ICommand ExportDwgCommand { get; }

        /// <summary>批量导出 DXF。</summary>
        public ICommand ExportDxfCommand { get; }

        /// <summary>批量打印为 PDF(依赖本机 PDF 打印机;没有则逐张报失败原因)。</summary>
        public ICommand ExportPdfCommand { get; }

        /// <summary>导出图纸清单 Excel(概况与图框 / 逐张图纸 / 批量出图记录 / 口径与待补)。</summary>
        public ICommand ExportExcelCommand { get; }

        /// <summary>已请求重新读取图纸。</summary>
        public bool ReloadRequested
        {
            get => _reloadRequested;
            private set => Set(ref _reloadRequested, value);
        }

        /// <summary>已请求批量出图(命令层据此执行导出)。</summary>
        public bool ExportRequested
        {
            get => _exportRequested;
            private set => Set(ref _exportRequested, value);
        }

        /// <summary>本次请求的导出格式(DWG / DXF / PDF)。</summary>
        public string PendingFormat
        {
            get => _pendingFormat;
            private set => Set(ref _pendingFormat, value);
        }

        private void RequestExport(string format)
        {
            PendingFormat = format;
            ExportRequested = true;
        }

        /// <summary>命令层执行完读/导出后清标记。</summary>
        public void ClearRequests()
        {
            ReloadRequested = false;
            ExportRequested = false;
            PendingFormat = "";
            TagRequested = false;
        }

        /// <summary>注入图纸清单(命令层读完模型后调用)。</summary>
        public void ApplyCatalog(IList<SheetItem> sheets, string note)
        {
            try
            {
                var result = new SheetCatalogService().Summarize(sheets);
                result.OutputDirectory = OutputDirectory;
                Result = result;
                SourceNote = note ?? "";
                PendingNote = result.PendingNote ?? "";
                OnPropertyChanged(nameof(Sheets));
                OnPropertyChanged(nameof(ExportRows));
                OnPropertyChanged(nameof(SummaryTitle));

                if (result.HasSheets)
                {
                    Table = SheetCatalogTable.ForSummary(result);
                    Status = "图纸清单读取完成:" + SummaryTitle + "。可批量导出 DWG/DXF/PDF 或导出清单 Excel。";
                }
                else
                {
                    Table = null;
                    Status = result.Note;
                }
            }
            catch (Exception ex)
            {
                Result = new SheetCatalogResult();
                Table = null;
                OnPropertyChanged(nameof(Sheets));
                OnPropertyChanged(nameof(SummaryTitle));
                Status = "图纸清单整理失败: " + ex.Message;
            }
        }

        /// <summary>回注一批导出记录(命令层导出后调用),并刷新统计与表格。</summary>
        public void ApplyExports(IList<SheetExportRecord> records, string outputDirectory)
        {
            if (records != null)
            {
                foreach (var record in records) SheetCatalogService.AddExport(_result, record);
            }
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                OutputDirectory = outputDirectory;
                _result.OutputDirectory = outputDirectory;
            }

            Table = SheetCatalogTable.ForSummary(_result);
            OnPropertyChanged(nameof(ExportRows));
            Status = "批量出图完成:成功 " + _result.ExportSucceeded + " 张 / 失败 " + _result.ExportFailed +
                     " 张(输出目录:" + _result.OutputDirectory + ")。失败原因见「批量出图记录」表。";
        }

        private void ExportExcel()
        {
            try
            {
                if (!_result.HasSheets)
                {
                    Status = "还没有可导出的图纸清单:请先在 Revit 里点【出图 → 图框】读取图纸。";
                    return;
                }

                _result.OutputDirectory = OutputDirectory;
                var workbook = SheetCatalogExcelExporter.Build(_result);
                string path = _excel.SaveWorkbook("图纸清单与批量出图", workbook);
                Status = "Excel 已生成(" + workbook.SheetCount + " 个工作表): " + path;
            }
            catch (Exception ex)
            {
                Status = "导出 Excel 失败: " + ex.Message;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 水力计算「计算结果」窗 ViewModel(Ribbon「水力计算 → 计算结果」):**全站多系统汇总**。
    /// <para>
    /// 一个车站有多套风系统与多套水系统,所以本窗做的是**全站汇总**而不是单系统结果:
    /// <see cref="HydraulicInputService.Summarize"/> 把 <c>hydraulic.xml</c> 里的每一套系统各自算一遍,
    /// 汇总表**逐系统一行**,选中某行看该系统的逐段明细 / 阻力项 / 并联平衡 / 特性曲线 / 计算书。
    /// </para>
    /// <para>
    /// **口径**:风机全压与水泵扬程**不能相加**(各系统管网相互独立),所以合计只给可加量
    /// (系统数 / 管段数 / 管段总长 / 总风量 / 总水量),压力类给最大值并逐系统列出。
    /// </para>
    /// <para>
    /// 与「大系统/小系统 计算结果」同规矩:**打开即算**;没录入过系统时**不摆结果**,只给"去拾取"的指引。
    /// </para>
    /// </summary>
    public class HydraulicResultViewModel : ViewModelBase
    {
        private readonly HydraulicInputService _service;
        private readonly ExcelReportGenerator _excel;

        private HydraulicSummary _summary = new HydraulicSummary();
        private IList<HydraulicSummaryRowView> _rows = new List<HydraulicSummaryRowView>();
        private HydraulicSummaryRowView _selectedRow;
        private ResultTable _table;
        private IList<HydraulicSegmentResult> _segmentRows = new List<HydraulicSegmentResult>();
        private IList<HydraulicItemResult> _itemRows = new List<HydraulicItemResult>();
        private IList<HydraulicBranchResult> _branchRows = new List<HydraulicBranchResult>();
        private IList<HydraulicCurvePoint> _curveRows = new List<HydraulicCurvePoint>();
        private string _detailTitle = "";
        private string _balanceNote = "";
        private string _resultText = "";
        private string _status = "";
        private string _note = "";
        private string _pendingNote = "";

        public HydraulicResultViewModel()
            : this(null)
        {
        }

        public HydraulicResultViewModel(IDataRepository repository)
            : this(repository, null)
        {
        }

        /// <summary>
        /// <paramref name="reportsDirectory"/> 用于自检时把 Excel 写到临时目录
        /// (为空则用 <c>%AppData%\HVACIDA\Reports</c>)。
        /// </summary>
        public HydraulicResultViewModel(IDataRepository repository, string reportsDirectory)
        {
            _service = new HydraulicInputService(repository);
            _excel = new ExcelReportGenerator(reportsDirectory);
            CalculateCommand = new RelayCommand(Calculate);
            ExportCommand = new RelayCommand(Export, () => _selectedRow != null && _selectedRow.Row.Result != null);
            ExportExcelCommand = new RelayCommand(ExportExcel, () => _summary.HasRows);

            Calculate();     // 打开即算(§4.9)
        }

        // ================================================================== 全站汇总

        /// <summary>全站汇总(逐系统一行的数据源)。</summary>
        public HydraulicSummary Summary
        {
            get => _summary;
            private set => Set(ref _summary, value);
        }

        /// <summary>汇总表数据源:一套系统一行(压力类不可加,「—」= 不涉及)。</summary>
        public IList<HydraulicSummaryRowView> Rows
        {
            get => _rows;
            private set => Set(ref _rows, value);
        }

        /// <summary>当前选中的系统行(变化时刷新下方明细)。</summary>
        public HydraulicSummaryRowView SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (!Set(ref _selectedRow, value)) return;
                RefreshDetail();
            }
        }

        /// <summary>全站合计表(ResultTableView 绑定它;只含可加量与压力最大值)。</summary>
        public ResultTable Table
        {
            get => _table;
            private set => Set(ref _table, value);
        }

        /// <summary>顶部一句话摘要。</summary>
        public string SummaryTitle => _rows.Count == 0
            ? "水力计算结果(尚未从模型拾取过系统)"
            : "全站共 " + _summary.SystemCount + " 套系统(风 " + _summary.AirCount + " / 水 " + _summary.WaterCount +
              ")/ 管段 " + _summary.SegmentCount + " 段 / 总长 " + _summary.TotalLengthM.ToString("N1") + " m";

        // ================================================================== 选中系统明细

        /// <summary>明细区标题。</summary>
        public string DetailTitle
        {
            get => _detailTitle;
            private set => Set(ref _detailTitle, value);
        }

        /// <summary>选中系统的逐段明细(含不在环路内的段)。</summary>
        public IList<HydraulicSegmentResult> SegmentRows
        {
            get => _segmentRows;
            private set => Set(ref _segmentRows, value);
        }

        /// <summary>选中系统的环路阻力项(末端 / 设备 / 出口动压)。</summary>
        public IList<HydraulicItemResult> ItemRows
        {
            get => _itemRows;
            private set => Set(ref _itemRows, value);
        }

        /// <summary>选中系统的并联环路平衡(逐支路)。</summary>
        public IList<HydraulicBranchResult> BranchRows
        {
            get => _branchRows;
            private set => Set(ref _branchRows, value);
        }

        /// <summary>选中系统的系统阻力特性曲线点。</summary>
        public IList<HydraulicCurvePoint> CurveRows
        {
            get => _curveRows;
            private set => Set(ref _curveRows, value);
        }

        /// <summary>并联平衡与特性曲线的口径说明。</summary>
        public string BalanceNote
        {
            get => _balanceNote;
            private set => Set(ref _balanceNote, value);
        }

        /// <summary>选中系统的计算书全文(仅由 ResultFormatter 生成)。</summary>
        public string ResultText
        {
            get => _resultText;
            private set => Set(ref _resultText, value);
        }

        /// <summary>全站汇总口径说明。</summary>
        public string Note
        {
            get => _note;
            private set => Set(ref _note, value);
        }

        /// <summary>全站待补 / 局限(红字)。</summary>
        public string PendingNote
        {
            get => _pendingNote;
            private set => Set(ref _pendingNote, value);
        }

        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        // ================================================================== 命令

        /// <summary>重新读取 hydraulic.xml 并逐系统汇总(别的窗保存过之后点它刷新)。</summary>
        public ICommand CalculateCommand { get; }

        /// <summary>导出**选中系统**的文本计算书。</summary>
        public ICommand ExportCommand { get; }

        /// <summary>导出**全站汇总** Excel(.xlsx:全站汇总 / 逐系统 / 取值与口径 + 各系统明细页)。</summary>
        public ICommand ExportExcelCommand { get; }

        // ================================================================== 实现

        private void Calculate()
        {
            try
            {
                var summary = _service.Summarize();
                Summary = summary;
                Note = summary.Note ?? "";
                PendingNote = summary.PendingNote ?? "";

                var rows = new List<HydraulicSummaryRowView>();
                foreach (var row in summary.Rows) rows.Add(new HydraulicSummaryRowView(row));
                Rows = rows;
                OnPropertyChanged(nameof(SummaryTitle));

                if (rows.Count == 0)
                {
                    // 没录入过系统就不摆结果(与其它窗同一纪律)
                    Table = null;
                    ResetDetail();
                    Status = "还没有水力系统:请到「水力计算 → 风系统 / 水系统」窗点【从模型读取该系统…】录入,再回来汇总。";
                    return;
                }

                Table = ResultTable.ForHydraulicSummary(summary);
                _selectedRow = null;
                SelectedRow = rows[0];
                Status = "已按保存的管网逐系统现算:" + SummaryTitle +
                         "。点汇总表任一行可看该系统的逐段明细、并联平衡与计算书。";
            }
            catch (Exception ex)
            {
                Summary = new HydraulicSummary();
                Rows = new List<HydraulicSummaryRowView>();
                Table = null;
                ResetDetail();
                Note = "";
                PendingNote = "";
                Status = "汇总失败: " + ex.Message;
            }
        }

        private void ResetDetail()
        {
            SegmentRows = new List<HydraulicSegmentResult>();
            ItemRows = new List<HydraulicItemResult>();
            BranchRows = new List<HydraulicBranchResult>();
            CurveRows = new List<HydraulicCurvePoint>();
            BalanceNote = "";
            ResultText = "";
            DetailTitle = "";
        }

        private void RefreshDetail()
        {
            var view = _selectedRow;
            if (view == null || view.Row.Result == null) return;

            var row = view.Row;
            var result = row.Result;
            DetailTitle = "选中介质明细:" + row.KindName + " · " + view.SystemCode + " · " + row.SystemName +
                          "(总阻力 / 需求值见上方汇总表)";
            SegmentRows = new List<HydraulicSegmentResult>(result.Segments);
            ItemRows = new List<HydraulicItemResult>(result.Items);
            BranchRows = new List<HydraulicBranchResult>(result.Branches);
            CurveRows = new List<HydraulicCurvePoint>(result.Curve);
            BalanceNote = result.BalanceNote;
            ResultText = row.ResultText;
            Status = "已选中「" + row.KindName + " " + view.SystemCode + "」:管段 " + SegmentRows.Count +
                     " 段、阻力项 " + ItemRows.Count + " 项、并联支路 " + BranchRows.Count +
                     " 条" + (result.CriticalSegmentCount > 0
                         ? ",最不利环路 " + result.CriticalSegmentCount + " 段"
                         : ",未判定最不利环路(按全部管段之和保守计入)") + "。";
        }

        private void Export()
        {
            try
            {
                var view = _selectedRow;
                if (view == null || view.Row.Result == null)
                {
                    Status = "没有可导出的结果:请先在「水力计算」窗录入系统。";
                    return;
                }

                var generator = new TextReportGenerator();
                string path = generator.SaveTextReport(
                    view.Row.KindName + "水力计算书_" + view.SystemCode,
                    view.Row.ResultText);
                Status = "文本计算书已生成(" + view.SystemCode + "): " + path;
            }
            catch (Exception ex)
            {
                Status = "导出失败: " + ex.Message;
            }
        }

        /// <summary>导出全站汇总 Excel:全站汇总 + 逐系统 + 取值与口径 + 各系统的管段/平衡明细页。</summary>
        private void ExportExcel()
        {
            try
            {
                if (!_summary.HasRows)
                {
                    Status = "还没有可导出的汇总:请先在「水力计算」窗录入系统。";
                    return;
                }

                var workbook = HydraulicExcelExporter.BuildSummary(_summary);
                string path = _excel.SaveWorkbook("全站水力计算汇总", workbook);
                Status = "全站汇总 Excel 已生成(" + _summary.SystemCount + " 套系统、" + workbook.SheetCount +
                         " 个工作表): " + path;
            }
            catch (Exception ex)
            {
                Status = "导出 Excel 失败: " + ex.Message;
            }
        }
    }
}

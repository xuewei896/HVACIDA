using System;
using System.Collections.Generic;
using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 水力计算「计算结果」窗 ViewModel(Ribbon「水力计算 → 计算结果」)。
    /// <para>
    /// 读 hydraulic.xml 里保存的**风系统**与**水系统**输入(系数集共用)→ 逐个现算最不利环路
    /// → 汇总表(一介质一行:计算总阻力 / 需求全压或扬程 / 设备校核)+ 选中介质的逐段明细、环路阻力项
    /// 与计算书全文。
    /// </para>
    /// <para>
    /// 与「大系统/小系统 计算结果」同规矩:**打开即算**(不用进来再点一次计算);
    /// 没拾取过系统时**不摆结果**,只给"去拾取"的指引。
    /// </para>
    /// </summary>
    public class HydraulicResultViewModel : ViewModelBase
    {
        private readonly HydraulicInputService _service;
        private readonly IHydraulicCalculator _calculator = new HydraulicCalculator();

        private HydraulicCoefficients _coefficients;
        private IList<HydraulicSummaryRowView> _rows = new List<HydraulicSummaryRowView>();
        private HydraulicSummaryRowView _selectedRow;
        private ResultTable _table;
        private IList<HydraulicSegmentResult> _segmentRows = new List<HydraulicSegmentResult>();
        private IList<HydraulicItemResult> _itemRows = new List<HydraulicItemResult>();
        private IList<HydraulicBranchResult> _branchRows = new List<HydraulicBranchResult>();
        private IList<HydraulicCurvePoint> _curveRows = new List<HydraulicCurvePoint>();
        private string _balanceNote = "";
        private string _detailTitle = "";
        private string _resultText = "";
        private string _status = "";
        private string _note = "";
        private string _pendingNote = "";
        private bool _hasAir;
        private bool _hasWater;

        public HydraulicResultViewModel()
            : this(null)
        {
        }

        public HydraulicResultViewModel(IDataRepository repository)
        {
            _service = new HydraulicInputService(repository, _calculator);
            CalculateCommand = new RelayCommand(Calculate);
            ExportCommand = new RelayCommand(Export, () => _selectedRow != null && _selectedRow.Result != null);

            Calculate();     // 打开即算(§4.9)
        }

        /// <summary>汇总行(风系统 / 水系统各一行,只列已拾取过的)。</summary>
        public IList<HydraulicSummaryRowView> Rows
        {
            get => _rows;
            private set => Set(ref _rows, value);
        }

        /// <summary>当前选中的介质行(变化时刷新下方明细)。</summary>
        public HydraulicSummaryRowView SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (!Set(ref _selectedRow, value)) return;
                RefreshDetail();
            }
        }

        /// <summary>选中介质的结果表(ResultTableView 绑定它;与计算书同源)。</summary>
        public ResultTable Table
        {
            get => _table;
            private set => Set(ref _table, value);
        }

        /// <summary>选中介质的逐段明细(含不在环路内的段,「环路」列标 ★)。</summary>
        public IList<HydraulicSegmentResult> SegmentRows
        {
            get => _segmentRows;
            private set => Set(ref _segmentRows, value);
        }

        /// <summary>选中介质的环路阻力项(末端 / 设备 / 出口动压)。</summary>
        public IList<HydraulicItemResult> ItemRows
        {
            get => _itemRows;
            private set => Set(ref _itemRows, value);
        }

        /// <summary>并联环路平衡(逐支路一行;没有拓扑数据时为空,界面提示"未做平衡分析")。</summary>
        public IList<HydraulicBranchResult> BranchRows
        {
            get => _branchRows;
            private set => Set(ref _branchRows, value);
        }

        /// <summary>系统阻力特性曲线点(50%~130% 设计流量)。</summary>
        public IList<HydraulicCurvePoint> CurveRows
        {
            get => _curveRows;
            private set => Set(ref _curveRows, value);
        }

        /// <summary>并联平衡与特性曲线的口径说明(界面显示;不假装算了工况点)。</summary>
        public string BalanceNote
        {
            get => _balanceNote;
            private set => Set(ref _balanceNote, value);
        }

        /// <summary>明细区标题。</summary>
        public string DetailTitle
        {
            get => _detailTitle;
            private set => Set(ref _detailTitle, value);
        }

        /// <summary>选中介质的计算书全文(仅由 ResultFormatter 生成)。</summary>
        public string ResultText
        {
            get => _resultText;
            private set => Set(ref _resultText, value);
        }

        /// <summary>顶部一句话摘要。</summary>
        public string SummaryTitle => _rows.Count == 0
            ? "水力计算结果(尚未从模型拾取过风系统 / 水系统)"
            : "已拾取 " + _rows.Count + " 个系统(" + (_hasAir ? "风系统" : "") + (_hasAir && _hasWater ? " + " : "") +
              (_hasWater ? "水系统" : "") + ")";

        /// <summary>选中介质的口径说明。</summary>
        public string Note
        {
            get => _note;
            private set => Set(ref _note, value);
        }

        /// <summary>待补 / 局限(红字)。</summary>
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

        /// <summary>重新读取已保存的管网并计算(别的窗保存过之后点它刷新)。</summary>
        public ICommand CalculateCommand { get; }

        /// <summary>导出**选中介质**的水力计算书。</summary>
        public ICommand ExportCommand { get; }

        // ================================================================== 实现

        private void Calculate()
        {
            try
            {
                var project = _service.LoadProject();
                _coefficients = project.Coefficients;

                var rows = new List<HydraulicSummaryRowView>();
                _hasAir = project.Air != null;
                _hasWater = project.Water != null;

                if (project.Air != null)
                    rows.Add(new HydraulicSummaryRowView(HydraulicKind.AirDuct, project.Air,
                        _calculator.Calculate(project.Air, _coefficients)));
                if (project.Water != null)
                    rows.Add(new HydraulicSummaryRowView(HydraulicKind.WaterPipe, project.Water,
                        _calculator.Calculate(project.Water, _coefficients)));

                Rows = rows;
                OnPropertyChanged(nameof(SummaryTitle));

                if (rows.Count == 0)
                {
                    // 没拾取过就不摆结果(不做兜底假结果)
                    Table = null;
                    SegmentRows = new List<HydraulicSegmentResult>();
                    ItemRows = new List<HydraulicItemResult>();
                    BranchRows = new List<HydraulicBranchResult>();
                    CurveRows = new List<HydraulicCurvePoint>();
                    BalanceNote = "";
                    ResultText = "";
                    DetailTitle = "";
                    Note = "";
                    PendingNote = "";
                    Status = "还没有从模型拾取过系统:请到「水力计算 → 风系统 / 水系统」窗点【从模型读取该系统…】," +
                             "选好构件后再回来。";
                    return;
                }

                SelectedRow = rows[0];
                Status = "已按保存的管网现算:" + SummaryTitle + "。选中汇总表某行可看该介质的逐段明细与计算书。";
            }
            catch (Exception ex)
            {
                Rows = new List<HydraulicSummaryRowView>();
                Table = null;
                SegmentRows = new List<HydraulicSegmentResult>();
                ItemRows = new List<HydraulicItemResult>();
                BranchRows = new List<HydraulicBranchResult>();
                CurveRows = new List<HydraulicCurvePoint>();
                BalanceNote = "";
                DetailTitle = "";
                ResultText = "";
                Note = "";
                PendingNote = "";
                Status = "计算失败: " + ex.Message;
            }
        }

        private void RefreshDetail()
        {
            var row = _selectedRow;
            if (row == null || row.Result == null) return;

            DetailTitle = "三、选中介质明细:" + row.KindName + " · " + row.SystemName +
                          "(总阻力 / 需求值见上方汇总表)";
            Table = ResultTable.ForHydraulic(row.Input, row.Result);
            SegmentRows = new List<HydraulicSegmentResult>(row.Result.Segments);
            ItemRows = new List<HydraulicItemResult>(row.Result.Items);
            BranchRows = new List<HydraulicBranchResult>(row.Result.Branches);
            CurveRows = new List<HydraulicCurvePoint>(row.Result.Curve);
            BalanceNote = row.Result.BalanceNote;
            Note = row.Result.Note;
            PendingNote = string.IsNullOrEmpty(row.Input.PendingNote)
                ? row.Result.PendingNote
                : row.Input.PendingNote + " " + row.Result.PendingNote;
            ResultText = ResultFormatter.FormatHydraulic(row.Input, row.Result, _coefficients);
            Status = "已选中「" + row.KindName + "」:管段 " + SegmentRows.Count + " 段、阻力项 " + ItemRows.Count +
                     " 项" + (row.Result.CriticalSegmentCount > 0
                         ? ",最不利环路 " + row.Result.CriticalSegmentCount + " 段"
                         : ",未判定最不利环路(按全部管段之和保守计入)") + "。";
        }

        private void Export()
        {
            try
            {
                var row = _selectedRow;
                if (row == null || row.Result == null)
                {
                    Status = "没有可导出的结果:请先在「水力计算」窗拾取系统。";
                    return;
                }

                var generator = new TextReportGenerator();
                string path = generator.SaveTextReport(
                    row.Kind == HydraulicKind.WaterPipe ? "水系统水力计算书" : "风系统水力计算书",
                    ResultFormatter.FormatHydraulic(row.Input, row.Result, _coefficients));
                Status = "计算书已生成: " + path;
            }
            catch (Exception ex)
            {
                Status = "导出失败: " + ex.Message;
            }
        }
    }
}

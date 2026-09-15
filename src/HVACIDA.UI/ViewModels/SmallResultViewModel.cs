using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 小系统「计算结果」窗 ViewModel(Ribbon「小系统 → 计算结果」)。
    /// <para>
    /// 需求 2.2.3.2 的小系统是**全站多套**的(AHU-A101 / AHU-A201 / EAF-A601 …),因此本窗做的是
    /// **全站汇总**而不是单系统结果:
    /// <see cref="SmallSystemInputService.LoadProject"/> 读整个小系统工程(含室外参数回填)→
    /// <see cref="SmallSystemSummaryService.Summarize"/> 逐系统各算一遍再汇总
    /// → <see cref="SummaryRows"/>(表格,逐系统一行)+ <see cref="Table"/>(全站合计表,交给 ResultTableView)。
    /// </para>
    /// <para>
    /// 选中表格某一行 → 只用**该行那套系统**现场重算(<see cref="SmallSystemLoadCalculator"/>),
    /// 于是房间明细 <see cref="RoomRows"/>、设备选型 <see cref="EquipmentRows"/>、计算书全文
    /// <see cref="ResultText"/> 三块一起刷新 —— 汇总表里的数字与单系统计算书的数字同源,不会分叉。
    /// </para>
    /// </summary>
    public class SmallResultViewModel : ViewModelBase
    {
        private readonly SmallSystemInputService _inputService;
        private readonly ISmallSystemLoadCalculator _calculator;
        private readonly SmallSystemSummaryService _summaryService;

        private SmallSystemProject _project = new SmallSystemProject();
        private SmallSystemSummary _summary = new SmallSystemSummary();
        private IList<SmallSummaryRowView> _summaryRows = new List<SmallSummaryRowView>();
        private SmallSummaryRowView _selectedRow;
        private ResultTable _table;
        private IList<SmallRoomResult> _roomRows = new List<SmallRoomResult>();
        private IList<SmallEquipmentSelection> _equipmentRows = new List<SmallEquipmentSelection>();
        private SmallSystemType _detailSystemType = SmallSystemType.AllAirOnceReturn;
        private string _detailTitle = "";
        private string _detailNote = "";
        private string _resultText = "";
        private string _status = "";
        private string _note = "";
        private string _pendingNote = "";
        private string _weatherNote = "";

        public SmallResultViewModel()
            : this(null)
        {
        }

        public SmallResultViewModel(IDataRepository repository)
        {
            _inputService = new SmallSystemInputService(repository);
            _calculator = new SmallSystemLoadCalculator();
            _summaryService = new SmallSystemSummaryService(_calculator);

            CalculateCommand = new RelayCommand(Calculate);
            ExportCommand = new RelayCommand(Export, () => _summary != null && _summary.Rows.Count > 0);

            Calculate();
        }

        // ================================================================== 全站汇总

        /// <summary>本次汇总的整个小系统工程(逐系统各算一遍的数据源)。</summary>
        public SmallSystemSummary Summary
        {
            get => _summary;
            private set => Set(ref _summary, value);
        }

        /// <summary>汇总表数据源:逐系统一行(零值列显示「—」,见 <see cref="SmallSummaryRowView"/>)。</summary>
        public IList<SmallSummaryRowView> SummaryRows
        {
            get => _summaryRows;
            private set => Set(ref _summaryRows, value);
        }

        /// <summary>汇总表当前选中的系统行(变化时刷新下方三块:房间明细 / 设备选型 / 计算书)。</summary>
        public SmallSummaryRowView SelectedSummaryRow
        {
            get => _selectedRow;
            set
            {
                if (!Set(ref _selectedRow, value)) return;
                RefreshDetail();
            }
        }

        /// <summary>全站合计结果表(ResultTableView 绑定它;与导出的汇总计算书同源)。</summary>
        public ResultTable Table
        {
            get => _table;
            private set => Set(ref _table, value);
        }

        /// <summary>顶部一句话摘要(套数 / 房间数 / 设备台数)。</summary>
        public string SummaryTitle => _summary == null || _summary.Rows.Count == 0
            ? "全站汇总(尚无已保存的小系统)"
            : "全站共 " + _summary.SystemCount + " 套小系统 / " + _summary.RoomCount + " 个房间·分区 / " +
              _summary.EquipmentCount + " 台设备";

        // ================================================================== 选中系统明细

        /// <summary>明细区当前展示的系统类型(界面据此重建房间明细表列)。</summary>
        public SmallSystemType DetailSystemType
        {
            get => _detailSystemType;
            private set => Set(ref _detailSystemType, value);
        }

        /// <summary>明细区标题(选中系统类型 + 编号)。</summary>
        public string DetailTitle
        {
            get => _detailTitle;
            private set => Set(ref _detailTitle, value);
        }

        /// <summary>选中系统的口径 / 待补说明(红字,必须可见)。</summary>
        public string DetailNote
        {
            get => _detailNote;
            private set => Set(ref _detailNote, value);
        }

        /// <summary>选中系统的房间 / 防烟分区明细(列由 <c>SmallRoomTable.ColumnsFor</c> 生成)。</summary>
        public IList<SmallRoomResult> RoomRows
        {
            get => _roomRows;
            private set => Set(ref _roomRows, value);
        }

        /// <summary>选中系统的设备选型行(系统代码 / 设备 / 系数 / 风量 / 冷量)。</summary>
        public IList<SmallEquipmentSelection> EquipmentRows
        {
            get => _equipmentRows;
            private set => Set(ref _equipmentRows, value);
        }

        /// <summary>选中系统的计算书全文(仅由 <see cref="ResultFormatter.FormatSmall"/> 生成)。</summary>
        public string ResultText
        {
            get => _resultText;
            private set => Set(ref _resultText, value);
        }

        /// <summary>选中系统的类型名(只读展示;取自 Core 统一用词)。</summary>
        public string SystemTypeName => ResultTable.SystemTypeName(DetailSystemType);

        // ================================================================== 说明 / 状态

        /// <summary>全站汇总口径说明(空工程时即"还没有保存过任何小系统…",取自 Core)。</summary>
        public string Note
        {
            get => _note;
            private set => Set(ref _note, value);
        }

        /// <summary>全站各系统的待补项 / 口径存疑说明汇总(顶部红字,必须可见)。</summary>
        public string PendingNote
        {
            get => _pendingNote;
            private set => Set(ref _pendingNote, value);
        }

        /// <summary>室外参数回填说明(来自「项目信息 → 气象参数」)。</summary>
        public string WeatherNote
        {
            get => _weatherNote;
            private set => Set(ref _weatherNote, value);
        }

        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        // ================================================================== 命令

        /// <summary>重新读取整个小系统工程并汇总(其它窗保存过新系统后点它刷新)。</summary>
        public ICommand CalculateCommand { get; }

        /// <summary>导出**全站汇总计算书**(汇总表 + 每套系统各自的完整计算书)。</summary>
        public ICommand ExportCommand { get; }

        // ================================================================== 实现

        private void Calculate()
        {
            try
            {
                _project = _inputService.LoadProject() ?? new SmallSystemProject();
                WeatherNote = _inputService.LastWeatherNote ?? "";

                var summary = _summaryService.Summarize(_project);
                Summary = summary;
                Note = summary.Note ?? "";

                var rows = new List<SmallSummaryRowView>();
                for (int i = 0; i < summary.Rows.Count; i++)
                {
                    // Summarize 按 project.Systems 的顺序逐套出行,故下标一一对应;
                    // 取回那一套输入是为了选中行时能现场重算(结构化房间表 / 设备表)。
                    var system = i < _project.Systems.Count ? _project.Systems[i] : null;
                    rows.Add(new SmallSummaryRowView(summary.Rows[i], system));
                }
                SummaryRows = rows;
                Table = ResultTable.ForSmallSystemSummary(summary);
                OnPropertyChanged(nameof(SummaryTitle));

                if (rows.Count == 0)
                {
                    // 一套系统都没保存过:汇总表为空、合计表为空,只给指引(提示语取 Core 的 SmallSystemSummary.Note)。
                    // **不做任何"兜底显示"** —— 没算过就不摆结果,避免把默认参数的假结果当成工程结果。
                    Table = null;
                    RoomRows = new List<SmallRoomResult>();
                    EquipmentRows = new List<SmallEquipmentSelection>();
                    ResultText = "";
                    DetailSystemType = SmallSystemType.AllAirOnceReturn;
                    DetailTitle = "";
                    DetailNote = "";
                    PendingNote = "";
                    Status = "还没有保存过任何小系统:请到「小系统」各按钮录入房间与参数并点【保 存 参 数】,再回来汇总。";
                    return;
                }

                PendingNote = AggregatePendingNotes(summary);
                _selectedRow = null;
                SelectedSummaryRow = rows[0];
                Status = "汇总完成:共 " + summary.SystemCount + " 套小系统(" + summary.TypeBreakdown +
                         ")。点汇总表任一行可看该系统的房间明细、设备选型与计算书。";
            }
            catch (Exception ex)
            {
                _project = new SmallSystemProject();
                Summary = new SmallSystemSummary();
                SummaryRows = new List<SmallSummaryRowView>();
                Table = null;
                RoomRows = new List<SmallRoomResult>();
                EquipmentRows = new List<SmallEquipmentSelection>();
                DetailNote = "";
                DetailTitle = "";
                Note = "";
                PendingNote = "";
                ResultText = "";
                Status = "汇总失败: " + ex.Message;
            }
        }

        /// <summary>选中行变化:用该行那套系统**现场重算**,三块内容(房间 / 设备 / 计算书)一起刷新。</summary>
        private void RefreshDetail()
        {
            var view = _selectedRow;
            if (view == null) return;

            string label = view.TypeName + (string.IsNullOrEmpty(view.SystemCode) ? "" : " " + view.SystemCode);

            // 优先用汇总时配对的输入对象(同类型同编号有多套时也不会取错);
            // 兜底再按「类型 + 编号」回项目里找。
            var system = view.System ?? _project.Find(view.SystemType, view.SystemCode);
            if (system == null)
            {
                DetailSystemType = view.SystemType;
                DetailTitle = "三、选中系统明细:" + label;
                DetailNote = view.Row.Note ?? "";
                RoomRows = new List<SmallRoomResult>();
                EquipmentRows = new List<SmallEquipmentSelection>();
                ResultText = view.Row.ResultText ?? "";
                Status = "已选中「" + label + "」:该项目数据已变化,请点【计 算】重新汇总。";
                return;
            }

            var result = _calculator.Calculate(system);
            DetailSystemType = system.SystemType;
            DetailTitle = "三、选中系统明细:" + label + "(面积 / 冷负荷 / 风量见上方汇总表)";
            DetailNote = result.PendingNote ?? "";
            RoomRows = new List<SmallRoomResult>(result.Rooms);
            EquipmentRows = new List<SmallEquipmentSelection>(result.Equipments);
            ResultText = ResultFormatter.FormatSmall(system, result);
            Status = "已选中「" + label + "」:房间/分区 " + RoomRows.Count + " 行、设备 " +
                     EquipmentRows.Count + " 台;计算书全文见下方。";
        }

        /// <summary>全站各系统的待补说明去重拼接(顶部红字提示)。</summary>
        private static string AggregatePendingNotes(SmallSystemSummary summary)
        {
            var parts = new List<string>();
            foreach (var row in summary.Rows)
            {
                if (string.IsNullOrEmpty(row.Note)) continue;
                if (parts.Contains(row.Note)) continue;
                parts.Add(row.Note);
            }
            return string.Join(" ", parts.ToArray());
        }

        /// <summary>
        /// 导出全站汇总计算书:汇总表文本(Core 的 <see cref="ResultTable.ToText"/>)+
        /// 每套系统各自的完整计算书(<see cref="SmallSystemSummaryRow.ResultText"/>,由 ResultFormatter 生成)。
        /// </summary>
        private void Export()
        {
            try
            {
                if (_summary == null || _summary.Rows.Count == 0)
                {
                    Status = "还没有可导出的汇总:请先在「小系统」各窗录入并点【保 存 参 数】。";
                    return;
                }

                var generator = new TextReportGenerator();
                string path = generator.SaveTextReport("小系统全站汇总计算书", BuildExportText());
                Status = "全站汇总计算书已生成(含 " + _summary.SystemCount + " 套系统): " + path;
            }
            catch (Exception ex)
            {
                Status = "导出失败: " + ex.Message;
            }
        }

        private string BuildExportText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("—— 全站汇总表(逐系统一行;「—」= 该系统不涉及该项) ——");
            sb.AppendLine(Table == null ? "" : Table.ToText());
            foreach (var row in _summary.Rows)
            {
                sb.AppendLine();
                sb.AppendLine("==================== " +
                              row.TypeName + (string.IsNullOrEmpty(row.SystemCode) ? "" : " " + row.SystemCode) +
                              " ====================");
                sb.AppendLine(row.ResultText ?? "");
            }
            return sb.ToString();
        }
    }
}

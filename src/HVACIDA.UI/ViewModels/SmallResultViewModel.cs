using System;
using System.Collections.Generic;
using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 小系统「计算结果」窗 ViewModel(Ribbon「小系统 → 计算结果」)。
    /// <para>
    /// 数据链路与「小系统负荷计算」窗完全一致:<see cref="SmallSystemInputService"/>.Load()(读
    /// small-system.xml 并回填室外干球/湿球)→ <see cref="SmallSystemLoadCalculator"/>.Calculate →
    /// 结果三张表(<see cref="Table"/> / <see cref="RoomRows"/> / <see cref="EquipmentRows"/>)
    /// + 计算书全文 <see cref="ResultText"/>。
    /// </para>
    /// </summary>
    public class SmallResultViewModel : ViewModelBase
    {
        private readonly SmallSystemInputService _inputService;
        private readonly ISmallSystemLoadCalculator _calculator;

        private SmallSystemInput _input;
        private SmallSystemResult _lastResult;
        private ResultTable _table;
        private IList<SmallRoomResult> _roomRows = new List<SmallRoomResult>();
        private IList<SmallEquipmentSelection> _equipmentRows = new List<SmallEquipmentSelection>();
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

            CalculateCommand = new RelayCommand(Calculate);
            ExportCommand = new RelayCommand(Export, () => _lastResult != null);

            Calculate();
        }

        /// <summary>本次计算使用的输入(来自 small-system.xml,含气象回填)。</summary>
        public SmallSystemInput Input
        {
            get => _input;
            private set => Set(ref _input, value);
        }

        /// <summary>系统类型名称(只读展示;取自 Core 统一用词)。</summary>
        public string SystemTypeName => _input == null ? "" : ResultTable.SystemTypeName(_input.SystemType);

        /// <summary>系统级结果表(ResultTableView 绑定它;与计算书同源)。</summary>
        public ResultTable Table
        {
            get => _table;
            private set => Set(ref _table, value);
        }

        /// <summary>房间 / 防烟分区明细(列由 <c>SmallRoomTable.ColumnsFor</c> 生成)。</summary>
        public IList<SmallRoomResult> RoomRows
        {
            get => _roomRows;
            private set => Set(ref _roomRows, value);
        }

        /// <summary>设备选型行(系统代码 / 设备 / 系数 / 风量 / 冷量)。</summary>
        public IList<SmallEquipmentSelection> EquipmentRows
        {
            get => _equipmentRows;
            private set => Set(ref _equipmentRows, value);
        }

        /// <summary>计算书全文(仅由 <see cref="ResultFormatter.FormatSmall"/> 生成)。</summary>
        public string ResultText
        {
            get => _resultText;
            private set => Set(ref _resultText, value);
        }

        /// <summary>系统级口径说明。</summary>
        public string Note
        {
            get => _note;
            private set => Set(ref _note, value);
        }

        /// <summary>待补项 / 口径存疑说明(界面必须可见)。</summary>
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

        public ICommand CalculateCommand { get; }

        public ICommand ExportCommand { get; }

        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        private void Calculate()
        {
            try
            {
                Input = _inputService.Load();
                WeatherNote = _inputService.LastWeatherNote ?? "";

                _lastResult = _calculator.Calculate(Input);
                Table = ResultTable.ForSmallSystem(Input, _lastResult);
                RoomRows = new List<SmallRoomResult>(_lastResult.Rooms);
                EquipmentRows = new List<SmallEquipmentSelection>(_lastResult.Equipments);
                Note = _lastResult.Note ?? "";
                PendingNote = _lastResult.PendingNote ?? "";
                ResultText = ResultFormatter.FormatSmall(Input, _lastResult);

                int roomCount = Input.Rooms == null ? 0 : Input.Rooms.Count;
                Status = "计算完成:已按 " + roomCount + " 个房间 / 分区行出量,结果见「系统结果 / 房间明细 / 设备选型」。" +
                         "如需改参数,请回「小系统负荷计算」窗修改并保存。";
            }
            catch (Exception ex)
            {
                _lastResult = null;
                Table = null;
                RoomRows = new List<SmallRoomResult>();
                EquipmentRows = new List<SmallEquipmentSelection>();
                Note = "";
                PendingNote = "";
                ResultText = "";
                Status = "计算失败: " + ex.Message;
            }
        }

        private void Export()
        {
            try
            {
                if (_lastResult == null) return;
                var generator = new TextReportGenerator();
                string path = generator.SaveTextReport(
                    "小系统计算书",
                    ResultFormatter.FormatSmall(Input, _lastResult));
                Status = "计算书已生成: " + path;
            }
            catch (Exception ex)
            {
                Status = "导出失败: " + ex.Message;
            }
        }
    }
}

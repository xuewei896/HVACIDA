using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 小系统负荷计算窗 ViewModel(需求 2.2.3.2;公式权威 =《小系统空调负荷、送排风、排烟计算公式.docx》)。
    /// <para>
    /// 界面结构:**多房间/分区录入**(<see cref="Rooms"/>,可增删改)+ **系统级参数**(<see cref="Input"/>)
    /// →【计算】→ 结果三张表:<see cref="Table"/>(系统级指标表,交给可复用控件 ResultTableView)、
    /// <see cref="RoomRows"/>(房间明细)、<see cref="EquipmentRows"/>(设备选型)。
    /// </para>
    /// <para>
    /// 纪律:本类**不做数值计算、不拼接结果字符串** —— 结果一律由 Core 的 <see cref="ResultTable"/> /
    /// <see cref="SmallRoomTable"/> 给出(界面表头也来自 <c>SmallRoomTable.InputColumnsFor</c> /
    /// <c>ColumnsFor</c>),计算书正文一律走 <see cref="ResultFormatter.FormatSmall"/>。
    /// 房间行在【计算】/【保存参数】前同步回 <see cref="Input"/>.Rooms(编辑期以 <see cref="Rooms"/> 为准)。
    /// </para>
    /// </summary>
    public class SmallSystemViewModel : ViewModelBase
    {
        private readonly ISmallSystemLoadCalculator _calculator;
        private readonly SmallSystemInputService _inputService;

        private SmallSystemInput _input;
        private readonly ObservableCollection<SmallRoomInput> _rooms;
        private SmallRoomInput _selectedRoom;
        private SmallSystemResult _lastResult;
        private ResultTable _table;
        private IList<SmallRoomResult> _roomRows = new List<SmallRoomResult>();
        private IList<SmallEquipmentSelection> _equipmentRows = new List<SmallEquipmentSelection>();
        private string _note = "";
        private string _pendingNote = "";
        private string _resultText = "";
        private string _status = "";
        private string _weatherNote = "";
        private int _roomSequence;

        public SmallSystemViewModel()
            : this(SmallSystemType.AllAirOnceReturn, null)
        {
        }

        /// <summary>按 Ribbon 选定的系统类型构造(六类小系统为 Ribbon 一级按钮,2026-09-11)。</summary>
        public SmallSystemViewModel(SmallSystemType systemType)
            : this(systemType, null)
        {
        }

        /// <summary>
        /// 通过仓库构造:类型一致时复用上次保存的参数(与「小系统 → 计算结果」共用一份数据);
        /// 室外干球/湿球温度由 <see cref="SmallSystemInputService"/> 从「项目信息 → 气象参数」回填。
        /// </summary>
        public SmallSystemViewModel(SmallSystemType systemType, IDataRepository repository)
        {
            _calculator = new SmallSystemLoadCalculator();
            _inputService = new SmallSystemInputService(repository);

            var saved = _inputService.Load();
            _input = saved != null && saved.SystemType == systemType
                ? saved
                : new SmallSystemInput { SystemType = systemType };
            if (_input.Rooms == null) _input.Rooms = new List<SmallRoomInput>();

            _rooms = new ObservableCollection<SmallRoomInput>(_input.Rooms);
            _roomSequence = _rooms.Count;
            _weatherNote = _inputService.LastWeatherNote ?? "";

            AddRoomCommand = new RelayCommand(AddRoom, () => !IsPressurization);
            RemoveRoomCommand = new RelayCommand(RemoveRoom, () => _selectedRoom != null);
            CalculateCommand = new RelayCommand(Calculate);
            SaveCommand = new RelayCommand(Save);
            ExportCommand = new RelayCommand(Export, () => _lastResult != null);
            ResetCommand = new RelayCommand(Reset);

            Calculate();
        }

        // ================================================================== 输入

        /// <summary>小系统输入(系统级参数 + 房间列表;房间列表界面侧以 <see cref="Rooms"/> 为准)。</summary>
        public SmallSystemInput Input
        {
            get => _input;
            private set => Set(ref _input, value);
        }

        /// <summary>当前系统类型名称(窗口只读展示,类型由 Ribbon 按钮决定;取自 Core 统一用词)。</summary>
        public string SystemTypeName => ResultTable.SystemTypeName(_input.SystemType);

        /// <summary>房间/分区列表(可增删改;计算/保存前同步回 <see cref="Input"/>)。</summary>
        public ObservableCollection<SmallRoomInput> Rooms => _rooms;

        /// <summary>录入表中当前选中的房间行(【删除行】的目标)。</summary>
        public SmallRoomInput SelectedRoom
        {
            get => _selectedRoom;
            set => Set(ref _selectedRoom, value);
        }

        /// <summary>室外参数回填说明(来自「项目信息 → 气象参数」)。</summary>
        public string WeatherNote
        {
            get => _weatherNote;
            private set => Set(ref _weatherNote, value);
        }

        /// <summary>
        /// 加压送风系统:**没有房间行**(按楼梯间门参数计算),界面据此折叠房间录入区。
        /// </summary>
        public bool IsPressurization => _input.SystemType == SmallSystemType.PressurizationSupply;

        /// <summary>是否显示房间录入区(加压送风为 false)。</summary>
        public bool ShowRoomArea => !IsPressurization;

        // ---- 参数区按系统类型显示适用项(系统类型在窗口生命周期内固定,无需通知) ----

        /// <summary>空调类(全空气一次回风 / 多联机+新风):用室内/送风/温差与负荷指标。</summary>
        private bool IsAirType =>
            _input.SystemType == SmallSystemType.AllAirOnceReturn ||
            _input.SystemType == SmallSystemType.VrfWithFreshAir;

        /// <summary>显示室外计算参数(干球/湿球;空调类要算焓湿过程)。</summary>
        public bool ShowOutdoorParams => IsAirType;

        /// <summary>显示室内与送风参数。</summary>
        public bool ShowIndoorParams => IsAirType;

        /// <summary>显示负荷指标(照明 / 壁面产湿 / 人员指标 / 每人新风量)。</summary>
        public bool ShowLoadIndexParams => IsAirType;

        /// <summary>全空气一次回风专有:送风温差、管道温升、露点相对湿度。</summary>
        public bool ShowAllAirParams => _input.SystemType == SmallSystemType.AllAirOnceReturn;

        /// <summary>多联机+新风专有:过渡季室外温度、室内相对湿度。</summary>
        public bool ShowVrfParams => _input.SystemType == SmallSystemType.VrfWithFreshAir;

        /// <summary>排烟类(排烟 / 送风排风排烟):显示排烟选型系数、补风比例、补风选型系数。</summary>
        public bool ShowSmokeParams =>
            _input.SystemType == SmallSystemType.SmokeExhaust ||
            _input.SystemType == SmallSystemType.SupplyExhaustSmoke;

        /// <summary>送风排风排烟专有:送风比例(送风 = 排风 × 比例)。</summary>
        public bool ShowSupplyFromExhaustRatio => _input.SystemType == SmallSystemType.SupplyExhaustSmoke;

        /// <summary>加压送风专有:门宽/门高/漏风风速/门数量/余压阀/压差。</summary>
        public bool ShowDoorParams => IsPressurization;

        // ================================================================== 结果

        /// <summary>系统级结果表(界面 ResultTableView 绑定它,与导出计算书同源)。</summary>
        public ResultTable Table
        {
            get => _table;
            private set => Set(ref _table, value);
        }

        /// <summary>房间明细(结果)表数据源(列由 <c>SmallRoomTable.ColumnsFor</c> 生成)。</summary>
        public IList<SmallRoomResult> RoomRows
        {
            get => _roomRows;
            private set => Set(ref _roomRows, value);
        }

        /// <summary>设备选型表数据源(系统代码 / 设备 / 系数 / 风量 / 冷量)。</summary>
        public IList<SmallEquipmentSelection> EquipmentRows
        {
            get => _equipmentRows;
            private set => Set(ref _equipmentRows, value);
        }

        /// <summary>系统级口径说明(结果区底部)。</summary>
        public string Note
        {
            get => _note;
            private set => Set(ref _note, value);
        }

        /// <summary>待补项 / 口径存疑说明(顶部提示条红字,必须可见)。</summary>
        public string PendingNote
        {
            get => _pendingNote;
            private set => Set(ref _pendingNote, value);
        }

        /// <summary>计算书全文(导出用;仅由 ResultFormatter 生成)。</summary>
        public string ResultText
        {
            get => _resultText;
            private set => Set(ref _resultText, value);
        }

        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        // ================================================================== 命令

        public ICommand AddRoomCommand { get; }

        public ICommand RemoveRoomCommand { get; }

        public ICommand CalculateCommand { get; }

        /// <summary>保存参数(供「小系统 → 计算结果」窗使用)。</summary>
        public ICommand SaveCommand { get; }

        public ICommand ExportCommand { get; }

        /// <summary>恢复公式文档默认参数(房间列表保留)。</summary>
        public ICommand ResetCommand { get; }

        // ================================================================== 实现

        /// <summary>把录入表的房间行同步回 <see cref="Input"/>.Rooms(计算/保存前必须调用)。</summary>
        private void SyncRoomsToInput()
        {
            if (_input == null) return;
            _input.Rooms = new List<SmallRoomInput>(_rooms);
        }

        private void AddRoom()
        {
            try
            {
                _roomSequence++;
                // 面积/层高留 0 由用户填写;屋顶面积默认随面积(Core 的 Create 口径)
                var room = SmallRoomInput.Create("房间" + _roomSequence, 0, 0);
                _rooms.Add(room);
                SelectedRoom = room;
                SyncRoomsToInput();
                Status = "已在录入表末尾添加 1 行,请填写面积 / 层高等参数,再点【计 算】。";
            }
            catch (Exception ex)
            {
                Status = "添加房间行失败: " + ex.Message;
            }
        }

        private void RemoveRoom()
        {
            try
            {
                var room = _selectedRoom;
                if (room == null) return;
                int index = _rooms.IndexOf(room);
                _rooms.Remove(room);
                SelectedRoom = _rooms.Count == 0 ? null : _rooms[Math.Min(index, _rooms.Count - 1)];
                SyncRoomsToInput();
                Status = "已删除房间行「" + (room.Name ?? "") + "」。";
            }
            catch (Exception ex)
            {
                Status = "删除房间行失败: " + ex.Message;
            }
        }

        private void Calculate()
        {
            try
            {
                SyncRoomsToInput();
                _lastResult = _calculator.Calculate(_input);

                Table = ResultTable.ForSmallSystem(_input, _lastResult);
                RoomRows = new List<SmallRoomResult>(_lastResult.Rooms);
                EquipmentRows = new List<SmallEquipmentSelection>(_lastResult.Equipments);
                Note = _lastResult.Note ?? "";
                PendingNote = _lastResult.PendingNote ?? "";
                ResultText = ResultFormatter.FormatSmall(_input, _lastResult);

                Status = _lastResult.Rooms.Count == 0 && !IsPressurization
                    ? "计算完成,但当前没有房间/分区行 —— 请在录入表点【添加行】并填写参数后重算。"
                    : "计算完成:结果见「系统结果 / 房间明细 / 设备选型」三张表,可导出计算书。";
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

        /// <summary>保存到 %AppData%\HVACIDA\small-system.xml(与「小系统 → 计算结果」窗共用)。</summary>
        private void Save()
        {
            try
            {
                SyncRoomsToInput();
                _inputService.Save(_input);
                Status = "参数与 " + _rooms.Count + " 个房间行已保存: " +
                         _inputService.StorageDirectory + "\\small-system.xml";
            }
            catch (Exception ex)
            {
                Status = "保存失败: " + ex.Message;
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
                    ResultFormatter.FormatSmall(_input, _lastResult));
                Status = "计算书已生成: " + path;
            }
            catch (Exception ex)
            {
                Status = "导出失败: " + ex.Message;
            }
        }

        /// <summary>恢复公式文档默认参数(房间列表保留);室外干球/湿球温度按气象参数重新回填。</summary>
        private void Reset()
        {
            try
            {
                _input.SetDocumentDefaults();
                _input.Rooms = new List<SmallRoomInput>(_rooms);
                _inputService.Sync(_input);
                WeatherNote = _inputService.LastWeatherNote ?? "";

                // Input 是同一实例,主动通知一次让所有 Input.* 绑定重新取值
                OnPropertyChanged(nameof(Input));
                Calculate();
                Status = "已恢复公式文档默认参数(房间列表保留)并重算。";
            }
            catch (Exception ex)
            {
                Status = "恢复默认失败: " + ex.Message;
            }
        }
    }
}

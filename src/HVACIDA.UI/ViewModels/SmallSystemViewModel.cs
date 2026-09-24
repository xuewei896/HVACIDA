using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
        private readonly ExcelReportGenerator _excel;

        private SmallSystemInput _input;
        private readonly ObservableCollection<SmallSystemBlockViewModel> _systems = new ObservableCollection<SmallSystemBlockViewModel>();
        private readonly ObservableCollection<SmallRoomInput> _emptyRooms = new ObservableCollection<SmallRoomInput>();
        private SmallSystemBlockViewModel _selectedSystem;
        private SmallSystemResult _lastResult;
        private ResultTable _table;
        private IList<SmallRoomResult> _roomRows = new List<SmallRoomResult>();
        private IList<SmallEquipmentSelection> _equipmentRows = new List<SmallEquipmentSelection>();
        private string _note = "";
        private string _pendingNote = "";
        private string _resultText = "";
        private string _status = "";
        private string _weatherNote = "";

        public SmallSystemViewModel()
            : this(SmallSystemType.AllAirOnceReturn, null, false)
        {
        }

        /// <summary>按 Ribbon 选定的系统类型构造(六类小系统为 Ribbon 一级按钮,2026-09-11)。</summary>
        public SmallSystemViewModel(SmallSystemType systemType)
            : this(systemType, null, false)
        {
        }

        /// <summary>
        /// 通过仓库构造:复用已保存的同类系统参数(与「小系统 → 计算结果」共用一份数据);
        /// 室外干球/湿球温度由 <see cref="SmallSystemInputService"/> 从「项目信息 → 气象参数」回填。
        /// 模型拾取按不可用处理(非 Revit 环境)。
        /// </summary>
        public SmallSystemViewModel(SmallSystemType systemType, IDataRepository repository)
            : this(systemType, repository, false)
        {
        }

        /// <summary>
        /// 通过仓库构造并声明**模型拾取是否可用**(Revit 命令侧按有无 ActiveUIDocument 传入)。
        /// <para>
        /// 输入由 VM 自己按「系统类型 + 系统编号」取(<see cref="SmallSystemInputService.Load(SmallSystemType, string)"/>):
        /// 同类型已有系统时复用它(含已保存的房间列表),没有则新建空壳便于首次录入;
        /// 系统编号可在窗内填写,点【保 存 参 数】时按「类型 + 编号」新增或覆盖(<see cref="SmallSystemInputService.Save"/>)。
        /// </para>
        /// </summary>
        public SmallSystemViewModel(SmallSystemType systemType, IDataRepository repository, bool pickAvailable)
            : this(systemType, repository, pickAvailable, null)
        {
        }

        /// <summary>
        /// <paramref name="reportsDirectory"/> 用于自检时把导出的计算书写到临时目录
        /// (为空则用 <c>%AppData%\HVACIDA\Reports</c>)。
        /// </summary>
        public SmallSystemViewModel(SmallSystemType systemType, IDataRepository repository, bool pickAvailable,
            string reportsDirectory)
        {
            _calculator = new SmallSystemLoadCalculator();
            _inputService = new SmallSystemInputService(repository);
            _excel = new ExcelReportGenerator(reportsDirectory);
            IsPickAvailable = pickAvailable;

            // 「计算参数」全窗共用:取本类型已保存系统里的一套参数作底(没有就用公式文档默认值)
            var project = _inputService.LoadProject();
            var savedList = new List<SmallSystemInput>();
            if (project != null && project.Systems != null)
            {
                foreach (var saved in project.Systems)
                {
                    if (saved != null && saved.SystemType == systemType) savedList.Add(saved);
                }
            }

            _input = savedList.Count > 0 ? savedList[0] : new SmallSystemInput { SystemType = systemType };
            _input.SystemType = systemType;
            _weatherNote = _inputService.LastWeatherNote ?? "";

            // 系统按窗口内顺序自动编号 1..N(2026-09-20 用户口径:删掉「系统编号」输入框,编号由顺序定)
            if (savedList.Count == 0)
            {
                AddSystem();
            }
            else
            {
                for (int i = 0; i < savedList.Count; i++) AddSystem(savedList[i].Rooms, savedList[i].SystemCode);
            }
            _selectedSystem = _systems[0];

            AddRoomCommand = new RelayCommand(AddRoom, () => !IsPressurization);
            RemoveRoomCommand = new RelayCommand(RemoveRoom, () => SelectedRoom != null);
            CalculateCommand = new RelayCommand(CalculateAndPersist);
            SaveCommand = new RelayCommand(Save);
            ExportCommand = new RelayCommand(Export, () => _lastResult != null);
            ExportExcelCommand = new RelayCommand(ExportExcel, () => _lastResult != null);
            ResetCommand = new RelayCommand(Reset);
            AddSystemCommand = new RelayCommand(() => AddSystem());
            RemoveSystemCommand = new RelayCommand(RemoveSystem, () => _systems.Count > 0);

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

        /// <summary>**多系统**(系统编号1、2、3…);每套带自己的房间列表与合计行。</summary>
        public ObservableCollection<SmallSystemBlockViewModel> Systems => _systems;

        /// <summary>当前系统(【添加系统/删除系统】与拾取的目标);默认第一套。</summary>
        public SmallSystemBlockViewModel SelectedSystem
        {
            get => _selectedSystem;
            set
            {
                if (value != null && !_systems.Contains(value)) return;
                if (Set(ref _selectedSystem, value))
                {
                    OnPropertyChanged(nameof(Rooms));
                    OnPropertyChanged(nameof(SelectedRoom));
                }
            }
        }

        public ICommand AddSystemCommand { get; }

        public ICommand RemoveSystemCommand { get; }

        /// <summary>当前系统的房间/分区列表(等价于 <see cref="SelectedSystem"/>.Rooms)。</summary>
        public ObservableCollection<SmallRoomInput> Rooms =>
            _selectedSystem == null ? _emptyRooms : _selectedSystem.Rooms;

        /// <summary>当前系统里选中的房间行(【删除行】的目标)。</summary>
        public SmallRoomInput SelectedRoom
        {
            get => _selectedSystem == null ? null : _selectedSystem.SelectedRoom;
            set
            {
                if (_selectedSystem != null) _selectedSystem.SelectedRoom = value;
                OnPropertyChanged();
            }
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

        /// <summary>
        /// 房间表是否用「全空气一次回风」**参考表口径**(2026-09-24 用户提供的
        /// 《全空气一次回风系统计算参数展示格式.xlsx》:23 列 = 序号 + 名称 + 录入项 + 逐房间计算值,前段可编辑后段只读)。
        /// 其余五类维持原录入表。
        /// </summary>
        public bool UseReferenceRoomTable => _input.SystemType == SmallSystemType.AllAirOnceReturn;

        /// <summary>除全空气外的五类:仍用原「房间 / 分区录入」表。</summary>
        public bool UsePlainRoomTable => !UseReferenceRoomTable;

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

        /// <summary>导出 **Excel(.xlsx)** 计算书(系统结果 + 房间明细 + 设备选型 + 口径与待补)。</summary>
        public ICommand ExportExcelCommand { get; }

        /// <summary>恢复公式文档默认参数(房间列表保留)。</summary>
        public ICommand ResetCommand { get; }

        // ================================================================== 模型拾取

        /// <summary>
        /// 模型拾取是否可用(Revit 命令侧传入)。界面据此启用/禁用两个拾取按钮;
        /// 非 Revit 环境(自检、单测)为 false,按钮呈灰。
        /// </summary>
        public bool IsPickAvailable { get; private set; }

        /// <summary>【从模型拾取空间…】已被请求(窗口已关闭,命令层据此执行拾取)。</summary>
        public bool PickSpacesRequested { get; private set; }

        /// <summary>【拾取墙体求外墙总长…】已被请求(窗口已关闭,命令层据此执行拾取)。</summary>
        public bool PickWallRequested { get; private set; }

        /// <summary>
        /// 请求拾取模型空间(需求:房间参数由用户依次选取模型空间)。
        /// <para>
        /// 为什么不在这里直接调 Revit API:WPF <c>ShowDialog()</c> 会在 Win32 层禁用 Revit 主窗,
        /// 模态期间模型点不动,<c>Hide()</c> 也不恢复 Owner —— 必须让模态循环真正结束(窗口 <c>Close()</c>),
        /// 由命令层拾取后再用**同一个 ViewModel** 重开窗(用户已填内容不丢,与「公共区参数」窗同构)。
        /// </para>
        /// </summary>
        public void RequestPickSpaces()
        {
            PickSpacesRequested = true;
        }

        /// <summary>请求拾取墙体求与土壤接触外墙总长(应先选中房间行,窗口侧已判空)。</summary>
        public void RequestPickWall()
        {
            PickWallRequested = true;
        }

        /// <summary>命令层执行完拾取后清空两个请求标记。</summary>
        public void ClearPickRequests()
        {
            PickSpacesRequested = false;
            PickWallRequested = false;
        }

        /// <summary>由命令层写入状态提示(用户在模型里 Esc 取消拾取等窗口外发生的情况)。</summary>
        public void SetStatus(string text)
        {
            Status = text ?? "";
        }

        /// <summary>
        /// 命令层拾取空间后的回填:**追加**房间行(同名房间跳过)。
        /// 名称 / 面积 / 层高取自空间快照,屋顶面积默认与面积相同(F27 默认 = C27),
        /// 设备冷负荷取系统默认 1000 W,外墙长度留 0(由【拾取墙体】按钮填),
        /// 换气次数留 0(= 按房间类型取默认值)。
        /// </summary>
        public void ApplyPickedSpaces(IList<SpaceSnapshot> spaces, string note)
        {
            try
            {
                if (spaces == null || spaces.Count == 0)
                {
                    Status = "已取消拾取空间;房间列表未变。";
                    return;
                }

                var block = _selectedSystem ?? (_systems.Count > 0 ? _systems[0] : null);
                if (block == null)
                {
                    Status = "请先添加一套系统,再拾取空间。";
                    return;
                }

                int added = 0;
                int skipped = 0;
                foreach (var space in spaces)
                {
                    if (space == null) continue;

                    string name = string.IsNullOrEmpty(space.Name) ? (space.Number ?? "") : space.Name;
                    if (string.IsNullOrEmpty(name)) name = "房间" + (block.RoomSequence + added + 1);

                    bool exists = false;
                    foreach (var room in block.Rooms)
                    {
                        if (room == null) continue;
                        if (string.Equals((room.Name ?? "").Trim(), name.Trim(), StringComparison.Ordinal))
                        {
                            exists = true;
                            break;
                        }
                    }
                    if (exists)
                    {
                        skipped++;
                        continue;
                    }

                    var picked = SmallRoomInput.Create(name, space.AreaM2, space.HeightM);
                    picked.RoofAreaM2 = space.AreaM2;
                    picked.EquipmentCoolingW = HVACIDA.Core.Utils.HvacConstants.SmallEquipmentCoolingW;
                    picked.WallLengthM = 0;
                    picked.AirChangePerHour = 0;
                    block.Rooms.Add(picked);
                    added++;
                }

                block.RoomSequence = block.Rooms.Count;
                Calculate();
                Status = "已把拾取到的空间追加到「" + block.Title + "」:新增 " + added + " 个、跳过 " + skipped + " 个同名" +
                         (string.IsNullOrEmpty(note) ? "" : "(" + note + ")") +
                         ";面积 / 层高 / 屋顶面积已按空间填入,请核对后点【计 算】(会同时保存)或【确 定】。";
            }
            catch (Exception ex)
            {
                Status = "拾取空间回填失败: " + ex.Message;
            }
        }

        /// <summary>
        /// 命令层拾取墙体后的回填:把**当前选中房间行**的与土壤接触外墙长度设为所选墙体长度之和
        /// (需求原文:可选取多个墙体,自动获取墙体属性的长度值之和)。
        /// </summary>
        public void ApplyPickedWallLength(double lengthM, string note)
        {
            try
            {
                var room = SelectedRoom;
                if (room == null)
                {
                    Status = "请先在房间表里选中一行再拾取墙体。";
                    return;
                }

                room.WallLengthM = lengthM;
                string name = room.Name ?? "";
                Calculate();
                Status = "已把房间「" + name + "」的与土壤接触外墙长度设为 " +
                         lengthM.ToString("0.##") + " m(所选墙体长度之和)" +
                         (string.IsNullOrEmpty(note) ? "" : "(" + note + ")") +
                         ";请核对后点【计 算】(会同时保存)或【确 定】。";
            }
            catch (Exception ex)
            {
                Status = "拾取墙体回填失败: " + ex.Message;
            }
        }

        // ================================================================== 实现

        /// <summary>加一套系统(编号默认取"最小未占用整数",也可由用户随后改写;可选带上已保存的房间列表)。</summary>
        private SmallSystemBlockViewModel AddSystem(IEnumerable<SmallRoomInput> rooms, string code = null)
        {
            var block = new SmallSystemBlockViewModel(
                string.IsNullOrEmpty(code) ? NextDefaultCode() : code);
            if (rooms != null) block.LoadRooms(rooms);
            _systems.Add(block);
            OnPropertyChanged(nameof(Systems));
            if (_selectedSystem == null) SelectedSystem = block;
            return block;
        }

        /// <summary>默认系统编号:当前未被占用的最小正整数(用户可改)。</summary>
        private string NextDefaultCode()
        {
            for (int i = 1; i <= 999; i++)
            {
                string candidate = i.ToString(CultureInfo.InvariantCulture);
                bool used = false;
                foreach (var block in _systems)
                {
                    if (string.Equals((block.Code ?? "").Trim(), candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        used = true;
                        break;
                    }
                }
                if (!used) return candidate;
            }
            return (_systems.Count + 1).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>【添加系统】:再加一套(系统编号顺延)。</summary>
        public SmallSystemBlockViewModel AddSystem()
        {
            var block = AddSystem(null);
            Calculate();
            Status = "已添加「" + block.Title + "」——请在它下面点【添加行】或【从模型拾取空间…】录入房间;点【确 定】保存。";
            return block;
        }

        /// <summary>
        /// 【删除系统】:删掉当前系统(2026-09-20 用户口径「用户添加系统后,原默认系统就可以删掉了」——
        /// 不再要求"至少保留一套");若删到一套不剩,自动补一套空白系统,窗口始终可用。
        /// </summary>
        private void RemoveSystem()
        {
            try
            {
                var block = _selectedSystem ?? (_systems.Count > 0 ? _systems[_systems.Count - 1] : null);
                if (block == null) return;

                int index = _systems.IndexOf(block);
                _systems.Remove(block);

                if (_systems.Count == 0)
                {
                    var fresh = AddSystem(null);
                    SelectedSystem = fresh;
                    Calculate();
                    Status = "已删除「" + block.Title + "」;本窗至少需要一套系统,已自动新建一套空白系统。";
                    return;
                }

                SelectedSystem = _systems[Math.Min(index, _systems.Count - 1)];
                Calculate();
                Status = "已删除「" + block.Title + "」,剩余 " + _systems.Count + " 套(点【确 定】或【计 算】才落盘)。";
            }
            catch (Exception ex)
            {
                Status = "删除系统失败: " + ex.Message;
            }
        }

        private void AddRoom()
        {
            try
            {
                var block = _selectedSystem;
                if (block == null)
                {
                    Status = "请先添加一套系统。";
                    return;
                }

                var room = block.AddRoom();
                SelectedRoom = room;
                Calculate();
                Status = "已在「" + block.Title + "」末尾添加 1 行,请填写面积 / 层高等参数,再点【计 算】。";
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
                var block = _selectedSystem;
                if (block == null) return;

                var room = block.RemoveRoom();
                if (room == null) return;

                Calculate();
                Status = "已从「" + block.Title + "」删除房间行「" + (room.Name ?? "") + "」。";
            }
            catch (Exception ex)
            {
                Status = "删除房间行失败: " + ex.Message;
            }
        }

        /// <summary>
        /// 【计 算】按钮入口:**先按「系统类型 + 系统编号」落盘,再计算**。
        /// <para>
        /// 为什么"计算"要顺带保存:small-systems.xml 是各录入窗与「小系统 → 计算结果」窗**唯一**的数据源。
        /// 只算不存,用户点完【计 算】再打开「计算结果」窗,那边汇总到的仍是**上一次保存**的参数 ——
        /// 同一份输入出现两个数(正是本工程一直在消除的口径分叉)。故按钮入口一律"算前先存"。
        /// </para>
        /// <para>
        /// 打开窗、拾取回填、恢复默认等**内部重算**仍走 <see cref="Calculate()"/>(不写盘):
        /// 只是打开看一眼、或拾取后还没核对,不会覆盖已保存的系统。
        /// </para>
        /// </summary>
        private void CalculateAndPersist()
        {
            string saveNote = SaveAll();
            Calculate();
            Status = saveNote + " " + Status;
        }

        /// <summary>
        /// 把所有**有房间行**的系统按「系统类型 + 系统编号」落盘(空系统不落盘,避免「计算结果」窗里留空系统;
        /// 加压送风没有房间行,照常保存)。返回给状态栏的说明。
        /// </summary>
        public string SaveAll()
        {
            string note;
            SaveAllCore(out note);
            return note;
        }

        /// <summary>【确 定】用:保存全部系统;返回是否成功(失败/编号有问题时不关窗,状态栏给原因)。</summary>
        public bool TrySaveAll()
        {
            string note;
            bool ok = SaveAllCore(out note);
            Status = note;
            return ok;
        }

        /// <summary>
        /// 保存实现:**先校验系统编号**(非空、同类型内不重复 —— 存储按「类型 + 编号」覆盖,重复会互相覆盖),
        /// 再逐套落盘。空系统(没有房间行且不是加压送风)不落盘,避免「计算结果」窗里留空系统。
        /// </summary>
        private bool SaveAllCore(out string note)
        {
            // 1) 编号校验(用户可自填编号,重复/为空必须当场拦住,不能静默互相覆盖)
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var block in _systems)
            {
                string code = (block.Code ?? "").Trim();
                if (code.Length == 0)
                {
                    note = "系统编号不能为空:请给「" + block.Title + "」填一个编号(如 1 / AHU-A101)后再保存。";
                    return false;
                }
                if (!seen.Add(code))
                {
                    note = "系统编号重复:「" + code + "」出现两次 —— 保存按「系统类型 + 编号」覆盖,请改成不同编号。";
                    return false;
                }
                block.Code = code;                       // 顺手去掉首尾空格
            }

            // 2) 逐套计算后**整体替换**本类型的系统:窗内每一套都保存
            //    —— 包括用户刚【添加系统】、还没来得及填房间的那一套(2026-09-20 用户口径:
            //      「点击确定按钮后,要保存新增和既有的系统」;此前"空系统不落盘"会把新增系统丢掉)
            try
            {
                var toSave = new List<SmallSystemInput>();
                foreach (var block in _systems)
                {
                    block.Calculate(_calculator, _input);          // 共用计算参数 + 本系统房间
                    toSave.Add(block.SystemInput);
                }

                int total = _inputService.ReplaceAll(_input.SystemType, toSave);
                note = "本次计算已同时保存 " + toSave.Count + " 套系统(当前工程共 " + total + " 套小系统)。";
                return true;
            }
            catch (Exception ex)
            {
                note = "⚠ 参数保存失败(" + ex.Message + "),本次结果仅存在于本窗。";
                return false;
            }
        }

        /// <summary>导出 Excel(.xlsx)计算书(当前系统):系统结果 + 房间明细 + 设备选型 + 口径与待补。</summary>
        private void ExportExcel()
        {
            try
            {
                var block = _selectedSystem ?? (_systems.Count > 0 ? _systems[0] : null);
                if (block == null || block.Result == null)
                {
                    Status = "还没有可导出的结果:请先点【计 算】。";
                    return;
                }

                var workbook = SmallSystemExcelExporter.BuildSystem(block.SystemInput, block.Result);
                string path = _excel.SaveWorkbook("小系统计算书_" + SystemTypeName + "_系统编号" + block.Code, workbook);
                Status = "Excel 计算书已生成(" + workbook.SheetCount + " 个工作表): " + path;
            }
            catch (Exception ex)
            {
                Status = "导出 Excel 失败: " + ex.Message;
            }
        }

        /// <summary>内部重算(不写盘):构造函数、拾取回填、增删系统/房间、恢复默认走这里。刷新各系统合计行。</summary>
        private void Calculate()
        {
            try
            {
                foreach (var block in _systems) block.Calculate(_calculator, _input);

                var current = _selectedSystem ?? (_systems.Count > 0 ? _systems[0] : null);
                if (current == null)
                {
                    Table = null;
                    RoomRows = new List<SmallRoomResult>();
                    EquipmentRows = new List<SmallEquipmentSelection>();
                    Note = "";
                    PendingNote = "";
                    ResultText = "";
                    Status = "还没有系统。";
                    return;
                }

                _lastResult = current.Result;
                Table = ResultTable.ForSmallSystem(current.SystemInput, _lastResult);
                RoomRows = new List<SmallRoomResult>(_lastResult.Rooms);
                EquipmentRows = new List<SmallEquipmentSelection>(_lastResult.Equipments);
                Note = _lastResult.Note ?? "";
                PendingNote = _lastResult.PendingNote ?? "";
                ResultText = ResultFormatter.FormatSmall(current.SystemInput, _lastResult);

                int rooms = 0;
                foreach (var block in _systems) rooms += block.Rooms.Count;

                Status = rooms == 0 && !IsPressurization
                    ? "计算完成,但还没有房间/分区行 —— 请在系统里点【添加行】或【从模型拾取空间…】后重算。"
                    : "计算完成:" + _systems.Count + " 套系统的合计行已刷新,可导出计算书或点【确 定】保存。";
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

        /// <summary>保存全部系统(点【保 存】/命令入口;【确 定】走 <see cref="TrySaveAll"/>)。</summary>
        private void Save()
        {
            Status = SaveAll();
        }

        private void Export()
        {
            try
            {
                var block = _selectedSystem ?? (_systems.Count > 0 ? _systems[0] : null);
                if (block == null || block.Result == null) return;

                var generator = new TextReportGenerator();
                string path = generator.SaveTextReport(
                    "小系统计算书_系统编号" + block.Code,
                    ResultFormatter.FormatSmall(block.SystemInput, block.Result));
                Status = "计算书已生成: " + path;
            }
            catch (Exception ex)
            {
                Status = "导出失败: " + ex.Message;
            }
        }

        /// <summary>恢复公式文档默认参数(**各系统房间列表保留**);室外干球/湿球温度按气象参数重新回填。</summary>
        private void Reset()
        {
            try
            {
                _input.SetDocumentDefaults();
                _inputService.Sync(_input);
                WeatherNote = _inputService.LastWeatherNote ?? "";

                // Input 是同一实例,主动通知一次让所有 Input.* 绑定重新取值
                OnPropertyChanged(nameof(Input));
                Calculate();
                Status = "已恢复公式文档默认参数(各系统房间列表保留)并重算。";
            }
            catch (Exception ex)
            {
                Status = "恢复默认失败: " + ex.Message;
            }
        }
    }
}

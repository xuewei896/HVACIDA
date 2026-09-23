using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;
using HVACIDA.Core.Utils;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 小系统窗里的**一套系统**(2026-09-20 用户口径:一个模型会有多个系统,按「系统编号1、2、3…」纵向排列)。
    /// <para>
    /// 每套系统 = 自己的**房间/分区列表** + **合计行**(总送风量 / 总回风量 / 总制冷量;
    /// 该类型另有意义的量——总新风量 / 总排风量 / 总排烟量 / 总补风量——非 0 时一并列出)。
    /// 「计算参数」是**全窗共用**的,由 <see cref="SmallSystemViewModel"/> 持有,
    /// 计算/保存前用 <see cref="SmallSystemInput.CopyParametersTo"/> 复制进本系统的
    /// <see cref="SystemInput"/> —— 所以本类不重复保存一份设计参数。
    /// </para>
    /// </summary>
    public class SmallSystemBlockViewModel : ViewModelBase
    {
        private readonly ObservableCollection<SmallRoomInput> _rooms = new ObservableCollection<SmallRoomInput>();
        private SmallRoomInput _selectedRoom;
        private string _totalsText = "";
        private string _code = "1";
        private int _roomSequence;

        public SmallSystemBlockViewModel(string code)
        {
            _code = string.IsNullOrEmpty(code) ? "1" : code;
            AddRoomCommand = new RelayCommand(() => AddRoom());
            RemoveRoomCommand = new RelayCommand(() => RemoveRoom(), () => _selectedRoom != null);
        }

        /// <summary>系统编号(= 保存时的 SystemCode)。2026-09-20:改成**用户可输入的文本框**,默认按顺序给 1、2、3…</summary>
        public string Code
        {
            get => _code;
            set
            {
                if (Set(ref _code, value ?? "")) OnPropertyChanged(nameof(Title));
            }
        }

        /// <summary>【添加行】/【删除行】(本系统的房间表)。</summary>
        public ICommand AddRoomCommand { get; }

        public ICommand RemoveRoomCommand { get; }

        /// <summary>界面上显示的名字:系统编号 + 用户输入的编号(状态栏文案用)。</summary>
        public string Title => "系统编号" + _code;

        /// <summary>本系统的房间 / 分区行。</summary>
        public ObservableCollection<SmallRoomInput> Rooms => _rooms;

        /// <summary>本系统当前选中的房间行(【删除行】/【拾取墙体】的目标)。</summary>
        public SmallRoomInput SelectedRoom
        {
            get => _selectedRoom;
            set => Set(ref _selectedRoom, value);
        }

        /// <summary>本系统的合计行(总送风量 / 总回风量 / 总制冷量;类型相关的量非 0 时追加)。</summary>
        public string TotalsText
        {
            get => _totalsText;
            private set => Set(ref _totalsText, value);
        }

        /// <summary>本系统的输入(共用计算参数 + 本系统房间);计算/保存时刷新。</summary>
        public SmallSystemInput SystemInput { get; private set; }

        /// <summary>本系统的计算结果(供合计行与导出用)。</summary>
        public SmallSystemResult Result { get; private set; }

        /// <summary>本系统已用过的房间序号(自动命名"房间N"用)。</summary>
        public int RoomSequence
        {
            get => _roomSequence;
            set => _roomSequence = value;
        }

        /// <summary>装载已保存的房间列表(打开窗时用)。</summary>
        public void LoadRooms(IEnumerable<SmallRoomInput> rooms)
        {
            _rooms.Clear();
            if (rooms != null)
            {
                foreach (var room in rooms)
                {
                    if (room != null) _rooms.Add(room);
                }
            }
            _roomSequence = _rooms.Count;
            SelectedRoom = _rooms.Count > 0 ? _rooms[0] : null;
        }

        /// <summary>加一行房间(面积/层高留 0 由用户填;屋顶面积默认随面积)。</summary>
        public SmallRoomInput AddRoom()
        {
            _roomSequence++;
            var room = SmallRoomInput.Create("房间" + _roomSequence, 0, 0);
            _rooms.Add(room);
            SelectedRoom = room;
            return room;
        }

        /// <summary>删除当前选中的房间行。</summary>
        public SmallRoomInput RemoveRoom()
        {
            var room = _selectedRoom;
            if (room == null) return null;

            int index = _rooms.IndexOf(room);
            _rooms.Remove(room);
            SelectedRoom = _rooms.Count == 0 ? null : _rooms[Math.Min(index, _rooms.Count - 1)];
            return room;
        }

        /// <summary>
        /// 用**共用计算参数** + 本系统房间算一次,并刷新合计行。
        /// 结果对象与输入对象留在本块上,供保存与导出使用。
        /// </summary>
        public SmallSystemResult Calculate(ISmallSystemLoadCalculator calculator, SmallSystemInput sharedParams)
        {
            var input = new SmallSystemInput { SystemType = sharedParams.SystemType, SystemCode = Code };
            sharedParams.CopyParametersTo(input);
            input.SystemCode = Code;                                    // 编号以本块为准(共用参数里不带编号)
            input.Rooms = new List<SmallRoomInput>(_rooms);
            SystemInput = input;

            Result = calculator.Calculate(input);
            TotalsText = BuildTotals(Result);
            return Result;
        }

        private static string BuildTotals(SmallSystemResult r)
        {
            if (r == null) return "总送风量 —     ·     总回风量 —     ·     总制冷量 —";

            var parts = new List<string>
            {
                "总送风量 " + r.TotalSupplyM3H.ToString("N0") + " m³/h",
                "总回风量 " + r.TotalReturnM3H.ToString("N0") + " m³/h",
                "总制冷量 " + r.TotalCoolingKw.ToString("N2") + " kW"
            };

            // 该类型另有意义的量:非 0 才列(排风/排烟/加压等系统没有送风与制冷量,列 0 会误导)
            if (r.TotalFreshAirM3H > 0) parts.Add("总新风量 " + r.TotalFreshAirM3H.ToString("N0") + " m³/h");
            if (r.TotalExhaustM3H > 0) parts.Add("总排风量 " + r.TotalExhaustM3H.ToString("N0") + " m³/h");
            if (r.TotalSmokeM3H > 0) parts.Add("总排烟量 " + r.TotalSmokeM3H.ToString("N0") + " m³/h");
            if (r.TotalMakeupAirM3H > 0) parts.Add("总补风量 " + r.TotalMakeupAirM3H.ToString("N0") + " m³/h");

            return string.Join("     ·     ", parts.ToArray());
        }
    }
}

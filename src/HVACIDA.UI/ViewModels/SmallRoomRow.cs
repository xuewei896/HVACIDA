using HVACIDA.Core.Models;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 「全空气一次回风」房间表的一行(2026-09-24 用户提供的参考表口径):
    /// **序号 + 房间名称 + 录入项(可编辑)+ 逐房间计算值(只读)**,一张表列全每个房间的数据。
    /// <para>
    /// 录入项(名称 / 面积 / 层高 / 外墙长度 / 屋顶面积 / 设备冷负荷 / 人数 / 换气次数)直接读写
    /// <see cref="SmallRoomInput"/> —— 改了以后点【计 算】或【确 定】参与重算与落盘;
    /// 计算项取自本次计算的 <see cref="SmallRoomResult"/>(Core 算的,界面不自己算)。
    /// </para>
    /// <para>
    /// 采用"计算后重建整个集合"的刷新方式(不做逐属性通知):行对象轻量、顺序稳定,
    /// 表头与列顺序由 Core 的 <c>SmallRoomTable.ReferenceColumnsForAllAir()</c> 定义。
    /// </para>
    /// </summary>
    public class SmallRoomRow
    {
        private readonly SmallRoomInput _input;
        private readonly SmallRoomResult _result;

        public SmallRoomRow(int index, SmallRoomInput input, SmallRoomResult result)
        {
            Index = index;
            _input = input;
            _result = result;
        }

        // ---------- 可编辑(写回 SmallRoomInput) ----------

        /// <summary>序号(只读,行序)。</summary>
        public int Index { get; private set; }

        public string Name
        {
            get => _input == null ? "" : (_input.Name ?? "");
            set { if (_input != null) _input.Name = value ?? ""; }
        }

        public double AreaM2
        {
            get => _input == null ? 0 : _input.AreaM2;
            set { if (_input != null) _input.AreaM2 = value; }
        }

        public double HeightM
        {
            get => _input == null ? 0 : _input.HeightM;
            set { if (_input != null) _input.HeightM = value; }
        }

        public double WallLengthM
        {
            get => _input == null ? 0 : _input.WallLengthM;
            set { if (_input != null) _input.WallLengthM = value; }
        }

        public double RoofAreaM2
        {
            get => _input == null ? 0 : _input.RoofAreaM2;
            set { if (_input != null) _input.RoofAreaM2 = value; }
        }

        public double EquipmentCoolingW
        {
            get => _input == null ? 0 : _input.EquipmentCoolingW;
            set { if (_input != null) _input.EquipmentCoolingW = value; }
        }

        public double Occupants
        {
            get => _input == null ? 0 : _input.Occupants;
            set { if (_input != null) _input.Occupants = value; }
        }

        public double AirChangePerHour
        {
            get => _input == null ? 0 : _input.AirChangePerHour;
            set { if (_input != null) _input.AirChangePerHour = value; }
        }

        // ---------- 只读(本次计算结果;未算时给 0) ----------

        public double LightingCoolingW => Value(r => r.LightingCoolingW);
        public double PeopleCoolingW => Value(r => r.PeopleCoolingW);
        public double PeopleMoistureGH => Value(r => r.PeopleMoistureGH);
        public double StructureMoistureGH => Value(r => r.StructureMoistureGH);
        public double TotalCoolingKw => Value(r => r.TotalCoolingKw);
        public double TotalMoistureGps => Value(r => r.TotalMoistureGps);
        public double HeatVentilationM3H => Value(r => r.HeatVentilationM3H);
        public double AchVentilationM3H => Value(r => r.AchVentilationM3H);
        public double ActualVentilationM3H => Value(r => r.ActualVentilationM3H);
        public double ActualAch => Value(r => r.ActualAch);
        public double FreshAirPersonM3H => Value(r => r.FreshAirPersonM3H);
        public double FreshAirSystemM3H => Value(r => r.FreshAirSystemM3H);
        public double UnitCoolingKw => Value(r => r.UnitCoolingKw);
        public double ReturnAirM3H => Value(r => r.ReturnAirM3H);

        private double Value(System.Func<SmallRoomResult, double> pick)
        {
            return _result == null ? 0 : pick(_result);
        }
    }
}

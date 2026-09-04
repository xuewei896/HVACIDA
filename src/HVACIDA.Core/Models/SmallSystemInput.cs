using System;

namespace HVACIDA.Core.Models
{
    /// <summary>小系统类型(需求文档 2.2.3.2)。</summary>
    public enum SmallSystemType
    {
        /// <summary>全空气一次回风系统(管理/设备用房等)</summary>
        AllAirOnceReturn,

        /// <summary>多联机 + 新风系统</summary>
        VrfWithFreshAir,

        /// <summary>排风系统(环控机房通风)</summary>
        ExhaustVentilation,

        /// <summary>排风系统(卫生间排风)</summary>
        ExhaustToilet,

        /// <summary>排烟系统</summary>
        SmokeExhaust,

        /// <summary>送风排风排烟系统</summary>
        SupplyExhaustSmoke,

        /// <summary>加压送风系统</summary>
        PressurizationSupply
    }

    /// <summary>
    /// 小系统负荷计算输入(需求文档 2.2.3.2)。默认值仅演示;系数待与公式文档核对。
    /// </summary>
    [Serializable]
    public class SmallSystemInput
    {
        public SmallSystemInput()
        {
            SystemType = SmallSystemType.AllAirOnceReturn;
            SetRepresentativeDefaults();
        }

        /// <summary>系统类型</summary>
        public SmallSystemType SystemType { get; set; }

        /// <summary>房间/区域名称</summary>
        public string RoomName { get; set; } = "";

        // ---- 空间物理参数 ----
        public double AreaM2 { get; set; }
        public double HeightM { get; set; }
        public double WallLengthM { get; set; }
        public double RoofAreaM2 { get; set; }

        // ---- 温度/负荷指标 ----
        public double IndoorTempC { get; set; }
        public double OutdoorTempC { get; set; }

        /// <summary>送风温差 ℃</summary>
        public double SupplyTempDiffC { get; set; }

        /// <summary>管道温升 ℃</summary>
        public double DuctTempRiseC { get; set; }

        /// <summary>照明功率密度 W/m²</summary>
        public double LightingDensityWm2 { get; set; }

        /// <summary>设备发热功率密度 W/m²</summary>
        public double EquipmentDensityWm2 { get; set; }

        /// <summary>人员数量 人</summary>
        public double Occupants { get; set; }

        /// <summary>人员显热散热 W/人</summary>
        public double OccupantSensibleW { get; set; }

        /// <summary>人员散湿量 g/(h·人)</summary>
        public double WaterVaporPerPersonGPerHour { get; set; }

        /// <summary>换气次数 次/h(排风/通风类系统使用)</summary>
        public double AirChangePerHour { get; set; }

        /// <summary>每人新风量 m³/(h·人)</summary>
        public double FreshAirPerPersonM3H { get; set; }

        private void SetRepresentativeDefaults()
        {
            AreaM2 = 60;
            HeightM = 4.5;
            WallLengthM = 30;
            RoofAreaM2 = 60;

            IndoorTempC = 26;
            OutdoorTempC = 33.5;
            SupplyTempDiffC = 10;
            DuctTempRiseC = 0;

            LightingDensityWm2 = 15;
            EquipmentDensityWm2 = 20;
            Occupants = 10;
            OccupantSensibleW = 61;
            WaterVaporPerPersonGPerHour = 109;

            AirChangePerHour = 6;
            FreshAirPerPersonM3H = 30;
        }
    }
}

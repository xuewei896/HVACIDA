using System;

namespace HVACIDA.Core.Models
{
    /// <summary>
    /// 大系统负荷计算结果(客流/设备负荷、送风/回风/新风、制冷量、排烟量、设备选型)。
    /// 结果仅供参考演示:公式骨架已实现,系数与算法细节待与《大系统负荷计算公式.docx》核对。
    /// </summary>
    [Serializable]
    public class LargeSystemResult
    {
        // ---- 负荷(W) ----
        public double PeopleSensibleW { get; set; }
        public double PeopleLatentW { get; set; }
        public double LightingW { get; set; }
        public double AdvertW { get; set; }
        public double EscalatorW { get; set; }
        public double ElevatorW { get; set; }
        public double AfcW { get; set; }
        public double PsdW { get; set; }

        /// <summary>总显热负荷 W</summary>
        public double TotalSensibleW { get; set; }

        /// <summary>总潜热负荷 W</summary>
        public double TotalLatentW { get; set; }

        /// <summary>总冷负荷 W(=显热+潜热,简化;空气侧校核见 CoolingByAirSideW)</summary>
        public double TotalCoolingW { get; set; }

        /// <summary>空气侧制冷量校核 W(按送/回风焓差,TODO 核对混合比)</summary>
        public double CoolingByAirSideW { get; set; }

        // ---- 风量(m³/h) ----
        public double SupplyAirVolumeM3H { get; set; }
        public double FreshAirVolumeM3H { get; set; }
        public double ReturnAirVolumeM3H { get; set; }
        public double FreshAirRatio { get; set; }

        // ---- 焓值(kJ/kg) ----
        public double IndoorEnthalpyKJKg { get; set; }
        public double SupplyEnthalpyKJKg { get; set; }
        public double MixedEnthalpyKJKg { get; set; }

        // ---- 排烟 ----
        public double SmokeHallM3H { get; set; }
        public double SmokePlatformM3H { get; set; }
        public double SmokeFanCount { get; set; }
        public double SmokeFanPerUnitM3H { get; set; }

        // ---- 设备选型 ----
        public double AhUnitAirVolumeM3H { get; set; }
        public double AhUnitCoolingW { get; set; }
        public double ReturnFanPerUnitM3H { get; set; }
    }
}

using System;

namespace HVACIDA.Core.Models
{
    /// <summary>小系统负荷计算结果(照明/人员/设备负荷、通风量、新风量、设备选型占位)。</summary>
    [Serializable]
    public class SmallSystemResult
    {
        public double LightingW { get; set; }
        public double PeopleSensibleW { get; set; }
        public double EquipmentW { get; set; }
        public double TotalSensibleW { get; set; }

        /// <summary>人员湿负荷 kg/h</summary>
        public double PeopleWaterVaporKgH { get; set; }

        /// <summary>人员潜热负荷 W</summary>
        public double PeopleLatentW { get; set; }

        /// <summary>总冷负荷 W</summary>
        public double TotalCoolingW { get; set; }

        /// <summary>消除余热通风量 m³/h</summary>
        public double VentilationByHeatM3H { get; set; }

        /// <summary>按换气次数通风量 m³/h</summary>
        public double VentilationByACHM3H { get; set; }

        /// <summary>实际通风量 m³/h(取大值)</summary>
        public double ActualVentilationM3H { get; set; }

        /// <summary>新风量 m³/h</summary>
        public double FreshAirM3H { get; set; }

        /// <summary>执行状态提示(如:该类型尚未实现)</summary>
        public string StatusMessage { get; set; } = "";

        /// <summary>设备选型文本(换行分隔),占位。</summary>
        public string EquipmentSelectionText { get; set; } = "";
    }
}

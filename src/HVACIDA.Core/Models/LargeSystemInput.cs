using System;

namespace HVACIDA.Core.Models
{
    /// <summary>
    /// 大系统负荷计算输入(需求文档 2.2.3.1 大系统:客流/设备/送风/回风/排烟/选型)。
    /// 单位见各属性注释;缺省工程默认值仅为演示,须按工程实测与《大系统负荷计算公式.docx》核对。
    /// </summary>
    [Serializable]
    public class LargeSystemInput
    {
        public LargeSystemInput()
        {
            SetRepresentativeDefaults();
        }

        // ---- 几何 ----
        /// <summary>站厅空调面积 m²</summary>
        public double HallAreaM2 { get; set; }

        /// <summary>站台空调面积 m²</summary>
        public double PlatformAreaM2 { get; set; }

        /// <summary>排烟计算层高 m(排烟量=面积×高度×60次/h 时使用,TODO 核对)</summary>
        public double SmokeZoneHeightM { get; set; }

        // ---- 客流负荷 ----
        /// <summary>站厅高峰同时停留人数 人</summary>
        public double HallOccupants { get; set; }

        /// <summary>站台高峰同时停留人数 人(含候车/上下车)</summary>
        public double PlatformOccupants { get; set; }

        /// <summary>集群系数</summary>
        public double ClusterFactor { get; set; }

        /// <summary>超高峰系数</summary>
        public double SuperPeakFactor { get; set; }

        /// <summary>人员显热散热 W/人(TODO 核对:取值参照规范/公式文档)</summary>
        public double OccupantSensibleHeatPerPersonW { get; set; }

        /// <summary>人员散湿量 g/(h·人)(用于潜热计算,TODO 核对)</summary>
        public double WaterVaporPerPersonGPerHour { get; set; }

        // ---- 设备/照明/渗透 ----
        /// <summary>照明功率密度 W/m²(站厅/站台可分别给出,暂按统一值)</summary>
        public double LightingDensityWm2 { get; set; }

        /// <summary>照明同时使用系数</summary>
        public double LightingUsageFactor { get; set; }

        /// <summary>广告牌发热量 W</summary>
        public double AdvertHeatW { get; set; }

        /// <summary>扶梯台数 台</summary>
        public double EscalatorCount { get; set; }

        /// <summary>单台扶梯发热量 W(运行时)</summary>
        public double EscalatorHeatPerUnitW { get; set; }

        /// <summary>直梯台数 台</summary>
        public double ElevatorCount { get; set; }

        /// <summary>单台直梯发热量 W</summary>
        public double ElevatorHeatPerUnitW { get; set; }

        /// <summary>AFC 设备发热量 W</summary>
        public double AfcHeatW { get; set; }

        /// <summary>屏蔽门系统负荷 W(漏风/传热综合,TODO 核对)</summary>
        public double PsdHeatW { get; set; }

        /// <summary>出入口渗透新风量 m³/h(TODO 核对:按出入口开启情况)</summary>
        public double InfiltrationAirVolumeM3H { get; set; }

        // ---- 送风/新风 ----
        /// <summary>每人新风量 m³/(h·人)(规范核对 TODO)</summary>
        public double FreshAirPerPersonM3H { get; set; }

        /// <summary>送风温差 ℃(室内-送风,TODO 核对)</summary>
        public double SupplyTemperatureDiffC { get; set; }

        /// <summary>设置代表性的演示默认值(加载/新建工程即用,便于界面演示;正式使用须覆盖)。</summary>
        private void SetRepresentativeDefaults()
        {
            HallAreaM2 = 1500;
            PlatformAreaM2 = 1200;
            SmokeZoneHeightM = 3.6;

            HallOccupants = 500;
            PlatformOccupants = 800;
            ClusterFactor = 1.0;
            SuperPeakFactor = 1.1;
            OccupantSensibleHeatPerPersonW = 61.0;
            WaterVaporPerPersonGPerHour = 109.0;

            LightingDensityWm2 = 12.0;
            LightingUsageFactor = 1.0;
            AdvertHeatW = 15000;
            EscalatorCount = 4;
            EscalatorHeatPerUnitW = 11000;
            ElevatorCount = 2;
            ElevatorHeatPerUnitW = 5500;
            AfcHeatW = 20000;
            PsdHeatW = 30000;
            InfiltrationAirVolumeM3H = 3000;

            FreshAirPerPersonM3H = 30.0;
            SupplyTemperatureDiffC = 8.0;
        }
    }
}

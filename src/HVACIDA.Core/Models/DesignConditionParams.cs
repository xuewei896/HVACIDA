using System;

namespace HVACIDA.Core.Models
{
    /// <summary>
    /// 设计/气象参数集(需求文档 2.1.2)。默认值仅为占位,入库前须按工程所在地气象库核对。
    /// TODO(公式核对):室内外计算参数以《气象数据库》与设计规范为准。
    /// </summary>
    [Serializable]
    public class DesignConditionParams
    {
        public DesignConditionParams()
        {
            LargeSystemOutdoor = new OutdoorAirParams();
            Common = new CommonAirParams();
            LargeSystemIndoor = new LargeSystemIndoorParams();
            SmallSystemIndoor = new SmallSystemIndoorParams();
        }

        /// <summary>大系统室外计算参数。</summary>
        public OutdoorAirParams LargeSystemOutdoor { get; set; }

        /// <summary>公共气象参数(大气压力/相对湿度)。</summary>
        public CommonAirParams Common { get; set; }

        /// <summary>大系统室内设计参数(站厅/站台)。</summary>
        public LargeSystemIndoorParams LargeSystemIndoor { get; set; }

        /// <summary>小系统室内设计参数(管理/设备用房)。</summary>
        public SmallSystemIndoorParams SmallSystemIndoor { get; set; }
    }

    /// <summary>室外计算参数(单位 ℃)。</summary>
    [Serializable]
    public class OutdoorAirParams
    {
        /// <summary>夏季空调室外计算干球温度 ℃</summary>
        public double SummerACDryBulbC { get; set; }

        /// <summary>夏季空调室外计算湿球温度 ℃</summary>
        public double SummerACWetBulbC { get; set; }

        /// <summary>夏季通风室外计算温度 ℃</summary>
        public double SummerVentDryBulbC { get; set; }

        /// <summary>冬季通风室外计算温度 ℃</summary>
        public double WinterVentDryBulbC { get; set; }

        /// <summary>冬季空调室外计算温度 ℃</summary>
        public double WinterACDryBulbC { get; set; }
    }

    /// <summary>公共气象参数。</summary>
    [Serializable]
    public class CommonAirParams
    {
        /// <summary>大气压力 kPa(默认 101.325)。</summary>
        public double AtmosphericPressureKPa { get; set; } = 101.325;

        /// <summary>室外相对湿度 %。</summary>
        public double OutdoorRelativeHumidityPercent { get; set; } = 60.0;
    }

    /// <summary>大系统室内设计参数(地铁站厅/站台)。</summary>
    [Serializable]
    public class LargeSystemIndoorParams
    {
        /// <summary>站厅夏季空调计算干球温度 ℃</summary>
        public double HallDryBulbC { get; set; } = 30.0;

        /// <summary>站厅夏季空调计算相对湿度 %</summary>
        public double HallRelativeHumidityPercent { get; set; } = 60.0;

        /// <summary>站台夏季空调计算干球温度 ℃</summary>
        public double PlatformDryBulbC { get; set; } = 28.0;

        /// <summary>站台夏季空调计算相对湿度 %</summary>
        public double PlatformRelativeHumidityPercent { get; set; } = 60.0;
    }

    /// <summary>小系统室内设计参数(管理/弱电/强电用房等)。</summary>
    [Serializable]
    public class SmallSystemIndoorParams
    {
        /// <summary>管理用房室内计算干球温度 ℃</summary>
        public double ManagementRoomDryBulbC { get; set; } = 26.0;

        /// <summary>弱电用房室内计算干球温度 ℃</summary>
        public double WeakCurrentRoomDryBulbC { get; set; } = 26.0;

        /// <summary>强电用房室内计算干球温度 ℃</summary>
        public double StrongCurrentRoomDryBulbC { get; set; } = 28.0;

        /// <summary>夏季空调室内计算湿球温度 ℃</summary>
        public double IndoorWetBulbC { get; set; } = 20.0;

        /// <summary>室内计算相对湿度 %</summary>
        public double IndoorRelativeHumidityPercent { get; set; } = 60.0;
    }
}

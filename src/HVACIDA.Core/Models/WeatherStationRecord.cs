using System;
using System.Globalization;

namespace HVACIDA.Core.Models
{
    /// <summary>
    /// 一个气象台站的室外空气计算参数(GB 50736-2012 附录A 表A)。
    /// <para>
    /// 数据来自内嵌资源 <c>Resources/weather-db.csv</c>(生成物,见 <c>tools/weatherdb/build_weather_db.py</c>),
    /// 由 <see cref="Services.WeatherDatabase"/> 解析。
    /// </para>
    /// <para>
    /// 数值一律可空:<c>null</c> = <strong>标准本身没有记录</strong>(附录A 条文说明承认咸阳、黔南州、
    /// 新疆塔城等台站的湿球温度无记录)。这种情况必须保持"未填",绝不用邻近台站或猜测值填上。
    /// </para>
    /// </summary>
    public class WeatherStationRecord
    {
        /// <summary>台站号(气象部门区站号,如北京 54511)。</summary>
        public string StationId { get; set; } = "";

        /// <summary>省/直辖市/自治区。</summary>
        public string Province { get; set; } = "";

        /// <summary>市/区/自治州。</summary>
        public string City { get; set; } = "";

        /// <summary>台站名称(与市名可能不同,如"张家界"市对应"桑植"站)。</summary>
        public string StationName { get; set; } = "";

        /// <summary>北纬(度分,原样保留)。</summary>
        public string Latitude { get; set; } = "";

        /// <summary>东经(度分,原样保留)。</summary>
        public string Longitude { get; set; } = "";

        /// <summary>统计年份(如 1971~2000)。</summary>
        public string StatsPeriod { get; set; } = "";

        /// <summary>海拔 m。</summary>
        public double? ElevationM { get; set; }

        /// <summary>年平均温度 ℃。</summary>
        public double? AnnualMeanTempC { get; set; }

        /// <summary>供暖室外计算温度 ℃。</summary>
        public double? HeatingOutdoorC { get; set; }

        /// <summary>冬季通风室外计算温度 ℃。</summary>
        public double? WinterVentOutdoorC { get; set; }

        /// <summary>冬季空气调节室外计算温度 ℃。</summary>
        public double? WinterAcOutdoorC { get; set; }

        /// <summary>冬季空气调节室外计算相对湿度 %。</summary>
        public double? WinterAcRhPct { get; set; }

        /// <summary>夏季空气调节室外计算干球温度 ℃。</summary>
        public double? SummerAcDryBulbC { get; set; }

        /// <summary>夏季空气调节室外计算湿球温度 ℃(个别台站标准无记录,见类型注释)。</summary>
        public double? SummerAcWetBulbC { get; set; }

        /// <summary>夏季通风室外计算温度 ℃。</summary>
        public double? SummerVentDryBulbC { get; set; }

        /// <summary>夏季通风室外计算相对湿度 %。</summary>
        public double? SummerVentRhPct { get; set; }

        /// <summary>夏季空气调节室外计算日平均温度 ℃。</summary>
        public double? SummerAcDailyMeanC { get; set; }

        /// <summary>冬季室外大气压力 hPa。</summary>
        public double? WinterAtmPressureHpa { get; set; }

        /// <summary>夏季室外大气压力 hPa。</summary>
        public double? SummerAtmPressureHpa { get; set; }

        /// <summary>极端最高气温 ℃。</summary>
        public double? ExtremeMaxC { get; set; }

        /// <summary>极端最低气温 ℃。</summary>
        public double? ExtremeMinC { get; set; }

        /// <summary>最大冻土深度 cm(南方台站标准留空)。</summary>
        public double? MaxFrostDepthCm { get; set; }

        /// <summary>冬季日照百分率 %。</summary>
        public double? WinterSunshinePct { get; set; }

        /// <summary>标准是否记录了夏季空调湿球温度。</summary>
        public bool HasSummerWetBulb => SummerAcWetBulbC.HasValue;

        /// <summary>显示用:"北京(54511)"。</summary>
        public string DisplayName => City + "(" + StationId + ")";

        /// <summary>台站信息一行(界面提示用)。</summary>
        public string StationInfoText()
        {
            return "台站 " + StationName + "(" + StationId + ") · 北纬 " + Latitude + " · 东经 " + Longitude +
                   " · 海拔 " + Num(ElevationM) + " m · 统计年份 " + StatsPeriod;
        }

        private static string Num(double? v)
        {
            return v.HasValue ? v.Value.ToString("0.##", CultureInfo.InvariantCulture) : "—";
        }
    }
}

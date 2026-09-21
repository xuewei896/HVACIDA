using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 全国省市气象参数库(GB 50736-2012 附录A 表A「室外空气计算参数」,294 个台站 / 31 个省级行政区)。
    /// <para>
    /// 数据以 CSV <strong>内嵌在 HVACIDA.Core.dll</strong>(<c>Resources/weather-db.csv</c>),
    /// 不依赖任何外部文件或联网 —— 插件拷到哪台机器都能直接按城市取参数。
    /// CSV 是生成物(<c>tools/weatherdb/build_weather_db.py</c>),头部记录了源文件 SHA256 与台站数,
    /// 门禁 <c>tools/weatherdb/check-weather-db-sync.ps1</c> 负责校验一致性。
    /// </para>
    /// <para>
    /// 纪律:标准没有记录的格一律为 <c>null</c>,取值时<strong>不覆盖</strong>目标参数,
    /// 绝不用邻近台站或猜测值补齐(见 <see cref="WeatherStationRecord"/> 注释)。
    /// </para>
    /// <para>
    /// 2026-09-20 用户口径「删掉所有告警」:不再生成告警文案(原 <c>WeatherApplyResult.Warning</c> 已删);
    /// "哪些项标准未记录"仍记录在 <see cref="WeatherApplyResult.MissingText"/> 里,界面上不提示。
    /// </para>
    /// </summary>
    public class WeatherDatabase
    {
        private const string ResourceName = "HVACIDA.Core.Resources.weather-db.csv";

        private readonly List<WeatherStationRecord> _all = new List<WeatherStationRecord>();
        private readonly List<string> _provinces = new List<string>();
        private readonly Dictionary<string, List<string>> _citiesByProvince =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, WeatherStationRecord> _byProvinceCity =
            new Dictionary<string, WeatherStationRecord>(StringComparer.Ordinal);

        private static readonly Lazy<WeatherDatabase> LazyDefault =
            new Lazy<WeatherDatabase>(() => Parse(ReadEmbeddedCsv()));

        /// <summary>默认实例(从内嵌资源加载,进程内只解析一次)。</summary>
        public static WeatherDatabase Default => LazyDefault.Value;

        /// <summary>全部台站(按标准顺序)。</summary>
        public IList<WeatherStationRecord> All => _all;

        /// <summary>省级行政区(按标准顺序:直辖市与省混排,如 北京、天津、河北…)。</summary>
        public IList<string> Provinces => _provinces;

        /// <summary>CSV 头部记录的数据来源说明(源文件名 / SHA256 / 台站数)。</summary>
        public string SourceNote { get; private set; } = "";

        /// <summary>某省下的城市(按标准顺序);未知省返回空列表。</summary>
        public IList<string> CitiesOf(string province)
        {
            List<string> cities;
            if (!string.IsNullOrEmpty(province) && _citiesByProvince.TryGetValue(province, out cities))
            {
                return cities;
            }
            return new List<string>();
        }

        /// <summary>按"省 + 市"取台站;找不到返回 null。</summary>
        public WeatherStationRecord Find(string province, string city)
        {
            if (string.IsNullOrEmpty(province) || string.IsNullOrEmpty(city)) return null;
            WeatherStationRecord record;
            return _byProvinceCity.TryGetValue(Key(province, city), out record) ? record : null;
        }

        /// <summary>
        /// 把台站参数写入设计/气象参数集(<see cref="DesignConditionParams"/>)。
        /// <para>
        /// 只写<strong>室外气象参数</strong>(大系统/小系统室外 + 大气压力 + 室外相对湿度);
        /// 室内设计参数(站厅/站台/用房温度湿度)属于设计取值,不由气象库改动。
        /// </para>
        /// </summary>
        public static WeatherApplyResult Apply(WeatherStationRecord station, DesignConditionParams design)
        {
            if (station == null) throw new ArgumentNullException(nameof(station));
            if (design == null) throw new ArgumentNullException(nameof(design));

            EnsureParts(design);
            var result = new WeatherApplyResult { Station = station };
            var filled = new List<string>();
            var missing = new List<string>();

            Fill(result, "夏季空调室外干球温度", station.SummerAcDryBulbC,
                () => design.LargeSystemOutdoor.SummerACDryBulbC, v => design.LargeSystemOutdoor.SummerACDryBulbC = v,
                filled, missing);
            Fill(result, "夏季空调室外湿球温度", station.SummerAcWetBulbC,
                () => design.LargeSystemOutdoor.SummerACWetBulbC, v => design.LargeSystemOutdoor.SummerACWetBulbC = v,
                filled, missing);
            Fill(result, "夏季通风室外计算温度", station.SummerVentDryBulbC,
                () => design.LargeSystemOutdoor.SummerVentDryBulbC, v => design.LargeSystemOutdoor.SummerVentDryBulbC = v,
                filled, missing);
            Fill(result, "冬季通风室外计算温度", station.WinterVentOutdoorC,
                () => design.LargeSystemOutdoor.WinterVentDryBulbC, v => design.LargeSystemOutdoor.WinterVentDryBulbC = v,
                filled, missing);
            Fill(result, "冬季空调室外计算温度", station.WinterAcOutdoorC,
                () => design.LargeSystemOutdoor.WinterACDryBulbC, v => design.LargeSystemOutdoor.WinterACDryBulbC = v,
                filled, missing);

            // 小系统室外参数与"大系统室外夏季三项"同源(需求 2.1.2:小系统室外仅夏季空调干球/湿球与夏季通风)
            Fill(result, "小系统夏季空调干球温度", station.SummerAcDryBulbC,
                () => design.SmallSystemOutdoor.SummerACDryBulbC, v => design.SmallSystemOutdoor.SummerACDryBulbC = v,
                filled, missing);
            Fill(result, "小系统夏季空调湿球温度", station.SummerAcWetBulbC,
                () => design.SmallSystemOutdoor.SummerACWetBulbC, v => design.SmallSystemOutdoor.SummerACWetBulbC = v,
                filled, missing);
            Fill(result, "小系统夏季通风计算温度", station.SummerVentDryBulbC,
                () => design.SmallSystemOutdoor.SummerVentDryBulbC, v => design.SmallSystemOutdoor.SummerVentDryBulbC = v,
                filled, missing);

            // 大气压力:采用**夏季**室外大气压力(夏季空调工况用),hPa → kPa
            if (station.SummerAtmPressureHpa.HasValue)
            {
                design.Common.AtmosphericPressureKPa = Math.Round(station.SummerAtmPressureHpa.Value / 10.0, 3);
                result.FilledCount++;
                filled.Add("夏季大气压力 " + Num(design.Common.AtmosphericPressureKPa) + " kPa");
            }
            else
            {
                missing.Add("夏季室外大气压力");
            }

            if (station.SummerVentRhPct.HasValue)
            {
                design.Common.OutdoorRelativeHumidityPercent = station.SummerVentRhPct.Value;
                result.FilledCount++;
                filled.Add("夏季通风室外相对湿度 " + Num(design.Common.OutdoorRelativeHumidityPercent) + " %");
            }
            else
            {
                missing.Add("夏季通风室外计算相对湿度");
            }

            result.FilledText = string.Join("、", filled.ToArray());
            result.MissingText = string.Join("、", missing.ToArray());
            result.Note = "已按「" + station.Province + station.City + "」台站(" + station.StationId + ")回填 " +
                          result.FilledCount + " 项室外气象参数,数据源 GB 50736-2012 附录A。";
            return result;
        }

        // ------------------------------------------------------------------ 加载与解析

        /// <summary>解析 CSV 文本(供自检直接喂字符串)。</summary>
        public static WeatherDatabase Parse(string csvText)
        {
            var db = new WeatherDatabase();
            if (string.IsNullOrEmpty(csvText)) return db;

            var lines = csvText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var source = new StringBuilder();
            var dataLines = new List<string>();
            foreach (var line in lines)
            {
                if (line.Length == 0) continue;
                if (line[0] == '#') { source.AppendLine(line.TrimStart('#', ' ')); continue; }
                dataLines.Add(line);
            }
            db.SourceNote = source.ToString().Trim();

            if (dataLines.Count == 0) return db;

            var header = ParseLine(dataLines[0]);
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++) index[header[i].Trim()] = i;

            for (int i = 1; i < dataLines.Count; i++)
            {
                var cells = ParseLine(dataLines[i]);
                var record = new WeatherStationRecord
                {
                    StationId = Cell(cells, index, "station_id"),
                    Province = Cell(cells, index, "province"),
                    City = Cell(cells, index, "city"),
                    StationName = Cell(cells, index, "station_name"),
                    Latitude = Cell(cells, index, "latitude"),
                    Longitude = Cell(cells, index, "longitude"),
                    StatsPeriod = Cell(cells, index, "stats_period"),
                    ElevationM = NumOrNull(Cell(cells, index, "elevation_m")),
                    AnnualMeanTempC = NumOrNull(Cell(cells, index, "annual_mean_temp_c")),
                    HeatingOutdoorC = NumOrNull(Cell(cells, index, "heating_outdoor_c")),
                    WinterVentOutdoorC = NumOrNull(Cell(cells, index, "winter_vent_outdoor_c")),
                    WinterAcOutdoorC = NumOrNull(Cell(cells, index, "winter_ac_outdoor_c")),
                    WinterAcRhPct = NumOrNull(Cell(cells, index, "winter_ac_rh_pct")),
                    SummerAcDryBulbC = NumOrNull(Cell(cells, index, "summer_ac_dry_bulb_c")),
                    SummerAcWetBulbC = NumOrNull(Cell(cells, index, "summer_ac_wet_bulb_c")),
                    SummerVentDryBulbC = NumOrNull(Cell(cells, index, "summer_vent_dry_bulb_c")),
                    SummerVentRhPct = NumOrNull(Cell(cells, index, "summer_vent_rh_pct")),
                    SummerAcDailyMeanC = NumOrNull(Cell(cells, index, "summer_ac_daily_mean_c")),
                    WinterAtmPressureHpa = NumOrNull(Cell(cells, index, "winter_atm_pressure_hpa")),
                    SummerAtmPressureHpa = NumOrNull(Cell(cells, index, "summer_atm_pressure_hpa")),
                    ExtremeMaxC = NumOrNull(Cell(cells, index, "extreme_max_c")),
                    ExtremeMinC = NumOrNull(Cell(cells, index, "extreme_min_c")),
                    MaxFrostDepthCm = NumOrNull(Cell(cells, index, "max_frost_depth_cm")),
                    WinterSunshinePct = NumOrNull(Cell(cells, index, "winter_sunshine_pct"))
                };

                if (string.IsNullOrEmpty(record.City) || string.IsNullOrEmpty(record.Province)) continue;

                db._all.Add(record);
                db._byProvinceCity[Key(record.Province, record.City)] = record;

                List<string> cities;
                if (!db._citiesByProvince.TryGetValue(record.Province, out cities))
                {
                    cities = new List<string>();
                    db._citiesByProvince[record.Province] = cities;
                    db._provinces.Add(record.Province);
                }
                cities.Add(record.City);
            }

            return db;
        }

        private static string ReadEmbeddedCsv()
        {
            Assembly assembly = typeof(WeatherDatabase).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException(
                        "内嵌气象数据库缺失(" + ResourceName + ")。请确认 HVACIDA.Core.csproj 里 " +
                        "Resources\\weather-db.csv 标记为 EmbeddedResource,并重跑 tools/weatherdb/build_weather_db.py。");
                }
                using (var reader = new StreamReader(stream, new UTF8Encoding(false), true))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        // ------------------------------------------------------------------ 小工具

        private static void EnsureParts(DesignConditionParams design)
        {
            if (design.LargeSystemOutdoor == null) design.LargeSystemOutdoor = new OutdoorAirParams();
            if (design.SmallSystemOutdoor == null) design.SmallSystemOutdoor = new SmallSystemOutdoorParams();
            if (design.Common == null) design.Common = new CommonAirParams();
        }

        private static void Fill(WeatherApplyResult result, string label, double? source,
            Func<double> getter, Action<double> setter, List<string> filled, List<string> missing)
        {
            if (!source.HasValue)
            {
                missing.Add(label);
                return;
            }
            setter(source.Value);
            result.FilledCount++;
            filled.Add(label + " " + Num(source.Value) + " ℃");
        }

        private static string Num(double v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string Key(string province, string city)
        {
            return province + "\u0001" + city;
        }

        private static string Cell(string[] cells, Dictionary<string, int> index, string column)
        {
            int i;
            if (!index.TryGetValue(column, out i) || i >= cells.Length) return "";
            return cells[i].Trim();
        }

        private static double? NumOrNull(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            double v;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? (double?)v : null;
        }

        /// <summary>解析一行 CSV(支持双引号包裹与 "" 转义;本库的字段内不含换行)。</summary>
        private static string[] ParseLine(string line)
        {
            var cells = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { cells.Add(field.ToString()); field.Length = 0; }
                else field.Append(c);
            }
            cells.Add(field.ToString());
            return cells.ToArray();
        }
    }

    /// <summary>一次"气象库 → 设计参数"回填的结果(供界面提示与自检断言)。</summary>
    public class WeatherApplyResult
    {
        /// <summary>数据来源台站。</summary>
        public WeatherStationRecord Station { get; set; }

        /// <summary>实际写入的项数。</summary>
        public int FilledCount { get; set; }

        /// <summary>写入项的可读列表。</summary>
        public string FilledText { get; set; } = "";

        /// <summary>标准未记录、因此保持原值的项。</summary>
        public string MissingText { get; set; } = "";

        /// <summary>状态栏文案。</summary>
        public string Note { get; set; } = "";
    }
}

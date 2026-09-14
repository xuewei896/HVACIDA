using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 「项目信息 → 气象参数」到大系统输入(<see cref="LargeSystemInput"/>)的联动
    /// (需求 2.1.2 → 2.2.3.1;《大系统负荷计算公式.docx》参数表中标注"从项目信息调取"的仅三格):
    /// <list type="bullet">
    ///   <item>C5 夏季空调室外计算湿球温度 ← <c>DesignConditionParams.LargeSystemOutdoor.SummerACWetBulbC</c></item>
    ///   <item>F4 站厅夏季空调计算干球温度 ← <c>DesignConditionParams.LargeSystemIndoor.HallDryBulbC</c></item>
    ///   <item>F6 站台夏季空调计算干球温度 ← <c>DesignConditionParams.LargeSystemIndoor.PlatformDryBulbC</c></item>
    /// </list>
    /// 其余各格一律取自公式文档默认值或用户输入,本类不越界修改。
    /// 纪律:气象参数"未填"时<strong>不覆盖</strong>目标值,只登记到 UnsetFields 由界面提示,
    /// 避免用未填的 0 ℃ 冲掉用户已填的数值。
    /// </summary>
    public static class ProjectDesignSync
    {
        /// <summary>
        /// 视为"气象参数未填"的温度阈值 ℃。夏季空调室外湿球温度、夏季空调室内计算干球温度
        /// 在工程上不可能 ≤ 0 ℃,故 ≤ 该值即判定为未填。
        /// </summary>
        public const double UnsetTemperatureC = 0.0;

        /// <summary>
        /// 把气象参数回填到 C5/F4/F6。返回本次联动结果(含未填项与界面提示文案)。
        /// </summary>
        public static WeatherSyncResult ApplyWeather(DesignConditionParams design, LargeSystemInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            double wetBulb = design?.LargeSystemOutdoor?.SummerACWetBulbC ?? UnsetTemperatureC;
            double hall = design?.LargeSystemIndoor?.HallDryBulbC ?? UnsetTemperatureC;
            double platform = design?.LargeSystemIndoor?.PlatformDryBulbC ?? UnsetTemperatureC;

            var result = new WeatherSyncResult
            {
                SourceWetBulbC = wetBulb,
                SourceHallC = hall,
                SourcePlatformC = platform
            };

            ApplyField(result, "C5 夏季空调室外湿球温度", wetBulb, input.OutdoorWetBulbC, v => input.OutdoorWetBulbC = v);
            ApplyField(result, "F4 站厅空调计算干球温度", hall, input.HallDesignTempC, v => input.HallDesignTempC = v);
            ApplyField(result, "F6 站台空调计算干球温度", platform, input.PlatformDesignTempC, v => input.PlatformDesignTempC = v);

            result.Note = result.AppliedCount > 0
                ? "已按「项目信息 → 气象参数」回填 " + DescribeSource(result) +
                  (result.AppliedCount < 3 ? "(其中 " + result.AppliedCount + " 格有变化)" : "") + "。"
                : "本窗 C5/F4/F6 与「项目信息 → 气象参数」一致(" + DescribeSource(result) + ")。";

            if (result.UnsetFields.Count > 0)
            {
                result.Warning =
                    "⚠ 项目信息中有未填项:" + string.Join("、", result.UnsetFields.ToArray()) +
                    "。已保留本窗原值不做覆盖;请到 Ribbon「项目信息 → 气象参数」点【从气象数据库获取】填入后,回到本窗点【重新同步】。";
            }

            return result;
        }

        /// <summary>当前气象参数的取值摘要(供界面状态栏显示来源)。</summary>
        public static string DescribeSource(WeatherSyncResult r)
        {
            if (r == null) return "";
            var sb = new StringBuilder();
            sb.Append("C5=").Append(Num(r.SourceWetBulbC)).Append(" ℃");
            sb.Append(" / F4=").Append(Num(r.SourceHallC)).Append(" ℃");
            sb.Append(" / F6=").Append(Num(r.SourcePlatformC)).Append(" ℃");
            return sb.ToString();
        }

        private static void ApplyField(WeatherSyncResult result, string fieldName, double source, double current, Action<double> setter)
        {
            if (source <= UnsetTemperatureC)
            {
                result.UnsetFields.Add(fieldName);
                return;
            }

            if (Math.Abs(source - current) > 1e-9)
            {
                setter(source);
                result.AppliedCount++;
            }
        }

        private static string Num(double v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>一次气象参数联动的结果(供 UI 状态提示与自检断言)。</summary>
    public sealed class WeatherSyncResult
    {
        /// <summary>实际写入(值有变化)的格数,0~3。</summary>
        public int AppliedCount { get; set; }

        /// <summary>气象参数中"未填"(≤0 ℃)因而未覆盖的格名。</summary>
        public List<string> UnsetFields { get; } = new List<string>();

        /// <summary>气象参数源值(℃),用于界面显示来源。</summary>
        public double SourceWetBulbC { get; set; }
        public double SourceHallC { get; set; }
        public double SourcePlatformC { get; set; }

        /// <summary>界面状态栏文案(非空)。</summary>
        public string Note { get; set; } = "";

        /// <summary>需要用户去补气象参数时的告警文案(可为空字符串)。</summary>
        public string Warning { get; set; } = "";
    }
}

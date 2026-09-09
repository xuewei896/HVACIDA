using System.Globalization;
using System.Text;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 将计算结果格式化为中文多行文本(供界面展示与计算书导出共用,保证两处一致)。
    /// 大系统文本分节与《大系统负荷计算公式.docx》保持一致,并标注单元格代号。
    /// </summary>
    public static class ResultFormatter
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        /// <summary>大系统结果文本(单元格代号见 LargeSystemResult 注释)。</summary>
        public static string FormatLarge(LargeSystemInput x, LargeSystemResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("【大系统负荷计算结果】(骨架已按公式文档移植,待 Excel 样例逐格核对)");
            sb.AppendLine("站厅公共区高峰客流 " + Num(r.HallPeakFlowPm) + " 个/min (C39);站台 " + Num(r.PlatformPeakFlowPm) + " 个/min (C40)");

            sb.AppendLine("—— 站厅冷负荷 (kW,D95~D107) ——");
            AppendLine(sb, "D95 乘客显热", r.HallSensibleKw, "kW");
            AppendLine(sb, "D96 乘客潜热", r.HallLatentKw, "kW");
            AppendLine(sb, "D97 照明", r.HallLightingKw, "kW");
            AppendLine(sb, "D98 广告灯箱", r.HallAdvertKw, "kW");
            AppendLine(sb, "D99 自动扶梯(B64/2)", r.HallEscalatorKw, "kW");
            AppendLine(sb, "D100 垂直电梯(D64/2)", r.HallElevatorKw, "kW");
            AppendLine(sb, "D101 AFC 设备", r.HallAfcKw, "kW");
            AppendLine(sb, "D102 出入口渗透", r.HallEntranceInfiltrationKw, "kW");
            AppendLine(sb, "D103 屏蔽门传热(输入)", r.HallPsdTransferKw, "kW");
            AppendLine(sb, "D104 屏蔽门漏风", r.HallPsdLeakKw, "kW");
            AppendLine(sb, "D105 屏蔽门发热(输入)", r.HallPsdHeatKw, "kW");
            AppendLine(sb, "D107 站厅冷负荷合计", r.HallTotalCoolingKw, "kW");

            sb.AppendLine("—— 站台冷负荷 (kW,E95~E107) ——");
            AppendLine(sb, "E95 乘客显热", r.PlatformSensibleKw, "kW");
            AppendLine(sb, "E96 乘客潜热", r.PlatformLatentKw, "kW");
            AppendLine(sb, "E97 照明", r.PlatformLightingKw, "kW");
            AppendLine(sb, "E98 广告灯箱", r.PlatformAdvertKw, "kW");
            AppendLine(sb, "E99 自动扶梯", r.PlatformEscalatorKw, "kW");
            AppendLine(sb, "E100 垂直电梯", r.PlatformElevatorKw, "kW");
            AppendLine(sb, "E103 屏蔽门传热", r.PlatformPsdTransferKw, "kW");
            AppendLine(sb, "E104 屏蔽门漏风", r.PlatformPsdLeakKw, "kW");
            AppendLine(sb, "E105 屏蔽门系统发热", r.PlatformPsdHeatKw, "kW");
            AppendLine(sb, "E107 站台冷负荷合计", r.PlatformTotalCoolingKw, "kW");

            sb.AppendLine("—— 湿负荷与热湿比 ——");
            AppendLine(sb, "D112 站厅湿负荷", r.HallTotalMoistureGps, "g/s");
            AppendLine(sb, "E112 站台湿负荷", r.PlatformTotalMoistureGps, "g/s");
            AppendLine(sb, "D113 站厅热湿比", r.HallHeatHumidityRatio, "kJ/kg");
            AppendLine(sb, "E113 站台热湿比", r.PlatformHeatHumidityRatio, "kJ/kg");

            sb.AppendLine("—— 焓湿过程 ——");
            AppendLine(sb, "A118 站厅送风温度", r.HallSupplyTempC, "℃");
            AppendLine(sb, "B118 露点温度", r.DewPointTempC, "℃");
            AppendLine(sb, "E118 露点含湿量", r.DewPointMoistureGkg, "g/kg");
            AppendLine(sb, "A121 送风点焓", r.HallSupplyEnthalpy, "kJ/kg");
            AppendLine(sb, "C121 站厅室内焓", r.HallIndoorEnthalpy, "kJ/kg");
            AppendLine(sb, "E121 站台室内焓", r.PlatformIndoorEnthalpy, "kJ/kg");

            sb.AppendLine("—— 风量 (m³/h) ——");
            AppendLine(sb, "A125 站厅送风量", r.HallSupplyFlowM3H, "m³/h");
            AppendLine(sb, "B125 站台送风量", r.PlatformSupplyFlowM3H, "m³/h");
            AppendLine(sb, "C125 总送风量", r.TotalSupplyFlowM3H, "m³/h");
            AppendLine(sb, "A136 实际新风量", r.ActualFreshAirM3H, "m³/h");
            AppendLine(sb, "E136 总回风量", r.TotalReturnFlowM3H, "m³/h");

            sb.AppendLine("—— 制冷量 (kW) ——");
            AppendLine(sb, "C145 回风混合焓", r.ReturnMixEnthalpy, "kJ/kg");
            AppendLine(sb, "C146 新回风混合焓", r.FreshReturnMixEnthalpy, "kJ/kg");
            AppendLine(sb, "C143 露点焓", r.DewPointEnthalpy, "kJ/kg");
            AppendLine(sb, "E159 总制冷量", r.TotalCoolingKw, "kW");

            sb.AppendLine("—— 排烟与选型 ——");
            AppendLine(sb, "C171 站厅排烟量", r.HallSmokeFlowM3H, "m³/h");
            AppendLine(sb, "D171 站台排烟量", r.PlatformSmokeFlowM3H, "m³/h");
            AppendLine(sb, "A165 单台机组送风量(C125/2)", r.UnitSupplyFlowM3H, "m³/h");
            AppendLine(sb, "B165 单台机组制冷量(E159/2)", r.UnitCoolingKw, "kW");
            AppendLine(sb, "C178 单台回排风机回风量(E136/2)", r.UnitReturnFlowM3H, "m³/h");
            AppendLine(sb, "E178 单台排烟风机风量(MAX/2)", r.UnitSmokeFlowM3H, "m³/h");

            sb.AppendLine();
            sb.AppendLine("注:公式已按《大系统负荷计算公式.docx》逐格移植(E159 中 D143 疑为 C143 笔误,按 C143 计算);请用 Excel 样例复核后再用于设计。");
            return sb.ToString();
        }

        /// <summary>小系统结果文本。</summary>
        public static string FormatSmall(SmallSystemInput x, SmallSystemResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("【小系统负荷计算结果】");
            sb.AppendLine("系统类型: " + x.SystemType + (string.IsNullOrEmpty(x.RoomName) ? "" : " / " + x.RoomName));
            AppendLine(sb, "照明负荷", r.LightingW, "W");
            AppendLine(sb, "人员显热负荷", r.PeopleSensibleW, "W");
            AppendLine(sb, "设备负荷", r.EquipmentW, "W");
            AppendLine(sb, "总显热负荷", r.TotalSensibleW, "W");
            AppendLine(sb, "人员潜热负荷", r.PeopleLatentW, "W");
            AppendLine(sb, "总冷负荷", r.TotalCoolingW, "W");
            AppendLine(sb, "除热通风量", r.VentilationByHeatM3H, "m³/h");
            AppendLine(sb, "换气次数通风量", r.VentilationByACHM3H, "m³/h");
            AppendLine(sb, "实际通风量", r.ActualVentilationM3H, "m³/h");
            AppendLine(sb, "新风量", r.FreshAirM3H, "m³/h");
            sb.AppendLine("设备选型: " + r.EquipmentSelectionText);
            sb.AppendLine();
            sb.AppendLine("提示: " + r.StatusMessage);
            sb.AppendLine("注:当前为骨架计算(公式待核对),仅用于流程演示。");
            return sb.ToString();
        }

        private static string Num(double value)
        {
            return value.ToString("N1", C);
        }

        private static void AppendLine(StringBuilder sb, string label, double value, string unit)
        {
            sb.Append(label).Append(": ").Append(Num(value)).Append(' ').Append(unit).AppendLine();
        }
    }
}

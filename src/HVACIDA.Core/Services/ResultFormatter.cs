using System.Globalization;
using System.Text;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 将计算结果格式化为中文多行文本(供界面展示与计算书导出共用,保证两处一致)。
    /// </summary>
    public static class ResultFormatter
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        /// <summary>大系统结果文本。</summary>
        public static string FormatLarge(LargeSystemInput x, LargeSystemResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("【大系统负荷计算结果】");
            sb.AppendLine("—— 负荷(W) ——");
            AppendLine(sb, "人员显热负荷", r.PeopleSensibleW, "W");
            AppendLine(sb, "人员潜热负荷", r.PeopleLatentW, "W");
            AppendLine(sb, "照明负荷", r.LightingW, "W");
            AppendLine(sb, "广告牌发热", r.AdvertW, "W");
            AppendLine(sb, "扶梯发热", r.EscalatorW, "W");
            AppendLine(sb, "直梯发热", r.ElevatorW, "W");
            AppendLine(sb, "AFC 设备发热", r.AfcW, "W");
            AppendLine(sb, "屏蔽门系统负荷", r.PsdW, "W");
            AppendLine(sb, "总显热负荷", r.TotalSensibleW, "W");
            AppendLine(sb, "总潜热负荷", r.TotalLatentW, "W");
            AppendLine(sb, "总冷负荷(显+潜)", r.TotalCoolingW, "W");
            AppendLine(sb, "空气侧校核制冷量", r.CoolingByAirSideW, "W");

            sb.AppendLine("—— 风量(m³/h) ——");
            AppendLine(sb, "送风量", r.SupplyAirVolumeM3H, "m³/h");
            AppendLine(sb, "新风量", r.FreshAirVolumeM3H, "m³/h");
            AppendLine(sb, "回风量", r.ReturnAirVolumeM3H, "m³/h");
            AppendLine(sb, "新风比", r.FreshAirRatio * 100.0, "%");
            AppendLine(sb, "室内焓", r.IndoorEnthalpyKJKg, "kJ/kg");
            AppendLine(sb, "送风焓", r.SupplyEnthalpyKJKg, "kJ/kg");
            AppendLine(sb, "混合焓", r.MixedEnthalpyKJKg, "kJ/kg");

            sb.AppendLine("—— 排烟(m³/h) ——");
            AppendLine(sb, "站厅排烟量", r.SmokeHallM3H, "m³/h");
            AppendLine(sb, "站台排烟量", r.SmokePlatformM3H, "m³/h");
            AppendLine(sb, "排烟风机台数", r.SmokeFanCount, "台");
            AppendLine(sb, "单台排烟风机风量", r.SmokeFanPerUnitM3H, "m³/h");

            sb.AppendLine("—— 设备选型(各取总量一半、2 台) ——");
            AppendLine(sb, "单台组合式空调机组送风量", r.AhUnitAirVolumeM3H, "m³/h");
            AppendLine(sb, "单台组合式空调机组制冷量", r.AhUnitCoolingW, "W");
            AppendLine(sb, "单台回排风机风量", r.ReturnFanPerUnitM3H, "m³/h");

            sb.AppendLine();
            sb.AppendLine("注:当前为骨架计算(公式待与《大系统负荷计算公式.docx》核对),仅用于流程演示。");
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

        private static void AppendLine(StringBuilder sb, string label, double value, string unit)
        {
            sb.Append(label).Append(": ").Append(value.ToString("N1", C)).Append(' ').Append(unit).AppendLine();
        }
    }
}

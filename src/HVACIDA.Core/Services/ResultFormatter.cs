using System.Globalization;
using System.Text;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 计算结果的中文文本渲染(供**导出计算书**使用)。
    /// <para>
    /// 2026-09-15 起:文本不再各写一套,而是由 <see cref="ResultTable"/> 这份**表格模型**渲染 ——
    /// 界面里的结果表格与导出的计算书同源,不会出现"窗口改了、计算书没改"或数字口径不一致。
    /// </para>
    /// </summary>
    public static class ResultFormatter
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        /// <summary>大系统负荷计算书(表格文本;单元格代号保留,便于与公式文档逐格核对)。</summary>
        public static string FormatLarge(LargeSystemInput x, LargeSystemResult r)
        {
            return ResultTable.ForLargeSystem(x, r).ToText();
        }

        /// <summary>大系统排烟计算书(表格文本)。</summary>
        public static string FormatLargeSmoke(LargeSystemInput areas, LargeSmokeInput x, LargeSmokeResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("【大系统排烟计算书】(需求 2.2.3.1)");
            sb.AppendLine(ResultTable.ForLargeSmoke(x, r).ToText());
            sb.AppendLine();
            sb.AppendLine("面积来源:站厅公共区 D55 = " + Num(areas.HallAreaM2) + " m²," +
                          "站台公共区 D56 = " + Num(areas.PlatformAreaM2) + " m²(与「公共区参数」同一份输入)。");
            return sb.ToString();
        }

        /// <summary>小系统负荷计算书(表格文本)。</summary>
        public static string FormatSmall(SmallSystemInput x, SmallSystemResult r)
        {
            return ResultTable.ForSmallSystem(x, r).ToText();
        }

        private static string Num(double value)
        {
            return value.ToString("N1", C);
        }
    }
}

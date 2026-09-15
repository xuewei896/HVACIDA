using System.Text;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 计算结果的中文文本渲染(供**导出计算书**使用)。
    /// <para>
    /// 2026-09-15 起:文本不再各写一套,而是由 <see cref="ResultTable"/> 这份**表格模型**渲染 ——
    /// 界面里的结果表格与导出的计算书同源,不会出现「窗口改了、计算书没改」或数字口径不一致。
    /// 小系统另有"房间明细 + 设备选型"两张表(<see cref="SmallRoomTable"/>),一并在计算书里给出。
    /// </para>
    /// </summary>
    public static class ResultFormatter
    {
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
            sb.AppendLine("面积来源:站厅公共区 " + areas.HallAreaM2.ToString("N1") + " m²," +
                          "站台公共区 " + areas.PlatformAreaM2.ToString("N1") + " m²(与「公共区参数」同一份输入)。");
            return sb.ToString();
        }

        /// <summary>小系统计算书(系统结果表 + 房间明细表 + 设备选型表)。</summary>
        public static string FormatSmall(SmallSystemInput x, SmallSystemResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("【小系统计算书】" + ResultTable.SystemTypeName(r.SystemType) +
                          (x == null || string.IsNullOrEmpty(x.SystemCode) ? "" : " · " + x.SystemCode) +
                          "(需求 2.2.3.2;公式源《小系统空调负荷、送排风、排烟计算公式.docx》)");
            sb.AppendLine();
            sb.AppendLine(ResultTable.ForSmallSystem(x, r).ToText());
            sb.AppendLine();
            sb.Append(SmallRoomTable.ToText(x, r));
            if (!string.IsNullOrEmpty(r.PendingNote))
            {
                sb.AppendLine();
                sb.AppendLine("⚠ " + r.PendingNote);
            }
            return sb.ToString();
        }
    }
}

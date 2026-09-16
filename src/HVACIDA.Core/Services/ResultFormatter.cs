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

        /// <summary>
        /// **全站水力计算书**:全站合计(可加量)+ 逐系统一行(需求值 / 校核 / 并联平衡)+ 每套系统的完整计算书。
        /// <para>口径与单系统一致:**逐系统各自算,汇总只相加可加量**;压力类只列不求和。</para>
        /// </summary>
        public static string FormatHydraulicSummary(HydraulicSummary summary)
        {
            if (summary == null) return "";
            var sb = new StringBuilder();
            sb.AppendLine("【全站水力计算书】(需求 2.3 / 2.4)");
            sb.AppendLine();
            sb.AppendLine(ResultTable.ForHydraulicSummary(summary).ToText());

            if (summary.Rows.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("—— 逐系统(压力类不可加,逐套列出) ——");
                sb.AppendLine(Pad("  介质", 10) + Pad("系统编号", 18) + Pad("系统名", 26) + Pad("段数", 8) +
                              Pad("设计流量 m³/h", 15) + Pad("计算总阻力 Pa", 16) + Pad("需求值", 16) +
                              Pad("校核", 10) + "最大不平衡 %");
                foreach (var row in summary.Rows)
                {
                    string required = row.Kind == HydraulicKind.WaterPipe
                        ? row.RequiredHeadM.ToString("N2") + " m"
                        : row.RequiredPressurePa.ToString("N1") + " Pa";
                    string check = double.IsNaN(row.Result == null ? double.NaN : row.Result.MarginPct)
                        ? "未校核"
                        : (row.Result.MarginPct < 0 ? "不足" : (row.Result.MarginPct < 10 ? "偏紧" : "满足"));
                    sb.AppendLine(Pad(row.KindName, 10) +
                                  Pad(string.IsNullOrEmpty(row.SystemCode) ? "—" : row.SystemCode, 18) +
                                  Pad(row.SystemName, 26) +
                                  Pad(row.SegmentCount.ToString(), 8) +
                                  Pad(row.DesignFlowM3H.ToString("N0"), 15) +
                                  Pad(row.TotalResistancePa.ToString("N1"), 16) +
                                  Pad(required, 16) +
                                  Pad(check, 10) +
                                  (row.BranchCount > 0 ? row.MaxImbalancePct.ToString("0.#") : "—"));
                }
            }

            foreach (var row in summary.Rows)
            {
                sb.AppendLine();
                sb.AppendLine("==================== " + row.KindName + " " +
                              (string.IsNullOrEmpty(row.SystemCode) ? "" : row.SystemCode) + " " + row.SystemName +
                              " ====================");
                sb.AppendLine(row.ResultText ?? "");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 水力计算书(风系统 / 水系统):系统与介质 + 最不利环路阻力 + 需求值 + 设备校核
        /// (由 <see cref="ResultTable.ForHydraulic"/> 渲染),再附**逐段明细**、环路阻力项与本次用到的局部阻力系数。
        /// </summary>
        public static string FormatHydraulic(HydraulicInput input, HydraulicResult result, HydraulicCoefficients coefficients = null)
        {
            if (result == null) return "";
            bool water = result.Kind == HydraulicKind.WaterPipe;

            var sb = new StringBuilder();
            sb.AppendLine(water ? "【水系统水力计算书】(需求 2.4)" : "【风系统水力计算书】(需求 2.3)");
            if (input != null && !string.IsNullOrEmpty(input.SourceNote))
            {
                sb.AppendLine("数据来源:" + input.SourceNote);
            }
            sb.AppendLine();
            sb.AppendLine(ResultTable.ForHydraulic(input, result).ToText());

            sb.AppendLine();
            sb.AppendLine("—— 管段明细(★ = 最不利环路上的管段) ——");
            sb.AppendLine(Pad("  段名", 34) + Pad("断面", 18) + Pad("流量 m³/h", 12) + Pad("流速 m/s", 11) +
                          Pad("λ", 10) + Pad("比摩阻 Pa/m", 13) + Pad("长度 m", 10) + Pad("沿程 Pa", 11) +
                          Pad("Σζ", 8) + Pad("局部 Pa", 11) + "合计 Pa");
            foreach (var row in result.Segments)
            {
                sb.AppendLine(Pad((row.OnCriticalPath ? "★ " : "  ") + row.Name, 34) +
                              Pad(row.SectionText, 18) +
                              Pad(row.FlowM3H.ToString("N0"), 12) +
                              Pad(row.VelocityMs.ToString("N2"), 11) +
                              Pad(row.FrictionFactor.ToString("0.0000"), 10) +
                              Pad(row.SpecificFrictionPaPerM.ToString("N2"), 13) +
                              Pad(row.LengthM.ToString("N2"), 10) +
                              Pad(row.FrictionLossPa.ToString("N1"), 11) +
                              Pad(row.LocalZetaSum.ToString("0.##"), 8) +
                              Pad(row.LocalLossPa.ToString("N1"), 11) +
                              row.TotalLossPa.ToString("N1"));
                if (!string.IsNullOrEmpty(row.LocalNote)) sb.AppendLine("      管件:" + row.LocalNote);
            }

            if (result.Items.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("—— 环路阻力项(★ = 计入最不利环路) ——");
                foreach (var item in result.Items)
                {
                    sb.AppendLine(Pad((item.OnCriticalPath ? "★ " : "  ") + item.KindText, 12) + Pad(item.Name, 34) +
                                  Pad(item.ResistancePa.ToString("N1") + " Pa", 16) +
                                  (string.IsNullOrEmpty(item.Source) ? "" : "(" + item.Source + ")"));
                }
            }

            sb.AppendLine();
            sb.AppendLine("—— 本次局部阻力系数取值(表内逐项可改;来源见括号) ——");
            foreach (var item in LocalLossItemsFor(coefficients, water))
            {
                sb.AppendLine("  " + Pad(item.Name, 26) + Pad("ζ=" + item.Zeta.ToString("0.##"), 12) + item.Source);
            }

            // 并联环路平衡(逐支路)
            sb.AppendLine();
            sb.AppendLine("—— 并联环路平衡(逐支路;★ = 最不利环路即平衡基准) ——");
            if (result.HasBranches)
            {
                sb.AppendLine(Pad("  支路(末端)", 30) + Pad("段数", 8) + Pad("管段 Pa", 12) + Pad("末端 Pa", 12) +
                              Pad("支路合计 Pa", 14) + Pad("不平衡 Pa", 12) + "不平衡 %");
                foreach (var branch in result.Branches)
                {
                    sb.AppendLine(Pad((branch.IsCritical ? "★ " : "  ") + branch.Name, 30) +
                                  Pad(branch.SegmentCount.ToString(), 8) +
                                  Pad(branch.SegmentLossPa.ToString("N1"), 12) +
                                  Pad(branch.TerminalPa.ToString("N1"), 12) +
                                  Pad(branch.TotalLossPa.ToString("N1"), 14) +
                                  Pad(branch.ImbalancePa.ToString("N1"), 12) +
                                  branch.ImbalancePct.ToString("0.#"));
                    if (!string.IsNullOrEmpty(branch.Conclusion)) sb.AppendLine("      结论:" + branch.Conclusion);
                    if (!string.IsNullOrEmpty(branch.Path)) sb.AppendLine("      路径:" + branch.Path);
                }
            }
            else
            {
                sb.AppendLine("  (没有支路拓扑数据,未做并联平衡分析)");
            }

            // 系统阻力特性曲线
            sb.AppendLine();
            sb.AppendLine("—— 系统阻力特性曲线(与厂家设备性能曲线的交点即工况点) ——");
            if (result.HasCurve)
            {
                sb.AppendLine(Pad("  流量比 %", 14) + Pad("流量 m³/h", 14) + Pad("系统阻力 Pa", 14) + "需求值 Pa");
                foreach (var point in result.Curve)
                {
                    sb.AppendLine(Pad(point.FlowRatioPct.ToString("0"), 14) +
                                  Pad(point.FlowM3H.ToString("N0"), 14) +
                                  Pad(point.ResistancePa.ToString("N1"), 14) +
                                  point.RequiredPa.ToString("N1"));
                }
                sb.AppendLine("  口径:ΔP(Q) = 静压 + (总阻力 − 静压) × (Q ÷ Q设计)²;");
                sb.AppendLine("        插件不内置风机/水泵性能曲线,工况点请用厂家样本曲线与本表求交。");
            }
            else
            {
                sb.AppendLine("  (没有流量数据,未给出曲线)");
            }

            if (!string.IsNullOrEmpty(result.PendingNote))
            {
                sb.AppendLine();
                sb.AppendLine("⚠ 待补 / 局限:" + result.PendingNote);
            }
            return sb.ToString();
        }

        /// <summary>本次介质用到的局部阻力系数表(优先取系数集里的表;没有则给默认表)。</summary>
        private static System.Collections.Generic.IList<HydraulicLocalLossItem> LocalLossItemsFor(
            HydraulicCoefficients coefficients, bool water)
        {
            if (coefficients != null && coefficients.LocalLossItems != null && coefficients.LocalLossItems.Count > 0)
            {
                var matched = new System.Collections.Generic.List<HydraulicLocalLossItem>();
                string wanted = water ? "水管" : "风管";
                foreach (var item in coefficients.LocalLossItems)
                {
                    if (item == null) continue;
                    if (string.Equals(item.AppliesTo, wanted, System.StringComparison.Ordinal)) matched.Add(item);
                }
                if (matched.Count > 0) return matched;
            }
            return water ? HydraulicLocalLossTable.CreatePipeDefaults() : HydraulicLocalLossTable.CreateDuctDefaults();
        }

        private static string Pad(string text, int width)
        {
            text = text ?? "";
            int w = 0;
            foreach (char ch in text) w += ch > 0x2E80 ? 2 : 1;
            return w >= width ? text + " " : text + new string(' ', width - w);
        }
    }
}

using System;
using System.Collections.Generic;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 水力计算书的 **Excel(.xlsx)导出**(需求 2.2.4 结果管理:计算书升级 Excel)。
    /// <para>
    /// 纪律与界面一致:**Excel 里的数字不是另外拼的** —— 汇总页直接由 Core 的
    /// <see cref="ResultTable.ForHydraulic"/> / <see cref="ResultTable.ForHydraulicSummary"/> 渲染
    /// (与界面表格、文本计算书同一份数据),逐段 / 阻力项 / 并联平衡 / 特性曲线页用的是同一批结果对象。
    /// </para>
    /// <para>
    /// 单系统工作簿 6 页:汇总 · 管段明细 · 环路阻力项 · 并联环路平衡 · 阻力特性曲线 · 取值与口径。
    /// 全站汇总工作簿:全站汇总 · 逐系统 · 取值与口径 + 每套系统的明细页(超过 12 套时只出前 12 套的明细并在首页注明)。
    /// </para>
    /// </summary>
    public static class HydraulicExcelExporter
    {
        /// <summary>全站汇总里最多附几套系统的明细页(避免工作簿过大)。</summary>
        public const int MaxDetailSystems = 12;

        // ================================================================== 单系统

        /// <summary>单系统水力计算书工作簿。</summary>
        public static XlsxWorkbook BuildSystem(HydraulicInput input, HydraulicResult result, HydraulicCoefficients coefficients)
        {
            var workbook = new XlsxWorkbook();
            if (result == null) return workbook;
            bool water = result.Kind == HydraulicKind.WaterPipe;

            // 1) 汇总(直接渲染 ResultTable,与界面/文本计算书同源)
            AddResultTable(workbook.AddSheet("汇总"), ResultTable.ForHydraulic(input, result));

            // 2) 管段明细
            var segments = workbook.AddSheet("管段明细");
            segments.AddHeader("段名", "断面", "流量 m³/h", "流速 m/s", "雷诺数", "λ", "比摩阻 Pa/m",
                "长度 m", "沿程 Pa", "Σζ", "局部 Pa", "段合计 Pa", "最不利环路", "数据来源");
            foreach (var row in result.Segments)
            {
                segments.AddRow(row.Name, row.SectionText, row.FlowM3H, row.VelocityMs, row.Reynolds,
                    row.FrictionFactor, row.SpecificFrictionPaPerM, row.LengthM, row.FrictionLossPa,
                    row.LocalZetaSum, row.LocalLossPa, row.TotalLossPa, row.OnCriticalPath ? "★" : "", row.Source);
            }

            // 3) 环路阻力项
            var items = workbook.AddSheet("环路阻力项");
            items.AddHeader("类别", "名称", "阻力 Pa", "来源", "计入最不利环路");
            foreach (var item in result.Items)
            {
                items.AddRow(item.KindText, item.Name, item.ResistancePa, item.Source, item.OnCriticalPath ? "是" : "否");
            }

            // 4) 并联环路平衡
            var branches = workbook.AddSheet("并联环路平衡");
            branches.AddHeader("支路(末端)", "段数", "管段 Pa", "末端 Pa", "支路合计 Pa", "不平衡 Pa", "不平衡 %",
                "需吸收 Pa", water ? "平衡阀 Kv" : "需增加 ζ", "阀权度", "结论", "路径");
            foreach (var branch in result.Branches)
            {
                branches.AddRow(branch.Name, branch.SegmentCount, branch.SegmentLossPa, branch.TerminalPa,
                    branch.TotalLossPa, branch.ImbalancePa, branch.ImbalancePct, branch.RequiredAbsorbPa,
                    water ? branch.ValveKv : branch.ZetaToAdd, branch.ValveAuthority, branch.Conclusion, branch.Path);
            }
            branches.AddBlankRow();
            branches.AddRow("允许不平衡率 %", result.ImbalanceLimitPct);
            branches.AddRow("口径", result.BalanceNote);

            // 5) 阻力特性曲线
            var curve = workbook.AddSheet("阻力特性曲线");
            curve.AddHeader("流量比 %", "流量 m³/h", "系统阻力 Pa", "需求值 Pa");
            foreach (var point in result.Curve)
            {
                curve.AddRow(point.FlowRatioPct, point.FlowM3H, point.ResistancePa, point.RequiredPa);
            }
            curve.AddBlankRow();
            curve.AddRow("口径", "ΔP(Q) = 静压 + (总阻力 − 静压) × (Q ÷ Q设计)²");
            curve.AddRow("提示", "与厂家风机 / 水泵性能曲线的交点才是工况点;插件不内置设备曲线,请用样本曲线核对。");

            // 6) 取值与口径
            AddCoefficientSheet(workbook.AddSheet("取值与口径"), input, result, coefficients);
            return workbook;
        }

        // ================================================================== 全站汇总

        /// <summary>全站多系统水力汇总工作簿。</summary>
        public static XlsxWorkbook BuildSummary(HydraulicSummary summary)
        {
            var workbook = new XlsxWorkbook();
            if (summary == null) return workbook;

            // 1) 全站汇总(直接渲染 ResultTable)
            AddResultTable(workbook.AddSheet("全站汇总"), ResultTable.ForHydraulicSummary(summary));

            // 2) 逐系统一行(压力类不可加,逐套列出)
            var rows = workbook.AddSheet("逐系统");
            rows.AddHeader("介质", "系统编号", "系统名", "段数", "最不利环路段数", "管段总长 m", "设计流量 m³/h",
                "计算总阻力 Pa", "需求值", "设备额定值", "余量 %", "校核结论", "并联支路", "最大不平衡 %", "超限支路");
            foreach (var row in summary.Rows)
            {
                var result = row.Result;
                string required = row.Kind == HydraulicKind.WaterPipe
                    ? row.RequiredHeadM.ToString("0.##") + " m"
                    : row.RequiredPressurePa.ToString("0.#") + " Pa";
                string rated = result == null
                    ? ""
                    : (row.Kind == HydraulicKind.WaterPipe
                        ? (result.RatedHeadM > 0 ? result.RatedHeadM.ToString("0.##") + " m" : "—")
                        : (result.RatedPressurePa > 0 ? result.RatedPressurePa.ToString("0.#") + " Pa" : "—"));
                string margin = result == null || double.IsNaN(result.MarginPct) ? "—" : result.MarginPct.ToString("0.#");
                string verdict = result == null ? "—" : result.CheckVerdict;
                rows.AddRow(row.KindName, string.IsNullOrEmpty(row.SystemCode) ? "—" : row.SystemCode, row.SystemName,
                    row.SegmentCount, row.CriticalSegmentCount, Math.Round(row.TotalLengthM, 1), row.DesignFlowM3H,
                    Math.Round(row.TotalResistancePa, 1), required, rated, margin, verdict,
                    row.BranchCount, row.BranchCount > 0 ? Math.Round(row.MaxImbalancePct, 1) : (object)"—",
                    row.UnbalancedBranchCount);
            }

            // 3) 取值与口径(全站共用系数)
            AddCoefficientSheet(workbook.AddSheet("取值与口径"), null, null, summary.Coefficients);

            // 4) 每套系统的明细页(超过上限只出前几套,并在首页注明)
            int detail = 0;
            foreach (var row in summary.Rows)
            {
                if (detail >= MaxDetailSystems) break;
                detail++;
                string label = (row.Kind == HydraulicKind.WaterPipe ? "水-" : "风-") +
                               (string.IsNullOrEmpty(row.SystemCode) ? row.SystemName : row.SystemCode);
                var segments = workbook.AddSheet("段-" + label);
                segments.AddHeader("段名", "断面", "流量 m³/h", "流速 m/s", "比摩阻 Pa/m", "长度 m",
                    "沿程 Pa", "Σζ", "局部 Pa", "段合计 Pa", "最不利环路");
                if (row.Result != null)
                {
                    foreach (var segment in row.Result.Segments)
                    {
                        segments.AddRow(segment.Name, segment.SectionText, segment.FlowM3H, segment.VelocityMs,
                            segment.SpecificFrictionPaPerM, segment.LengthM, segment.FrictionLossPa,
                            segment.LocalZetaSum, segment.LocalLossPa, segment.TotalLossPa,
                            segment.OnCriticalPath ? "★" : "");
                    }
                }

                var branches = workbook.AddSheet("衡-" + label);
                branches.AddHeader("支路(末端)", "端数", "支路合计 Pa", "不平衡 Pa", "不平衡 %", "需吸收 Pa",
                    row.Kind == HydraulicKind.WaterPipe ? "平衡阀 Kv" : "需增加 ζ", "结论");
                if (row.Result != null)
                {
                    foreach (var branch in row.Result.Branches)
                    {
                        branches.AddRow(branch.Name, branch.SegmentCount, branch.TotalLossPa, branch.ImbalancePa,
                            branch.ImbalancePct, branch.RequiredAbsorbPa,
                            row.Kind == HydraulicKind.WaterPipe ? branch.ValveKv : branch.ZetaToAdd,
                            branch.Conclusion);
                    }
                }
            }
            if (summary.Rows.Count > MaxDetailSystems)
            {
                var note = workbook.AddSheet("说明");
                note.AddRow("明细页数量上限",
                    "全站共 " + summary.Rows.Count + " 套系统,本工作簿只附了前 " + MaxDetailSystems +
                    " 套的管段与平衡明细(避免文件过大);其余系统请在各自的「水力计算」窗点【导出 Excel】。");
            }
            return workbook;
        }

        // ================================================================== 公共渲染

        /// <summary>把 Core 的结果表渲染成一张工作表(与界面/文本计算书同源)。</summary>
        public static void AddResultTable(XlsxSheet sheet, ResultTable table)
        {
            ExcelReportBuilder.AddResultTable(sheet, table);
        }

        private static void AddCoefficientSheet(XlsxSheet sheet, HydraulicInput input, HydraulicResult result,
            HydraulicCoefficients coefficients)
        {
            sheet.AddHeader("项", "取值", "单位 / 说明");
            sheet.AddRow("重力加速度 g", HvacConstantsG(), "m/s²");
            if (coefficients != null)
            {
                sheet.AddRow("风管绝对粗糙度 K", coefficients.DuctRoughnessMm, "mm(镀锌钢板常用 0.15)");
                sheet.AddRow("水管绝对粗糙度 K", coefficients.PipeRoughnessMm, "mm(焊接钢管常用 0.2)");
                sheet.AddRow("风系统富余系数", coefficients.AirExtraFactor, "");
                sheet.AddRow("水系统富余系数", coefficients.WaterExtraFactor, "");
                sheet.AddRow("并联环路允许不平衡率", coefficients.ImbalanceLimitPct, "%(工程通行口径 15%)");
            }
            if (result != null)
            {
                sheet.AddRow("介质", result.Kind == HydraulicKind.WaterPipe ? "水" : "空气", "");
                sheet.AddRow("介质温度", input == null ? 0 : input.MediumTempC, "℃");
                sheet.AddRow("密度 ρ(本次实际取值)", result.DensityKgM3, "kg/m³");
                sheet.AddRow("运动粘度 ν(本次实际取值)", result.KinematicViscosityM2S, "m²/s");
                sheet.AddRow("摩擦系数公式", "λ = 0.11×(K/d + 68/Re)^0.25(Re 小于 2320 按 64/Re)", "阿尔特舒利显式式");
                sheet.AddRow("局部阻力公式", "ΔP = Σζ × ρv²/2", "");
            }

            sheet.AddBlankRow();
            sheet.AddRow("局部阻力系数表(可在水力窗里改;项目应按手册或设备样本替换)");
            sheet.AddHeader("管件", "适用", "ζ", "来源");
            if (coefficients != null && coefficients.LocalLossItems != null)
            {
                foreach (var item in coefficients.LocalLossItems)
                {
                    if (item == null) continue;
                    sheet.AddRow(item.Name, item.AppliesTo, item.Zeta, item.Source);
                }
            }
        }

        private static double HvacConstantsG()
        {
            return HVACIDA.Core.Utils.HvacConstants.GravityM2S;
        }
    }

    /// <summary>
    /// Excel 计算书落盘器:默认写到 <c>%AppData%\HVACIDA\Reports</c>(与文本计算书同目录,
    /// 文件名带时间戳,不会互相覆盖)。
    /// </summary>
    public class ExcelReportGenerator
    {
        private readonly string _reportDirectory;

        public ExcelReportGenerator()
            : this(System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HVACIDA", "Reports"))
        {
        }

        public ExcelReportGenerator(string reportDirectory)
        {
            _reportDirectory = string.IsNullOrEmpty(reportDirectory)
                ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HVACIDA", "Reports")
                : reportDirectory;
        }

        /// <summary>计算书目录(界面提示用)。</summary>
        public string ReportDirectory => _reportDirectory;

        /// <summary>保存工作簿,返回完整路径。</summary>
        public string SaveWorkbook(string title, XlsxWorkbook workbook)
        {
            if (workbook == null) throw new ArgumentNullException(nameof(workbook));
            return workbook.SaveTo(_reportDirectory, title);
        }
    }
}

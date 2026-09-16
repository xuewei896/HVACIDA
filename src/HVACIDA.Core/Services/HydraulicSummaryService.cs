using System;
using System.Collections.Generic;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 全站水力汇总的一行(**一套系统**):输入 + 结果 + 可用于汇总的指标。
    /// </summary>
    public class HydraulicSummaryRow
    {
        /// <summary>介质(风 / 水)。</summary>
        public HydraulicKind Kind { get; set; } = HydraulicKind.AirDuct;

        /// <summary>介质文字。</summary>
        public string KindName => Kind == HydraulicKind.WaterPipe ? "水系统" : "风系统";

        /// <summary>系统编号(upsert 键)。</summary>
        public string SystemCode { get; set; } = "";

        /// <summary>系统名(模型里的名称)。</summary>
        public string SystemName { get; set; } = "";

        /// <summary>输入(选中的系统要看明细时用)。</summary>
        public HydraulicInput Input { get; set; }

        /// <summary>结果。</summary>
        public HydraulicResult Result { get; set; }

        /// <summary>该系统的完整计算书文本(与单系统窗同源)。</summary>
        public string ResultText { get; set; } = "";

        /// <summary>管段数。</summary>
        public int SegmentCount => Result == null ? 0 : Result.Segments.Count;

        /// <summary>最不利环路上的管段数。</summary>
        public int CriticalSegmentCount => Result == null ? 0 : Result.CriticalSegmentCount;

        /// <summary>管段总长 m(可加量)。</summary>
        public double TotalLengthM
        {
            get
            {
                if (Result == null) return 0;
                double sum = 0;
                foreach (var row in Result.Segments) sum += row.LengthM;
                return sum;
            }
        }

        /// <summary>设计流量 m³/h(可加量:全站总风量 / 总水量)。</summary>
        public double DesignFlowM3H => Result == null ? 0 : Result.DesignFlowM3H;

        /// <summary>计算总阻力 Pa(**不可加**:各系统独立)。</summary>
        public double TotalResistancePa => Result == null ? 0 : Result.TotalResistancePa;

        /// <summary>需求全压 Pa(风系统;**不可加**)。</summary>
        public double RequiredPressurePa => Result == null ? 0 : Result.RequiredPressurePa;

        /// <summary>需求扬程 m(水系统;**不可加**)。</summary>
        public double RequiredHeadM => Result == null ? 0 : Result.RequiredHeadM;

        /// <summary>并联支路数。</summary>
        public int BranchCount => Result == null || Result.Branches == null ? 0 : Result.Branches.Count;

        /// <summary>超出允许不平衡率的支路数。</summary>
        public int UnbalancedBranchCount => Result == null ? 0 : Result.UnbalancedBranchCount;

        /// <summary>最大不平衡率 %。</summary>
        public double MaxImbalancePct => Result == null ? 0 : Result.MaxImbalancePct;

        /// <summary>是否已校核(模型里读到了额定参数)。</summary>
        public bool Checked => Result != null && !double.IsNaN(Result.MarginPct);
    }

    /// <summary>
    /// **全站水力汇总**(需求 2.3 / 2.4;与小系统汇总同一套路:逐系统各自算,汇总只做"可加量"的相加)。
    /// <para>
    /// ⚠ **关键口径:风机全压与水泵扬程不能相加** —— 各系统是独立的管网,阻力不能求和。
    /// 因此合计只给**可加量**(系统数 / 管段数 / 管段总长 / 总风量 / 总水量 / 设备台数),
    /// 压力类给"**逐系统列出 + 取最大**",并在说明里写明理由 —— 不做一个看起来漂亮但没有物理意义的合计数。
    /// </para>
    /// </summary>
    public class HydraulicSummary
    {
        /// <summary>逐系统一行。</summary>
        public List<HydraulicSummaryRow> Rows { get; set; } = new List<HydraulicSummaryRow>();

        /// <summary>系数集(全站共用;界面的系数表与 Excel 的取值页都用它)。</summary>
        public HydraulicCoefficients Coefficients { get; set; } = HydraulicCoefficients.CreateDefault();

        /// <summary>系统总数。</summary>
        public int SystemCount => Rows.Count;

        /// <summary>风系统数。</summary>
        public int AirCount { get; set; }

        /// <summary>水系统数。</summary>
        public int WaterCount { get; set; }

        /// <summary>全部管段数(可加量)。</summary>
        public int SegmentCount { get; set; }

        /// <summary>全部管段总长 m(可加量)。</summary>
        public double TotalLengthM { get; set; }

        /// <summary>全站风系统设计风量合计 m³/h(可加量)。</summary>
        public double TotalAirFlowM3H { get; set; }

        /// <summary>全站水系统设计水量合计 m³/h(可加量)。</summary>
        public double TotalWaterFlowM3H { get; set; }

        /// <summary>末端 / 设备阻力项总数。</summary>
        public int EquipmentCount { get; set; }

        /// <summary>最大需求全压 Pa(风系统;不可加故取最大)。</summary>
        public double MaxRequiredPressurePa { get; set; }

        /// <summary>最大需求扬程 m(水系统;不可加故取最大)。</summary>
        public double MaxRequiredHeadM { get; set; }

        /// <summary>存在超限并联支路的系统数。</summary>
        public int UnbalancedSystemCount { get; set; }

        /// <summary>未做设备校核的系统数(模型里没读到额定参数)。</summary>
        public int UncheckedSystemCount { get; set; }

        /// <summary>汇总口径说明(界面与计算书都显示)。</summary>
        public string Note { get; set; } = "";

        /// <summary>待补 / 局限说明。</summary>
        public string PendingNote { get; set; } = "";

        /// <summary>是否有可汇总的系统。</summary>
        public bool HasRows => Rows.Count > 0;
    }

    /// <summary>
    /// 水力计算**全站汇总服务**:把容器里的每一套系统各自算一遍,再汇总"可加量"。
    /// <para>
    /// 与 <see cref="SmallSystemSummaryService"/> 同一纪律:**逐系统用各自的输入现算,汇总只相加、不跨系统重算** ——
    /// 所以汇总表里的数字与单系统窗里的数字必然一致。
    /// </para>
    /// </summary>
    public class HydraulicSummaryService
    {
        private readonly IHydraulicCalculator _calculator;

        public HydraulicSummaryService()
            : this(null)
        {
        }

        public HydraulicSummaryService(IHydraulicCalculator calculator)
        {
            _calculator = calculator ?? new HydraulicCalculator();
        }

        /// <summary>汇总整个水力工程(逐系统各算一遍)。</summary>
        public HydraulicSummary Summarize(HydraulicProject project)
        {
            var summary = new HydraulicSummary();
            if (project == null)
            {
                summary.Note = "还没有水力系统数据:请到「水力计算 → 风系统 / 水系统」窗点【从模型读取该系统…】录入。";
                return summary;
            }

            summary.Coefficients = project.Coefficients ?? HydraulicCoefficients.CreateDefault();

            var systems = project.Systems ?? new List<HydraulicInput>();

            // 固定顺序:风系统在前、水系统在后,各按系统编号排序 —— 汇总表/计算书/Excel 的行序不随录入顺序漂移
            var ordered = new List<HydraulicInput>();
            foreach (var system in systems) if (system != null) ordered.Add(system);
            ordered.Sort(delegate (HydraulicInput a, HydraulicInput b)
            {
                int kindCompare = a.Kind.CompareTo(b.Kind);
                if (kindCompare != 0) return kindCompare;
                int codeCompare = string.Compare(a.SystemCode ?? "", b.SystemCode ?? "", StringComparison.Ordinal);
                if (codeCompare != 0) return codeCompare;
                return string.Compare(a.SystemName ?? "", b.SystemName ?? "", StringComparison.Ordinal);
            });

            foreach (var input in ordered)
            {
                var result = _calculator.Calculate(input, summary.Coefficients);
                var row = new HydraulicSummaryRow
                {
                    Kind = input.Kind,
                    SystemCode = input.SystemCode ?? "",
                    SystemName = string.IsNullOrEmpty(input.SystemName) ? "(未命名)" : input.SystemName,
                    Input = input,
                    Result = result,
                    ResultText = ResultFormatter.FormatHydraulic(input, result, summary.Coefficients)
                };
                summary.Rows.Add(row);

                if (input.Kind == HydraulicKind.WaterPipe)
                {
                    summary.WaterCount++;
                    summary.TotalWaterFlowM3H += row.DesignFlowM3H;
                    if (row.RequiredHeadM > summary.MaxRequiredHeadM) summary.MaxRequiredHeadM = row.RequiredHeadM;
                }
                else
                {
                    summary.AirCount++;
                    summary.TotalAirFlowM3H += row.DesignFlowM3H;
                    if (row.RequiredPressurePa > summary.MaxRequiredPressurePa)
                        summary.MaxRequiredPressurePa = row.RequiredPressurePa;
                }

                summary.SegmentCount += row.SegmentCount;
                summary.TotalLengthM += row.TotalLengthM;
                summary.EquipmentCount += input.Terminals == null ? 0 : input.Terminals.Count;
                if (row.UnbalancedBranchCount > 0) summary.UnbalancedSystemCount++;
                if (!row.Checked) summary.UncheckedSystemCount++;
            }

            if (summary.Rows.Count == 0)
            {
                summary.Note = "水力工程里还没有系统:请到「水力计算 → 风系统 / 水系统」窗点【从模型读取该系统…】录入。";
                return summary;
            }

            summary.Note =
                "全站汇总口径:共 " + summary.SystemCount + " 套系统(风 " + summary.AirCount + " / 水 " + summary.WaterCount +
                "),**每套系统各自用自己的输入计算,汇总只对可加量求和**。" +
                "⚠ **风机全压与水泵扬程不可相加**(各系统管网相互独立,阻力不能求和),故只逐系统列出并给出最大值;";
            summary.PendingNote = "";
            if (summary.UnbalancedSystemCount > 0)
                summary.PendingNote += "有 " + summary.UnbalancedSystemCount + " 套系统存在超限并联支路(见逐系统明细的并联环路平衡表);";
            if (summary.UncheckedSystemCount > 0)
                summary.PendingNote += "有 " + summary.UncheckedSystemCount + " 套系统未做设备校核(模型里没读到风机全压 / 水泵扬程参数,可手工填额定值);";
            if (summary.PendingNote.Length == 0) summary.PendingNote = "";
            return summary;
        }
    }
}

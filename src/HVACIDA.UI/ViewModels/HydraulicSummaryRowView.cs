using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 水力计算全站汇总表的一行(**一套系统**:风或水)。
    /// <para>
    /// 「—」表示该系统不涉及该项(如风系统没有扬程与静压、水系统没有出口动压)或结果不存在,
    /// **不显示 0.00**;压力类**不能相加**,所以汇总表是"逐系统一行",合计只给可加量。
    /// </para>
    /// </summary>
    public class HydraulicSummaryRowView
    {
        public HydraulicSummaryRowView(HydraulicSummaryRow row)
        {
            Row = row;
        }

        /// <summary>Core 的汇总行(输入 + 结果 + 指标)。</summary>
        public HydraulicSummaryRow Row { get; }

        public HydraulicKind Kind => Row.Kind;

        public string KindName => Row.KindName;

        private bool IsWater => Row.Kind == HydraulicKind.WaterPipe;

        private HydraulicResult Result => Row.Result;

        private bool HasResult => Row.Result != null && Row.Result.HasSegments;

        /// <summary>系统编号(空显示「—」)。</summary>
        public string SystemCode => string.IsNullOrEmpty(Row.SystemCode) ? "—" : Row.SystemCode;

        public string SystemName => string.IsNullOrEmpty(Row.SystemName) ? "—" : Row.SystemName;

        /// <summary>最不利环路末端名。</summary>
        public string CriticalPathName => Result == null || string.IsNullOrEmpty(Result.CriticalPathName)
            ? "—"
            : Result.CriticalPathName;

        /// <summary>环路段数 / 总段数。</summary>
        public string SegmentText => HasResult ? Row.CriticalSegmentCount + " / " + Row.SegmentCount : "—";

        /// <summary>管段总长 m(可加量)。</summary>
        public string LengthText => HasResult ? Row.TotalLengthM.ToString("N1") + " m" : "—";

        /// <summary>设计流量 m³/h(可加量)。</summary>
        public string FlowText => Row.DesignFlowM3H > 0 ? Row.DesignFlowM3H.ToString("N0") + " m³/h" : "—";

        public string FrictionText => HasResult ? Pa(Result.FrictionTotalPa) : "—";

        public string LocalText => HasResult ? Pa(Result.LocalTotalPa) : "—";

        /// <summary>末端 + 设备阻力合计。</summary>
        public string EquipmentText => HasResult ? Pa(Result.EquipmentTotalPa + Result.TerminalTotalPa) : "—";

        /// <summary>出口动压(风系统才有)。</summary>
        public string OutletText => !HasResult || IsWater ? "—" : Pa(Result.OutletDynamicPa);

        /// <summary>静压(水系统才有)。</summary>
        public string StaticText => !HasResult || !IsWater ? "—" : Pa(Result.StaticPa);

        public string TotalText => HasResult ? Pa(Result.TotalResistancePa) : "—";

        /// <summary>需求值:风 = 需求全压(Pa);水 = 需求扬程(m)。</summary>
        public string RequiredText
        {
            get
            {
                if (!HasResult) return "—";
                return IsWater
                    ? Result.RequiredHeadM.ToString("N2") + " m"
                    : Result.RequiredPressurePa.ToString("N1") + " Pa";
            }
        }

        /// <summary>设备额定值(模型里读到的;没有给「—」)。</summary>
        public string RatedText
        {
            get
            {
                if (!HasResult) return "—";
                if (IsWater) return Result.RatedHeadM > 0 ? Result.RatedHeadM.ToString("N2") + " m" : "—";
                return Result.RatedPressurePa > 0 ? Result.RatedPressurePa.ToString("N1") + " Pa" : "—";
            }
        }

        public string MarginText => !HasResult || double.IsNaN(Result.MarginPct)
            ? "—"
            : Result.MarginPct.ToString("N1") + " %";

        /// <summary>并联支路数(没有拓扑数据给「—」)。</summary>
        public string BranchText => !HasResult || !Result.HasBranches ? "—" : Result.Branches.Count.ToString();

        public string ImbalanceText => !HasResult || !Result.HasBranches
            ? "—"
            : Result.MaxImbalancePct.ToString("N1") + " %";

        public string UnbalancedText => !HasResult || !Result.HasBranches
            ? "—"
            : Result.UnbalancedBranchCount.ToString();

        public string VerdictText => !HasResult || string.IsNullOrEmpty(Result.CheckVerdict)
            ? "—"
            : Result.CheckVerdict;

        private static string Pa(double value)
        {
            return value.ToString("N1") + " Pa";
        }
    }
}

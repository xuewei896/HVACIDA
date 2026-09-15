using HVACIDA.Core.Models;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 水力计算汇总表的一行(一个介质一行:风系统 / 水系统)。
    /// <para>
    /// 「—」表示该系统不涉及该项(如风系统没有扬程、水系统没有出口动压),**不显示 0.00** ——
    /// 与小系统汇总窗同一规矩:不涉及 ≠ 等于零。
    /// </para>
    /// </summary>
    public class HydraulicSummaryRowView
    {
        public HydraulicSummaryRowView(HydraulicKind kind, HydraulicInput input, HydraulicResult result)
        {
            Kind = kind;
            Input = input;
            Result = result;
            KindName = kind == HydraulicKind.WaterPipe ? "水系统" : "风系统";
        }

        public HydraulicKind Kind { get; }

        public HydraulicInput Input { get; }

        public HydraulicResult Result { get; }

        public string KindName { get; }

        public string SystemName => Result == null || string.IsNullOrEmpty(Result.SystemName)
            ? (Input == null || string.IsNullOrEmpty(Input.SystemName) ? "—" : Input.SystemName)
            : Result.SystemName;

        public string CriticalPathName => Result == null || string.IsNullOrEmpty(Result.CriticalPathName)
            ? "—"
            : Result.CriticalPathName;

        /// <summary>管段数(总 / 最不利环路上)。</summary>
        public string SegmentText
        {
            get
            {
                if (Result == null || !Result.HasSegments) return "—";
                return Result.CriticalSegmentCount + " / " + Result.Segments.Count;
            }
        }

        public string FrictionText => Text(Result, r => r.FrictionTotalPa);
        public string LocalText => Text(Result, r => r.LocalTotalPa);

        /// <summary>设备 + 末端阻力合计。</summary>
        public string EquipmentText => Result == null ? "—" : Pa(Result.EquipmentTotalPa + Result.TerminalTotalPa);

        /// <summary>出口动压(风系统才有)。</summary>
        public string OutletText => Result == null || Result.Kind == HydraulicKind.WaterPipe ? "—" : Pa(Result.OutletDynamicPa);

        /// <summary>静压(水系统才有;风系统为「—」)。</summary>
        public string StaticText => Result == null || Result.Kind != HydraulicKind.WaterPipe ? "—" : Pa(Result.StaticPa);

        public string TotalText => Text(Result, r => r.TotalResistancePa);

        /// <summary>需求值:风系统 = 需求全压(Pa);水系统 = 需求扬程(m)。</summary>
        public string RequiredText
        {
            get
            {
                if (Result == null) return "—";
                return Result.Kind == HydraulicKind.WaterPipe
                    ? Result.RequiredHeadM.ToString("N2") + " m"
                    : Result.RequiredPressurePa.ToString("N1") + " Pa";
            }
        }

        /// <summary>额定值(模型读到的;读不到给「—」)。</summary>
        public string RatedText
        {
            get
            {
                if (Result == null) return "—";
                if (Result.Kind == HydraulicKind.WaterPipe)
                    return Result.RatedHeadM > 0 ? Result.RatedHeadM.ToString("N2") + " m" : "—";
                return Result.RatedPressurePa > 0 ? Result.RatedPressurePa.ToString("N1") + " Pa" : "—";
            }
        }

        public string MarginText => Result == null || double.IsNaN(Result.MarginPct)
            ? "—"
            : Result.MarginPct.ToString("N1") + " %";

        public string VerdictText => Result == null || string.IsNullOrEmpty(Result.CheckVerdict)
            ? "—"
            : Result.CheckVerdict;

        private static string Text(HydraulicResult result, System.Func<HydraulicResult, double> selector)
        {
            if (result == null || !result.HasSegments) return "—";
            return Pa(selector(result));
        }

        private static string Pa(double value)
        {
            return value.ToString("N1") + " Pa";
        }
    }
}

using System;
using HVACIDA.Core.Utils;

namespace HVACIDA.Core.Models
{
    /// <summary>
    /// 大系统排烟计算参数(需求 2.2.3.1「排烟量计算 / 排烟风机选型」)。
    /// <para>
    /// <strong>站厅/站台公共区面积不在这里</strong> —— 面积来自 <see cref="LargeSystemInput"/>(D55/D56,
    /// 由「公共区参数」窗录入或模型取值),保持单一数据源;本类只放排烟专用的计算参数。
    /// </para>
    /// </summary>
    [Serializable]
    public class LargeSmokeInput
    {
        public LargeSmokeInput()
        {
            SetDocumentDefaults();
        }

        /// <summary>
        /// 单位面积排烟量 m³/(h·m²)。需求原文"站厅/站台总排烟量计算(按 60 倍/小时)",
        /// 公式文档示例单元格 C171 = D55×60 —— 即按**公共区面积**取 60 m³/(h·m²)(非体积换气)。
        /// </summary>
        public double SmokeRateM3HPerM2 { get; set; }

        /// <summary>
        /// 排烟风机选型系数(领域确认 2026-09-04):选型风量 = 计算风量 × 1.2
        /// (等效 防烟分区面积×72)。选型时才用,计算风量本身不加系数。
        /// </summary>
        public double SelectionFactor { get; set; }

        /// <summary>排烟风机台数(需求:2 台)。</summary>
        public double FanUnitCount { get; set; }

        /// <summary>恢复公式文档/需求默认值。</summary>
        public void SetDocumentDefaults()
        {
            SmokeRateM3HPerM2 = HvacConstants.SmokeAirChangesPerHour;      // 60
            SelectionFactor = HvacConstants.SmokeExhaustSelectionFactor;    // 1.2
            FanUnitCount = HvacConstants.SmokeFanUnitCount;                 // 2
        }
    }
}

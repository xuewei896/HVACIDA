using System;
using System.Collections.Generic;

namespace HVACIDA.Core.Models
{
    /// <summary>
    /// 排烟计算结果(需求 2.2.3.1「排烟量计算 + 排烟风机选型」)。
    /// 界面与计算书都以 <see cref="Zones"/> 为**表格的数据源**(一行一个区域)。
    /// </summary>
    public class LargeSmokeResult
    {
        /// <summary>分区结果行(站厅公共区 / 站台公共区)。</summary>
        public List<LargeSmokeZoneRow> Zones { get; } = new List<LargeSmokeZoneRow>();

        /// <summary>风机选型基准区名称(站厅/站台计算排烟量的较大者)。</summary>
        public string GoverningZoneName { get; set; } = "";

        /// <summary>基准区的计算排烟量 m³/h。</summary>
        public double GoverningCalculatedFlowM3H { get; set; }

        /// <summary>基准区的选型风量 m³/h(= 计算 × 选型系数)。</summary>
        public double GoverningSelectionFlowM3H { get; set; }

        /// <summary>排烟风机台数(需求:2 台)。</summary>
        public double FanUnitCount { get; set; }

        /// <summary>单台排烟风机选型风量 m³/h(= 基准区选型风量 / 台数)。</summary>
        public double UnitSelectionFlowM3H { get; set; }

        /// <summary>
        /// 公式文档口径的"单台排烟风机风量" = MAX(站厅, 站台) / 2 —— **不含选型系数**,
        /// 与《大系统负荷计算公式-示例.xls》单元格 E178 及 30 项回归口径一致。
        /// 与 <see cref="UnitSelectionFlowM3H"/> 并列给出,便于与公式文档逐格对齐。
        /// </summary>
        public double UnitFlowPerFormulaDocM3H { get; set; }

        /// <summary>本次计算所用参数(便于计算书/界面回显)。</summary>
        public double SmokeRateM3HPerM2 { get; set; }

        /// <summary>本次计算所用选型系数。</summary>
        public double SelectionFactor { get; set; }

        /// <summary>口径说明(界面状态栏 / 计算书抬头)。</summary>
        public string Note { get; set; } = "";

        /// <summary>待补项说明(防烟分区几何) —— 界面必须显示,避免把整体分区口径当成最终结论。</summary>
        public string PendingNote { get; set; } = "";
    }

    /// <summary>排烟计算结果表的一行。</summary>
    public class LargeSmokeZoneRow
    {
        /// <summary>区域名(站厅公共区 / 站台公共区)。</summary>
        public string ZoneName { get; set; } = "";

        /// <summary>面积 m²(D55 / D56)。</summary>
        public double AreaM2 { get; set; }

        /// <summary>计算排烟量 m³/h(面积 × 单位面积排烟量)。</summary>
        public double CalculatedFlowM3H { get; set; }

        /// <summary>选型排烟量 m³/h(计算排烟量 × 选型系数)。</summary>
        public double SelectionFlowM3H { get; set; }

        /// <summary>单台风机风量 m³/h(选型排烟量 / 台数)。</summary>
        public double UnitFlowM3H { get; set; }

        /// <summary>是否为风机选型基准区(取大者)。</summary>
        public bool IsGoverning { get; set; }
    }
}

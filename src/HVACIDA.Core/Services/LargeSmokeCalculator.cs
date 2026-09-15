using System;
using System.Collections.Generic;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>大系统排烟计算器接口(需求 2.2.3.1 排烟量计算 + 排烟风机选型)。</summary>
    public interface ILargeSmokeCalculator
    {
        /// <param name="areas">公共区面积来源(取 D55/D56,与负荷计算同一份输入)。</param>
        /// <param name="parameters">排烟计算参数(单位面积排烟量 / 选型系数 / 风机台数)。</param>
        LargeSmokeResult Calculate(LargeSystemInput areas, LargeSmokeInput parameters);
    }

    /// <summary>
    /// 大系统排烟计算(需求 2.2.3.1)。口径(与《大系统负荷计算公式.docx》及 2026-09-04 领域确认一致):
    /// <list type="number">
    ///   <item><strong>计算排烟量 = 公共区面积 × 60 m³/(h·m²)</strong>
    ///         —— 需求原文"按 60 倍/小时",示例单元格 C171 = D55×60、D171 = D56×60(按面积,非体积换气);</item>
    ///   <item><strong>选型排烟量 = 计算排烟量 × 1.2</strong>(2026-09-04 决策,等效 防烟分区面积×72);</item>
    ///   <item><strong>排烟风机取站厅、站台中的大者</strong>(需求:"单台排烟风机风量计算(取站厅、站台最大值的一半)");</item>
    ///   <item><strong>风机 2 台</strong>,单台选型风量 = 基准区选型风量 ÷ 2。</item>
    /// </list>
    /// <para>
    /// <strong>已知过渡口径(界面必须同时显示)</strong>:防烟分区几何(分区面积、挡烟垂壁、储烟仓)尚未接入模型,
    /// 当前把站厅/站台公共区**各当作一个分区**计算;分区几何到位后应改为"逐防烟分区取量、风机按最大分区选型",
    /// 那时本口径给出的量会偏大(整体面积 ≥ 任一单个分区)。
    /// </para>
    /// </summary>
    public class LargeSmokeCalculator : ILargeSmokeCalculator
    {
        public LargeSmokeResult Calculate(LargeSystemInput areas, LargeSmokeInput parameters)
        {
            if (areas == null) throw new ArgumentNullException(nameof(areas));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));

            var result = new LargeSmokeResult
            {
                FanUnitCount = parameters.FanUnitCount,
                SmokeRateM3HPerM2 = parameters.SmokeRateM3HPerM2,
                SelectionFactor = parameters.SelectionFactor,
                Note = "计算排烟量 = 面积 × " + Num(parameters.SmokeRateM3HPerM2) + " m³/(h·m²);" +
                       "选型排烟量 = 计算排烟量 × " + Num(parameters.SelectionFactor) + "(2026-09-04 决策);" +
                       "排烟风机 " + Num(parameters.FanUnitCount) + " 台,按站厅/站台大者选型。",
                PendingNote = "待补:防烟分区几何(分区面积、挡烟垂壁、储烟仓)未接入模型 —— " +
                              "当前按「站厅公共区」「站台公共区」各作为一个分区计算;" +
                              "接入后应逐防烟分区取量、风机按最大分区选型(现口径偏大)。"
            };

            result.Zones.Add(BuildRow("站厅公共区", "D55", areas.HallAreaM2, parameters));
            result.Zones.Add(BuildRow("站台公共区", "D56", areas.PlatformAreaM2, parameters));

            // 选型基准:计算排烟量的较大者(与公式文档 MAX(C171,D171) 同义)
            LargeSmokeZoneRow governing = result.Zones[0];
            foreach (var zone in result.Zones)
            {
                if (zone.CalculatedFlowM3H > governing.CalculatedFlowM3H) governing = zone;
            }
            governing.IsGoverning = true;

            result.GoverningZoneName = governing.ZoneName;
            result.GoverningCalculatedFlowM3H = governing.CalculatedFlowM3H;
            result.GoverningSelectionFlowM3H = governing.SelectionFlowM3H;
            result.UnitFlowPerFormulaDocM3H = parameters.FanUnitCount > 0
                ? governing.CalculatedFlowM3H / parameters.FanUnitCount
                : 0;
            result.UnitSelectionFlowM3H = governing.UnitFlowM3H;

            return result;
        }

        private static LargeSmokeZoneRow BuildRow(string zoneName, string cell, double areaM2, LargeSmokeInput p)
        {
            double calculated = areaM2 * p.SmokeRateM3HPerM2;
            double selection = calculated * p.SelectionFactor;
            return new LargeSmokeZoneRow
            {
                ZoneName = zoneName + "(" + cell + ")",
                AreaM2 = areaM2,
                CalculatedFlowM3H = calculated,
                SelectionFlowM3H = selection,
                UnitFlowM3H = p.FanUnitCount > 0 ? selection / p.FanUnitCount : 0
            };
        }

        private static string Num(double v)
        {
            return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}

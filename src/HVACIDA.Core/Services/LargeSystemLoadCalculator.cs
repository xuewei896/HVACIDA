using System;
using HVACIDA.Core.Models;
using HVACIDA.Core.Utils;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 大系统负荷计算(需求文档 2.2.3.1)——按《大系统负荷计算公式.docx》逐格移植。
    /// 每条语句注释保留单元格代号与中文名,方便与 Excel 计算书逐格比对;
    /// 饱和含湿量采用公式文档给出的 7 次多项式(温度℃→g/kg)。
    /// 已确认(2026-09-04,领域核对):公式文档 E159 中引用的 D143 系 C143(露点焓)之笔误,按 C143 计算。
    /// </summary>
    public class LargeSystemLoadCalculator : ILargeSystemLoadCalculator
    {
        public LargeSystemResult Calculate(LargeSystemInput x)
        {
            var r = new LargeSystemResult();
            double eps = 1e-9;

            // ================= 二、送风量中间参数 =================
            // 1. 高峰客流(个/min)
            // C35 = (A27+C27)/60*D29 + E27/60*D31   站厅上车高峰
            r.HallBoardingFlowPm =
                (x.UpLineBoardCount + x.DownLineBoardCount) / 60.0 * x.HallBoardStayMin +
                x.TransferBoardCount / 60.0 * x.HallTransferBoardStayMin;
            // C36 = (B27+D27)/60*D30 + F27/60*D32   站厅下车高峰
            r.HallAlightingFlowPm =
                (x.UpLineAlightCount + x.DownLineAlightCount) / 60.0 * x.HallAlightStayMin +
                x.TransferAlightCount / 60.0 * x.HallTransferAlightStayMin;
            // F35 / F36 站台同构
            r.PlatformBoardingFlowPm =
                (x.UpLineBoardCount + x.DownLineBoardCount) / 60.0 * x.PlatformBoardStayMin +
                x.TransferBoardCount / 60.0 * x.PlatformTransferBoardStayMin;
            r.PlatformAlightingFlowPm =
                (x.UpLineAlightCount + x.DownLineAlightCount) / 60.0 * x.PlatformAlightStayMin +
                x.TransferAlightCount / 60.0 * x.PlatformTransferAlightStayMin;
            // C39/C40 高峰客流 = (上+下)×集群×超高峰
            r.HallPeakFlowPm = (r.HallBoardingFlowPm + r.HallAlightingFlowPm) * x.ClusterFactor * x.SuperPeakHourFactor;
            r.PlatformPeakFlowPm = (r.PlatformBoardingFlowPm + r.PlatformAlightingFlowPm) * x.ClusterFactor * x.SuperPeakHourFactor;

            // 2. 站厅冷负荷合计 kW
            // C49=D45*C39→D95=C49/1000  站厅乘客显热
            r.HallSensibleKw = x.HallOccupantSensibleW * r.HallPeakFlowPm / 1000.0;
            // D49=E45*C39→D96=D49/1000  站厅乘客潜热
            r.HallLatentKw = x.HallOccupantLatentW * r.HallPeakFlowPm / 1000.0;
            // D97=C55*D55/1000 站厅照明
            r.HallLightingKw = x.HallLightingWm2 * x.HallAreaM2 / 1000.0;
            r.HallAdvertKw = x.HallAdvertKw; // D98=C58
            r.EscalatorTotalKw = x.EscalatorKwPerUnit * x.EscalatorCount;   // B64=B62*B63
            r.HallEscalatorKw = r.EscalatorTotalKw / 2.0;                   // D99=B64/2(照抄公式)
            r.ElevatorTotalKw = x.ElevatorKwPerUnit * x.ElevatorCount;      // D64=D62*D63
            r.HallElevatorKw = r.ElevatorTotalKw / 2.0;                     // D100=D64/2
            r.AfcTotalKw = x.AfcKwPerUnit * x.AfcCount;                     // E64=E62*E63
            r.HallAfcKw = r.AfcTotalKw;                                     // D101=E64
            // D82..D85 = B82×出入口宽×高/1000;D102=D86=Σ
            double entrance =
                x.EntranceLoadIndexW * x.EntranceAWidthM * x.EntranceAHeightM / 1000.0 +
                x.EntranceLoadIndexW * x.EntranceBWidthM * x.EntranceBHeightM / 1000.0 +
                x.EntranceLoadIndexW * x.EntranceCWidthM * x.EntranceCHeightM / 1000.0 +
                x.EntranceLoadIndexW * x.EntranceDWidthM * x.EntranceDHeightM / 1000.0;
            r.HallEntranceInfiltrationKw = entrance;                        // D102
            r.HallPsdTransferKw = x.HallPsdTransferKw;                      // D103(输入)
            r.HallPsdLeakKw = x.HallPsdLeakKw;                              // D104=A75
            r.HallPsdHeatKw = x.HallPsdHeatKw;                              // D105(输入)
            // D107=Σ(显热+潜热+照明+广告+扶梯+直梯+AFC+渗透+屏蔽门×3+其他)
            r.HallTotalCoolingKw = r.HallSensibleKw + r.HallLatentKw + r.HallLightingKw + r.HallAdvertKw +
                                   r.HallEscalatorKw + r.HallElevatorKw + r.HallAfcKw +
                                   r.HallEntranceInfiltrationKw + r.HallPsdTransferKw + r.HallPsdLeakKw +
                                   r.HallPsdHeatKw + x.HallExtraHeatKw;     // +D106

            // 3. 站厅湿负荷 g/s
            r.HallPeopleMoisture = r.HallPeakFlowPm * x.HallOccupantMoistureGH / 1000.0; // D108=C39*F45/1000
            r.HallStructureSurfaceM2 = x.HallHeightM * 2.0 * x.HallLengthM + x.HallAreaM2; // B91=C13*2*C14+D55
            r.HallStructureMoisture = x.WallMoistureEmission * r.HallStructureSurfaceM2 / 1000.0; // D109=A91*B91/1000
            r.HallTotalMoistureGps = (r.HallPeopleMoisture + r.HallStructureMoisture + x.HallOtherMoisture) * 1000.0 / 3600.0; // D112

            // 4/5/6/7. 热湿比线与焓(站厅)
            r.HallHeatHumidityRatio = r.HallTotalMoistureGps > eps ? r.HallTotalCoolingKw / r.HallTotalMoistureGps * 1000.0 : 0; // D113
            r.HallSupplyTempC = x.HallDesignTempC - x.SupplyTempDiffC;      // A118=F4-C8
            r.DewPointTempC = r.HallSupplyTempC - x.DuctTempRiseC;          // B118=A118-C10
            r.SaturatedMoistureAtDewGkg = SaturatedMoistureGPerKg(r.DewPointTempC); // D118 多项式(B118)
            r.DewPointMoistureGkg = x.DewPointRelativeHumidityPercent / 100.0 * r.SaturatedMoistureAtDewGkg; // E118=C118/100*D118
            r.HallSupplyEnthalpy = SupplyEnthalpy(r.HallSupplyTempC, r.DewPointMoistureGkg); // A121
            // B121=(A121*1000-E118*D113-1.01*F4*1000)/((2500+1.84*F4)-D113)
            double denomHall = (2500.0 + 1.84 * x.HallDesignTempC) - r.HallHeatHumidityRatio;
            r.HallIndoorHumidityGkg = Math.Abs(denomHall) > eps
                ? (r.HallSupplyEnthalpy * 1000.0 - r.DewPointMoistureGkg * r.HallHeatHumidityRatio - 1.01 * x.HallDesignTempC * 1000.0) / denomHall
                : r.DewPointMoistureGkg;
            // C121=1.01*F4+(2500+1.84*F4)*B121/1000+0.4
            r.HallIndoorEnthalpy = 1.01 * x.HallDesignTempC +
                                   (2500.0 + 1.84 * x.HallDesignTempC) * r.HallIndoorHumidityGkg / 1000.0 + 0.4;

            // 10. 站厅送风量 A125=D107/1.15/(C121-A121)*3600
            r.HallSupplyFlowM3H = SupplyFlowByEnthalpy(r.HallTotalCoolingKw, r.HallIndoorEnthalpy, r.HallSupplyEnthalpy);

            // 11. 站台冷负荷 kW
            r.PlatformSensibleKw = x.PlatformOccupantSensibleW * r.PlatformPeakFlowPm / 1000.0; // C50→E95
            r.PlatformLatentKw = x.PlatformOccupantLatentW * r.PlatformPeakFlowPm / 1000.0;     // D50→E96
            r.PlatformLightingKw = x.PlatformLightingWm2 * x.PlatformAreaM2 / 1000.0;            // E56→E97
            r.PlatformAdvertKw = x.PlatformAdvertKw;                                             // E98=C59
            r.PlatformEscalatorKw = r.EscalatorTotalKw / 2.0;                                    // E99=B64/2
            r.PlatformElevatorKw = r.ElevatorTotalKw / 2.0;                                      // E100=D64/2
            // F71=A71*B71*C71*D71*E71/1000 → E103 站台屏蔽门传热
            r.PlatformPsdTransferKw =
                x.PsdHeatTransferCoeffWm2C * x.PsdHeightM * x.PsdLengthM * x.PsdTempDiffC * x.PsdHeatSafetyFactor / 1000.0;
            r.PlatformPsdLeakKw = x.PlatformPsdLeakKw;      // E104=B75
            r.PlatformPsdHeatKw = x.PlatformPsdSystemHeatKw; // E105=C76
            // E107=Σ(E95..E100,E103..E106)
            r.PlatformTotalCoolingKw = r.PlatformSensibleKw + r.PlatformLatentKw + r.PlatformLightingKw +
                                       r.PlatformAdvertKw + r.PlatformEscalatorKw + r.PlatformElevatorKw +
                                       r.PlatformPsdTransferKw + r.PlatformPsdLeakKw + r.PlatformPsdHeatKw +
                                       x.PlatformExtraHeatKw;

            // 12. 站台湿负荷 g/s
            r.PlatformPeopleMoisture = r.PlatformPeakFlowPm * x.PlatformOccupantMoistureGH / 1000.0; // F50→E108
            r.PlatformTotalMoistureGps = (r.PlatformPeopleMoisture + x.PlatformStructureMoistureOverride + x.PlatformOtherMoisture) * 1000.0 / 3600.0; // E112

            // 13/14/15. 站台热湿比与焓、送风量
            r.PlatformHeatHumidityRatio = r.PlatformTotalMoistureGps > eps ? r.PlatformTotalCoolingKw / r.PlatformTotalMoistureGps * 1000.0 : 0; // E113
            // D121=(A121*1000-E118*E113-1.01*F6*1000)/((2500+1.84*F6)-E113)
            double denomPlatform = (2500.0 + 1.84 * x.PlatformDesignTempC) - r.PlatformHeatHumidityRatio;
            r.PlatformIndoorHumidityGkg = Math.Abs(denomPlatform) > eps
                ? (r.HallSupplyEnthalpy * 1000.0 - r.DewPointMoistureGkg * r.PlatformHeatHumidityRatio - 1.01 * x.PlatformDesignTempC * 1000.0) / denomPlatform
                : r.DewPointMoistureGkg;
            r.PlatformIndoorEnthalpy = 1.01 * x.PlatformDesignTempC +
                                       (2500.0 + 1.84 * x.PlatformDesignTempC) * r.PlatformIndoorHumidityGkg / 1000.0 + 0.4;
            r.PlatformSupplyFlowM3H = SupplyFlowByEnthalpy(r.PlatformTotalCoolingKw, r.PlatformIndoorEnthalpy, r.HallSupplyEnthalpy);

            // 16. 总送风量 C125
            r.TotalSupplyFlowM3H = r.HallSupplyFlowM3H + r.PlatformSupplyFlowM3H;

            // ================= 三、回风量 =================
            r.PublicAreaPeople = r.HallPeakFlowPm + r.PlatformPeakFlowPm;       // A132=C39+C40
            // A136=MAX(A132*B132, C125*0.1)
            r.ActualFreshAirM3H = Math.Max(r.PublicAreaPeople * x.FreshAirPerPersonM3H, r.TotalSupplyFlowM3H * 0.1);
            r.FreshAirRatio = r.TotalSupplyFlowM3H > eps ? r.ActualFreshAirM3H / r.TotalSupplyFlowM3H : 0; // B136
            r.HallReturnFlowM3H = r.HallSupplyFlowM3H * (1.0 - r.FreshAirRatio);    // C136
            r.PlatformReturnFlowM3H = r.PlatformSupplyFlowM3H * (1.0 - r.FreshAirRatio); // D136
            r.TotalReturnFlowM3H = r.HallReturnFlowM3H + r.PlatformReturnFlowM3H;    // E136

            // ================= 四、制冷量 =================
            // C145=(C121*C136+E121*D136)/E136 回风混合焓
            r.ReturnMixEnthalpy = r.TotalReturnFlowM3H > eps
                ? (r.HallIndoorEnthalpy * r.HallReturnFlowM3H + r.PlatformIndoorEnthalpy * r.PlatformReturnFlowM3H) / r.TotalReturnFlowM3H
                : 0;
            r.FreshSaturatedMoistureGkg = SaturatedMoistureGPerKg(x.OutdoorWetBulbC); // D149 多项式(C5)
            r.FreshEnthalpy = 1.01 * x.OutdoorWetBulbC +
                              (2500.0 + 1.84 * x.OutdoorWetBulbC) * r.FreshSaturatedMoistureGkg / 1000.0 + 0.4; // C149
            // C146=(C145*E136+C149*A136)/C125 新回风混合焓
            r.FreshReturnMixEnthalpy = r.TotalSupplyFlowM3H > eps
                ? (r.ReturnMixEnthalpy * r.TotalReturnFlowM3H + r.FreshEnthalpy * r.ActualFreshAirM3H) / r.TotalSupplyFlowM3H
                : 0;
            // C143 露点焓
            r.DewPointEnthalpy = 1.01 * r.DewPointTempC +
                                 (2500.0 + 1.84 * r.DewPointTempC) * r.DewPointMoistureGkg / 1000.0 + 0.4;
            // E159=C125*1.15*(C146-C143)/3600(已确认:公式文档 D143 系 C143 笔误)
            r.TotalCoolingKw = Safe(
                r.TotalSupplyFlowM3H * 1.15 * (r.FreshReturnMixEnthalpy - r.DewPointEnthalpy) / 3600.0);

            // ================= 五、排烟量 =================
            // 领域确认(2026-09-04):计算风量 = 公共区面积×60(即 C171/D171,与公式文档一致);
            // 选型风量 = 计算风量×1.2(=防烟分区×72),需防烟分区几何,当前由 MAX(C171,D171)/2 过渡(见 HvacConstants 注)。
            r.HallSmokeFlowM3H = x.HallAreaM2 * HvacConstants.SmokeAirChangesPerHour; // C171
            r.PlatformSmokeFlowM3H = x.PlatformAreaM2 * HvacConstants.SmokeAirChangesPerHour; // D171

            // ================= 六、单台设备选型 =================
            r.UnitSupplyFlowM3H = r.TotalSupplyFlowM3H / 2.0;                    // A165
            r.UnitCoolingKw = r.TotalCoolingKw / 2.0;                            // B165
            r.UnitReturnFlowM3H = r.TotalReturnFlowM3H / 2.0;                    // C178
            r.UnitSmokeFlowM3H = Math.Max(r.HallSmokeFlowM3H, r.PlatformSmokeFlowM3H) / 2.0; // E178

            return r;
        }

        /// <summary>送风点/露点焓:1.01t+(2500+1.84t)×d/1000+0.4,kJ/kg。</summary>
        private static double SupplyEnthalpy(double temperatureC, double moistureGPerKg)
        {
            return 1.01 * temperatureC + (2500.0 + 1.84 * temperatureC) * moistureGPerKg / 1000.0 + 0.4;
        }

        /// <summary>饱和含湿量 g/kg(温度℃)— 公式文档 7 次多项式。</summary>
        private static double SaturatedMoistureGPerKg(double t)
        {
            return -0.0000000004171 * Math.Pow(t, 7)
                   + 0.00000004843 * Math.Pow(t, 6)
                   - 0.000002133 * Math.Pow(t, 5)
                   + 0.00005009 * Math.Pow(t, 4)
                   - 0.0004032 * Math.Pow(t, 3)
                   + 0.01264 * Math.Pow(t, 2)
                   + 0.265 * t
                   + 3.787;
        }

        /// <summary>
        /// 送风量 = Q/1.15/(h_室-h_送)×3600(公式文档 A125/B125)。
        /// 退化保护:室内-送风焓差过低(如客流与湿负荷均为 0 的退化场景)时认为该区无有效
        /// 空调除热过程,送风量按 0 处理,避免除近零焓差导致的爆表风量。
        /// </summary>
        private static double SupplyFlowByEnthalpy(double coolingKw, double indoorEnthalpy, double supplyEnthalpy)
        {
            const double minEnthalpyDiff = 1.0; // kJ/kg;正常空调工况室内-送风焓差远大于该值
            double diff = indoorEnthalpy - supplyEnthalpy;
            if (diff < minEnthalpyDiff) return 0;
            return Safe(coolingKw / 1.15 / diff * 3600.0);
        }

        private static double Safe(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 0 : value;
        }
    }
}

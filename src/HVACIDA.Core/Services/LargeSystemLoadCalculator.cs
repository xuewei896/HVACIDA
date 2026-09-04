using HVACIDA.Core.Models;
using HVACIDA.Core.Utils;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 大系统负荷计算(需求文档 2.2.3.1)。
    /// 实现说明:框架已按文档条目落地;凡"显热/潜热/排烟/焓差"等数值口径均标注 TODO,
    /// 系数以 <see cref="HvacConstants"/> 集中管理,正式交付前须与《大系统负荷计算公式.docx》逐条核对。
    /// </summary>
    public class LargeSystemLoadCalculator : ILargeSystemLoadCalculator
    {
        public LargeSystemResult Calculate(LargeSystemInput x)
        {
            var r = new LargeSystemResult();

            // ---- 客流负荷 ----
            double totalOccupants = (x.HallOccupants + x.PlatformOccupants) * x.ClusterFactor * x.SuperPeakFactor;
            r.PeopleSensibleW = totalOccupants * x.OccupantSensibleHeatPerPersonW;
            double moistureKgH = totalOccupants * x.WaterVaporPerPersonGPerHour / 1000.0;
            r.PeopleLatentW = PsychrometricHelper.LatentHeatFromMoistureW(moistureKgH);
            // TODO(公式核对):上下车停留时间、换乘客流、站台散湿按人群活动强度修正。

            // ---- 照明 / 设备 / 屏蔽门 ----
            double airCondArea = x.HallAreaM2 + x.PlatformAreaM2;
            r.LightingW = airCondArea * x.LightingDensityWm2 * x.LightingUsageFactor;
            r.AdvertW = x.AdvertHeatW;
            r.EscalatorW = x.EscalatorCount * x.EscalatorHeatPerUnitW;
            r.ElevatorW = x.ElevatorCount * x.ElevatorHeatPerUnitW;
            r.AfcW = x.AfcHeatW;
            r.PsdW = x.PsdHeatW;

            r.TotalSensibleW = r.PeopleSensibleW + r.LightingW + r.AdvertW + r.EscalatorW + r.ElevatorW + r.AfcW + r.PsdW;
            r.TotalLatentW = r.PeopleLatentW;
            r.TotalCoolingW = r.TotalSensibleW + r.TotalLatentW;

            // ---- 送风量(按显热与送风温差) ----
            r.SupplyAirVolumeM3H = PsychrometricHelper.SensibleHeatAirVolumeM3H(r.TotalSensibleW, x.SupplyTemperatureDiffC);

            // ---- 新风量 = 人数×每人新风量 + 出入口渗透 ----
            double basePeople = (x.HallOccupants + x.PlatformOccupants) * x.ClusterFactor * x.SuperPeakFactor;
            r.FreshAirVolumeM3H = basePeople * x.FreshAirPerPersonM3H + x.InfiltrationAirVolumeM3H;
            if (r.FreshAirVolumeM3H > r.SupplyAirVolumeM3H)
            {
                // 新风不得小于按人计算值,但不得超过送风量。
                r.FreshAirVolumeM3H = r.SupplyAirVolumeM3H;
            }
            r.ReturnAirVolumeM3H = r.SupplyAirVolumeM3H - r.FreshAirVolumeM3H;
            r.FreshAirRatio = r.SupplyAirVolumeM3H > 1e-9 ? r.FreshAirVolumeM3H / r.SupplyAirVolumeM3H : 0;

            // ---- 焓值(室内/送风/混合,用于制冷量校核) ----
            // TODO(公式核对):站厅/站台按面积加权平均确定室内状态点,并区分回风/新风混合焓。
            r.CoolingByAirSideW = ComputeAirSideCooling(x, r);

            // ---- 排烟量(60 次/h,需求文档:单台取站厅/站台最大值的一半、2 台) ----
            r.SmokeHallM3H = x.HallAreaM2 * x.SmokeZoneHeightM * HvacConstants.SmokeAirChangesPerHour;
            r.SmokePlatformM3H = x.PlatformAreaM2 * x.SmokeZoneHeightM * HvacConstants.SmokeAirChangesPerHour;
            double smokeMax = System.Math.Max(r.SmokeHallM3H, r.SmokePlatformM3H);
            r.SmokeFanCount = HvacConstants.SmokeFanUnitCount;
            r.SmokeFanPerUnitM3H = smokeMax / HvacConstants.SmokeFanUnitCount;
            // TODO(公式核对):排烟是否以全部面积(含设备区)计算、层高取值方式。

            // ---- 设备选型(需求文档:取总量一半 → 台数 2 台) ----
            r.AhUnitAirVolumeM3H = r.SupplyAirVolumeM3H / HvacConstants.AhUnitCount;
            r.AhUnitCoolingW = r.TotalCoolingW / HvacConstants.AhUnitCount;
            r.ReturnFanPerUnitM3H = r.ReturnAirVolumeM3H / HvacConstants.AhUnitCount;
            return r;
        }

        private double ComputeAirSideCooling(LargeSystemInput x, LargeSystemResult r)
        {
            // 简化混合焓模型(质量流量近似体积流量):
            // h_mix = (G_回·h_室 + G_新·h_外)/G_送; Q = ρ·V_送/3600·(h_mix − h_送)。
            // TODO(公式核对):室内/送风焓由空气处理过程(露点/再热)确定,当前用温差近似送风点;
            // 室内外状态点正式值取 DesignConditionParams,以下温度 30/28/33.5/0.6 仅为演示。
            double hallShare = x.HallAreaM2 + x.PlatformAreaM2 > 0
                ? x.HallAreaM2 / (x.HallAreaM2 + x.PlatformAreaM2)
                : 0.5;
            double indoorTemp = hallShare * 30.0 + (1 - hallShare) * 28.0;
            double indoorD = PsychrometricHelper.HumidityRatioKgKg(0.6, indoorTemp, 101325);
            double indoorH = PsychrometricHelper.EnthalpyKJKg(indoorTemp, indoorD);

            double outdoorD = PsychrometricHelper.HumidityRatioKgKg(0.6, 33.5, 101325);
            double outdoorH = PsychrometricHelper.EnthalpyKJKg(33.5, outdoorD);

            double supplyTemp = indoorTemp - x.SupplyTemperatureDiffC;
            double supplyD = indoorD; // 近似:送风点含湿量=室内(TODO 按机器露点修正)
            double supplyH = PsychrometricHelper.EnthalpyKJKg(supplyTemp, supplyD);

            double freshRatio = r.FreshAirRatio;
            double mixedH = (1 - freshRatio) * indoorH + freshRatio * outdoorH;

            r.IndoorEnthalpyKJKg = indoorH;
            r.SupplyEnthalpyKJKg = supplyH;
            r.MixedEnthalpyKJKg = mixedH;

            double coolingW = HvacConstants.AirDensity * (r.SupplyAirVolumeM3H / 3600.0) * (mixedH - supplyH) * 1000.0;
            return coolingW > 0 ? coolingW : 0;
        }
    }
}

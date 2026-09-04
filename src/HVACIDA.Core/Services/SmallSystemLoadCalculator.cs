using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 小系统负荷计算(需求文档 2.2.3.2)。
    /// 当前完整实现:全空气一次回风系统(照明+人员+设备负荷,除热/换气/新风三条风量路径)。
    /// 其余系统类型返回状态提示,供后续按各自条目实现。
    /// TODO(公式核对):人员湿负荷、结构湿负荷、设备选型系数待核对公式文档。
    /// </summary>
    public class SmallSystemLoadCalculator : ISmallSystemLoadCalculator
    {
        public SmallSystemResult Calculate(SmallSystemInput x)
        {
            var r = new SmallSystemResult();
            switch (x.SystemType)
            {
                case SmallSystemType.AllAirOnceReturn:
                    CalculateAllAirOnceReturn(x, r);
                    break;
                default:
                    r.StatusMessage = "该小系统类型尚未实现(" + x.SystemType + ")。" +
                                      "TODO:按需求文档 2.2.3.2 对应条目补充算法。";
                    break;
            }
            return r;
        }

        private void CalculateAllAirOnceReturn(SmallSystemInput x, SmallSystemResult r)
        {
            // ---- 负荷 ----
            r.LightingW = x.AreaM2 * x.LightingDensityWm2;
            r.EquipmentW = x.AreaM2 * x.EquipmentDensityWm2;
            r.PeopleSensibleW = x.Occupants * x.OccupantSensibleW;
            r.TotalSensibleW = r.LightingW + r.EquipmentW + r.PeopleSensibleW;

            r.PeopleWaterVaporKgH = x.Occupants * x.WaterVaporPerPersonGPerHour / 1000.0;
            r.PeopleLatentW = PsychrometricHelper.LatentHeatFromMoistureW(r.PeopleWaterVaporKgH);
            r.TotalCoolingW = r.TotalSensibleW + r.PeopleLatentW;
            // TODO(公式核对):结构湿负荷(围护/敞开水面)当前按 0 处理。

            // ---- 通风量 ----
            double effectiveDiff = x.SupplyTempDiffC - x.DuctTempRiseC; // 有效送风温差
            r.VentilationByHeatM3H = PsychrometricHelper.SensibleHeatAirVolumeM3H(r.TotalSensibleW, effectiveDiff);
            r.VentilationByACHM3H = x.AreaM2 * x.HeightM * x.AirChangePerHour;
            r.ActualVentilationM3H = System.Math.Max(r.VentilationByHeatM3H, r.VentilationByACHM3H);

            r.FreshAirM3H = x.Occupants * x.FreshAirPerPersonM3H;
            if (r.FreshAirM3H > r.ActualVentilationM3H)
            {
                r.FreshAirM3H = r.ActualVentilationM3H;
            }

            r.EquipmentSelectionText =
                "柜式空调机组、回排风机选型 TODO(需求文档 2.2.3.2 全空气一次回风-设备选型)。";

            r.StatusMessage = "全空气一次回风系统:计算完成(公式待核对)。";
        }
    }
}

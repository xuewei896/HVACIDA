using System;
using HVACIDA.Core.Utils;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 湿空气热力学辅助(焓湿图基础)。
    /// 公式:Magnus 饱和水蒸气分压 ps=610.94·exp(17.625·t/(t+243.04)) Pa;
    /// 含湿量 d=0.622·φ·ps/(p−φ·ps)(kg/kg);
    /// 焓 h=1.006·t + d·(2501+1.86·t)(kJ/kg)。
    /// 单位约定:温度 ℃、压力 Pa、含湿量 kg/kg(对外 g/kg 换算)。
    /// </summary>
    public static class PsychrometricHelper
    {
        /// <summary>Magnus 公式饱和水蒸气分压 Pa(温度 ℃)。</summary>
        public static double SaturationPressurePa(double temperatureC)
        {
            return HvacConstants.MagnusA * Math.Exp(
                HvacConstants.MagnusB * temperatureC / (HvacConstants.MagnusC + temperatureC));
        }

        /// <summary>
        /// 由相对湿度与温度求含湿量。
        /// </summary>
        /// <param name="relativeHumidity">相对湿度 0~1</param>
        /// <param name="temperatureC">干球温度 ℃</param>
        /// <param name="atmosphericPressurePa">大气压力 Pa</param>
        /// <returns>含湿量 kg/kg</returns>
        public static double HumidityRatioKgKg(double relativeHumidity, double temperatureC, double atmosphericPressurePa)
        {
            if (relativeHumidity <= 0) return 0;
            double ps = SaturationPressurePa(temperatureC);
            double partial = relativeHumidity * ps;
            double p = Math.Max(atmosphericPressurePa, partial + 1e-6);
            return HvacConstants.GasConstantRatio * partial / (p - partial);
        }

        /// <summary>湿空气焓 kJ/kg。</summary>
        /// <param name="temperatureC">干球温度 ℃</param>
        /// <param name="humidityKgKg">含湿量 kg/kg</param>
        public static double EnthalpyKJKg(double temperatureC, double humidityKgKg)
        {
            return 1.006 * temperatureC + humidityKgKg * (HvacConstants.WaterLatentHeat + 1.86 * temperatureC);
        }

        /// <summary>由相对湿度与干球温度求露点温度 ℃(Magnus 反解,φ∈(0,1])。</summary>
        public static double DewPointC(double relativeHumidity, double temperatureC)
        {
            if (relativeHumidity <= 0) return -999.0; // 极干空气,无意义露点
            double ps = SaturationPressurePa(temperatureC);
            double vapor = Math.Max(relativeHumidity * ps, 1e-9);
            double ln = Math.Log(vapor / HvacConstants.MagnusA);
            return HvacConstants.MagnusC * ln / (HvacConstants.MagnusB - ln);
        }

        /// <summary>含湿量 g/kg = kg/kg×1000</summary>
        public static double GPerKg(double humidityKgKg) => humidityKgKg * 1000.0;

        /// <summary>
        /// 热湿比 ε = Q / W (kJ/kg)。Q 为全热 kW,W 为湿负荷 kg/h。
        /// </summary>
        public static double HeatHumidityRatio(double coolingKw, double moistureKgPerHour)
        {
            if (Math.Abs(moistureKgPerHour) < 1e-12) return double.PositiveInfinity;
            return (coolingKw * 3600.0) / moistureKgPerHour;
        }

        /// <summary>
        /// 消除显热的通风量 m³/h:V = Q_sensible(W)·3600 / (ρ·cp·ΔT)。
        /// ΔT 为有效温差 ℃(送风温差扣除管道温升等)。
        /// </summary>
        public static double SensibleHeatAirVolumeM3H(double sensibleW, double effectiveTempDiffC)
        {
            if (effectiveTempDiffC <= 1e-9) return 0;
            return sensibleW * 3600.0 / (HvacConstants.AirDensity * HvacConstants.AirCp * 1000.0 * effectiveTempDiffC);
        }

        /// <summary>
        /// 散湿量(kg/h)换算潜热负荷 W:Q = G·r / 3.6(1 kJ/h = 1/3.6 W)。
        /// </summary>
        public static double LatentHeatFromMoistureW(double moistureKgPerHour)
        {
            return moistureKgPerHour * HvacConstants.WaterLatentHeat / 3.6;
        }
    }
}

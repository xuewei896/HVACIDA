namespace HVACIDA.Core.Utils
{
    /// <summary>
    /// 暖通常用物性常量与待核对系数集中处。
    /// TODO(公式核对):以下默认系数仅为工程参考值,须逐项与《大系统负荷计算公式.docx》及国家/行业规范核对后再投入使用。
    /// </summary>
    public static class HvacConstants
    {
        /// <summary>空气密度 kg/m³(标准状态常用值)</summary>
        public const double AirDensity = 1.2;

        /// <summary>空气定压比热 kJ/(kg·K)</summary>
        public const double AirCp = 1.01;

        /// <summary>水的汽化潜热 kJ/kg(用于散湿量→潜热)</summary>
        public const double WaterLatentHeat = 2501.0;

        /// <summary>饱和水蒸气分压公式(Magnus)常数 Pa</summary>
        public const double MagnusA = 610.94;
        public const double MagnusB = 17.625;
        public const double MagnusC = 243.04;

        /// <summary>水蒸气与干空气气体常数比 0.622</summary>
        public const double GasConstantRatio = 0.622;

        /// <summary>排烟换气次数 次/h(需求文档:按 60 倍/小时)</summary>
        public const double SmokeAirChangesPerHour = 60.0;

        /// <summary>组合式空调机组台数(需求文档:取总量一半,共 2 台)</summary>
        public const double AhUnitCount = 2.0;

        /// <summary>排烟风机台数(需求文档:2 台)</summary>
        public const double SmokeFanUnitCount = 2.0;
    }
}

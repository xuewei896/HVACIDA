using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 小系统输入的<strong>单一读取入口</strong>:读 small-system.xml,并把「项目信息 → 气象参数」的
    /// 夏季空调室外计算干球/湿球温度回填到 <see cref="SmallSystemInput.OutdoorDryBulbC"/>/<see cref="SmallSystemInput.OutdoorWetBulbC"/>
    /// —— 公式文档明确 E4/E5"直接从项目信息中获取"。
    /// <para>
    /// 与 <see cref="LargeSystemInputService"/> 同一套路:房间参数与工程参数都只在这里进出,
    /// 保证「小系统各窗」读到的是一致的一份;是否落盘仍由用户点【保存】决定。
    /// </para>
    /// </summary>
    public class SmallSystemInputService
    {
        private readonly IDataRepository _repository;

        public SmallSystemInputService()
            : this(null)
        {
        }

        public SmallSystemInputService(IDataRepository repository)
        {
            _repository = repository ?? new XmlProjectRepository();
        }

        /// <summary>数据目录(界面提示用)。</summary>
        public string StorageDirectory => _repository.StorageDirectory;

        /// <summary>最近一次室外参数回填说明(界面状态栏用)。</summary>
        public string LastWeatherNote { get; private set; } = "";

        /// <summary>室外参数是否已回填(项目信息里有值)。</summary>
        public bool WeatherApplied { get; private set; }

        /// <summary>读取小系统输入(含室外参数回填)。</summary>
        public SmallSystemInput Load()
        {
            var input = _repository.LoadSmallSystem();
            Sync(input);
            return input;
        }

        /// <summary>按当前 project.xml 的气象参数回填室外干球/湿球温度(未填则不覆盖)。</summary>
        public void Sync(SmallSystemInput input)
        {
            WeatherApplied = false;
            LastWeatherNote = "";
            if (input == null) return;

            if (input.WeatherManuallyOverridden)
            {
                LastWeatherNote = "已关闭室外参数自动联动:室外干球/湿球温度按本窗手工输入值参与计算。";
                return;
            }

            var design = _repository.LoadProject()?.Design;
            var outdoor = design?.LargeSystemOutdoor;
            var small = design?.SmallSystemOutdoor;

            // 优先取小系统室外参数,缺失时回退到大系统室外(需求 2.1.2:小系统室外仅夏季干球/湿球/通风)
            double dry = small != null && small.SummerACDryBulbC > 0
                ? small.SummerACDryBulbC
                : (outdoor?.SummerACDryBulbC ?? 0);
            double wet = small != null && small.SummerACWetBulbC > 0
                ? small.SummerACWetBulbC
                : (outdoor?.SummerACWetBulbC ?? 0);
            double transition = outdoor?.SummerVentDryBulbC ?? 0;

            int filled = 0;
            if (dry > 0) { input.OutdoorDryBulbC = dry; filled++; }
            if (wet > 0) { input.OutdoorWetBulbC = wet; filled++; }
            // 过渡季通风室外计算温度默认 14 ℃;气象参数里的"夏季通风室外计算温度"若更高,不作替换(文档另有默认)
            if (transition > 0 && input.TransitionOutdoorC <= 0) { input.TransitionOutdoorC = transition; }

            WeatherApplied = filled > 0;
            LastWeatherNote = filled > 0
                ? "室外计算参数取自「项目信息 → 气象参数」:干球 " + input.OutdoorDryBulbC.ToString("0.##") +
                  " ℃、湿球 " + input.OutdoorWetBulbC.ToString("0.##") + " ℃。"
                : "⚠ 项目信息里未填夏季空调室外计算干球/湿球温度,请在「项目信息 → 气象参数」选择城市或手工填写。";
        }

        /// <summary>保存小系统输入(房间列表 + 系统参数)。</summary>
        public void Save(SmallSystemInput input)
        {
            _repository.SaveSmallSystem(input);
        }
    }
}

using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 大系统输入的<strong>单一读取入口</strong>:读 large-system.xml,并(在未脱离联动时)
    /// 用「项目信息 → 气象参数」回填 C5/F4/F6。
    ///
    /// 为什么必须走这里:「公共区参数 / 负荷计算 / 计算结果」三个窗口共用同一份输入,
    /// 若各自直接调 <see cref="IDataRepository.LoadLargeSystem"/>,就会出现"负荷计算窗按 25 ℃ 湿球算、
    /// 计算结果窗按文件里旧的 0 ℃ 湿球算"的口径分叉。本类把联动集中在一处,三个窗口读到的必然一致。
    ///
    /// 注意:联动结果只作用于内存中的输入对象;是否落盘仍由用户在窗口里点【保存】决定。
    /// </summary>
    public class LargeSystemInputService
    {
        private readonly IDataRepository _repository;

        public LargeSystemInputService()
            : this(null)
        {
        }

        public LargeSystemInputService(IDataRepository repository)
        {
            _repository = repository ?? new XmlProjectRepository();
        }

        /// <summary>数据目录(界面提示用)。</summary>
        public string StorageDirectory => _repository.StorageDirectory;

        /// <summary>最近一次联动所用的大系统室外参数(可能为 null)。</summary>
        public OutdoorAirParams LastOutdoor { get; private set; }

        /// <summary>最近一次联动的室内设计参数(可能为 null)。</summary>
        public LargeSystemIndoorParams LastIndoor { get; private set; }

        /// <summary>最近一次联动结果(可能为 null)。</summary>
        public WeatherSyncResult LastWeatherSync { get; private set; }

        /// <summary>
        /// 读取大系统输入(含气象参数联动)。
        /// 若用户已在本窗脱离联动(<see cref="LargeSystemInput.WeatherManuallyOverridden"/>),
        /// 则原样返回文件中的值,不做任何覆盖。
        /// </summary>
        public LargeSystemInput Load()
        {
            var input = _repository.LoadLargeSystem();
            Sync(input);
            return input;
        }

        /// <summary>按当前 project.xml 的气象参数重新联动一次(勾选/取消勾选、点【重新同步】时调用)。</summary>
        public WeatherSyncResult Sync(LargeSystemInput input)
        {
            var design = _repository.LoadProject()?.Design;
            LastOutdoor = design?.LargeSystemOutdoor;
            LastIndoor = design?.LargeSystemIndoor;

            if (input != null && input.WeatherManuallyOverridden)
            {
                LastWeatherSync = new WeatherSyncResult
                {
                    Note = "已关闭气象参数联动:C5/F4/F6 按本窗手工输入值参与计算。",
                    SourceWetBulbC = input.OutdoorWetBulbC,
                    SourceHallC = input.HallDesignTempC,
                    SourcePlatformC = input.PlatformDesignTempC
                };
                return LastWeatherSync;
            }

            LastWeatherSync = ProjectDesignSync.ApplyWeather(design, input);
            return LastWeatherSync;
        }

        /// <summary>保存大系统输入。</summary>
        public void Save(LargeSystemInput input)
        {
            _repository.SaveLargeSystem(input);
        }
    }
}

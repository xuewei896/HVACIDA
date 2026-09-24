using System.Collections.Generic;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 小系统输入的<strong>单一读取入口</strong>:读 small-system.xml,并把「项目信息 → 气象参数」的
    /// 夏季空调室外计算干球/湿球温度回填到 <see cref="SmallSystemInput.OutdoorDryBulbC"/>/<see cref="SmallSystemInput.OutdoorWetBulbC"/>
    /// —— 公式文档明确 E4/E5"直接从项目信息中获取"。
    /// <para>
    /// 与 <see cref="LargeSystemInputService"/> 同一套路:房间参数与工程参数都只在这里进出,
    /// 保证「小系统各窗」读到的是一致的一份。
    /// 落盘时机有两个:录入窗点【计 算】(算前先存 —— 否则「计算结果」窗汇总到的还是上一次保存的参数)
    /// 或点【保 存 参 数】;打开窗、拾取回填等内部重算不写盘。
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

        /// <summary>读取小系统输入(单系统,含室外参数回填)。</summary>
        public SmallSystemInput Load()
        {
            var input = _repository.LoadSmallSystem();
            Sync(input);
            return input;
        }

        /// <summary>读取**某类型 + 编号**的系统(不存在时新建一个该类型的默认系统,便于首次录入)。</summary>
        public SmallSystemInput Load(SmallSystemType type, string systemCode)
        {
            var project = _repository.LoadSmallSystems();
            var input = project.Find(type, systemCode) ?? new SmallSystemInput { SystemType = type, SystemCode = systemCode ?? "" };
            input.SystemType = type;
            if (!string.IsNullOrEmpty(systemCode)) input.SystemCode = systemCode;
            Sync(input);
            return input;
        }

        /// <summary>读取整个小系统工程(多系统,含室外参数回填)。</summary>
        public SmallSystemProject LoadProject()
        {
            var project = _repository.LoadSmallSystems();
            foreach (var system in project.Systems) Sync(system);
            return project;
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
                : "";   // 2026-09-20 用户口径「删掉所有告警」:未取到室外参数时不再给告警文案
        }

        /// <summary>
        /// 保存小系统输入(房间列表 + 系统参数):按"系统类型 + 系统编号"**新增或覆盖**到小系统工程里,
        /// 不覆盖其它系统。返回该工程当前共有多少套系统。
        /// </summary>
        public int Save(SmallSystemInput input)
        {
            var project = _repository.LoadSmallSystems();
            project.Upsert(input);
            _repository.SaveSmallSystems(project);
            return project.Systems.Count;
        }

        /// <summary>
        /// 用 <paramref name="systems"/> **整体替换**某类型的全部系统(多系统窗的保存语义)。
        /// <para>
        /// 为什么不能只 upsert:用户在小系统窗里【删除系统】后,若只逐套 upsert,
        /// 被删的那套仍留在 small-systems.xml 里 —— **关窗再打开它又回来了**(2026-09-20 实机反馈)。
        /// 所以先删掉存储里同类型的现有系统,再把窗内的系统写进去,让窗口状态对该类型是权威的。
        /// </para>
        /// <para>其它类型的系统与其它工程数据一概不动。返回保存后该工程的小系统总数。</para>
        /// </summary>
        public int ReplaceAll(SmallSystemType type, IList<SmallSystemInput> systems)
        {
            var project = _repository.LoadSmallSystems();

            var remaining = new List<SmallSystemInput>();
            foreach (var existing in project.Systems)
            {
                if (existing != null && existing.SystemType != type) remaining.Add(existing);
            }
            project.Systems = remaining;

            if (systems != null)
            {
                foreach (var system in systems)
                {
                    if (system != null) project.Upsert(system);
                }
            }

            _repository.SaveSmallSystems(project);
            return project.Systems.Count;
        }

        /// <summary>保存整个小系统工程(多系统;删除系统时用)。</summary>
        public void SaveProject(SmallSystemProject project)
        {
            _repository.SaveSmallSystems(project);
        }
    }
}

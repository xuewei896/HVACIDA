using System;
using System.Collections.Generic;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 水力计算数据的**单一读取入口**(需求 2.3 风系统 / 2.4 水系统),**全站多套系统**。
    /// <para>
    /// 落盘 = 系数集(全站共用一份)+ 多套系统输入(<c>hydraulic.xml</c>,按「介质 + 系统编号」upsert);
    /// **结果不落盘** —— 「水力计算 → 计算结果」窗按输入现算(打开即算,与其它模块一致),
    /// 所以窗口里的数字永远和当前输入对得上。
    /// </para>
    /// </summary>
    public class HydraulicInputService
    {
        private readonly IDataRepository _repository;
        private readonly IHydraulicCalculator _calculator;
        private readonly HydraulicSummaryService _summaryService;

        public HydraulicInputService()
            : this(null)
        {
        }

        public HydraulicInputService(IDataRepository repository)
            : this(repository, null)
        {
        }

        public HydraulicInputService(IDataRepository repository, IHydraulicCalculator calculator)
        {
            _repository = repository ?? new XmlProjectRepository();
            _calculator = calculator ?? new HydraulicCalculator();
            _summaryService = new HydraulicSummaryService(_calculator);
        }

        /// <summary>数据目录(界面提示用)。</summary>
        public string StorageDirectory => _repository.StorageDirectory;

        /// <summary>最近一次读取是否发生了"旧版单系统 → 多系统容器"的迁移(界面提示用)。</summary>
        public bool LastLoadMigrated { get; private set; }

        /// <summary>读取水力工程(系数集 + 全部系统);首次读到旧版单系统文件时自动迁移并落盘。</summary>
        public HydraulicProject LoadProject()
        {
            LastLoadMigrated = false;
            var project = _repository.LoadHydraulic() ?? new HydraulicProject();
            if (project.Coefficients == null) project.Coefficients = HydraulicCoefficients.CreateDefault();
            if (project.Coefficients.LocalLossItems == null || project.Coefficients.LocalLossItems.Count == 0)
                project.Coefficients.LocalLossItems = HydraulicLocalLossTable.CreateDefaults();
            if (project.Systems == null) project.Systems = new List<HydraulicInput>();

            bool migrated = project.MigrateLegacy();
            if (migrated)
            {
                LastLoadMigrated = true;
                try { _repository.SaveHydraulic(project); } catch { /* 迁移落盘失败不阻断读取 */ }
            }
            return project;
        }

        /// <summary>保存水力工程(旧版单系统字段一律不写回,避免新旧两份数据并存)。</summary>
        public void SaveProject(HydraulicProject project)
        {
            if (project == null) return;
            project.LegacyAir = null;
            project.LegacyWater = null;
            _repository.SaveHydraulic(project);
        }

        /// <summary>读取系数集。</summary>
        public HydraulicCoefficients LoadCoefficients()
        {
            return LoadProject().Coefficients;
        }

        /// <summary>保存系数集(不动已录入的系统)。</summary>
        public void SaveCoefficients(HydraulicCoefficients coefficients)
        {
            if (coefficients == null) return;
            var project = LoadProject();
            project.Coefficients = coefficients;
            SaveProject(project);
        }

        /// <summary>按「介质 + 系统编号」读取一套系统(没有返回 null)。</summary>
        public HydraulicInput Load(HydraulicKind kind, string systemCode)
        {
            return LoadProject().Find(kind, systemCode);
        }

        /// <summary>读某介质的**第一套**系统(兼容入口:打开录入窗时先把已有数据摆出来)。</summary>
        public HydraulicInput Load(HydraulicKind kind)
        {
            return LoadProject().FirstOf(kind);
        }

        /// <summary>读某介质的全部系统。</summary>
        public IList<HydraulicInput> LoadAll(HydraulicKind kind)
        {
            var result = new List<HydraulicInput>();
            foreach (var system in LoadProject().Systems)
            {
                if (system != null && system.Kind == kind) result.Add(system);
            }
            return result;
        }

        /// <summary>
        /// 保存一套系统(按「介质 + 系统编号」upsert;同类型其它编号的系统不受影响)。
        /// </summary>
        /// <returns>保存后全站共有多少套系统。</returns>
        public int Save(HydraulicInput input)
        {
            if (input == null) return 0;
            var project = LoadProject();
            project.Upsert(input);
            SaveProject(project);
            return project.SystemCount;
        }

        /// <summary>删除一套系统(按「介质 + 系统编号」),返回是否删掉了。</summary>
        public bool Remove(HydraulicKind kind, string systemCode)
        {
            var project = LoadProject();
            bool removed = project.Remove(kind, systemCode);
            if (removed) SaveProject(project);
            return removed;
        }

        /// <summary>清空某介质的**全部**系统,返回清掉的套数。</summary>
        public int Clear(HydraulicKind kind)
        {
            var project = LoadProject();
            int removed = project.RemoveAllOf(kind);
            if (removed > 0) SaveProject(project);
            return removed;
        }

        /// <summary>按已保存的输入现算一套系统(「计算结果」窗走这里)。没有返回 null。</summary>
        public HydraulicResult Calculate(HydraulicKind kind, string systemCode)
        {
            var project = LoadProject();
            var input = project.Find(kind, systemCode);
            if (input == null) return null;
            return _calculator.Calculate(input, project.Coefficients);
        }

        /// <summary>按已保存的输入现算某介质的第一套系统(兼容入口)。</summary>
        public HydraulicResult Calculate(HydraulicKind kind)
        {
            var project = LoadProject();
            var input = project.FirstOf(kind);
            if (input == null) return null;
            return _calculator.Calculate(input, project.Coefficients);
        }

        /// <summary>全站汇总(逐系统各算一遍;汇总只对可加量求和)。</summary>
        public HydraulicSummary Summarize()
        {
            return _summaryService.Summarize(LoadProject());
        }
    }
}

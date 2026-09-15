using System;
using System.Collections.Generic;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 水力计算数据的**单一读取入口**(需求 2.3 风系统 / 2.4 水系统)。
    /// <para>
    /// 落盘 = 系数集 + 最近一次从模型读到的风系统 / 水系统输入(<c>hydraulic.xml</c>);
    /// **结果不落盘** —— 「水力计算 → 计算结果」窗按输入现算(打开即算,与其它模块一致),
    /// 这样窗口里看到的数字永远和当前输入对得上。
    /// </para>
    /// </summary>
    public class HydraulicInputService
    {
        private readonly IDataRepository _repository;
        private readonly IHydraulicCalculator _calculator;

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
        }

        /// <summary>数据目录(界面提示用)。</summary>
        public string StorageDirectory => _repository.StorageDirectory;

        /// <summary>读取水力计算数据(系数集 + 两次读取的管网输入)。</summary>
        public HydraulicProject LoadProject()
        {
            var project = _repository.LoadHydraulic() ?? new HydraulicProject();
            if (project.Coefficients == null) project.Coefficients = HydraulicCoefficients.CreateDefault();
            if (project.Coefficients.LocalLossItems == null || project.Coefficients.LocalLossItems.Count == 0)
                project.Coefficients.LocalLossItems = HydraulicLocalLossTable.CreateDefaults();
            return project;
        }

        /// <summary>保存水力计算数据。</summary>
        public void SaveProject(HydraulicProject project)
        {
            if (project == null) return;
            _repository.SaveHydraulic(project);
        }

        /// <summary>读取系数集。</summary>
        public HydraulicCoefficients LoadCoefficients()
        {
            return LoadProject().Coefficients;
        }

        /// <summary>保存系数集(不动已读取的管网输入)。</summary>
        public void SaveCoefficients(HydraulicCoefficients coefficients)
        {
            if (coefficients == null) return;
            var project = LoadProject();
            project.Coefficients = coefficients;
            SaveProject(project);
        }

        /// <summary>读取某介质的管网输入(未读取过返回 null,由界面提示"先去拾取系统")。</summary>
        public HydraulicInput Load(HydraulicKind kind)
        {
            return LoadProject()[kind];
        }

        /// <summary>保存某介质的管网输入(upsert;不影响另一介质)。</summary>
        public void Save(HydraulicInput input)
        {
            if (input == null) return;
            var project = LoadProject();
            project[input.Kind] = input;
            SaveProject(project);
        }

        /// <summary>清空某介质的管网输入(用户要重新拾取系统时用)。</summary>
        public void Clear(HydraulicKind kind)
        {
            var project = LoadProject();
            project[kind] = null;
            SaveProject(project);
        }

        /// <summary>按已保存的输入现算(「计算结果」窗打开即算走这里)。无输入返回 null。</summary>
        public HydraulicResult Calculate(HydraulicKind kind)
        {
            var project = LoadProject();
            var input = project[kind];
            if (input == null) return null;
            return _calculator.Calculate(input, project.Coefficients);
        }
    }
}

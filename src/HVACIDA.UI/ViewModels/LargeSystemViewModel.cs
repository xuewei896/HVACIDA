using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>大系统负荷计算窗 ViewModel(需求文档 2.2.3.1,参数表按公式文档分节)。</summary>
    public class LargeSystemViewModel : ViewModelBase
    {
        private readonly ILargeSystemLoadCalculator _calculator;
        private readonly IDataRepository _repository;
        private LargeSystemInput _input;
        private string _resultText = "";
        private string _status = "";

        public LargeSystemViewModel()
            : this(null)
        {
        }

        /// <summary>
        /// 通过仓库构造:与「大系统 → 公共区参数」共用同一份输入
        /// (公共区几何 + 高峰客流在那边录入,这里继续补其余各节)。
        /// </summary>
        public LargeSystemViewModel(IDataRepository repository)
        {
            _calculator = new LargeSystemLoadCalculator();
            _repository = repository ?? new XmlProjectRepository();
            _input = _repository.LoadLargeSystem();
            CalculateCommand = new RelayCommand(Calculate, () => true);
            ExportCommand = new RelayCommand(ExportReport, () => _lastResult != null);
            ResetCommand = new RelayCommand(Reset);
            SaveCommand = new RelayCommand(Save);
        }

        /// <summary>输入参数(绑定路径 Input.*)。</summary>
        public LargeSystemInput Input
        {
            get => _input;
            private set => Set(ref _input, value);
        }

        /// <summary>保存输入(供「计算结果」窗与后续复用)。</summary>
        public ICommand SaveCommand { get; }

        /// <summary>计算命令。</summary>
        public ICommand CalculateCommand { get; }

        /// <summary>导出计算书命令(需先计算)。</summary>
        public ICommand ExportCommand { get; }

        /// <summary>恢复公式文档默认参数。</summary>
        public ICommand ResetCommand { get; }

        private LargeSystemResult _lastResult;

        /// <summary>结果文本。</summary>
        public string ResultText
        {
            get => _resultText;
            private set => Set(ref _resultText, value);
        }

        /// <summary>状态提示。</summary>
        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        private void Calculate()
        {
            try
            {
                _lastResult = _calculator.Calculate(Input);
                ResultText = ResultFormatter.FormatLarge(Input, _lastResult);
                Status = "计算完成(与北京站算例同口径)。可导出计算书。";
            }
            catch (System.Exception ex)
            {
                ResultText = "";
                Status = "计算失败: " + ex.Message;
            }
        }

        private void ExportReport()
        {
            try
            {
                if (_lastResult == null) return;
                var generator = new TextReportGenerator();
                string path = generator.SaveTextReport(
                    "大系统负荷计算书",
                    ResultFormatter.FormatLarge(Input, _lastResult));
                Status = "计算书已生成: " + path;
            }
            catch (System.Exception ex)
            {
                Status = "导出失败: " + ex.Message;
            }
        }

        private void Reset()
        {
            Input = new LargeSystemInput();
            _lastResult = null;
            ResultText = "";
            Status = "已恢复公式文档默认参数(客流量需重新输入)。";
        }

        /// <summary>保存到 %AppData%\HVACIDA\large-system.xml(与公共区参数窗共用)。</summary>
        private void Save()
        {
            try
            {
                _repository.SaveLargeSystem(Input);
                Status = "参数已保存: " + _repository.StorageDirectory + "\\large-system.xml(「计算结果」窗将按此计算)";
            }
            catch (System.Exception ex)
            {
                Status = "保存失败: " + ex.Message;
            }
        }
    }
}

using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>大系统负荷计算窗 ViewModel(需求文档 2.2.3.1,参数表按公式文档分节)。</summary>
    public class LargeSystemViewModel : ViewModelBase
    {
        private readonly ILargeSystemLoadCalculator _calculator;
        private LargeSystemInput _input;
        private string _resultText = "";
        private string _status = "";

        public LargeSystemViewModel()
        {
            _calculator = new LargeSystemLoadCalculator();
            _input = new LargeSystemInput();
            CalculateCommand = new RelayCommand(Calculate, () => true);
            ExportCommand = new RelayCommand(ExportReport, () => _lastResult != null);
            ResetCommand = new RelayCommand(Reset);
        }

        /// <summary>输入参数(绑定路径 Input.*)。</summary>
        public LargeSystemInput Input
        {
            get => _input;
            private set => Set(ref _input, value);
        }

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
    }
}

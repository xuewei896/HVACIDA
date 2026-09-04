using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>小系统负荷计算窗 ViewModel(需求文档 2.2.3.2)。</summary>
    public class SmallSystemViewModel : ViewModelBase
    {
        private readonly ISmallSystemLoadCalculator _calculator;
        private string _resultText = "";
        private string _status = "";

        public SmallSystemViewModel()
        {
            _calculator = new SmallSystemLoadCalculator();
            Input = new SmallSystemInput();
            CalculateCommand = new RelayCommand(Calculate);
            ExportCommand = new RelayCommand(ExportReport, () => _lastResult != null);
        }

        public SmallSystemInput Input { get; }

        public ICommand CalculateCommand { get; }

        public ICommand ExportCommand { get; }

        private SmallSystemResult _lastResult;

        public string ResultText
        {
            get => _resultText;
            private set => Set(ref _resultText, value);
        }

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
                ResultText = ResultFormatter.FormatSmall(Input, _lastResult);
                Status = "计算完成(骨架算法,公式待核对)。可导出计算书。";
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
                    "小系统负荷计算书",
                    ResultFormatter.FormatSmall(Input, _lastResult));
                Status = "计算书已生成: " + path;
            }
            catch (System.Exception ex)
            {
                Status = "导出失败: " + ex.Message;
            }
        }
    }
}

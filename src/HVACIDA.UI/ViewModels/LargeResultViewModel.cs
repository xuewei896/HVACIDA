using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 大系统「计算结果」窗 ViewModel(Ribbon「大系统 → 计算结果」)。
    /// 读取「公共区参数 / 负荷计算」保存的同一份输入 → 计算 → 展示结果文本。
    /// </summary>
    public class LargeResultViewModel : ViewModelBase
    {
        private readonly IDataRepository _repository;
        private readonly ILargeSystemLoadCalculator _calculator;
        private LargeSystemInput _input;
        private LargeSystemResult _lastResult;
        private string _resultText = "";
        private string _status = "";

        public LargeResultViewModel()
            : this(null)
        {
        }

        public LargeResultViewModel(IDataRepository repository)
        {
            _repository = repository ?? new XmlProjectRepository();
            _calculator = new LargeSystemLoadCalculator();
            _input = _repository.LoadLargeSystem();
            CalculateCommand = new RelayCommand(Calculate);
            ExportCommand = new RelayCommand(Export, () => _lastResult != null);
        }

        public LargeSystemInput Input
        {
            get => _input;
            private set => Set(ref _input, value);
        }

        public ICommand CalculateCommand { get; }

        public ICommand ExportCommand { get; }

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
                Input = _repository.LoadLargeSystem();   // 每次计算前重新读取,确保与其它窗口同步
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

        private void Export()
        {
            try
            {
                if (_lastResult == null) return;
                var generator = new TextReportGenerator();
                string path = generator.SaveTextReport("大系统负荷计算书", ResultFormatter.FormatLarge(Input, _lastResult));
                Status = "计算书已生成: " + path;
            }
            catch (System.Exception ex)
            {
                Status = "导出失败: " + ex.Message;
            }
        }
    }
}

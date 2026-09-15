using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 小系统「计算结果」窗 ViewModel(Ribbon「小系统 → 计算结果」)。
    /// 当前仅「全空气一次回风系统」完成计算链路,其余五类实现后自动纳入汇总。
    /// </summary>
    public class SmallResultViewModel : ViewModelBase
    {
        private readonly IDataRepository _repository;
        private readonly ISmallSystemLoadCalculator _calculator;
        private SmallSystemInput _input;
        private SmallSystemResult _lastResult;
        private string _resultText = "";
        private string _status = "";
        private ResultTable _table;

        public SmallResultViewModel()
            : this(null)
        {
        }

        public SmallResultViewModel(IDataRepository repository)
        {
            _repository = repository ?? new XmlProjectRepository();
            _calculator = new SmallSystemLoadCalculator();
            _input = _repository.LoadSmallSystem();
            CalculateCommand = new RelayCommand(Calculate);
            ExportCommand = new RelayCommand(Export, () => _lastResult != null);
        }

        public SmallSystemInput Input
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

        /// <summary>小系统计算结果表(界面按分区分组渲染;与导出计算书同源)。</summary>
        public ResultTable Table
        {
            get => _table;
            private set => Set(ref _table, value);
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
                Input = _repository.LoadSmallSystem();
                _lastResult = _calculator.Calculate(Input);
                Table = ResultTable.ForSmallSystem(Input, _lastResult);
                ResultText = ResultFormatter.FormatSmall(Input, _lastResult);
                Status = "计算完成(骨架算法,公式待核对)。结果已按分区列表格呈现,可导出计算书。";
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
                string path = generator.SaveTextReport("小系统负荷计算书", ResultFormatter.FormatSmall(Input, _lastResult));
                Status = "计算书已生成: " + path;
            }
            catch (System.Exception ex)
            {
                Status = "导出失败: " + ex.Message;
            }
        }
    }
}

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
            : this(SmallSystemType.AllAirOnceReturn)
        {
        }

        /// <summary>按 Ribbon 选定的系统类型构造(六类小系统为 Ribbon 一级按钮,2026-09-11)。</summary>
        public SmallSystemViewModel(SmallSystemType systemType)
        {
            _calculator = new SmallSystemLoadCalculator();
            Input = new SmallSystemInput { SystemType = systemType };
            SystemTypeName = DescribeSystemType(systemType);
            CalculateCommand = new RelayCommand(Calculate);
            ExportCommand = new RelayCommand(ExportReport, () => _lastResult != null);
        }

        /// <summary>当前系统类型名称(窗口只读展示,类型由 Ribbon 按钮决定)。</summary>
        public string SystemTypeName { get; }

        private static string DescribeSystemType(SmallSystemType type)
        {
            switch (type)
            {
                case SmallSystemType.AllAirOnceReturn: return "全空气一次回风系统";
                case SmallSystemType.VrfWithFreshAir: return "多联机 + 新风系统";
                case SmallSystemType.ExhaustVentilation: return "排风系统 — 环控机房通风";
                case SmallSystemType.ExhaustToilet: return "排风系统 — 卫生间排风";
                case SmallSystemType.SmokeExhaust: return "排烟系统";
                case SmallSystemType.SupplyExhaustSmoke: return "送风排风排烟系统";
                case SmallSystemType.PressurizationSupply: return "加压送风系统";
                default: return type.ToString();
            }
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

using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>小系统负荷计算窗 ViewModel(需求文档 2.2.3.2)。</summary>
    public class SmallSystemViewModel : ViewModelBase
    {
        private readonly ISmallSystemLoadCalculator _calculator;
        private readonly IDataRepository _repository;
        private string _resultText = "";
        private ResultTable _table;
        private string _status = "";

        public SmallSystemViewModel()
            : this(SmallSystemType.AllAirOnceReturn, null)
        {
        }

        /// <summary>按 Ribbon 选定的系统类型构造(六类小系统为 Ribbon 一级按钮,2026-09-11)。</summary>
        public SmallSystemViewModel(SmallSystemType systemType)
            : this(systemType, null)
        {
        }

        /// <summary>
        /// 通过仓库构造:类型一致时复用上次保存的参数(与「小系统 → 计算结果」共用一份数据)。
        /// </summary>
        public SmallSystemViewModel(SmallSystemType systemType, IDataRepository repository)
        {
            _calculator = new SmallSystemLoadCalculator();
            _repository = repository ?? new XmlProjectRepository();

            var saved = _repository.LoadSmallSystem();
            Input = saved != null && saved.SystemType == systemType
                ? saved
                : new SmallSystemInput { SystemType = systemType };

            SystemTypeName = DescribeSystemType(systemType);
            CalculateCommand = new RelayCommand(Calculate);
            ExportCommand = new RelayCommand(ExportReport, () => _lastResult != null);
            SaveCommand = new RelayCommand(Save);
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

        /// <summary>保存参数(供「小系统 → 计算结果」窗使用)。</summary>
        public ICommand SaveCommand { get; }

        private SmallSystemResult _lastResult;

        public string ResultText
        {
            get => _resultText;
            private set => Set(ref _resultText, value);
        }

        /// <summary>计算结果表(界面右栏按分区分组渲染;与导出计算书同源)。</summary>
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
                _lastResult = _calculator.Calculate(Input);
                Table = ResultTable.ForSmallSystem(Input, _lastResult);
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

        /// <summary>保存到 %AppData%\HVACIDA\small-system.xml(与「计算结果」窗共用)。</summary>
        private void Save()
        {
            try
            {
                _repository.SaveSmallSystem(Input);
                Status = "参数已保存: " + _repository.StorageDirectory + "\\small-system.xml";
            }
            catch (System.Exception ex)
            {
                Status = "保存失败: " + ex.Message;
            }
        }
    }
}

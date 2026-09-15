using System.Windows;
using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 大系统负荷计算窗 ViewModel(需求文档 2.2.3.1,参数表按公式文档分节)。
    /// <para>
    /// 气象参数(C5/F4/F6)默认由 <see cref="LargeSystemInputService"/> 从「项目信息 → 气象参数」自动回填:
    /// 勾选「自动联动」时这三格只读(灰底,见 UI设计规范 §2.5),取消勾选转为手工覆盖。
    /// </para>
    /// </summary>
    public class LargeSystemViewModel : ViewModelBase
    {
        private readonly ILargeSystemLoadCalculator _calculator;
        private readonly LargeSystemInputService _service;
        private LargeSystemInput _input;
        private string _resultText = "";
        private string _status = "";
        private string _weatherNote = "";
        private string _weatherWarning = "";
        private ResultTable _table;

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
            _service = new LargeSystemInputService(repository);
            _input = _service.Load();
            ApplyWeatherNotes(_service.LastWeatherSync);

            CalculateCommand = new RelayCommand(Calculate, () => true);
            ExportCommand = new RelayCommand(ExportReport, () => _lastResult != null);
            ResetCommand = new RelayCommand(Reset);
            SaveCommand = new RelayCommand(Save);
            SyncWeatherCommand = new RelayCommand(() =>
            {
                ApplyWeatherNotes(_service.Sync(Input));
                OnPropertyChanged(nameof(Input));
                Status = "已按当前「项目信息 → 气象参数」重新同步 C5/F4/F6。";
            });
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

        /// <summary>按当前项目信息重新同步 C5/F4/F6。</summary>
        public ICommand SyncWeatherCommand { get; }

        /// <summary>
        /// 是否随「项目信息 → 气象参数」自动联动 C5/F4/F6(默认 true)。
        /// 取消勾选 = 置 <see cref="LargeSystemInput.WeatherManuallyOverridden"/>,三格转为可编辑。
        /// </summary>
        public bool AutoSyncWeather
        {
            get => !Input.WeatherManuallyOverridden;
            set
            {
                if (value == AutoSyncWeather) return;
                Input.WeatherManuallyOverridden = !value;

                if (value)
                {
                    ApplyWeatherNotes(_service.Sync(Input));
                    OnPropertyChanged(nameof(Input));
                    Status = "已恢复气象参数自动联动,C5/F4/F6 按项目信息取值。";
                }
                else
                {
                    ApplyWeatherNotes(null);
                    WeatherNote = "已改为手工输入:C5/F4/F6 由本窗数值参与计算,不再随项目信息变化。";
                    WeatherWarning = "";
                    Status = "已关闭气象参数联动,C5/F4/F6 可手工输入。";
                }

                OnPropertyChanged(nameof(AutoSyncWeather));
                OnPropertyChanged(nameof(WeatherLinked));
            }
        }

        /// <summary>C5/F4/F6 当前是否被联动锁定(只读)。供 XAML DataTrigger 使用。</summary>
        public bool WeatherLinked => !Input.WeatherManuallyOverridden;

        /// <summary>联动状态说明(来源取值或"已手工输入")。</summary>
        public string WeatherNote
        {
            get => _weatherNote;
            private set => Set(ref _weatherNote, value);
        }

        /// <summary>气象参数有未填项时的告警文案(无告警时折叠)。</summary>
        public string WeatherWarning
        {
            get => _weatherWarning;
            private set
            {
                if (Set(ref _weatherWarning, value)) OnPropertyChanged(nameof(WeatherWarningVisibility));
            }
        }

        public Visibility WeatherWarningVisibility =>
            string.IsNullOrEmpty(_weatherWarning) ? Visibility.Collapsed : Visibility.Visible;

        private LargeSystemResult _lastResult;

        /// <summary>结果文本(导出计算书用;界面用 <see cref="Table"/> 分组表格展示)。</summary>
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

        /// <summary>状态提示。</summary>
        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        private void ApplyWeatherNotes(WeatherSyncResult sync)
        {
            if (sync == null)
            {
                WeatherNote = "";
                WeatherWarning = "";
                return;
            }

            WeatherNote = sync.Note;
            WeatherWarning = sync.Warning;
        }

        private void Calculate()
        {
            try
            {
                _lastResult = _calculator.Calculate(Input);
                Table = ResultTable.ForLargeSystem(Input, _lastResult);
                ResultText = ResultFormatter.FormatLarge(Input, _lastResult);
                Status = AutoSyncWeather
                    ? "计算完成(C5/F4/F6 取自项目信息气象参数,与北京站算例同口径)。可导出计算书。"
                    : "计算完成(C5/F4/F6 为手工输入值)。可导出计算书。";
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
            Table = null;
            ApplyWeatherNotes(_service.Sync(Input));   // 恢复默认后仍按项目信息回填 C5/F4/F6
            OnPropertyChanged(nameof(AutoSyncWeather));
            OnPropertyChanged(nameof(WeatherLinked));
            _lastResult = null;
            ResultText = "";
            Status = "已恢复公式文档默认参数(客流量需重新输入)。";
        }

        /// <summary>保存到 %AppData%\HVACIDA\large-system.xml(与公共区参数窗共用)。</summary>
        private void Save()
        {
            try
            {
                _service.Save(Input);
                Status = "参数已保存: " + _service.StorageDirectory + "\\large-system.xml(「计算结果」窗将按此计算)";
            }
            catch (System.Exception ex)
            {
                Status = "保存失败: " + ex.Message;
            }
        }
    }
}

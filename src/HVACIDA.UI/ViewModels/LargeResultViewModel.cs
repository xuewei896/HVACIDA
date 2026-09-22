using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 大系统「计算结果」窗 ViewModel(Ribbon「大系统 → 计算结果」)。
    /// 读取「公共区参数 / 负荷计算」保存的同一份输入 → 计算 → 展示结果文本。
    /// 走 <see cref="LargeSystemInputService"/>,与负荷计算窗共享同一套气象参数联动口径
    /// (否则会出现"负荷计算窗按联动后的 C5 算、本窗按文件里的旧 C5 算"的分叉)。
    /// </summary>
    public class LargeResultViewModel : ViewModelBase
    {
        private readonly LargeSystemInputService _service;
        private readonly IDataRepository _repository;
        private readonly ILargeSystemLoadCalculator _calculator;        private readonly ILargeSmokeCalculator _smokeCalculator = new LargeSmokeCalculator();
        private readonly ExcelReportGenerator _excel;
        private LargeSystemInput _input;
        private LargeSystemResult _lastResult;
        private LargeSmokeResult _lastSmoke;
        private ResultTable _table;
        private ResultTable _summaryTable;
        private IList<LargeSmokeZoneRow> _smokeRows = new List<LargeSmokeZoneRow>();
        private string _smokeSummary = "";
        private string _smokeNote = "";
        private string _resultText = "";
        private string _status = "";

        public LargeResultViewModel()
            : this(null)
        {
        }

        public LargeResultViewModel(IDataRepository repository)
            : this(repository, null)
        {
        }

        /// <summary>
        /// <paramref name="reportsDirectory"/> 用于自检时把导出的计算书写到临时目录
        /// (为空则用 <c>%AppData%\HVACIDA\Reports</c>)。
        /// </summary>
        public LargeResultViewModel(IDataRepository repository, string reportsDirectory)
        {
            _repository = repository ?? new XmlProjectRepository();
            _service = new LargeSystemInputService(_repository);
            _calculator = new LargeSystemLoadCalculator();
            _excel = new ExcelReportGenerator(reportsDirectory);
            _input = _service.Load();
            CalculateCommand = new RelayCommand(Calculate);
            ExportCommand = new RelayCommand(Export, () => _lastResult != null);
            ExportExcelCommand = new RelayCommand(ExportExcel, () => _lastResult != null);

            // 打开即算:本窗名为「计算结果」,打开就该有结果,不该让用户进来再点一次【计 算】。
            // Calculate 只读 large-system.xml / large-smoke.xml(不写盘、不动模型),放在构造函数里没有副作用;
            // 界面上仍保留【计 算】,用于"别的窗改完参数后刷新本窗"。
            Calculate();
        }

        public LargeSystemInput Input
        {
            get => _input;
            private set => Set(ref _input, value);
        }

        public ICommand CalculateCommand { get; }

        public ICommand ExportCommand { get; }

        /// <summary>导出 **Excel(.xlsx)** 计算书(负荷汇总 + 排烟分区 / 选型 + 口径与待补)。</summary>
        public ICommand ExportExcelCommand { get; }

        public string ResultText
        {
            get => _resultText;
            private set => Set(ref _resultText, value);
        }

        /// <summary>大系统负荷计算结果表(界面按分区分组渲染;与导出计算书同源)。</summary>
        public ResultTable Table
        {
            get => _table;
            private set => Set(ref _table, value);
        }

        /// <summary>
        /// 本窗页面主体的**两段小结**:一、计算参数(8 项)+ 二、选型参数(5 项)。
        /// 与 Excel / 文本计算书由同一份 <see cref="ResultTable"/> 渲染(§4.8)。
        /// </summary>
        public ResultTable SummaryTable
        {
            get => _summaryTable;
            private set => Set(ref _summaryTable, value);
        }

        /// <summary>排烟计算结果表(大系统 → 计算结果;与「排烟计算」窗同源)。</summary>
        public IList<LargeSmokeZoneRow> SmokeRows
        {
            get => _smokeRows;
            private set => Set(ref _smokeRows, value);
        }

        /// <summary>排烟风机选型结论一行。</summary>
        public string SmokeSummary
        {
            get => _smokeSummary;
            private set => Set(ref _smokeSummary, value);
        }

        /// <summary>排烟口径 + 过渡口径说明。</summary>
        public string SmokeNote
        {
            get => _smokeNote;
            private set => Set(ref _smokeNote, value);
        }

        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        /// <summary>导出 Excel(.xlsx)计算书:负荷汇总 + 排烟分区宽表 + 排烟选型 + 口径与待补。</summary>
        private void ExportExcel()
        {
            try
            {
                if (_lastResult == null)
                {
                    Status = "还没有可导出的结果:请先点【计 算】。";
                    return;
                }

                // 排烟参数在导出时重读一次(与 Calculate 用的是同一份 large-smoke.xml)
                var smokeInput = _repository.LoadLargeSmoke();
                var workbook = LargeSystemExcelExporter.BuildLoadAndSmoke(Input, _lastResult, smokeInput, _lastSmoke);
                string path = _excel.SaveWorkbook("大系统计算结果", workbook);
                Status = "Excel 计算书已生成(" + workbook.SheetCount + " 个工作表): " + path;
            }
            catch (System.Exception ex)
            {
                Status = "导出 Excel 失败: " + ex.Message;
            }
        }

        /// <summary>
        /// 用系统默认程序打开刚导出的计算书(2026-09-20 用户口径:导出后自动打开)。
        /// 文件不存在或打开失败**只写状态、不抛异常** —— 导出本身已经成功,不该因为打不开就报错。
        /// </summary>
        public bool OpenExportedFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    Status = "找不到刚导出的计算书: " + path;
                    return false;
                }

                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                Status = "计算书已保存并打开: " + path;
                return true;
            }
            catch (Exception ex)
            {
                Status = "计算书已保存,但自动打开失败(" + ex.Message + "): " + path;
                return false;
            }
        }

        private void Calculate()
        {
            try
            {
                Input = _service.Load();   // 每次计算前重新读取(含气象参数联动),确保与其它窗口同步
                _lastResult = _calculator.Calculate(Input);

                // 排烟计算:面积用同一份输入,参数取「排烟计算」窗保存的 large-smoke.xml
                var smokeInput = _repository.LoadLargeSmoke();
                _lastSmoke = _smokeCalculator.Calculate(Input, smokeInput);
                SmokeRows = new List<LargeSmokeZoneRow>(_lastSmoke.Zones);
                SmokeSummary =
                    "选型基准:" + _lastSmoke.GoverningZoneName + "   ·   排烟风机 " +
                    _lastSmoke.FanUnitCount.ToString("N0") + " 台   ·   单台选型风量 " +
                    _lastSmoke.UnitSelectionFlowM3H.ToString("N1") + " m³/h   ·   参考(公式文档 MAX/2)" +
                    _lastSmoke.UnitFlowPerFormulaDocM3H.ToString("N1") + " m³/h";
                SmokeNote = _lastSmoke.Note + "  " + _lastSmoke.PendingNote;

                Table = ResultTable.ForLargeSystem(Input, _lastResult);
                SummaryTable = ResultTable.ForLargeSystemSummary(_lastResult, _lastSmoke);
                ResultText = ResultFormatter.FormatLarge(Input, _lastResult);
                Status = "计算完成(与北京站算例同口径)。结果已按「计算参数 / 选型参数」两段呈现,可导出计算书。";
            }
            catch (System.Exception ex)
            {
                ResultText = "";
                Status = "计算失败: " + ex.Message;
            }
        }

        /// <summary>
        /// 导出计算书到**用户指定路径**(窗口用"另存为"对话框取路径):
        /// <c>.xlsx</c> → 排版优化过的完整工作簿(计算参数与选型 / 输入参数 / 负荷汇总 / 排烟分区 / 排烟选型 / 口径与待补);
        /// 其它扩展名 → 文本计算书(同样含小结 + 输入 + 负荷 + 排烟)。返回是否成功。
        /// </summary>
        public bool ExportCalculationBook(string path)
        {
            try
            {
                if (_lastResult == null)
                {
                    Status = "还没有可导出的结果:请先点【计 算】。";
                    return false;
                }
                if (string.IsNullOrEmpty(path))
                {
                    Status = "没有选择保存位置,导出已取消。";
                    return false;
                }

                var smokeInput = _repository.LoadLargeSmoke();

                if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    var workbook = LargeSystemExcelExporter.BuildLoadAndSmoke(Input, _lastResult, smokeInput, _lastSmoke);
                    workbook.Save(path);
                    Status = "Excel 计算书已保存(" + workbook.SheetCount + " 个工作表): " + path;
                    return true;
                }

                var sb = new StringBuilder();
                sb.AppendLine("【大系统计算书】负荷 + 排烟(需求 2.2.3.1)");
                sb.AppendLine("导出时间:" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine();
                sb.AppendLine(ResultTable.ForLargeSystemSummary(_lastResult, _lastSmoke).ToText());
                sb.AppendLine();
                sb.AppendLine(ResultTable.ForLargeSystemInput(Input).ToText());
                sb.AppendLine();
                sb.AppendLine(ResultFormatter.FormatLarge(Input, _lastResult));
                sb.AppendLine();
                sb.AppendLine(ResultFormatter.FormatLargeSmoke(Input, smokeInput, _lastSmoke));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));   // 带 BOM,记事本打开中文不乱码
                Status = "计算书已保存: " + path;
                return true;
            }
            catch (Exception ex)
            {
                Status = "导出失败: " + ex.Message;
                return false;
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

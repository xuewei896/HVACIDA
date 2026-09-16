using System;
using System.Collections.Generic;
using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 大系统「排烟计算」窗 ViewModel(需求 2.2.3.1 排烟量计算 + 排烟风机选型)。
    /// <para>
    /// 面积(D55/D56)来自与「公共区参数 / 负荷计算」共用的 <see cref="LargeSystemInput"/>(只读展示,单一数据源);
    /// 排烟专用参数(单位面积排烟量 / 选型系数 / 风机台数)存 <c>large-smoke.xml</c>。
    /// 结果以**表格**(<see cref="Rows"/>)给出,并给出选型基准区与单台风量。
    /// </para>
    /// </summary>
    public class LargeSmokeViewModel : ViewModelBase
    {
        private readonly ILargeSmokeCalculator _calculator;
        private readonly IDataRepository _repository;
        private readonly LargeSystemInputService _largeService;
        private readonly ExcelReportGenerator _excel;

        private LargeSmokeInput _input;
        private LargeSystemInput _areas;
        private LargeSmokeResult _result;
        private IList<LargeSmokeZoneRow> _rows = new List<LargeSmokeZoneRow>();
        private string _status = "";
        private string _selectionSummary = "";
        private string _note = "";
        private string _pendingNote = "";

        public LargeSmokeViewModel()
            : this(null)
        {
        }

        public LargeSmokeViewModel(IDataRepository repository)
            : this(repository, null)
        {
        }

        /// <summary>
        /// <paramref name="reportsDirectory"/> 用于自检时把导出的计算书写到临时目录
        /// (为空则用 <c>%AppData%\HVACIDA\Reports</c>)。
        /// </summary>
        public LargeSmokeViewModel(IDataRepository repository, string reportsDirectory)
        {
            _calculator = new LargeSmokeCalculator();
            _repository = repository ?? new XmlProjectRepository();
            _largeService = new LargeSystemInputService(_repository);
            _excel = new ExcelReportGenerator(reportsDirectory);

            _input = _repository.LoadLargeSmoke();
            _areas = _largeService.Load();          // 与公共区参数/负荷计算同一份(含气象联动)

            CalculateCommand = new RelayCommand(CalculateAndPersist);
            SaveCommand = new RelayCommand(Save);
            ExportCommand = new RelayCommand(Export, () => _result != null);
            ExportExcelCommand = new RelayCommand(ExportExcel, () => _result != null);
            ResetCommand = new RelayCommand(Reset);

            Calculate();
        }

        /// <summary>排烟计算参数(绑定路径 Input.*)。</summary>
        public LargeSmokeInput Input
        {
            get => _input;
            private set => Set(ref _input, value);
        }

        /// <summary>公共区面积来源(只读展示 D55/D56)。</summary>
        public LargeSystemInput Areas
        {
            get => _areas;
            private set
            {
                if (Set(ref _areas, value)) OnPropertyChanged(nameof(AreaSourceText));
            }
        }

        /// <summary>面积来源提示(公共区参数窗是否录入过)。</summary>
        public string AreaSourceText =>
            "站厅公共区面积 D55 = " + _areas.HallAreaM2.ToString("N1") + " m²," +
            "站台公共区面积 D56 = " + _areas.PlatformAreaM2.ToString("N1") + " m²" +
            "(来自「大系统 → 公共区参数」,本窗只读;修改请回该窗或点【重取面积】)";

        /// <summary>结果表格数据源(一行一个区域)。</summary>
        public IList<LargeSmokeZoneRow> Rows
        {
            get => _rows;
            private set => Set(ref _rows, value);
        }

        /// <summary>风机选型结论(基准区 / 台数 / 单台风量)。</summary>
        public string SelectionSummary
        {
            get => _selectionSummary;
            private set => Set(ref _selectionSummary, value);
        }

        /// <summary>口径说明。</summary>
        public string Note
        {
            get => _note;
            private set => Set(ref _note, value);
        }

        /// <summary>过渡口径 / 待补项(界面必须显示)。</summary>
        public string PendingNote
        {
            get => _pendingNote;
            private set => Set(ref _pendingNote, value);
        }

        public ICommand CalculateCommand { get; }

        public ICommand SaveCommand { get; }

        public ICommand ExportCommand { get; }

        /// <summary>导出 **Excel(.xlsx)** 计算书(排烟分区宽表 + 选型 + 口径与待补)。</summary>
        public ICommand ExportExcelCommand { get; }

        public ICommand ResetCommand { get; }

        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        /// <summary>
        /// 【计 算】按钮入口:**先把排烟参数落盘,再计算**。
        /// <para>
        /// large-smoke.xml 是「排烟计算」窗与「大系统 → 计算结果」窗共用的排烟参数源;只算不存会让
        /// 「计算结果」窗里的排烟结果仍按上一次保存的参数。重新取面积(<see cref="ReloadAreas"/>)、
        /// 恢复默认等内部重算走 <see cref="Calculate()"/>(不写盘)。
        /// </para>
        /// </summary>
        private void CalculateAndPersist()
        {
            string saveNote;
            try
            {
                _repository.SaveLargeSmoke(_input);
                saveNote = "本次计算已同时保存到 large-smoke.xml。";
            }
            catch (Exception ex)
            {
                saveNote = "⚠ 排烟参数保存失败(" + ex.Message + "),本次结果仅存在于本窗。";
            }

            Calculate();
            Status = saveNote + " " + Status;
        }

        /// <summary>导出 Excel(.xlsx)计算书:排烟分区(逐区域宽表)+ 排烟选型 + 口径与待补。</summary>
        private void ExportExcel()
        {
            try
            {
                if (_result == null)
                {
                    Status = "还没有可导出的结果:请先点【计 算】。";
                    return;
                }

                var workbook = LargeSystemExcelExporter.BuildSmoke(_input, _result);
                string path = _excel.SaveWorkbook("大系统排烟计算书", workbook);
                Status = "Excel 计算书已生成(" + workbook.SheetCount + " 个工作表): " + path;
            }
            catch (Exception ex)
            {
                Status = "导出 Excel 失败: " + ex.Message;
            }
        }

        /// <summary>内部重算(不写盘):构造函数、重新取面积、恢复默认走这里。</summary>
        private void Calculate()
        {
            try
            {
                _result = _calculator.Calculate(_areas, _input);
                Rows = new List<LargeSmokeZoneRow>(_result.Zones);
                Note = _result.Note;
                PendingNote = _result.PendingNote;
                SelectionSummary =
                    "风机选型基准:" + _result.GoverningZoneName + "(取站厅/站台大者)" +
                    "   ·   排烟风机 " + _result.FanUnitCount.ToString("N0") + " 台" +
                    "   ·   单台选型风量 " + _result.UnitSelectionFlowM3H.ToString("N1") + " m³/h" +
                    "   ·   参考(公式文档口径 MAX/2,不含系数)" + _result.UnitFlowPerFormulaDocM3H.ToString("N1") + " m³/h";
                Status = "计算完成。" + SelectionSummary;
            }
            catch (Exception ex)
            {
                Rows = new List<LargeSmokeZoneRow>();
                Status = "计算失败: " + ex.Message;
            }
        }

        /// <summary>重新从「公共区参数」读取面积(在那边改完面积后不用重开本窗)。</summary>
        public void ReloadAreas()
        {
            Areas = _largeService.Load();
            Calculate();
            Status = "已重新读取公共区面积并重算。" + SelectionSummary;
        }

        private void Save()
        {
            try
            {
                _repository.SaveLargeSmoke(_input);
                Status = "排烟计算参数已保存: " + _repository.StorageDirectory + "\\large-smoke.xml";
            }
            catch (Exception ex)
            {
                Status = "保存失败: " + ex.Message;
            }
        }

        private void Reset()
        {
            Input = new LargeSmokeInput();
            OnPropertyChanged(nameof(Input));
            Calculate();
            Status = "已恢复需求/公式文档默认值(60 / 1.2 / 2 台)并重算。";
        }

        private void Export()
        {
            try
            {
                if (_result == null) return;
                var generator = new TextReportGenerator();
                string path = generator.SaveTextReport(
                    "大系统排烟计算书",
                    ResultFormatter.FormatLargeSmoke(_areas, _input, _result));
                Status = "计算书已生成: " + path;
            }
            catch (Exception ex)
            {
                Status = "导出失败: " + ex.Message;
            }
        }
    }
}

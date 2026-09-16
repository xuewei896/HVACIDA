using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 水力计算窗 ViewModel(需求 2.3 风系统 / 2.4 水系统;风与水共用,介质由 Ribbon 按钮注入)。
    /// <para>
    /// 流程:【从模型拾取该系统…】→ 命令层关闭本窗、在模型里选构件、读回管网(同一个 ViewModel 重开窗)
    /// → 管段/末端/系数在界面上**可见可改** →【计算并保存】→ 结果表 + 计算书。
    /// </para>
    /// <para>
    /// 纪律:数值一律由 Core 的 <see cref="HydraulicCalculator"/> 算、<see cref="ResultTable"/> 与
    /// <see cref="ResultFormatter"/> 渲染;本类不写公式、不拼结果字符串。
    /// 模型里没读到的东西(管件匹配不到、末端没有阻力参数、缺断面…)**逐条列在待补提示里**,不静默按 0。
    /// </para>
    /// </summary>
    public class HydraulicSystemViewModel : ViewModelBase
    {
        private readonly HydraulicInputService _service;
        private readonly IHydraulicCalculator _calculator = new HydraulicCalculator();
        private readonly ExcelReportGenerator _excel;

        private HydraulicInput _input;
        private readonly ObservableCollection<HydraulicSegment> _segments;
        private readonly ObservableCollection<HydraulicTerminal> _terminals;
        private HydraulicSegment _selectedSegment;
        private HydraulicTerminal _selectedTerminal;
        private HydraulicCoefficients _coefficients;
        private HydraulicResult _result;
        private ResultTable _table;
        private string _resultText = "";
        private string _status = "";
        private string _note = "";
        private string _pendingNote = "";
        private string _sourceNote = "";
        private int _sequence;

        public HydraulicSystemViewModel()
            : this(HydraulicKind.AirDuct)
        {
        }

        public HydraulicSystemViewModel(HydraulicKind kind)
            : this(kind, null, false)
        {
        }

        public HydraulicSystemViewModel(HydraulicKind kind, IDataRepository repository)
            : this(kind, repository, false)
        {
        }

        public HydraulicSystemViewModel(HydraulicKind kind, IDataRepository repository, bool pickAvailable)
            : this(kind, repository, pickAvailable, null)
        {
        }

        /// <summary>
        /// <paramref name="reportsDirectory"/> 用于自检时把导出的计算书写到临时目录(为空则用
        /// <c>%AppData%\HVACIDA\Reports</c>,与文本计算书同目录)。
        /// </summary>
        public HydraulicSystemViewModel(HydraulicKind kind, IDataRepository repository, bool pickAvailable,
            string reportsDirectory)
        {
            _service = new HydraulicInputService(repository);
            _excel = new ExcelReportGenerator(reportsDirectory);
            IsPickAvailable = pickAvailable;
            _coefficients = _service.LoadCoefficients();
            _input = _service.Load(kind) ?? new HydraulicInput
            {
                Kind = kind,
                MediumTempC = kind == HydraulicKind.WaterPipe ? 10.0 : 20.0
            };
            _input.Kind = kind;
            if (_input.Segments == null) _input.Segments = new List<HydraulicSegment>();
            if (_input.Terminals == null) _input.Terminals = new List<HydraulicTerminal>();

            _segments = new ObservableCollection<HydraulicSegment>(_input.Segments);
            _terminals = new ObservableCollection<HydraulicTerminal>(_input.Terminals);
            _sequence = _segments.Count;

            CalculateCommand = new RelayCommand(CalculateAndSave);
            SaveCommand = new RelayCommand(Save);
            ExportCommand = new RelayCommand(Export, () => _result != null && _result.HasSegments);
            ExportExcelCommand = new RelayCommand(ExportExcel, () => _result != null && _result.HasSegments);
            DeleteSystemCommand = new RelayCommand(DeleteSystem);
            ClearCommand = new RelayCommand(Clear);
            AddSegmentCommand = new RelayCommand(AddSegment);
            RemoveSegmentCommand = new RelayCommand(RemoveSegment, () => _selectedSegment != null);
            AddTerminalCommand = new RelayCommand(AddTerminal);
            RemoveTerminalCommand = new RelayCommand(RemoveTerminal, () => _selectedTerminal != null);

            Calculate();     // 打开即算(与「计算结果」窗同规矩):有已保存数据就先把结果摆出来
        }

        // ================================================================== 介质与输入

        public HydraulicKind Kind => _input.Kind;

        /// <summary>是否水系统(界面用它切换单位与适用字段)。</summary>
        public bool IsWater => Kind == HydraulicKind.WaterPipe;

        /// <summary>是否风系统。</summary>
        public bool IsAir => !IsWater;

        /// <summary>介质名。</summary>
        public string KindName => IsWater ? "水系统" : "风系统";

        public string WindowTitle => "水力计算 — " + KindName + " - HVACIDA";

        public HydraulicInput Input
        {
            get => _input;
            private set => Set(ref _input, value);
        }

        /// <summary>管段表(可改长度/流量/粗糙度/Σζ;断面只读,来自模型几何)。</summary>
        public ObservableCollection<HydraulicSegment> Segments => _segments;

        /// <summary>末端 / 设备阻力项(可增删改)。</summary>
        public ObservableCollection<HydraulicTerminal> Terminals => _terminals;

        public HydraulicSegment SelectedSegment
        {
            get => _selectedSegment;
            set => Set(ref _selectedSegment, value);
        }

        public HydraulicTerminal SelectedTerminal
        {
            get => _selectedTerminal;
            set => Set(ref _selectedTerminal, value);
        }

        /// <summary>系数集(粗糙度 / 富余系数 / 局部阻力系数表;全部可改并可保存)。</summary>
        public HydraulicCoefficients Coefficients
        {
            get => _coefficients;
            private set => Set(ref _coefficients, value);
        }

        /// <summary>数据来源说明(拾取到的系统/构件数)。</summary>
        public string SourceNote
        {
            get => _sourceNote;
            private set => Set(ref _sourceNote, value);
        }

        /// <summary>待补 / 本次局限(红字,必须可见)。</summary>
        public string PendingNote
        {
            get => _pendingNote;
            private set => Set(ref _pendingNote, value);
        }

        /// <summary>口径说明。</summary>
        public string Note
        {
            get => _note;
            private set => Set(ref _note, value);
        }

        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        // ================================================================== 结果

        /// <summary>结果表(界面用共享控件 ResultTableView 渲染;与计算书同源)。</summary>
        public ResultTable Table
        {
            get => _table;
            private set => Set(ref _table, value);
        }

        /// <summary>逐段明细(结果)。</summary>
        public IList<HydraulicSegmentResult> SegmentRows => _result == null
            ? new List<HydraulicSegmentResult>()
            : _result.Segments;

        /// <summary>环路阻力项(结果)。</summary>
        public IList<HydraulicItemResult> ItemRows => _result == null
            ? new List<HydraulicItemResult>()
            : _result.Items;

        /// <summary>结论摘要(需求全压 / 需求扬程 + 校核)。</summary>
        public string Summary
        {
            get
            {
                if (_result == null) return "";
                string required = IsWater
                    ? "需求扬程 " + _result.RequiredHeadM.ToString("0.##") + " m"
                    : "需求全压 " + _result.RequiredPressurePa.ToString("0.#") + " Pa";
                string balance = _result.HasBranches
                    ? "   ·   并联支路 " + _result.Branches.Count + " 条,最大不平衡率 " +
                      _result.MaxImbalancePct.ToString("0.#") + "%" +
                      (_result.UnbalancedBranchCount > 0 ? "(超限 " + _result.UnbalancedBranchCount + " 条)" : "")
                    : "";
                return "计算总阻力 " + _result.TotalResistancePa.ToString("N1") + " Pa   ·   " + required +
                       (_result.CriticalSegmentCount > 0 ? "   ·   最不利环路 " + _result.CriticalSegmentCount + " 段" : "") +
                       balance;
            }
        }

        /// <summary>校核结论(模型额定值对需求值)。</summary>
        public string CheckVerdict => _result == null ? "" : _result.CheckVerdict;

        /// <summary>计算书全文(仅由 ResultFormatter 生成)。</summary>
        public string ResultText
        {
            get => _resultText;
            private set => Set(ref _resultText, value);
        }

        // ================================================================== 命令

        public ICommand CalculateCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ExportCommand { get; }

        /// <summary>导出 **Excel(.xlsx)计算书**(6 个工作表:汇总/管段明细/阻力项/并联平衡/特性曲线/取值与口径)。</summary>
        public ICommand ExportExcelCommand { get; }

        /// <summary>删除**当前编号**的这一套系统(按「介质 + 系统编号」;不影响同介质其它编号的系统)。</summary>
        public ICommand DeleteSystemCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand AddSegmentCommand { get; }
        public ICommand RemoveSegmentCommand { get; }
        public ICommand AddTerminalCommand { get; }
        public ICommand RemoveTerminalCommand { get; }

        // ================================================================== 模型拾取

        /// <summary>拾取是否可用(Revit 命令侧按有无 ActiveUIDocument 传入)。</summary>
        public bool IsPickAvailable { get; private set; }

        /// <summary>【从模型读取该系统…】已被请求(窗口已关闭,命令层据此拾取)。</summary>
        public bool PickRequested { get; private set; }

        /// <summary>请求从模型读取(理由同小系统窗:模态窗会禁用 Revit 主窗,必须先关窗)。</summary>
        public void RequestPick()
        {
            PickRequested = true;
        }

        /// <summary>命令层读完模型后清标记。</summary>
        public void ClearPickRequest()
        {
            PickRequested = false;
        }

        /// <summary>由命令层写入状态提示。</summary>
        public void SetStatus(string text)
        {
            Status = text ?? "";
        }

        /// <summary>
        /// 命令层从模型读回的系统 → 替换当前输入并立即算一遍(不落盘:落盘由【计算并保存】决定)。
        /// </summary>
        public void ApplyPickedSystem(HydraulicInput input, string note)
        {
            try
            {
                if (input == null)
                {
                    Status = "没有读到系统数据:" + (note ?? "");
                    return;
                }

                input.Kind = Kind;
                if (input.ExtraFactor <= 0)
                    input.ExtraFactor = IsWater ? _coefficients.WaterExtraFactor : _coefficients.AirExtraFactor;
                // 系统编号留空 → 用模型里的系统名兜底(否则多套系统会互相覆盖)
                if (string.IsNullOrEmpty(input.SystemCode))
                    input.SystemCode = string.IsNullOrEmpty(input.SystemName) ? "" : input.SystemName;
                Input = input;
                SyncCollections();
                Calculate();
                Status = note ?? "";
            }
            catch (Exception ex)
            {
                Status = "读取模型数据后计算失败: " + ex.Message;
            }
        }

        // ================================================================== 实现

        private void SyncCollections()
        {
            _segments.Clear();
            foreach (var segment in _input.Segments) _segments.Add(segment);
            _terminals.Clear();
            foreach (var terminal in _input.Terminals) _terminals.Add(terminal);
            _sequence = _segments.Count;
            OnPropertyChanged(nameof(Segments));
            OnPropertyChanged(nameof(Terminals));
            OnPropertyChanged(nameof(Input));
        }

        private void SyncInput()
        {
            _input.Segments = new List<HydraulicSegment>(_segments);
            _input.Terminals = new List<HydraulicTerminal>(_terminals);
            // 界面上改了宽/高/内径 → 归一化断面形状(矩形 / 圆形),避免"改了尺寸形状没跟上"
            foreach (var segment in _input.Segments)
            {
                if (segment != null) segment.NormalizeShape();
            }
        }

        private void Calculate()
        {
            try
            {
                SyncInput();
                _result = _calculator.Calculate(_input, _coefficients);

                if (_result.Segments.Count == 0 && _result.Items.Count == 0)
                {
                    // 没有管网数据就不摆结果(与小系统/大系统同一纪律:没算过就不显示数字)
                    Table = null;
                    Note = "";
                    PendingNote = _result.PendingNote;
                    SourceNote = _input.SourceNote;
                    ResultText = "";
                    OnPropertyChanged(nameof(SegmentRows));
                    OnPropertyChanged(nameof(ItemRows));
                    OnPropertyChanged(nameof(Summary));
                    OnPropertyChanged(nameof(CheckVerdict));
                    Status = "还没有管网数据:点【从模型读取该系统…】在模型里选本系统的构件(风管/水管/管件/风口/末端/风机/水泵),或点【加行】手工录入。";
                    return;
                }

                Table = ResultTable.ForHydraulic(_input, _result);
                Note = _result.Note;
                PendingNote = string.IsNullOrEmpty(_input.PendingNote)
                    ? _result.PendingNote
                    : _input.PendingNote + " " + _result.PendingNote;
                SourceNote = _input.SourceNote;
                ResultText = ResultFormatter.FormatHydraulic(_input, _result, _coefficients);
                OnPropertyChanged(nameof(SegmentRows));
                OnPropertyChanged(nameof(ItemRows));
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(CheckVerdict));
            }
            catch (Exception ex)
            {
                _result = null;
                Table = null;
                Note = "";
                PendingNote = "";
                ResultText = "";
                Status = "计算失败: " + ex.Message;
            }
        }

        /// <summary>
        /// 【计算并保存】:先把本次输入与系数落盘,再计算(与「计算结果」窗读同一份数据 —— 口径分叉的老问题)。
        /// 没有管段时不落盘(避免在「计算结果」窗里留一个空系统)。
        /// </summary>
        private void CalculateAndSave()
        {
            string saveNote;
            try
            {
                SyncInput();
                if (_segments.Count == 0)
                {
                    saveNote = "当前没有管段,未保存(避免在「计算结果」窗里留一个空系统);请先【从模型读取该系统…】或手工加行。";
                }
                else
                {
                    int systemCount = _service.Save(_input);
                    _service.SaveCoefficients(_coefficients);
                    saveNote = "本次计算已同时保存(系统编号「" + CodeText + "」," + _segments.Count +
                               " 段;全站共 " + systemCount + " 套系统):" + _service.StorageDirectory + "\\hydraulic.xml。";
                }
            }
            catch (Exception ex)
            {
                saveNote = "⚠ 保存失败(" + ex.Message + "),本次结果仅存在于本窗。";
            }

            Calculate();
            Status = saveNote + " " + Status;
        }

        /// <summary>当前系统编号(空则显示"—")。</summary>
        public string CodeText => string.IsNullOrEmpty(_input.SystemCode) ? "—" : _input.SystemCode;

        private void Save()
        {
            try
            {
                SyncInput();
                int systemCount = _service.Save(_input);
                _service.SaveCoefficients(_coefficients);
                Status = "已保存(系数集 + 系统编号「" + CodeText + "」;全站共 " + systemCount + " 套系统):" +
                         _service.StorageDirectory + "\\hydraulic.xml";
            }
            catch (Exception ex)
            {
                Status = "保存失败: " + ex.Message;
            }
        }

        private void Export()
        {
            try
            {
                if (_result == null) return;
                var generator = new TextReportGenerator();
                string path = generator.SaveTextReport(
                    IsWater ? "水系统水力计算书" : "风系统水力计算书",
                    ResultFormatter.FormatHydraulic(_input, _result, _coefficients));
                Status = "文本计算书已生成: " + path;
            }
            catch (Exception ex)
            {
                Status = "导出失败: " + ex.Message;
            }
        }

        /// <summary>导出 Excel(.xlsx)计算书:6 页(汇总 / 管段明细 / 环路阻力项 / 并联环路平衡 / 阻力特性曲线 / 取值与口径)。</summary>
        private void ExportExcel()
        {
            try
            {
                if (_result == null || !_result.HasSegments)
                {
                    Status = "还没有可导出的结果:请先【从模型读取该系统…】或手工加行。";
                    return;
                }

                var workbook = HydraulicExcelExporter.BuildSystem(_input, _result, _coefficients);
                string path = _excel.SaveWorkbook(
                    (IsWater ? "水系统水力计算书_" : "风系统水力计算书_") + CodeText, workbook);
                Status = "Excel 计算书已生成(" + workbook.SheetCount + " 个工作表): " + path;
            }
            catch (Exception ex)
            {
                Status = "导出 Excel 失败: " + ex.Message;
            }
        }

        /// <summary>删除当前编号的这一套系统(按「介质 + 系统编号」;同介质其它编号的系统保留)。</summary>
        private void DeleteSystem()
        {
            try
            {
                string code = _input.SystemCode ?? "";
                if (!_service.Remove(Kind, code))
                {
                    Status = "没有可删除的系统:当前编号「" + CodeText + "」在 hydraulic.xml 里不存在(尚未保存过)。";
                    return;
                }

                _input = new HydraulicInput
                {
                    Kind = Kind,
                    MediumTempC = IsWater ? 10.0 : 20.0,
                    ExtraFactor = IsWater ? _coefficients.WaterExtraFactor : _coefficients.AirExtraFactor
                };
                SyncCollections();
                Calculate();
                Status = "已删除系统「" + (string.IsNullOrEmpty(code) ? "—" : code) + "」(同介质其它编号的系统保留);" +
                         "全站现有 " + _service.LoadProject().SystemCount + " 套系统。";
            }
            catch (Exception ex)
            {
                Status = "删除失败: " + ex.Message;
            }
        }

        private void Clear()
        {
            try
            {
                _service.Clear(Kind);
                _input = new HydraulicInput
                {
                    Kind = Kind,
                    MediumTempC = IsWater ? 10.0 : 20.0,
                    ExtraFactor = IsWater ? _coefficients.WaterExtraFactor : _coefficients.AirExtraFactor
                };
                SyncCollections();
                Calculate();
                Status = "已清空本介质的管网数据(不影响另一介质与系数集)。";
            }
            catch (Exception ex)
            {
                Status = "清空失败: " + ex.Message;
            }
        }

        private void AddSegment()
        {
            try
            {
                _sequence++;
                var segment = new HydraulicSegment
                {
                    Name = "手工管段 " + _sequence,
                    Shape = HydraulicShape.Round,
                    DiameterM = 0.5,
                    LengthM = 10,
                    FlowM3H = 1000
                };
                _segments.Add(segment);
                SelectedSegment = segment;
                SyncInput();
                Status = "已加一行管段(默认 Φ0.500 m / 10 m / 1000 m³/h),请按实际修改断面、长度与流量。";
            }
            catch (Exception ex)
            {
                Status = "加管段失败: " + ex.Message;
            }
        }

        private void RemoveSegment()
        {
            var segment = _selectedSegment;
            if (segment == null) return;
            _segments.Remove(segment);
            SelectedSegment = _segments.Count == 0 ? null : _segments[0];
            SyncInput();
            Status = "已删除管段「" + (segment.Name ?? "") + "」。";
        }

        private void AddTerminal()
        {
            try
            {
                var terminal = new HydraulicTerminal
                {
                    Name = "手工阻力项 " + (_terminals.Count + 1),
                    Kind = HydraulicItemKind.Terminal,
                    Source = "用户输入"
                };
                _terminals.Add(terminal);
                SelectedTerminal = terminal;
                SyncInput();
                Status = "已加一行末端/设备阻力项,请填阻力(水系统按 Pa 填,1 kPa = 1000 Pa;风系统填 Pa)。";
            }
            catch (Exception ex)
            {
                Status = "加阻力项失败: " + ex.Message;
            }
        }

        private void RemoveTerminal()
        {
            var terminal = _selectedTerminal;
            if (terminal == null) return;
            _terminals.Remove(terminal);
            SelectedTerminal = _terminals.Count == 0 ? null : _terminals[0];
            SyncInput();
            Status = "已删除阻力项「" + (terminal.Name ?? "") + "」。";
        }
    }
}

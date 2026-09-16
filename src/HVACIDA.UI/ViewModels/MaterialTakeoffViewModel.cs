using System;
using System.Collections.Generic;
using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 出图 → 明细表(**材料表统计**,需求 2.5)窗 ViewModel。
    /// <para>
    /// 数据由 Revit 命令层读取(UI 层不碰 Revit API)后通过 <see cref="ApplyTakeoff"/> 注入;
    /// 归并口径、类别小计、表格与 Excel 全在 Core(<see cref="MaterialTakeoffService"/> /
    /// <see cref="MaterialTakeoffTable"/> / <see cref="MaterialTakeoffExcelExporter"/>)。
    /// </para>
    /// </summary>
    public class MaterialTakeoffViewModel : ViewModelBase
    {
        private readonly ExcelReportGenerator _excel;

        private MaterialTakeoffResult _result = new MaterialTakeoffResult();
        private ResultTable _table;
        private string _sourceNote = "";
        private string _pendingNote = "";
        private string _status = "";
        private bool _reloadRequested;

        public MaterialTakeoffViewModel()
            : this(null)
        {
        }

        public MaterialTakeoffViewModel(string reportsDirectory)
        {
            _excel = new ExcelReportGenerator(reportsDirectory);
            ExportExcelCommand = new RelayCommand(ExportExcel, () => _result != null && _result.HasRows);
            ReloadCommand = new RelayCommand(RequestReload);
            ApplyTakeoff(null, "尚未读取模型。");
        }

        public string WindowTitle => "出图 — 明细表(材料表统计) - HVACIDA";

        /// <summary>统计结果(逐类型明细 = <see cref="MaterialTakeoffResult.Rows"/>)。</summary>
        public MaterialTakeoffResult Result
        {
            get => _result;
            private set => Set(ref _result, value);
        }

        /// <summary>逐类型明细(界面 DataGrid 绑定;列在 XAML 里,数值保持数值)。</summary>
        public IList<MaterialTakeoffRow> Rows => _result == null ? new List<MaterialTakeoffRow>() : _result.Rows;

        /// <summary>类别小计表(ResultTableView 绑定;与计算书 / Excel 同源)。</summary>
        public ResultTable Table
        {
            get => _table;
            private set => Set(ref _table, value);
        }

        /// <summary>数据来源说明(读了哪些类别、多少构件)。</summary>
        public string SourceNote
        {
            get => _sourceNote;
            private set => Set(ref _sourceNote, value);
        }

        /// <summary>待补 / 局限(红字)。</summary>
        public string PendingNote
        {
            get => _pendingNote;
            private set => Set(ref _pendingNote, value);
        }

        /// <summary>汇总标题(件数 / 类型数 / 类别数)。</summary>
        public string SummaryTitle => _result == null || !_result.HasRows
            ? "材料表统计(尚无数据)"
            : "共 " + _result.ItemCount + " 个构件 / " + _result.Rows.Count + " 种类型 / " +
              _result.CategoryTotals.Count + " 个类别";

        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        /// <summary>导出 **Excel(.xlsx)** 材料表(类别小计 / 逐类型明细 / 口径与待补)。</summary>
        public ICommand ExportExcelCommand { get; }

        /// <summary>重新读取模型(窗口会关闭,由命令层读完再用同一 ViewModel 重开窗)。</summary>
        public ICommand ReloadCommand { get; }

        /// <summary>已请求重新读取模型(命令层据此读模型并重开窗)。</summary>
        public bool ReloadRequested
        {
            get => _reloadRequested;
            private set => Set(ref _reloadRequested, value);
        }

        private void RequestReload()
        {
            ReloadRequested = true;
        }

        /// <summary>命令层清标记。</summary>
        public void ClearReloadRequest()
        {
            ReloadRequested = false;
        }

        /// <summary>
        /// 注入读取结果(命令层读完模型后调用)。为 null 时表示没有数据 —— 不摆结果,只给指引。
        /// </summary>
        public void ApplyTakeoff(IList<MaterialItem> items, string note)
        {
            try
            {
                var result = new MaterialTakeoffService().Summarize(items);
                Result = result;
                SourceNote = note ?? "";
                PendingNote = result.PendingNote ?? "";
                OnPropertyChanged(nameof(Rows));
                OnPropertyChanged(nameof(SummaryTitle));

                if (result.HasRows)
                {
                    Table = MaterialTakeoffTable.ForSummary(result);
                    Status = "材料表统计完成:" + SummaryTitle + "。可导出 Excel 或点【重新读取模型】刷新。";
                }
                else
                {
                    // 没读到东西就不摆结果(与其它模块同一纪律)
                    Table = null;
                    Status = result.Note;
                }
            }
            catch (Exception ex)
            {
                Result = new MaterialTakeoffResult();
                Table = null;
                OnPropertyChanged(nameof(Rows));
                OnPropertyChanged(nameof(SummaryTitle));
                Status = "材料表统计失败: " + ex.Message;
            }
        }

        private void ExportExcel()
        {
            try
            {
                if (_result == null || !_result.HasRows)
                {
                    Status = "还没有可导出的材料表:请先在 Revit 里点【出图 → 明细表】读取模型。";
                    return;
                }

                var workbook = MaterialTakeoffExcelExporter.Build(_result);
                string path = _excel.SaveWorkbook("材料表统计", workbook);
                Status = "Excel 材料表已生成(" + workbook.SheetCount + " 个工作表): " + path;
            }
            catch (Exception ex)
            {
                Status = "导出 Excel 失败: " + ex.Message;
            }
        }
    }
}

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// 小系统计算结果窗(Ribbon「小系统 → 计算结果」):**全站多系统汇总**。
    /// <para>
    /// 界面结构:汇总表(逐系统一行,列由 Core 的 <see cref="SmallRoomTable.SummaryColumns"/> 生成)
    /// + 全站合计表(<see cref="ResultTableView"/>) + 选中系统的房间明细 / 设备选型 / 计算书全文。
    /// 数值口径全在 Core;code-behind 只按 Core 的列定义生成表格列,并在"选中行换系统类型"时重建房间明细列。
    /// </para>
    /// </summary>
    public partial class SmallSystemResultWindow : Window
    {
        private SmallResultViewModel _viewModel;

        public SmallSystemResultWindow()
        {
            InitializeComponent();
            DataContext = new SmallResultViewModel();
            Attach();
        }

        public SmallSystemResultWindow(SmallResultViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            Attach();
        }

        /// <summary>生成汇总表列(由 Core 定义)、房间明细列,并订阅"选中行变化"。</summary>
        private void Attach()
        {
            SmallSystemColumns.BuildSummary(SummaryGrid);

            _viewModel = DataContext as SmallResultViewModel;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                SmallSystemColumns.BuildRoomDetail(RoomDetailGrid, _viewModel.DetailSystemType);
            }
            else
            {
                SmallSystemColumns.BuildRoomDetail(RoomDetailGrid, SmallSystemType.AllAirOnceReturn);
            }
        }

        /// <summary>选中的系统行换了系统类型 → 房间明细表列按新类型重建(列数与 Core 的 ColumnsFor 一致)。</summary>
        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SmallResultViewModel.DetailSystemType)) return;

            var viewModel = sender as SmallResultViewModel;
            if (viewModel == null) return;
            SmallSystemColumns.BuildRoomDetail(RoomDetailGrid, viewModel.DetailSystemType);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
            base.OnClosed(e);
        }
    }
}

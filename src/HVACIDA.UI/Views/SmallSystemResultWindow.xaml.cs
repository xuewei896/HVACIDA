using System.Windows;
using HVACIDA.Core.Models;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// 小系统计算结果窗(Ribbon「小系统 → 计算结果」)。
    /// code-behind 只按 Core 的列定义生成房间明细表列;数值口径全在 Core。
    /// </summary>
    public partial class SmallSystemResultWindow : Window
    {
        public SmallSystemResultWindow()
        {
            InitializeComponent();
            DataContext = new SmallResultViewModel();
            BuildColumns();
        }

        public SmallSystemResultWindow(SmallResultViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            BuildColumns();
        }

        /// <summary>房间 / 分区明细表的列由 <c>SmallRoomTable.ColumnsFor</c> 生成(按系统类型取舍)。</summary>
        private void BuildColumns()
        {
            var viewModel = DataContext as SmallResultViewModel;
            SmallSystemType type = viewModel == null || viewModel.Input == null
                ? SmallSystemType.AllAirOnceReturn
                : viewModel.Input.SystemType;

            SmallSystemColumns.BuildRoomDetail(RoomDetailGrid, type);
        }
    }
}

using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// 小系统负荷计算窗(Ribbon「小系统」六类按钮共用,系统类型由按钮决定)。
    /// <para>
    /// code-behind 只做三件事:**按 Core 的列定义生成 DataGrid 列**、无参构造、事件转发;
    /// 数值口径、表头文案、勾稽关系全部在 Core(View / ViewModel 都不写死结果)。
    /// </para>
    /// </summary>
    public partial class SmallSystemWindow : Window
    {
        public SmallSystemWindow()
        {
            InitializeComponent();
            DataContext = new SmallSystemViewModel();
            ApplyViewModel();
        }

        public SmallSystemWindow(SmallSystemViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            ApplyViewModel();
        }

        /// <summary>标题与两张由 Core 定义列的 DataGrid(录入表 / 房间明细表)在此装配。</summary>
        private void ApplyViewModel()
        {
            var viewModel = DataContext as SmallSystemViewModel;
            if (viewModel == null) return;

            Title = "小系统 — " + viewModel.SystemTypeName + " - HVACIDA";
            SmallSystemColumns.BuildInput(InputGrid, viewModel.Input.SystemType);
            SmallSystemColumns.BuildRoomDetail(RoomDetailGrid, viewModel.Input.SystemType);
        }
    }
}

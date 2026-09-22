using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>大系统「排烟计算」窗(Ribbon「大系统 → 排烟计算」)。结果以表格呈现。</summary>
    public partial class LargeSmokeWindow : Window
    {
        public LargeSmokeWindow()
        {
            InitializeComponent();
            DataContext = new LargeSmokeViewModel();
        }

        public LargeSmokeWindow(LargeSmokeViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>【重取面积】:在「公共区参数」改过 D55/D56 后,不必重开本窗。</summary>
        private void OnReloadAreasClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as LargeSmokeViewModel;
            if (viewModel != null) viewModel.ReloadAreas();
        }

        /// <summary>【确 定】= 保存并关闭;保存失败时不关窗,以便看到失败原因。</summary>
        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as LargeSmokeViewModel;
            if (viewModel == null)
            {
                Close();
                return;
            }

            if (viewModel.TrySave()) Close();
        }
    }
}

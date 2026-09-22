using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>大系统负荷计算窗。</summary>
    public partial class LargeSystemWindow : Window
    {
        public LargeSystemWindow()
        {
            InitializeComponent();
            DataContext = new LargeSystemViewModel();
        }

        public LargeSystemWindow(LargeSystemViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>【确 定】= 保存并关闭;保存失败时不关窗,以便看到失败原因。</summary>
        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as LargeSystemViewModel;
            if (viewModel == null)
            {
                Close();
                return;
            }

            if (viewModel.TrySave()) Close();
        }
    }
}

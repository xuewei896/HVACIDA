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
    }
}

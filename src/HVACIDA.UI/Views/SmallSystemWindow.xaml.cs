using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>小系统负荷计算窗。</summary>
    public partial class SmallSystemWindow : Window
    {
        public SmallSystemWindow()
        {
            InitializeComponent();
            DataContext = new SmallSystemViewModel();
        }

        public SmallSystemWindow(SmallSystemViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}

using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>公共区参数窗(Ribbon「大系统 → 公共区参数」)。</summary>
    public partial class PublicAreaWindow : Window
    {
        public PublicAreaWindow()
        {
            InitializeComponent();
            DataContext = new PublicAreaViewModel();
        }

        public PublicAreaWindow(PublicAreaViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}

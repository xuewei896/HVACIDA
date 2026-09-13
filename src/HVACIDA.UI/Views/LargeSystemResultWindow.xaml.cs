using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>大系统计算结果窗(Ribbon「大系统 → 计算结果」)。</summary>
    public partial class LargeSystemResultWindow : Window
    {
        public LargeSystemResultWindow()
        {
            InitializeComponent();
            DataContext = new LargeResultViewModel();
        }

        public LargeSystemResultWindow(LargeResultViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}

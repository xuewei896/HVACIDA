using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>小系统计算结果窗(Ribbon「小系统 → 计算结果」)。</summary>
    public partial class SmallSystemResultWindow : Window
    {
        public SmallSystemResultWindow()
        {
            InitializeComponent();
            DataContext = new SmallResultViewModel();
        }

        public SmallSystemResultWindow(SmallResultViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}

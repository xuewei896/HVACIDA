using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// 水力计算「计算结果」窗(Ribbon「水力计算 → 计算结果」):风系统 / 水系统各一行汇总 +
    /// 选中介质的逐段明细、环路阻力项与计算书全文。打开即算(不用进来再点一次计算)。
    /// </summary>
    public partial class HydraulicResultWindow : Window
    {
        public HydraulicResultWindow()
        {
            InitializeComponent();
            DataContext = new HydraulicResultViewModel();
        }

        public HydraulicResultWindow(HydraulicResultViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}

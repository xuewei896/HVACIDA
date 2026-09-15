using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// 水力计算窗(Ribbon「水力计算 → 风系统 / 水系统」;风与水共用,介质由命令注入)。
    /// <para>
    /// code-behind 只做两件事:无参构造(给命令基类用)与**拾取请求转发**。
    /// 数值、表头、口径全在 Core(<see cref="HVACIDA.Core.Services.HydraulicCalculator"/> /
    /// <see cref="HVACIDA.Core.Services.ResultTable"/>)。
    /// </para>
    /// <para>
    /// **拾取闭环**:模态窗会在 Win32 层禁用 Revit 主窗,所以【从模型读取该系统…】只置标记并关闭本窗,
    /// 由命令层在模型里拾取、读管网,再用**同一个 ViewModel** 重开本窗(用户已改的系数不丢)。
    /// </para>
    /// </summary>
    public partial class HydraulicSystemWindow : Window
    {
        public HydraulicSystemWindow()
        {
            InitializeComponent();
            DataContext = new HydraulicSystemViewModel();
        }

        public HydraulicSystemWindow(HydraulicSystemViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>【从模型读取该系统…】已请求(本窗已关闭,命令层据此拾取)。</summary>
        public bool PickRequested { get; private set; }

        private void OnPickClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as HydraulicSystemViewModel;
            if (viewModel == null || !viewModel.IsPickAvailable) return;

            viewModel.RequestPick();
            PickRequested = true;
            Close();
        }
    }
}

using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// 气象参数窗(需求 2.1.2):与工程信息窗共用 ProjectInfoViewModel / project.xml。
    /// <para>
    /// 固定尺寸窗口(<c>ResizeMode=NoResize</c>),按「室外气象参数 / 室内设计参数」两个分区排版;
    /// 【确 定】= 保存并关闭本窗(保存失败不关窗)。
    /// </para>
    /// </summary>
    public partial class WeatherWindow : Window
    {
        public WeatherWindow()
        {
            InitializeComponent();
            DataContext = new ProjectInfoViewModel();
        }

        public WeatherWindow(ProjectInfoViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>【确 定】= 保存并关闭;保存失败时不关窗,以便看到失败原因(同工程信息窗 / 公共区参数窗)。</summary>
        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as ProjectInfoViewModel;
            if (viewModel == null)
            {
                Close();
                return;
            }

            if (viewModel.TrySave()) Close();
        }
    }
}

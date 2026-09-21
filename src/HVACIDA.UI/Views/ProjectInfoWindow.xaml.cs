using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// 项目信息与气象参数窗(需求 2.1.1)。
    /// <para>固定尺寸窗口(<c>ResizeMode=NoResize</c>);【确 定】= 保存并关闭本窗。</para>
    /// </summary>
    public partial class ProjectInfoWindow : Window
    {
        public ProjectInfoWindow()
        {
            InitializeComponent();
            DataContext = new ProjectInfoViewModel();
        }

        public ProjectInfoWindow(ProjectInfoViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>【确 定】= 保存并关闭;保存失败时不关窗,以便看到失败原因(同 §4.2 公共区参数窗)。</summary>
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

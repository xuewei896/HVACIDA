using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>项目信息与气象参数窗。</summary>
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
    }
}

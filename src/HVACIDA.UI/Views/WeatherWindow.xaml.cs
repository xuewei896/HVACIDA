using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>气象参数窗(需求 2.1.2):与工程信息窗共用 ProjectInfoViewModel / project.xml。</summary>
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
    }
}

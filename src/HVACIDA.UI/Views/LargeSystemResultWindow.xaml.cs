using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>大系统计算结果窗(Ribbon「大系统 → 计算结果」)。页面主体 = 计算参数 + 选型参数两段。</summary>
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

        /// <summary>
        /// 【导出计算书】:先弹"另存为"对话框让用户选保存位置,再写文件
        /// (.xlsx = 排版优化过的完整工作簿;.txt = 文本计算书,两者都含小结 / 输入 / 负荷 / 排烟)。
        /// </summary>
        private void OnExportClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as LargeResultViewModel;
            if (viewModel == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出计算书",
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx|文本计算书 (*.txt)|*.txt",
                FilterIndex = 1,
                DefaultExt = ".xlsx",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = "大系统计算书_" + System.DateTime.Now.ToString("yyyyMMdd_HHmm")
            };

            if (dialog.ShowDialog(this) == true) viewModel.ExportCalculationBook(dialog.FileName);
        }

        /// <summary>【确 定】= 关闭本窗(本窗只读展示,不写盘)。</summary>
        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

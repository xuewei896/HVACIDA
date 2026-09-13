using System;
using System.Linq;
using System.Text;
using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// 通用信息窗:显示"待实现说明 / 操作指南 / 帮助"等只读内容(内容来自 ModuleCatalog)。
    /// </summary>
    public partial class InfoWindow : Window
    {
        public InfoWindow()
            : this(InfoViewModel.Guide())
        {
        }

        public InfoWindow(InfoViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>把窗口内容复制到剪贴板(便于反馈问题时附上口径说明)。</summary>
        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as InfoViewModel;
                if (vm == null) return;

                var sb = new StringBuilder();
                sb.AppendLine(vm.Title);
                sb.AppendLine(new string('=', 40));
                sb.AppendLine(vm.Intro);
                sb.AppendLine("状态: " + vm.StatusText);
                sb.AppendLine();
                foreach (var section in vm.Sections)
                {
                    sb.AppendLine("【" + section.Heading + "】");
                    foreach (var line in section.Lines) sb.AppendLine("  · " + line);
                    sb.AppendLine();
                }

                Clipboard.SetText(sb.ToString());
                StatusText.Text = "已复制到剪贴板(" + sb.Length + " 字)";
            }
            catch (Exception ex)
            {
                StatusText.Text = "复制失败: " + ex.Message;
            }
        }
    }
}

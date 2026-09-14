using System.Windows;
using System.Windows.Controls;
using HVACIDA.Core.Services;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// 公共区参数窗(Ribbon「大系统 → 公共区参数」)。
    /// <para>
    /// 「拾取站厅/站台空间…」不在这里调用 Revit API,而是置
    /// <see cref="PublicAreaViewModel.RequestPick"/> 后<strong>关闭本窗</strong>:
    /// WPF 模态对话框会在 Win32 层禁用 Owner(Revit 主窗),模态期间模型不可点选,
    /// 且 <c>Hide()</c> 也不会恢复 Owner —— 必须让模态循环真正结束。
    /// 关闭后由 <c>HVACIDA.Revit.Commands.ShowPublicAreaCommand</c> 调 <c>Selection.PickObjects</c>,
    /// 再用同一个 ViewModel 重新开窗(用户已填内容不丢)。
    /// </para>
    /// </summary>
    public partial class PublicAreaWindow : Window
    {
        public PublicAreaWindow()
        {
            InitializeComponent();
            DataContext = new PublicAreaViewModel();
        }

        public PublicAreaWindow(PublicAreaViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>用户请求模型拾取(本窗已关闭,命令层据此执行拾取)。</summary>
        public bool PickRequested { get; private set; }

        /// <summary>【确 定】= 保存并关闭(UI设计规范 §4.2);保存失败不关窗,以便看到失败原因。</summary>
        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as PublicAreaViewModel;
            if (viewModel == null)
            {
                Close();
                return;
            }

            if (viewModel.TrySave()) Close();
        }

        private void OnPickClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as PublicAreaViewModel;
            var button = sender as Button;
            if (viewModel == null || button == null) return;

            var target = string.Equals(button.Tag as string, "platform", System.StringComparison.Ordinal)
                ? PublicAreaTarget.Platform
                : PublicAreaTarget.Hall;

            viewModel.RequestPick(target);
            PickRequested = true;
            Close();
        }
    }
}

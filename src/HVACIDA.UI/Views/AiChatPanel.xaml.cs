using System.Windows.Controls;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// **AI 助手面板**(停靠面板的内容控件,不是独立窗口 —— 承参考文档「在 Revit 右侧嵌一个聊天面板」)。
    /// <para>
    /// 逻辑全在 <see cref="AiAssistantViewModel"/>;这里只做绑定与"打开面板时刷新设置/命令集状态"。
    /// </para>
    /// </summary>
    public partial class AiChatPanel : UserControl
    {
        public AiChatPanel()
            : this(new AiAssistantViewModel())
        {
        }

        public AiChatPanel(AiAssistantViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = viewModel;
        }

        /// <summary>面板的 ViewModel(命令层/停靠面板可调用 <see cref="AiAssistantViewModel.Reload"/>)。</summary>
        public AiAssistantViewModel ViewModel { get; }

        /// <summary>面板每次显示时刷新一次(文档切换、命令集是否就绪都会变)。</summary>
        public void RefreshOnShow()
        {
            ViewModel.Reload();
        }
    }
}

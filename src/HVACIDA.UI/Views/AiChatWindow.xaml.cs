using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// 独立「AI 对话」窗(2026-09-28 用户口径:在 AI问答模块里植入 AI 对话框、可调用 AI 工具,
    /// 效果对标 Revit 2027 的 Autodesk Assistant)。
    /// <para>
    /// **非模态**:工具调用要经 <c>ExternalEvent</c> 回到 Revit 主线程执行,模态窗会嵌套消息循环、
    /// 把 Idling 派活挡住(与停靠面板同一原因,见 <see cref="AiChatPanel"/>);窗口由命令层静态持有,
    /// 避免被 GC(参考文档点名的坑)。
    /// </para>
    /// <para>
    /// 引擎与「AI助手」面板完全共用 <see cref="AiAssistantViewModel"/>:同一套检索依据 / Revit 上下文 /
    /// SSE 流式 / function calling / 进程内 CommandBus / 两级安全开关 / DPAPI 密钥保险箱;
    /// 本窗额外把**每次工具调用逐条列出来并打勾**(✓ 完成 / ✗ 失败 + 命令名 + 耗时)。
    /// </para>
    /// </summary>
    public partial class AiChatWindow : Window
    {
        public AiChatWindow()
        {
            InitializeComponent();
            DataContext = new AiAssistantViewModel();
        }

        public AiChatWindow(AiAssistantViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}

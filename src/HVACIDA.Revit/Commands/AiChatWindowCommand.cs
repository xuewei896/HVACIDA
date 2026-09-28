using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HVACIDA.UI.ViewModels;
using HVACIDA.UI.Views;

namespace HVACIDA.Revit.Commands
{
    /// <summary>
    /// Ribbon「AI问答 → AI对话」:打开独立的 AI 对话窗(非模态,可调用进程内命令取工程数据)。
    /// <para>
    /// 与停靠面板(<see cref="Services.AiPaneProvider"/>)同一套注入:UI 层拿不到 Revit,
    /// 通过两个委托把「上下文快照」与「工作区标识」喂给 <see cref="AiAssistantViewModel"/>。
    /// 命令集的注册仍由 App 在 ApplicationInitialized 时完成,与本入口无关。
    /// </para>
    /// <para>
    /// **为什么要静态字段**:非模态窗如果只被局部变量引用,命令返回后就可能被 GC 回收 ——
    /// 窗口会莫名消失(参考文档点名的坑,与停靠面板 Provider 同理)。
    /// </para>
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ShowAiChatWindowCommand : IExternalCommand
    {
        /// <summary>已打开的对话窗(非模态;静态持有防 GC)。</summary>
        private static AiChatWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = commandData == null ? null : commandData.Application;

                // UI 层拿不到 Revit:用委托把上下文与工作区喂进去(与停靠面板一致)
                AiAssistantViewModel.ContextSnapshotProvider = () => Services.RevitAiContext.BuildSnapshot(uiApp);
                AiAssistantViewModel.ScopeProvider = () => Services.RevitAiContext.BuildScope(uiApp);

                if (_window != null && _window.IsVisible)
                {
                    if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
                    _window.Activate();
                    return Result.Succeeded;
                }

                _window = new AiChatWindow();
                _window.Closed += (sender, args) => _window = null;
                _window.Show();
                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                message = "打开 AI 对话窗失败: " + ex.Message;
                return Result.Failed;
            }
        }
    }
}

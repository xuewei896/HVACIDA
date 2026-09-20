using System;
using Autodesk.Revit.UI;
using HVACIDA.Core.Services;
using HVACIDA.UI.ViewModels;
using HVACIDA.UI.Views;

namespace HVACIDA.Revit.Services
{
    /// <summary>
    /// **AI 助手停靠面板**(Revit 右侧;承参考文档 2.1)。
    /// <para>
    /// ⚠ 两个必守的点(参考文档点名的坑):
    /// ① Provider 必须是**静态字段**持有 —— 写成局部变量会被 GC 回收,之后点按钮就报
    ///    「pane has not been created yet」;
    /// ② <c>RegisterDockablePane</c> 在 <c>OnStartup</c> 里注册,但**命令集初始化**要等到
    ///    <c>ApplicationInitialized</c>(那时 Revit 才完全就绪)。
    /// </para>
    /// <para>
    /// UI 层不引用 Revit:这里把「上下文快照」与「工作区标识」两个委托注入 ViewModel。
    /// </para>
    /// </summary>
    internal sealed class AiPaneProvider : IDockablePaneProvider
    {
        /// <summary>面板 Id(固定 GUID:升级插件后仍指向同一个面板)。</summary>
        public static readonly DockablePaneId PaneId = new DockablePaneId(new Guid("7C1B4A2E-9F3D-4E77-9C21-5B0A6D2F1A31"));

        /// <summary>面板标题(Revit 界面显示)。</summary>
        public const string PaneTitle = "AI 助手";

        private AiChatPanel _panel;

        /// <summary>Revit 提供的上下文(注册时由 App 注入)。</summary>
        public UIApplication UiApplication { get; set; }

        /// <summary>面板控件(命令层用来刷新)。</summary>
        public AiChatPanel Panel => _panel;

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            // UI 层拿不到 Revit:通过委托把上下文与工作区喂给它
            AiAssistantViewModel.ContextSnapshotProvider =
                () => RevitAiContext.BuildSnapshot(UiApplication);
            AiAssistantViewModel.ScopeProvider =
                () => RevitAiContext.BuildScope(UiApplication);

            _panel = new AiChatPanel();
            data.FrameworkElement = _panel;
            data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
        }
    }
}

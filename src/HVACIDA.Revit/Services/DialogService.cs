using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;

namespace HVACIDA.Revit.Services
{
    /// <summary>
    /// WPF 对话框宿主辅助:以 Revit 主窗口为 Owner 模态显示,
    /// 避免无主窗口导致的切换问题(技能规范第 3.7 条)。
    /// </summary>
    public static class DialogService
    {
        public static void ShowModal(Window window)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));
            IntPtr owner = GetRevitMainWindowHandle();
            if (owner != IntPtr.Zero)
            {
                new WindowInteropHelper(window).Owner = owner;
            }
            window.ShowDialog();
        }

        private static IntPtr GetRevitMainWindowHandle()
        {
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    return process.MainWindowHandle;
                }
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
    }
}

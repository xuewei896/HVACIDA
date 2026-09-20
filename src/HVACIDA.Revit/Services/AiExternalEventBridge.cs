using System;
using System.Threading;
using Autodesk.Revit.UI;
using HVACIDA.Core.Services;

namespace HVACIDA.Revit.Services
{
    /// <summary>
    /// **Revit API 线程桥**:AI 助手在后台线程跑(不能阻塞 UI、也不能在非主线程碰 Revit API),
    /// 而命令必须在 Revit 主线程执行 —— 用 <see cref="ExternalEvent"/> 把工作丢回主线程再等结果。
    /// <para>
    /// 参考文档 2.4 的做法是「一命令一 Command + 一 EventHandler」;这里把两者合并成
    /// **一个通用 handler + 委托**(机制完全相同:Raise → Revit 调度到主线程 → ManualResetEvent 通知完成),
    /// 命令多起来时少写几十对类。**注意参考文档点名的坑**:每次派活前必须 <c>Reset()</c>,
    /// 否则上一次 Execute 留下的已触发信号会让 <c>WaitOne</c> 立刻返回、命令其实没执行。
    /// </para>
    /// </summary>
    internal sealed class AiExternalEventBridge : IExternalEventHandler
    {
        private readonly ManualResetEvent _done = new ManualResetEvent(false);
        private readonly object _sync = new object();
        private ExternalEvent _externalEvent;
        private Func<UIApplication, string> _work;
        private string _result = "";
        private Exception _error;

        /// <summary>创建 ExternalEvent(必须在 Revit 主线程上调用;幂等)。</summary>
        public void EnsureCreated()
        {
            if (_externalEvent == null) _externalEvent = ExternalEvent.Create(this);
        }

        /// <summary>是否已创建(界面显示"命令通道是否就绪")。</summary>
        public bool IsCreated => _externalEvent != null;

        /// <summary>把一段工作在 Revit 主线程执行并等结果(超时返回 JSON 错误,不抛)。</summary>
        public string Run(UIApplication uiApp, Func<UIApplication, string> work, int timeoutMs)
        {
            try
            {
                EnsureCreated();
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + JsonValue.Escape("创建 ExternalEvent 失败:" + ex.Message) + "\"}";
            }

            lock (_sync)
            {
                _done.Reset();                 // ⚠ 必须先复位(参考文档点名的坑)
                _work = work;
                _result = "";
                _error = null;
            }

            _externalEvent.Raise();
            if (!_done.WaitOne(timeoutMs))
            {
                return "{\"error\":\"" + JsonValue.Escape(
                    "Revit 主线程在 " + timeoutMs + " ms 内没有执行该命令(可能被模态对话框占用,或有其它事务未结束)") + "\"}";
            }

            lock (_sync)
            {
                if (_error != null)
                {
                    return "{\"error\":\"" + JsonValue.Escape("命令执行失败:" + _error.Message) + "\"}";
                }
                return string.IsNullOrEmpty(_result) ? "{\"ok\":true}" : _result;
            }
        }

        /// <summary>Revit 在主线程回调这里。</summary>
        public void Execute(UIApplication app)
        {
            Func<UIApplication, string> work;
            lock (_sync) { work = _work; }
            try
            {
                string result = work == null ? "{}" : work(app);
                lock (_sync) { _result = result; }
            }
            catch (Exception ex)
            {
                lock (_sync) { _error = ex; }
            }
            finally
            {
                _done.Set();                   // 通知等待方:完成了
            }
        }

        /// <summary>ExternalEvent 名称(Revit 日志里可见)。</summary>
        public string GetName()
        {
            return "HVACIDA AI 助手命令通道";
        }
    }
}

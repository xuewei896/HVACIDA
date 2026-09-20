using System.Net;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 出网前的 HTTPS 协议准备。
    /// <para>
    /// Revit 2020 跑在 .NET Framework 4.8 上,但**进程级默认协议可能只开到 TLS 1.0/1.1**
    /// (取决于系统与 exe.config);DeepSeek / ima 的接口都要求 TLS 1.2,不显式打开就会
    /// 报"基础连接已关闭/无法建立 SSL 连接"。这里只做一件事:把 TLS 1.2 加进允许列表。
    /// </para>
    /// </summary>
    public static class HttpTls
    {
        private static bool _ready;

        /// <summary>确保允许 TLS 1.2(幂等;老系统上没有该枚举值也不影响,连不上会照实报错)。</summary>
        public static void EnsureTls12()
        {
            if (_ready) return;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
                // 忽略:失败时后续请求会给出真实的网络错误
            }
            _ready = true;
        }
    }
}

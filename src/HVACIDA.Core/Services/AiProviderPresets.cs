using System.Collections.Generic;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// **大模型服务预设**(照参考文档「内置若干模型预设 + 支持自定义任何 OpenAI 兼容接口」)。
    /// <para>
    /// ⚠ 预设只是**方便**,不是权威:各家的地址与模型名会变,**以各家官方文档为准**。
    /// 填错时接口会返回 400/422,插件照实显示(不猜、不自动改模型名)。
    /// </para>
    /// </summary>
    public static class AiProviderPresets
    {
        /// <summary>自定义预设的键(选了它就用设置里的地址与模型名)。</summary>
        public const string CustomKey = "custom";

        /// <summary>全部预设(接口地址均为 OpenAI 兼容的 base,不含 /chat/completions)。</summary>
        public static IList<AiProviderPreset> All { get; } = new List<AiProviderPreset>
        {
            new AiProviderPreset
            {
                Key = "deepseek",
                DisplayName = "DeepSeek",
                Endpoint = DeepSeekClient.DefaultEndpoint,
                Model = DeepSeekClient.DefaultModel,
                KeyPage = "https://platform.deepseek.com/api_keys"
            },
            new AiProviderPreset
            {
                Key = "deepseek-pro",
                DisplayName = "DeepSeek(推理/Pro)",
                Endpoint = DeepSeekClient.DefaultEndpoint,
                Model = "deepseek-v4-pro",
                KeyPage = "https://platform.deepseek.com/api_keys"
            },
            new AiProviderPreset
            {
                Key = "qwen",
                DisplayName = "通义千问(阿里云百炼)",
                Endpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1",
                Model = "qwen-plus",
                KeyPage = "https://bailian.console.aliyun.com/"
            },
            new AiProviderPreset
            {
                Key = "glm",
                DisplayName = "智谱 GLM",
                Endpoint = "https://open.bigmodel.cn/api/paas/v4",
                Model = "glm-4-flash",
                KeyPage = "https://open.bigmodel.cn/usercenter/apikeys"
            },
            new AiProviderPreset
            {
                Key = "kimi",
                DisplayName = "Kimi(月之暗面)",
                Endpoint = "https://api.moonshot.cn/v1",
                Model = "moonshot-v1-128k",
                KeyPage = "https://platform.moonshot.cn/console/api-keys"
            },
            new AiProviderPreset
            {
                Key = "openai",
                DisplayName = "OpenAI",
                Endpoint = "https://api.openai.com/v1",
                Model = "gpt-4o-mini",
                KeyPage = "https://platform.openai.com/api-keys"
            },
            new AiProviderPreset
            {
                Key = CustomKey,
                DisplayName = "自定义(OpenAI 兼容)",
                Endpoint = "",
                Model = "",
                KeyPage = ""
            }
        };

        /// <summary>按键取预设(找不到返回 null)。</summary>
        public static AiProviderPreset Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var preset in All)
            {
                if (string.Equals(preset.Key, key, System.StringComparison.OrdinalIgnoreCase)) return preset;
            }
            return null;
        }

        /// <summary>是否内置预设(非自定义)。</summary>
        public static bool IsBuiltIn(string key)
        {
            var preset = Find(key);
            return preset != null && !string.Equals(preset.Key, CustomKey, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}

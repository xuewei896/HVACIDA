using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// **AI 问答(DeepSeek)设置的落盘**(<c>%AppData%\HVACIDA\ai.xml</c>)。
    /// <para>
    /// 与工程数据分开存:API key 属**本机个人**信息,单独一个文件便于清理,也避免把 key
    /// 混进工程资料里发给别人。⚠ **API key 在本机是明文保存的**:文件与界面都写明。
    /// </para>
    /// <para>
    /// 读写**都不抛异常**:文件损坏 / 无权限时返回默认设置并把原因写进 <c>note</c>。
    /// </para>
    /// </summary>
    public static class AiSettingsStore
    {
        /// <summary>默认设置文件(<c>%AppData%\HVACIDA\ai.xml</c>)。</summary>
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HVACIDA", "ai.xml");
            }
        }

        /// <summary>文件里写给用户看的提醒。</summary>
        public const string PlainTextWarning = "DeepSeek API key 在本机为明文保存,请勿把本文件外发或提交到代码库。";

        /// <summary>读取设置(文件不存在时返回默认:未启用)。</summary>
        public static AiChatSettings Load()
        {
            string note;
            return Load(DefaultPath, out note);
        }

        /// <summary>读取设置(不抛异常)。</summary>
        public static AiChatSettings Load(string path, out string note)
        {
            return Load(path, "", out note);
        }

        /// <summary>
        /// 读取设置(不抛异常)。<paramref name="scopeId"/> 非空时,API key 从
        /// <see cref="ApiKeyVault"/>(DPAPI 加密)按工作区取;取不到才回退到 ai.xml 里的明文元素(旧版兼容)。
        /// </summary>
        public static AiChatSettings Load(string path, string scopeId, out string note)
        {
            var settings = new AiChatSettings();
            note = "";
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    note = "还没有 AI 设置文件(" + (path ?? "") + "),当前为默认:未启用 AI 助手。";
                    return settings;
                }

                var document = new XmlDocument();
                document.Load(path);
                var root = document.DocumentElement;
                if (root == null)
                {
                    note = "AI 设置文件是空的,当前为默认:未启用 AI 助手。";
                    return settings;
                }

                settings.Enabled = ReadBool(root, "enabled", false);
                settings.ApiKey = ReadText(root, "apiKey");
                settings.Endpoint = ReadText(root, "endpoint");
                settings.Model = ReadText(root, "model");
                settings.Provider = ReadText(root, "provider");
                settings.Stream = ReadBool(root, "stream", true);
                settings.Temperature = ReadDouble(root, "temperature", 0.2);
                settings.MaxTokens = ReadInt(root, "maxTokens", 1024);
                settings.TimeoutSeconds = ReadInt(root, "timeoutSeconds", 60);
                settings.ContextEntryLimit = ReadInt(root, "contextEntryLimit", 5);
                settings.ContextMaxChars = ReadInt(root, "contextMaxChars", 6000);

                string keySource = "ai.xml";
                if (!string.IsNullOrEmpty(scopeId))
                {
                    string vaultKey = ApiKeyVault.Load(scopeId, VaultPathOf(path));
                    if (!string.IsNullOrEmpty(vaultKey))
                    {
                        settings.ApiKey = vaultKey;
                        keySource = "DPAPI 密钥保险箱(按工作区)";
                    }
                }

                if (settings.MaxTokens <= 0) settings.MaxTokens = 1024;
                if (settings.TimeoutSeconds <= 0) settings.TimeoutSeconds = 60;
                if (settings.ContextEntryLimit <= 0) settings.ContextEntryLimit = 5;
                if (settings.ContextMaxChars <= 0) settings.ContextMaxChars = 6000;
                if (settings.Temperature < 0) settings.Temperature = 0;
                if (settings.Temperature > 2) settings.Temperature = 2;

                note = "已读取 AI 设置(" + path + ";key 来源:" + keySource + "):" +
                       (settings.Enabled ? "已启用" : "未启用") + "," + settings.MissingCredentialText();
            }
            catch (Exception ex)
            {
                note = "读取 AI 设置失败(" + path + "):" + ex.Message + "。当前为默认:未启用 AI 助手。";
                return new AiChatSettings();
            }
            return settings;
        }

        /// <summary>保存设置;返回实际写入的文件路径。</summary>
        public static string Save(AiChatSettings settings)
        {
            return Save(settings, DefaultPath);
        }

        /// <summary>保存设置(API key 明文写在 ai.xml 里;仅用于旧路径与自检)。</summary>
        public static string Save(AiChatSettings settings, string path)
        {
            return Save(settings, path, "");
        }

        /// <summary>
        /// 保存设置。<paramref name="scopeId"/> 非空时,**API key 走 DPAPI 加密保险箱**
        /// (<c>ai-key.bin</c>),ai.xml 里不再写 key(照参考文档 2.6 的做法)。
        /// </summary>
        public static string Save(AiChatSettings settings, string path, string scopeId)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrEmpty(path)) path = DefaultPath;

            bool useVault = !string.IsNullOrEmpty(scopeId);
            if (useVault)
            {
                ApiKeyVault.Save(scopeId, settings.ApiKey ?? "", VaultPathOf(path));
            }

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var writerSettings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                Encoding = new UTF8Encoding(false),
                CloseOutput = true
            };

            using (var writer = XmlWriter.Create(path, writerSettings))
            {
                writer.WriteStartDocument();
                writer.WriteComment(" HVACIDA 的 AI 助手设置。");
                WriteComment(writer, useVault);
                writer.WriteComment(" 接口与模型名默认取各家官方文档(OpenAI 兼容 /chat/completions)。");
                writer.WriteComment(" 调用只发送「你的问题 + 插件检索到的依据文本」;开启「操作 Revit」后,命令返回的工程数据也会发给模型服务方。");
                writer.WriteStartElement("aiSettings");

                WriteText(writer, "enabled", settings.Enabled ? "true" : "false");
                WriteText(writer, "apiKey", useVault ? "" : (settings.ApiKey ?? ""));
                WriteText(writer, "provider", settings.Provider);
                WriteText(writer, "endpoint", settings.Endpoint);
                WriteText(writer, "model", settings.Model);
                WriteText(writer, "stream", settings.Stream ? "true" : "false");
                WriteText(writer, "temperature", settings.Temperature.ToString("0.###", CultureInfo.InvariantCulture));
                WriteText(writer, "maxTokens", settings.MaxTokens.ToString(CultureInfo.InvariantCulture));
                WriteText(writer, "timeoutSeconds", settings.TimeoutSeconds.ToString(CultureInfo.InvariantCulture));
                WriteText(writer, "contextEntryLimit", settings.ContextEntryLimit.ToString(CultureInfo.InvariantCulture));
                WriteText(writer, "contextMaxChars", settings.ContextMaxChars.ToString(CultureInfo.InvariantCulture));

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
            return path;
        }

        /// <summary>保险箱文件与设置文件同目录(<c>ai-key.bin</c>)。</summary>
        public static string VaultPathOf(string settingsPath)
        {
            string directory = string.IsNullOrEmpty(settingsPath) ? null : Path.GetDirectoryName(settingsPath);
            if (string.IsNullOrEmpty(directory)) return ApiKeyVault.DefaultPath;
            return Path.Combine(directory, "ai-key.bin");
        }

        private static void WriteComment(XmlWriter writer, bool useVault)
        {
            writer.WriteComment(useVault
                ? " API key 由 Windows DPAPI 加密保存在同目录 ai-key.bin(按工作区隔离),本文件里不写 key。" +
                  " " + PlainTextWarning
                : " ⚠ 本次未指定工作区:API key 以明文写在下面(旧路径/自检用)。" + PlainTextWarning);
        }

        /// <summary>给界面用的一句话状态。</summary>
        public static string Summary(AiChatSettings settings)
        {
            if (settings == null) return "没有 AI 设置。";
            if (!settings.Enabled) return "AI 问答:未启用(只用本地知识库)。";
            if (!settings.IsConfigured)
                return "AI 问答:已勾选启用,但" + settings.MissingCredentialText();
            return "AI 问答:已启用(模型 " + DeepSeekClient.ResolveModel(settings) +
                   ",接口 " + DeepSeekClient.ResolveUrl(settings) + ")。" + PlainTextWarning;
        }

        private static void WriteText(XmlWriter writer, string name, string value)
        {
            writer.WriteStartElement(name);
            writer.WriteValue(value ?? "");
            writer.WriteEndElement();
        }

        private static string ReadText(XmlElement root, string name)
        {
            var node = root.SelectSingleNode(name);
            return node == null ? "" : (node.InnerText ?? "").Trim();
        }

        private static bool ReadBool(XmlElement root, string name, bool fallback)
        {
            bool value;
            return bool.TryParse(ReadText(root, name), out value) ? value : fallback;
        }

        private static int ReadInt(XmlElement root, string name, int fallback)
        {
            int value;
            return int.TryParse(ReadText(root, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value : fallback;
        }

        private static double ReadDouble(XmlElement root, string name, double fallback)
        {
            double value;
            return double.TryParse(ReadText(root, name), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value : fallback;
        }
    }
}

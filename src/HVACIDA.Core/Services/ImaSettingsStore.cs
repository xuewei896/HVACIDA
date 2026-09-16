using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// **ima 在线知识库设置的落盘**(<c>%AppData%\HVACIDA\ima.xml</c>)。
    /// <para>
    /// 与工程数据分开存:工程数据在 project.xml 等文件里(可随项目走),凭证属于**本机个人**信息,
    /// 单独一个文件便于用户自己清理,也避免把 API Key 混进工程资料里发给别人。
    /// </para>
    /// <para>
    /// ⚠ **API Key 在本机是明文保存的**(做成加密需要额外密钥管理,超出本插件范围):
    /// 文件里会写明这一句,界面也照实显示。请勿把 ima.xml 外发或提交到代码库。
    /// </para>
    /// <para>
    /// 读写**都不抛异常**:文件损坏 / 无权限时返回默认设置并把原因写进 <c>note</c>,
    /// 界面照实显示"用的是默认设置、原因是……",不让知识库窗因为一个配置文件崩掉。
    /// </para>
    /// </summary>
    public static class ImaSettingsStore
    {
        /// <summary>默认设置文件(<c>%AppData%\HVACIDA\ima.xml</c>)。</summary>
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HVACIDA", "ima.xml");
            }
        }

        /// <summary>文件里写给用户看的一句提醒。</summary>
        public const string PlainTextWarning = "API Key 在本机为明文保存,请勿把本文件外发或提交到代码库。";

        /// <summary>读取设置(文件不存在时返回默认设置:未启用)。</summary>
        public static ImaKnowledgeSettings Load()
        {
            string note;
            return Load(DefaultPath, out note);
        }

        /// <summary>读取设置(不抛异常;<paramref name="note"/> 说明读取结果)。</summary>
        public static ImaKnowledgeSettings Load(string path, out string note)
        {
            var settings = new ImaKnowledgeSettings();
            note = "";
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    note = "还没有 ima 设置文件(" + (path ?? "") + "),当前为默认:未启用在线知识库。";
                    return settings;
                }

                var document = new XmlDocument();
                document.Load(path);
                var root = document.DocumentElement;
                if (root == null)
                {
                    note = "ima 设置文件是空的,当前为默认:未启用在线知识库。";
                    return settings;
                }

                settings.Enabled = ReadBool(root, "enabled", false);
                settings.ClientId = ReadText(root, "clientId");
                settings.ApiKey = ReadText(root, "apiKey");
                settings.KnowledgeBaseId = ReadText(root, "knowledgeBaseId");
                settings.ShareId = ReadText(root, "shareLink");
                settings.Endpoint = ReadText(root, "endpoint");
                settings.Limit = ReadInt(root, "limit", 10);
                if (settings.Limit <= 0) settings.Limit = 10;

                note = "已读取 ima 设置(" + path + "):" +
                       (settings.Enabled ? "已启用" : "未启用") + "," + settings.MissingCredentialText();
            }
            catch (Exception ex)
            {
                note = "读取 ima 设置失败(" + path + "):" + ex.Message + "。当前为默认:未启用在线知识库。";
                return new ImaKnowledgeSettings();
            }
            return settings;
        }

        /// <summary>保存设置;返回实际写入的文件路径(失败抛异常,由调用方显示原因)。</summary>
        public static string Save(ImaKnowledgeSettings settings)
        {
            return Save(settings, DefaultPath);
        }

        /// <summary>保存设置到指定文件;返回实际写入的文件路径。</summary>
        public static string Save(ImaKnowledgeSettings settings, string path)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrEmpty(path)) path = DefaultPath;

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
                writer.WriteComment(" HVACIDA 的 ima 在线知识库设置。");
                writer.WriteComment(" " + PlainTextWarning);
                writer.WriteComment(" shareId/分享链接只用于在浏览器里打开分享页,**不是接口凭证**;");
                writer.WriteComment(" 接口凭证是 clientId + apiKey + knowledgeBaseId 三样。");
                writer.WriteStartElement("imaSettings");

                writer.WriteStartElement("enabled");
                writer.WriteValue(settings.Enabled ? "true" : "false");
                writer.WriteEndElement();

                WriteText(writer, "clientId", settings.ClientId);
                WriteText(writer, "apiKey", settings.ApiKey);
                WriteText(writer, "knowledgeBaseId", settings.KnowledgeBaseId);
                WriteText(writer, "shareLink", settings.ShareId);
                WriteText(writer, "endpoint", settings.Endpoint);
                WriteText(writer, "limit", settings.Limit.ToString(CultureInfo.InvariantCulture));

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
            return path;
        }

        /// <summary>给界面用的一句话状态。</summary>
        public static string Summary(ImaKnowledgeSettings settings)
        {
            if (settings == null) return "没有 ima 设置。";
            if (!settings.Enabled) return "ima 在线知识库:未启用(只用本地知识库)。";
            if (!settings.IsConfigured)
                return "ima 在线知识库:已勾选启用,但凭证不全 —— " + settings.MissingCredentialText();
            return "ima 在线知识库:已启用(知识库 ID " + settings.KnowledgeBaseId +
                   ",单次最多显示 " + settings.Limit + " 条)。" + PlainTextWarning;
        }

        /// <summary>把分享链接规范化成可打开的地址(不是 http/https 就返回空 —— 插件不猜地址)。</summary>
        public static string ResolveShareUrl(string shareText)
        {
            string text = (shareText ?? "").Trim();
            if (text.Length == 0) return "";
            if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return text;
            return "";
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
            string text = ReadText(root, name);
            bool value;
            if (bool.TryParse(text, out value)) return value;
            return fallback;
        }

        private static int ReadInt(XmlElement root, string name, int fallback)
        {
            string text = ReadText(root, name);
            int value;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return value;
            return fallback;
        }
    }
}

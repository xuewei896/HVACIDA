using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// **API Key 保险箱**:用 Windows DPAPI(<see cref="ProtectedData"/>)按用户加密保存,
    /// 文件里**不出现明文**,且不同工作区(Revit 版本 + 用户 + 项目)的密钥互相解不开
    /// (照参考文档 2.6 的做法)。
    /// <para>
    /// 存储格式:一行一个工作区,<c>scopeId=base64(密文)</c>;空行与 <c>#</c> 开头为注释。
    /// </para>
    /// <para>
    /// 若 DPAPI 不可用(极少数环境),<see cref="Save"/> 会退化为明文 base64 存储,
    /// 并通过 <see cref="LastNote"/> 与 <see cref="LastSaveWasEncrypted"/> **明确告知用户**
    /// —— 不静默降级。
    /// </para>
    /// </summary>
    public static class ApiKeyVault
    {
        /// <summary>默认保险箱文件(<c>%AppData%\HVACIDA\ai-key.bin</c>)。</summary>
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HVACIDA", "ai-key.bin");
            }
        }

        /// <summary>最近一次操作的说明(界面显示:加密了还是退化了)。</summary>
        public static string LastNote { get; private set; } = "";

        /// <summary>最近一次保存是否真的用了 DPAPI 加密。</summary>
        public static bool LastSaveWasEncrypted { get; private set; }

        /// <summary>保存某个工作区的 API Key(<paramref name="apiKey"/> 为空表示删除)。</summary>
        public static void Save(string scopeId, string apiKey, string path)
        {
            if (string.IsNullOrEmpty(path)) path = DefaultPath;
            var keys = ReadAll(path);

            if (string.IsNullOrEmpty(apiKey))
            {
                keys.Remove(scopeId ?? "");
                LastNote = "已清除该工作区的 API Key";
            }
            else
            {
                byte[] entropy = EntropyOf(scopeId);
                byte[] blob = TryProtect(apiKey, entropy);
                if (blob != null)
                {
                    LastSaveWasEncrypted = true;
                    keys[scopeId ?? ""] = Convert.ToBase64String(blob);
                    LastNote = "API Key 已用 Windows DPAPI 加密保存(仅本机本用户可解)";
                }
                else
                {
                    LastSaveWasEncrypted = false;
                    keys[scopeId ?? ""] = "plain:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(apiKey));
                    LastNote = "⚠ 本机 DPAPI 不可用,API Key 只能以**明文等价**形式保存在 ai-key.bin —— 请勿把该文件外发";
                }
            }

            WriteAll(path, keys);
        }

        /// <summary>读取某个工作区的 API Key(没有则返回空串)。</summary>
        public static string Load(string scopeId, string path)
        {
            if (string.IsNullOrEmpty(path)) path = DefaultPath;
            string stored;
            return ReadAll(path).TryGetValue(scopeId ?? "", out stored) ? Decode(stored, scopeId) : "";
        }

        /// <summary>该工作区是否已存有 API Key。</summary>
        public static bool Has(string scopeId, string path)
        {
            return !string.IsNullOrEmpty(Load(scopeId, path));
        }

        /// <summary>清理整个保险箱(界面"清除本机密钥"用)。</summary>
        public static void Clear(string path)
        {
            if (string.IsNullOrEmpty(path)) path = DefaultPath;
            try { if (File.Exists(path)) File.Delete(path); LastNote = "已清空本机 AI 密钥文件"; }
            catch (Exception ex) { LastNote = "清空密钥文件失败:" + ex.Message; }
        }

        /// <summary>解密一条存储值(供自检直接核对)。</summary>
        public static string Decode(string stored, string scopeId)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            try
            {
                if (stored.StartsWith("plain:", StringComparison.Ordinal))
                {
                    return Encoding.UTF8.GetString(Convert.FromBase64String(stored.Substring(6)));
                }
                byte[] blob = Convert.FromBase64String(stored);
                byte[] plain = ProtectedData.Unprotect(blob, EntropyOf(scopeId), DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                // 换用户/换机器/换工作区都解不开 —— 这是"隔离"生效的表现,按"没有密钥"处理
                return "";
            }
        }

        private static byte[] TryProtect(string plain, byte[] entropy)
        {
            try
            {
                return ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), entropy, DataProtectionScope.CurrentUser);
            }
            catch
            {
                return null;
            }
        }

        private static byte[] EntropyOf(string scopeId)
        {
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes("HVACIDA-AI-KEY-" + (scopeId ?? "")));
            }
        }

        private static System.Collections.Generic.Dictionary<string, string> ReadAll(string path)
        {
            var keys = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(path)) return keys;
                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string text = (line ?? "").Trim();
                    if (text.Length == 0 || text.StartsWith("#", StringComparison.Ordinal)) continue;
                    int index = text.IndexOf('=');
                    if (index <= 0) continue;
                    keys[text.Substring(0, index).Trim()] = text.Substring(index + 1).Trim();
                }
            }
            catch
            {
                // 读不了就当没有密钥:调用方会提示"未配置",不会崩
            }
            return keys;
        }

        private static void WriteAll(string path, System.Collections.Generic.Dictionary<string, string> keys)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            var sb = new StringBuilder();
            sb.AppendLine("# HVACIDA 的 AI 密钥保险箱 —— 由插件用 Windows DPAPI 加密写入,请勿外发或提交到代码库。");
            sb.AppendLine("# 格式:工作区标识=base64(密文)。工作区 = Revit 版本 + Windows 用户 + 项目路径哈希。");
            foreach (var pair in keys)
            {
                sb.Append(pair.Key).Append('=').AppendLine(pair.Value);
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>导入结果(读到了哪些文件、解析出多少条、哪些文件没解析)。</summary>
    public class ClauseImportResult
    {
        /// <summary>扫描的目录。</summary>
        public string Directory { get; set; } = "";

        /// <summary>解析成功的文件。</summary>
        public List<string> Files { get; set; } = new List<string>();

        /// <summary>解析出的条文条目。</summary>
        public List<StandardClauseEntry> Entries { get; set; } = new List<StandardClauseEntry>();

        /// <summary>跳过的文件(格式不支持 / 解析不出条文号)。</summary>
        public List<string> Skipped { get; set; } = new List<string>();

        /// <summary>提示(给用户看:该怎么命名、支持哪些格式)。</summary>
        public string Note { get; set; } = "";

        /// <summary>条文条数。</summary>
        public int Count => Entries.Count;
    }

    /// <summary>
    /// **标准条文电子版导入**(需求 2.7):把用户手上的标准条文文件读成可检索的条文条目。
    /// <para>
    /// 支持格式:<c>.txt</c> / <c>.md</c> / <c>.csv</c>(纯文本)<b>以及</b> <c>.docx</c>
    /// (Word 文档 = ZIP + XML,用 .NET 自带的 ZipArchive 读,不引任何第三方库)。
    /// **不支持 .pdf / .xls**:请另存为 .docx 或 .txt 后放入(fake OCR 与 PDF 解析不做,避免读错条文)。
    /// </para>
    /// <para>
    /// 文件命名建议:<c>GB 50736-2012 民用建筑供暖通风与空气调节设计规范.docx</c>
    /// —— 插件从**文件名**里取标准编号与名称;也可以在文件首行写 <c>#标准:编号 名称</c> 覆盖。
    /// 条文识别:行首的 <c>4.1.2</c> 或 <c>第 4.1.2 条</c> 视为**条文号**,其后的文字直到下一条文号为止作为**条文原文**。
    /// </para>
    /// </summary>
    public static class ClauseDocumentReader
    {
        /// <summary>默认条文目录(插件运行时读取这里;<c>%AppData%\HVACIDA\规范条文</c>)。</summary>
        public static string DefaultDirectory
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HVACIDA", "规范条文");
            }
        }

        private static readonly Regex ClauseHead = new Regex(
            @"^\s*(?:第\s*)?([0-9]+(?:\.[0-9]+){1,3})\s*(?:条|节)?\s*[:：、.．]?\s*(.*)$",
            RegexOptions.Compiled);

        private static readonly Regex StandardHeader = new Regex(
            @"^\s*#?\s*(?:标准|规范)\s*[:：]\s*(.+)$", RegexOptions.Compiled);

        /// <summary>目录里的说明文件内容(首次创建目录时写入,用户照着放文件即可)。</summary>
        public static string InstructionText(string directory)
        {
            return "# 标准条文电子版放这里\\n\\n" +
                   "插件读取的条文目录:" + directory + "\\n\\n" +
                   "支持格式:.txt / .md / .csv / .docx(pdf、xls 请先另存为 docx 或 txt)。\\n\\n" +
                   "文件命名建议:「GB 50736-2012 民用建筑供暖通风与空气调节设计规范.docx」—— 插件从文件名取标准编号与名称,\\n" +
                   "也可以在文件首行写「#标准:GB 50736-2012 民用建筑供暖通风与空气调节设计规范」覆盖。\\n\\n" +
                   "条文写法:行首以「4.1.2」或「第 4.1.2 条」开头即为一条条文的开始,\\n" +
                   "从该行往下直到下一个条文号为止的文字,作为这条条文的原文。\\n\\n" +
                   "放好文件后,在插件里打开「AI问答 → 规范知识库」→ 点【重新导入条文】:\\n" +
                   "界面会显示「已从 N 个文件解析出 M 条条文」,并列出没解析成功的文件与原因。\\n";
        }

        /// <summary>读取目录下全部受支持的条文文件。</summary>
        public static ClauseImportResult Load(string directory)
        {
            var result = new ClauseImportResult { Directory = directory ?? "" };
            result.Note = "支持的格式:.txt / .md / .csv / .docx;文件命名建议「GB 50736-2012 民用建筑供暖通风与空气调节设计规范」" +
                          "(插件从文件名取标准编号与名称);条文以行首「4.1.2」或「第 4.1.2 条」开头;" +
                          "不支持 .pdf / .xls,请另存为 .docx 或 .txt。";

            if (string.IsNullOrEmpty(directory))
            {
                result.Note = "未指定条文目录。" + result.Note;
                return result;
            }
            try
            {
                // 目录不存在就建一个,并放一份说明文件 —— 用户只要打开一次知识库窗就知道该放哪、怎么放
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(Path.Combine(directory, "说明.md"), InstructionText(directory),
                        new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                result.Note = "条文目录不可用(" + directory + "):" + ex.Message + "。" + result.Note;
                return result;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (Exception ex)
            {
                result.Note = "读取条文目录失败:" + ex.Message;
                return result;
            }

            foreach (var file in files)
            {
                string ext = (Path.GetExtension(file) ?? "").ToLowerInvariant();
                if (ext == ".pdf" || ext == ".xls" || ext == ".xlsx")
                {
                    result.Skipped.Add(Path.GetFileName(file) + "(格式暂不支持,请另存为 .docx 或 .txt)");
                    continue;
                }
                if (ext != ".txt" && ext != ".md" && ext != ".csv" && ext != ".docx")
                {
                    continue;   // 无关文件直接忽略,不报噪声
                }

                try
                {
                    string text = ext == ".docx" ? ReadDocx(file) : ReadText(file);
                    var parsed = Parse(text, Path.GetFileNameWithoutExtension(file));
                    if (parsed.Count == 0)
                    {
                        result.Skipped.Add(Path.GetFileName(file) + "(没解析出条文号,请检查条文是否以「4.1.2」或「第 4.1.2 条」开头)");
                        continue;
                    }
                    result.Files.Add(Path.GetFileName(file));
                    result.Entries.AddRange(parsed);
                }
                catch (Exception ex)
                {
                    result.Skipped.Add(Path.GetFileName(file) + "(" + ex.Message + ")");
                }
            }

            result.Note = "已从 " + result.Files.Count + " 个文件解析出 " + result.Count + " 条条文。" +
                          (result.Skipped.Count > 0 ? "跳过 " + result.Skipped.Count + " 个文件。" : "") +
                          "支持的格式:.txt / .md / .csv / .docx;文件命名建议「GB 50736-2012 民用建筑供暖通风与空气调节设计规范」。";
            return result;
        }

        /// <summary>把一段文本解析成条文条目(标准编号与名称从 <paramref name="fileTitle"/> 里取)。</summary>
        public static List<StandardClauseEntry> Parse(string text, string fileTitle)
        {
            var entries = new List<StandardClauseEntry>();
            if (string.IsNullOrEmpty(text)) return entries;

            string standardCode = "";
            string standardName = fileTitle ?? "";
            var match = Regex.Match(standardName, @"^\s*((?:GB|JGJ|CJJ|DB|GB/T|JGJ/T)\s*[0-9]+(?:\.[0-9]+)*\s*[-—]?\s*[0-9]{0,4})\s*(.*)$",
                RegexOptions.IgnoreCase);
            if (match.Success)
            {
                standardCode = match.Groups[1].Value.Trim();
                standardName = match.Groups[2].Value.Trim();
            }
            if (string.IsNullOrEmpty(standardName)) standardName = fileTitle ?? "";

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            string currentNo = null;
            var body = new StringBuilder();
            int index = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                var header = StandardHeader.Match(line);
                if (header.Success && i < 3)
                {
                    string declared = header.Groups[1].Value.Trim();
                    var declaredMatch = Regex.Match(declared, @"^\s*((?:GB|JGJ|CJJ|DB|GB/T|JGJ/T)\s*[0-9]+(?:\.[0-9]+)*\s*[-—]?\s*[0-9]{0,4})\s*(.*)$",
                        RegexOptions.IgnoreCase);
                    if (declaredMatch.Success)
                    {
                        standardCode = declaredMatch.Groups[1].Value.Trim();
                        standardName = declaredMatch.Groups[2].Value.Trim();
                    }
                    else
                    {
                        standardName = declared;
                    }
                    continue;
                }

                var head = ClauseHead.Match(line);
                if (head.Success)
                {
                    if (currentNo != null) entries.Add(Build(currentNo, body, standardCode, standardName, ref index));
                    currentNo = head.Groups[1].Value;
                    body.Length = 0;
                    body.Append(head.Groups[2].Value);
                }
                else if (currentNo != null)
                {
                    body.AppendLine();
                    body.Append(line);
                }
            }
            if (currentNo != null) entries.Add(Build(currentNo, body, standardCode, standardName, ref index));
            return entries;
        }

        private static StandardClauseEntry Build(string clauseNo, StringBuilder body, string code, string name, ref int index)
        {
            index++;
            string text = (body.ToString() ?? "").Trim();
            var entry = new StandardClauseEntry
            {
                Id = "import-" + (code ?? "std").Replace(" ", "") + "-" + clauseNo,
                StandardCode = string.IsNullOrEmpty(code) ? "(未识别标准编号)" : code,
                StandardName = string.IsNullOrEmpty(name) ? "(未识别标准名称)" : name,
                Discipline = GuessDiscipline(name),
                ClauseNo = clauseNo,
                ClauseText = text,
                Title = "第 " + clauseNo + " 条 " + FirstSentence(text),
                ClauseHint = "条文 " + clauseNo + "(原文已导入)",
                Summary = text,
                Question = "第 " + clauseNo + " 条是什么?",
                BoundaryNote = "本条为**用户导入的标准原文**,以你导入的文件为准;" +
                               "如需引用请对照标准正式版本核对。",
                Keywords = BuildKeywords(text)
            };
            return entry;
        }

        /// <summary>按标准名称猜专业(仅用于检索分类,不影响内容)。</summary>
        private static CodeDiscipline GuessDiscipline(string name)
        {
            string text = name ?? "";
            if (text.Contains("给水") || text.Contains("排水")) return CodeDiscipline.Plumbing;
            if (text.Contains("防火") || text.Contains("防烟") || text.Contains("排烟")) return CodeDiscipline.Fire;
            if (text.Contains("地铁") || text.Contains("轨道交通")) return CodeDiscipline.Metro;
            if (text.Contains("制图") || text.Contains("验收") || text.Contains("施工")) return CodeDiscipline.Drawing;
            return CodeDiscipline.Hvac;
        }

        private static List<string> BuildKeywords(string text)
        {
            var keywords = new List<string>();
            if (string.IsNullOrEmpty(text)) return keywords;
            string plain = Regex.Replace(text, @"\s+", " ");
            if (plain.Length > 40) plain = plain.Substring(0, 40);
            keywords.Add(plain);
            return keywords;
        }

        private static string FirstSentence(string text)
        {
            if (string.IsNullOrEmpty(text)) return "(无正文)";
            string plain = Regex.Replace(text, @"\s+", " ").Trim();
            return plain.Length <= 30 ? plain : plain.Substring(0, 30) + "…";
        }

        private static string ReadText(string file)
        {
            // 优先按 UTF-8 读;失败(GBK 老文件)再按默认编码读一次
            try
            {
                return File.ReadAllText(file, new UTF8Encoding(false, true));
            }
            catch
            {
                return File.ReadAllText(file, Encoding.Default);
            }
        }

        /// <summary>读 .docx 的正文文字(Word 文档 = ZIP + XML;段落按 w:p 切,文本在 w:t)。</summary>
        private static string ReadDocx(string file)
        {
            using (var zip = ZipFile.OpenRead(file))
            {
                ZipArchiveEntry entry = null;
                foreach (var candidate in zip.Entries)
                {
                    if (string.Equals(candidate.FullName, "word/document.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        entry = candidate;
                        break;
                    }
                }
                if (entry == null) throw new InvalidOperationException("docx 里找不到 word/document.xml");

                using (var stream = entry.Open())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string xml = reader.ReadToEnd();
                    var sb = new StringBuilder();
                    // Word 正文按段落 w:p 切,段落内把所有 w:t 文本拼起来(保留 tab/br)
                    foreach (var paragraph in Regex.Split(xml, "</w:p>", RegexOptions.Singleline))
                    {
                        var line = new StringBuilder();
                        foreach (Match run in Regex.Matches(paragraph, "<w:t[^>]*>(.*?)</w:t>", RegexOptions.Singleline))
                        {
                            line.Append(Unescape(run.Groups[1].Value));
                        }
                        if (paragraph.IndexOf("<w:tab/>", StringComparison.Ordinal) >= 0) line.Append('\t');
                        sb.AppendLine(line.ToString());
                    }
                    string text = sb.ToString();                    return text;
                }
            }
        }

        private static string Unescape(string text)
        {
            return text.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"")
                       .Replace("&apos;", "'").Replace("&amp;", "&");
        }
    }
}

using System;
using System.IO;
using System.Text;

namespace HVACIDA.Core.Services
{
    /// <summary>文本计算书生成器(骨架期实现)。</summary>
    public class TextReportGenerator : ICalculationReportGenerator
    {
        private readonly string _reportDirectory;

        public TextReportGenerator()
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HVACIDA", "Reports"))
        {
        }

        public TextReportGenerator(string reportDirectory)
        {
            _reportDirectory = reportDirectory;
        }

        public string SaveTextReport(string title, string content)
        {
            Directory.CreateDirectory(_reportDirectory);
            string safe = Sanitize(title);
            string path = Path.Combine(
                _reportDirectory,
                string.Format("{0:yyyyMMdd_HHmmss}_{1}.txt", DateTime.Now, safe));

            var sb = new StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine(new string('=', Math.Max(12, title.Length)));
            sb.AppendLine(content);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        private static string Sanitize(string title)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = (title ?? "Report").ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            }
            return new string(chars);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// **最小 XLSX 写入器**(.xlsx = ZIP + 若干 XML 部件),零外部依赖。
    /// <para>
    /// 为什么自己写而不是引库:项目基线是 net48 +「不引 NuGet 的 netstandard 桥接」(技能规范 §2),
    /// 而 .NET Framework 自带 <see cref="ZipArchive"/> 就够拼出一个 Excel 能正常打开的工作簿:
    /// 只要按 OPC 规范写好 [Content_Types].xml、_rels/.rels、xl/workbook.xml、xl/_rels/workbook.xml.rels、
    /// xl/styles.xml 与各 xl/worksheets/sheetN.xml 即可。**不做公式、图表、合并单元格** —— 只做
    /// "多工作表 + 表头加粗 + 文本/数值单元格",这正是计算书需要的部分。
    /// </para>
    /// <para>
    /// 文本用 <c>t="inlineStr"</c> 内联字符串,省掉 sharedStrings 部件(少一个出错点);
    /// 数值一律按 <see cref="CultureInfo.InvariantCulture"/> 写,避免中文区域把小数点写成逗号导致 Excel 读成文本。
    /// </para>
    /// </summary>
    public sealed class XlsxWorkbook
    {
        private readonly List<XlsxSheet> _sheets = new List<XlsxSheet>();

        /// <summary>新建一个工作表(名称会自动去掉 Excel 不允许的字符并截到 31 字符)。</summary>
        public XlsxSheet AddSheet(string name)
        {
            var sheet = new XlsxSheet(SanitizeSheetName(name, _sheets.Count + 1));
            _sheets.Add(sheet);
            return sheet;
        }

        /// <summary>工作表数。</summary>
        public int SheetCount => _sheets.Count;

        /// <summary>保存为 .xlsx。</summary>
        public void Save(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path 不能为空", nameof(path));
            if (_sheets.Count == 0) AddSheet("Sheet1");

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            if (File.Exists(path)) File.Delete(path);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "[Content_Types].xml", ContentTypes());
                WriteEntry(zip, "_rels/.rels", RootRels());
                WriteEntry(zip, "xl/workbook.xml", Workbook());
                WriteEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRels());
                WriteEntry(zip, "xl/styles.xml", Styles());
                for (int i = 0; i < _sheets.Count; i++)
                {
                    WriteEntry(zip, "xl/worksheets/sheet" + (i + 1) + ".xml", _sheets[i].ToXml());
                }
            }
        }

        /// <summary>保存为 .xlsx 并返回路径(调用方用于提示用户)。</summary>
        public string SaveTo(string directory, string fileNameWithoutExtension)
        {
            string safe = SanitizeFileName(fileNameWithoutExtension);
            string path = Path.Combine(directory ?? ".",
                string.Format(CultureInfo.InvariantCulture, "{0:yyyyMMdd_HHmmss}_{1}.xlsx", DateTime.Now, safe));
            Save(path);
            return path;
        }

        // ------------------------------------------------------------------ 部件

        private string ContentTypes()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (int i = 0; i < _sheets.Count; i++)
            {
                sb.Append("<Override PartName=\"/xl/worksheets/sheet").Append(i + 1)
                  .Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            }
            sb.Append("</Types>");
            return sb.ToString();
        }

        private static string RootRels()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                   "</Relationships>";
        }

        private string Workbook()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ");
            sb.Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            sb.Append("<sheets>");
            for (int i = 0; i < _sheets.Count; i++)
            {
                sb.Append("<sheet name=\"").Append(Escape(_sheets[i].Name)).Append("\" sheetId=\"")
                  .Append(i + 1).Append("\" r:id=\"rId").Append(i + 1).Append("\"/>");
            }
            sb.Append("</sheets></workbook>");
            return sb.ToString();
        }

        private string WorkbookRels()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 0; i < _sheets.Count; i++)
            {
                sb.Append("<Relationship Id=\"rId").Append(i + 1)
                  .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet")
                  .Append(i + 1).Append(".xml\"/>");
            }
            sb.Append("<Relationship Id=\"rId").Append(_sheets.Count + 1)
              .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        /// <summary>两种字体(常规 / 加粗),供表头使用;填充与边框给最小合法集合。</summary>
        private static string Styles()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                   "<fonts count=\"2\">" +
                   "<font><sz val=\"11\"/><name val=\"Microsoft YaHei\"/></font>" +
                   "<font><b/><sz val=\"11\"/><name val=\"Microsoft YaHei\"/></font>" +
                   "</fonts>" +
                   "<fills count=\"2\">" +
                   "<fill><patternFill patternType=\"none\"/></fill>" +
                   "<fill><patternFill patternType=\"gray125\"/></fill>" +
                   "</fills>" +
                   "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                   "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                   "<cellXfs count=\"2\">" +
                   "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
                   "</cellXfs>" +
                   "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                   "</styleSheet>";
        }

        private static void WriteEntry(ZipArchive zip, string entryName, string content)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            {
                var bytes = new UTF8Encoding(false).GetBytes(content ?? "");
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        // ------------------------------------------------------------------ 工具

        /// <summary>Excel 工作表名:去除 []:*?/\ 并截到 31 字符;空名给默认名。</summary>
        private static string SanitizeSheetName(string name, int index)
        {
            if (string.IsNullOrEmpty(name)) return "Sheet" + index;
            var chars = name.ToCharArray();
            char[] invalid = { '[', ']', ':', '*', '?', '/', '\\' };
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            }
            string result = new string(chars).Trim();
            if (result.Length == 0) result = "Sheet" + index;
            return result.Length > 31 ? result.Substring(0, 31) : result;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Report";
            var chars = name.ToCharArray();
            var invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            }
            return new string(chars);
        }

        /// <summary>XML 文本转义(工作表名与单元格文本都要用)。</summary>
        internal static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length + 8);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>XLSX 工作表(只支持顺序追加行:表头加粗、文本内联、数值按不变文化写)。</summary>
    public sealed class XlsxSheet
    {
        private readonly List<List<XlsxCell>> _rows = new List<List<XlsxCell>>();

        internal XlsxSheet(string name)
        {
            Name = name;
        }

        /// <summary>工作表名(已按 Excel 规则清洗)。</summary>
        public string Name { get; }

        /// <summary>已写入的行数(自检用)。</summary>
        public int RowCount => _rows.Count;

        /// <summary>加一行表头(加粗)。</summary>
        public void AddHeader(params string[] cells)
        {
            AddRow(true, cells);
        }

        /// <summary>
        /// 加一行数据。单元格支持 string(内联文本)、double / int / decimal(数值)、null(空)。
        /// 数值一律用不变文化写,避免区域设置把小数点变成逗号。
        /// </summary>
        public void AddRow(params object[] cells)
        {
            AddRow(false, cells);
        }

        /// <summary>加一个空行(用于分组之间的间隔)。</summary>
        public void AddBlankRow()
        {
            _rows.Add(new List<XlsxCell>());
        }

        private void AddRow(bool bold, object[] cells)
        {
            var row = new List<XlsxCell>();
            if (cells != null)
            {
                foreach (var cell in cells)
                {
                    row.Add(XlsxCell.Create(cell, bold));
                }
            }
            _rows.Add(row);
        }

        internal string ToXml()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append("<sheetData>");
            for (int r = 0; r < _rows.Count; r++)
            {
                sb.Append("<row r=\"").Append(r + 1).Append("\">");
                var row = _rows[r];
                for (int c = 0; c < row.Count; c++)
                {
                    sb.Append(row[c].ToXml(ColumnName(c) + (r + 1)));
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        /// <summary>0 → A、25 → Z、26 → AA …(Excel 列名)。</summary>
        private static string ColumnName(int index)
        {
            var sb = new StringBuilder();
            int value = index;
            do
            {
                sb.Insert(0, (char)('A' + value % 26));
                value = value / 26 - 1;
            } while (value >= 0);
            return sb.ToString();
        }
    }

    /// <summary>一个单元格(文本 / 数值 / 空)。</summary>
    internal sealed class XlsxCell
    {
        private bool _isText;
        private string _text;
        private double _number;
        private bool _bold;

        internal static XlsxCell Create(object value, bool bold)
        {
            var cell = new XlsxCell { _bold = bold };
            if (value == null) return cell;

            if (value is string)
            {
                cell._isText = true;
                cell._text = (string)value;
                return cell;
            }
            if (value is bool)
            {
                cell._isText = true;
                cell._text = ((bool)value) ? "是" : "否";
                return cell;
            }

            try
            {
                cell._number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                cell._isText = false;
                return cell;
            }
            catch
            {
                cell._isText = true;
                cell._text = value.ToString();
                return cell;
            }
        }

        internal string ToXml(string reference)
        {
            string style = _bold ? " s=\"1\"" : "";
            if (_isText)
            {
                if (string.IsNullOrEmpty(_text)) return "<c r=\"" + reference + "\"" + style + "/>";
                return "<c r=\"" + reference + "\" t=\"inlineStr\"" + style + "><is><t xml:space=\"preserve\">" +
                       XlsxWorkbook.Escape(_text) + "</t></is></c>";
            }
            return "<c r=\"" + reference + "\"" + style + "><v>" +
                   _number.ToString("R", CultureInfo.InvariantCulture) + "</v></c>";
        }
    }
}

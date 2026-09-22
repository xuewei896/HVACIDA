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

        /// <summary>
        /// 样式表:常规 / 加粗 / **表头(加粗+浅蓝底+细边框+居中)** / **分区标题(加粗+更浅底+边框)** /
        /// **数值(千分位,最多 4 位小数)** / 大标题(加粗 12)。2026-09-20 排版优化:补列宽、冻结首行、合并分区标题。
        /// </summary>
        private static string Styles()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                   "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"#,##0.####\"/></numFmts>" +
                   "<fonts count=\"3\">" +
                   "<font><sz val=\"11\"/><name val=\"Microsoft YaHei\"/></font>" +
                   "<font><b/><sz val=\"11\"/><name val=\"Microsoft YaHei\"/></font>" +
                   "<font><b/><sz val=\"12\"/><name val=\"Microsoft YaHei\"/></font>" +
                   "</fonts>" +
                   "<fills count=\"4\">" +
                   "<fill><patternFill patternType=\"none\"/></fill>" +
                   "<fill><patternFill patternType=\"gray125\"/></fill>" +
                   "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD9E2F3\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                   "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFEEF3F9\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                   "</fills>" +
                   "<borders count=\"2\">" +
                   "<border><left/><right/><top/><bottom/><diagonal/></border>" +
                   "<border><left style=\"thin\"><color rgb=\"FFBFBFBF\"/></left><right style=\"thin\"><color rgb=\"FFBFBFBF\"/></right>" +
                   "<top style=\"thin\"><color rgb=\"FFBFBFBF\"/></top><bottom style=\"thin\"><color rgb=\"FFBFBFBF\"/></bottom><diagonal/></border>" +
                   "</borders>" +
                   "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                   "<cellXfs count=\"6\">" +
                   "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
                   "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" +
                   "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
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

    /// <summary>
    /// XLSX 工作表(只支持顺序追加行)。2026-09-20 排版优化:可设**列宽**、**冻结首行**、
    /// **分区标题跨列合并**;数值统一用千分位格式(最多 4 位小数),文本内联。
    /// </summary>
    public sealed class XlsxSheet
    {
        private readonly List<List<XlsxCell>> _rows = new List<List<XlsxCell>>();
        private readonly List<double> _columnWidths = new List<double>();
        private readonly List<string> _merges = new List<string>();
        private int _freezeRows;

        internal XlsxSheet(string name)
        {
            Name = name;
        }

        /// <summary>工作表名(已按 Excel 规则清洗)。</summary>
        public string Name { get; }

        /// <summary>已写入的行数(自检用)。</summary>
        public int RowCount => _rows.Count;

        /// <summary>设置各列宽度(0 = 该列用默认宽;只影响显示,不影响数据)。</summary>
        public void SetColumnWidths(params double[] widths)
        {
            _columnWidths.Clear();
            if (widths == null) return;
            foreach (var w in widths) _columnWidths.Add(w);
        }

        /// <summary>冻结前若干行(表头不在第一行时传 0 取消)。</summary>
        public void FreezeRows(int count)
        {
            _freezeRows = count < 0 ? 0 : count;
        }

        /// <summary>加一行大标题(加粗 12)。</summary>
        public void AddTitle(string text)
        {
            AddStyledRow(XlsxStyles.Title, new object[] { text });
        }

        /// <summary>加一行分区标题(加粗 + 浅底 + 边框),并跨 <paramref name="columns"/> 列合并。</summary>
        public void AddSectionTitle(string text, int columns)
        {
            int rowIndex = _rows.Count;
            AddStyledRow(XlsxStyles.Section, new object[] { text });
            if (columns > 1)
            {
                _merges.Add(ColumnName(0) + (rowIndex + 1) + ":" + ColumnName(columns - 1) + (rowIndex + 1));
            }
        }

        /// <summary>加一行表头(加粗 + 浅蓝底 + 细边框 + 居中)。</summary>
        public void AddHeader(params string[] cells)
        {
            var values = new object[cells == null ? 0 : cells.Length];
            for (int i = 0; i < values.Length; i++) values[i] = cells[i];
            AddStyledRow(XlsxStyles.Header, values);
        }

        /// <summary>
        /// 加一行数据。单元格支持 string(内联文本)、double / int / decimal(数值)、null(空)。
        /// 数值一律用不变文化写,避免区域设置把小数点变成逗号。
        /// </summary>
        public void AddRow(params object[] cells)
        {
            AddStyledRow(XlsxStyles.Body, cells);
        }

        /// <summary>加一行**整行加粗**的数据(合计行等)。</summary>
        public void AddBoldRow(params object[] cells)
        {
            AddStyledRow(XlsxStyles.Bold, cells);
        }

        /// <summary>加一个空行(用于分组之间的间隔)。</summary>
        public void AddBlankRow()
        {
            _rows.Add(new List<XlsxCell>());
        }

        private void AddStyledRow(int style, object[] cells)
        {
            var row = new List<XlsxCell>();
            if (cells != null)
            {
                foreach (var cell in cells)
                {
                    // 数值在正文行里单独走"千分位"样式;表头/标题/加粗行不套数值格式
                    int cellStyle = style == XlsxStyles.Body && XlsxCell.IsNumeric(cell)
                        ? XlsxStyles.Number
                        : style;
                    row.Add(XlsxCell.Create(cell, cellStyle));
                }
            }
            _rows.Add(row);
        }

        internal string ToXml()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");

            if (_freezeRows > 0)
            {
                sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"").Append(_freezeRows)
                  .Append("\" topLeftCell=\"A").Append(_freezeRows + 1)
                  .Append("\" activePane=\"bottomLeft\" state=\"frozen\"/><selection pane=\"bottomLeft\" activeCell=\"A")
                  .Append(_freezeRows + 1).Append("\" sqref=\"A").Append(_freezeRows + 1)
                  .Append("\"/></sheetView></sheetViews>");
            }

            if (_columnWidths.Count > 0)
            {
                sb.Append("<cols>");
                for (int i = 0; i < _columnWidths.Count; i++)
                {
                    if (_columnWidths[i] <= 0) continue;
                    sb.Append("<col min=\"").Append(i + 1).Append("\" max=\"").Append(i + 1)
                      .Append("\" width=\"").Append(_columnWidths[i].ToString("0.##", CultureInfo.InvariantCulture))
                      .Append("\" customWidth=\"1\"/>");
                }
                sb.Append("</cols>");
            }

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
            sb.Append("</sheetData>");

            if (_merges.Count > 0)
            {
                sb.Append("<mergeCells count=\"").Append(_merges.Count).Append("\">");
                foreach (var merge in _merges) sb.Append("<mergeCell ref=\"").Append(merge).Append("\"/>");
                sb.Append("</mergeCells>");
            }

            sb.Append("</worksheet>");
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

    /// <summary>工作表/单元格样式索引(与 <c>styles.xml</c> 的 cellXfs 一一对应)。</summary>
    internal static class XlsxStyles
    {
        /// <summary>常规(文本)。</summary>
        internal const int Body = 0;

        /// <summary>加粗。</summary>
        internal const int Bold = 1;

        /// <summary>表头:加粗 + 浅蓝底 + 细边框 + 居中。</summary>
        internal const int Header = 2;

        /// <summary>分区标题:加粗 + 更浅底 + 细边框。</summary>
        internal const int Section = 3;

        /// <summary>数值:千分位,最多 4 位小数。</summary>
        internal const int Number = 4;

        /// <summary>大标题:加粗 12。</summary>
        internal const int Title = 5;
    }

    /// <summary>一个单元格(文本 / 数值 / 空)。</summary>
    internal sealed class XlsxCell
    {
        private bool _isText;
        private string _text;
        private double _number;
        private int _style;

        internal static bool IsNumeric(object value)
        {
            if (value == null || value is string || value is bool) return false;
            double parsed;
            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Any, CultureInfo.InvariantCulture, out parsed);
        }

        internal static XlsxCell Create(object value, int style)
        {
            var cell = new XlsxCell { _style = style };
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
            string style = _style > 0 ? " s=\"" + _style + "\"" : "";
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

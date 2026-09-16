using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HVACIDA.Core.Services
{
    /// <summary>JSON 值的种类。</summary>
    public enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object
    }

    /// <summary>
    /// **最小 JSON 读取器**(只读,不引任何第三方库 —— 与 XLSX 写入器同一取舍)。
    /// <para>
    /// 用途:解析 ima 开放接口的应答(统一结构 <c>{ retcode, errmsg, data }</c>)。
    /// 只实现"读"和"转义字符串"两件事,**不**做序列化框架、不反射、不依赖 Newtonsoft。
    /// </para>
    /// <para>
    /// 取值一律走带默认值的 <see cref="AsString(string)"/> / <see cref="AsInt(int)"/> / <see cref="AsBool(bool)"/>,
    /// 字段缺失或类型不符时返回默认值 —— 接口改字段名时表现为"没数据"而不是崩窗口;
    /// JSON 语法错误会抛 <see cref="FormatException"/>,由调用方兜住并照实报错。
    /// </para>
    /// </summary>
    public sealed class JsonValue
    {
        private readonly JsonKind _kind;
        private readonly string _text;
        private readonly double _number;
        private readonly bool _boolean;
        private readonly List<JsonValue> _items;
        private readonly Dictionary<string, JsonValue> _members;

        private JsonValue(JsonKind kind)
        {
            _kind = kind;
        }

        private JsonValue(string text)
        {
            _kind = JsonKind.String;
            _text = text;
        }

        private JsonValue(double number)
        {
            _kind = JsonKind.Number;
            _number = number;
        }

        private JsonValue(bool boolean)
        {
            _kind = JsonKind.Bool;
            _boolean = boolean;
        }

        /// <summary>取值失败时返回的"空值"对象(不返回 C# null,链式取值永远安全)。</summary>
        public static readonly JsonValue Null = new JsonValue(JsonKind.Null);

        /// <summary>值的种类。</summary>
        public JsonKind Kind => _kind;

        /// <summary>是否为 null(或字段缺失)。</summary>
        public bool IsNull => _kind == JsonKind.Null;

        /// <summary>是否为对象。</summary>
        public bool IsObject => _kind == JsonKind.Object;

        /// <summary>是否为数组。</summary>
        public bool IsArray => _kind == JsonKind.Array;

        /// <summary>数组元素个数 / 对象成员个数(其它种类为 0)。</summary>
        public int Count => _items != null ? _items.Count : (_members != null ? _members.Count : 0);

        /// <summary>对象成员名(顺序不定;不是对象则为空序列)。</summary>
        public IEnumerable<string> Names => _members != null ? (IEnumerable<string>)_members.Keys : new string[0];

        /// <summary>数组元素(不是数组则为空序列)。</summary>
        public IEnumerable<JsonValue> Items => _items != null ? (IEnumerable<JsonValue>)_items : new JsonValue[0];

        /// <summary>
        /// 按成员名取值。**取不到返回 <see cref="Null"/> 而不是 C# null** ——
        /// 这样 <c>root.Get("data").Get("info_list")</c> 这种链式写法不会因为接口改字段名而空引用崩溃,
        /// 只是表现为"没数据"(再配合 <see cref="AsString(string)"/> 的默认值)。
        /// </summary>
        public JsonValue Get(string name)
        {
            if (_members == null || name == null) return Null;
            JsonValue value;
            return _members.TryGetValue(name, out value) ? value : Null;
        }

        /// <summary>按下标取值(**越界返回 <see cref="Null"/>**)。</summary>
        public JsonValue Get(int index)
        {
            if (_items == null || index < 0 || index >= _items.Count) return Null;
            return _items[index];
        }

        /// <summary>取字符串(不是字符串则返回 <paramref name="fallback"/>)。</summary>
        public string AsString(string fallback = "")
        {
            if (_kind == JsonKind.String) return _text ?? "";
            if (_kind == JsonKind.Number) return _number.ToString("R", CultureInfo.InvariantCulture);
            if (_kind == JsonKind.Bool) return _boolean ? "true" : "false";
            return fallback;
        }

        /// <summary>取整数(不是数字则返回 <paramref name="fallback"/>;JSON 数字一律按 double 存)。</summary>
        public int AsInt(int fallback = 0)
        {
            if (_kind != JsonKind.Number) return fallback;
            if (double.IsNaN(_number) || double.IsInfinity(_number)) return fallback;
            if (_number > int.MaxValue || _number < int.MinValue) return fallback;
            return (int)Math.Round(_number, MidpointRounding.AwayFromZero);
        }

        /// <summary>取浮点(不是数字则返回 <paramref name="fallback"/>)。</summary>
        public double AsDouble(double fallback = 0)
        {
            return _kind == JsonKind.Number ? _number : fallback;
        }

        /// <summary>取布尔(不是布尔则返回 <paramref name="fallback"/>)。</summary>
        public bool AsBool(bool fallback = false)
        {
            return _kind == JsonKind.Bool ? _boolean : fallback;
        }

        /// <summary>解析一段 JSON 文本(整段必须是**一个**值;多余的尾巴视为语法错误)。</summary>
        public static JsonValue Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            int index = 0;
            SkipWhitespace(json, ref index);
            JsonValue value = ParseValue(json, ref index);
            SkipWhitespace(json, ref index);
            if (index != json.Length) throw new FormatException("JSON 末尾有多余内容(第 " + (index + 1) + " 个字符起)。");
            return value;
        }

        /// <summary>把字符串转义成可以塞进 JSON 字符串字面量的形式(含中文原样保留、控制字符走 \u)。</summary>
        public static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length + 8);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ 解析实现

        private static JsonValue ParseValue(string json, ref int index)
        {
            if (index >= json.Length) throw new FormatException("JSON 不完整:期望一个值。");
            char c = json[index];
            switch (c)
            {
                case '{': return ParseObject(json, ref index);
                case '[': return ParseArray(json, ref index);
                case '"': return new JsonValue(ParseString(json, ref index));
                case 't': Expect(json, ref index, "true"); return new JsonValue(true);
                case 'f': Expect(json, ref index, "false"); return new JsonValue(false);
                case 'n': Expect(json, ref index, "null"); return new JsonValue(JsonKind.Null);
                default: return new JsonValue(ParseNumber(json, ref index));
            }
        }

        private static JsonValue ParseObject(string json, ref int index)
        {
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            index++;                                    // '{'
            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == '}')
            {
                index++;
                return new JsonValue(JsonKind.Object, null, 0, false, null, members);
            }
            while (true)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] != '"')
                    throw new FormatException("JSON 对象里期望成员名(第 " + (index + 1) + " 个字符)。");
                string name = ParseString(json, ref index);
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] != ':')
                    throw new FormatException("JSON 对象成员「" + name + "」后缺冒号。");
                index++;
                SkipWhitespace(json, ref index);
                members[name] = ParseValue(json, ref index);
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',')
                {
                    index++;
                    continue;
                }
                if (index < json.Length && json[index] == '}')
                {
                    index++;
                    break;
                }
                throw new FormatException("JSON 对象成员「" + name + "」后缺逗号或右花括号(第 " + (index + 1) + " 个字符)。");
            }
            return new JsonValue(JsonKind.Object, null, 0, false, null, members);
        }

        private static JsonValue ParseArray(string json, ref int index)
        {
            var items = new List<JsonValue>();
            index++;                                    // '['
            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == ']')
            {
                index++;
                return new JsonValue(JsonKind.Array, null, 0, false, items, null);
            }
            while (true)
            {
                SkipWhitespace(json, ref index);
                items.Add(ParseValue(json, ref index));
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',')
                {
                    index++;
                    continue;
                }
                if (index < json.Length && json[index] == ']')
                {
                    index++;
                    break;
                }
                throw new FormatException("JSON 数组元素后缺逗号或右方括号(第 " + (index + 1) + " 个字符)。");
            }
            return new JsonValue(JsonKind.Array, null, 0, false, items, null);
        }

        private JsonValue(JsonKind kind, string text, double number, bool boolean,
            List<JsonValue> items, Dictionary<string, JsonValue> members)
        {
            _kind = kind;
            _text = text;
            _number = number;
            _boolean = boolean;
            _items = items;
            _members = members;
        }

        private static string ParseString(string json, ref int index)
        {
            index++;                                    // 开引号
            var sb = new StringBuilder();
            while (true)
            {
                if (index >= json.Length) throw new FormatException("JSON 字符串没有收尾的引号。");
                char c = json[index++];
                if (c == '"') break;
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (index >= json.Length) throw new FormatException("JSON 字符串以反斜杠结尾。");
                char escape = json[index++];
                switch (escape)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (index + 4 > json.Length) throw new FormatException("JSON 的 \\u 转义不完整。");
                        int code;
                        if (!int.TryParse(json.Substring(index, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out code))
                            throw new FormatException("JSON 的 \\u 转义不是合法十六进制:" + json.Substring(index, 4));
                        sb.Append((char)code);
                        index += 4;
                        break;
                    default:
                        throw new FormatException("JSON 字符串里不认识的转义:\\" + escape);
                }
            }
            return sb.ToString();
        }

        private static double ParseNumber(string json, ref int index)
        {
            int start = index;
            while (index < json.Length)
            {
                char c = json[index];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') index++;
                else break;
            }
            if (index == start) throw new FormatException("JSON 里有不认识的字符「" + json[start] + "」(第 " + (start + 1) + " 个)。");
            double number;
            if (!double.TryParse(json.Substring(start, index - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out number))
                throw new FormatException("JSON 数字不合法:" + json.Substring(start, index - start));
            return number;
        }

        private static void Expect(string json, ref int index, string literal)
        {
            if (index + literal.Length > json.Length ||
                string.CompareOrdinal(json, index, literal, 0, literal.Length) != 0)
                throw new FormatException("JSON 里期望字面量 " + literal + "(第 " + (index + 1) + " 个字符)。");
            index += literal.Length;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length)
            {
                char c = json[index];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') index++;
                else break;
            }
        }
    }
}

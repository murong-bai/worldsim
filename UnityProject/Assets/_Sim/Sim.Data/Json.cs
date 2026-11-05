using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sim.Data
{
    /// <summary>
    /// 极简 JSON 值模型 + 解析器（零依赖：Unity 与 dotnet 双端行为一致，无第三方包）。
    /// 支持：Object / Array / String / Number / Bool / Null。
    /// Object 字段保持文件出现顺序（加载器自行排序保证确定性）。
    /// </summary>
    public sealed class JsonValue
    {
        public enum Kind { Object, Array, String, Number, Bool, Null }

        public Kind Type;
        public string Str;
        public double Num;
        public bool Bool;
        public List<JsonValue> Items;                                  // Array
        public List<KeyValuePair<string, JsonValue>> Fields;           // Object

        public JsonValue Get(string key)
        {
            if (Fields == null) return null;
            for (int i = 0; i < Fields.Count; i++)
            {
                if (Fields[i].Key == key) return Fields[i].Value;
            }
            return null;
        }

        public string GetString(string key, string defaultValue)
        {
            var v = Get(key);
            return v != null && v.Type == Kind.String ? v.Str : defaultValue;
        }

        public int GetInt(string key, int defaultValue)
        {
            var v = Get(key);
            return v != null && v.Type == Kind.Number ? (int)v.Num : defaultValue;
        }

        public bool GetBool(string key, bool defaultValue)
        {
            var v = Get(key);
            return v != null && v.Type == Kind.Bool ? v.Bool : defaultValue;
        }
    }

    public static class Json
    {
        public static JsonValue Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            var parser = new Parser(text);
            parser.SkipWs();
            var value = parser.ParseValue();
            parser.SkipWs();
            if (!parser.AtEnd) throw parser.Error("trailing characters");
            return value;
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s)
            {
                _s = s;
            }

            public bool AtEnd
            {
                get { return _i >= _s.Length; }
            }

            private char Current
            {
                get { return _s[_i]; }
            }

            public Exception Error(string message)
            {
                return new FormatException("JSON parse error at " + _i + ": " + message);
            }

            public void SkipWs()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') _i++;
                    else break;
                }
            }

            public JsonValue ParseValue()
            {
                if (AtEnd) throw Error("unexpected end of input");
                char c = Current;
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"':
                        return new JsonValue { Type = JsonValue.Kind.String, Str = ParseStringRaw() };
                    case 't': Expect("true"); return new JsonValue { Type = JsonValue.Kind.Bool, Bool = true };
                    case 'f': Expect("false"); return new JsonValue { Type = JsonValue.Kind.Bool, Bool = false };
                    case 'n': Expect("null"); return new JsonValue { Type = JsonValue.Kind.Null };
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Error("unexpected character '" + c + "'");
                }
            }

            private void Expect(string word)
            {
                if (_i + word.Length > _s.Length || _s.Substring(_i, word.Length) != word)
                    throw Error("expected '" + word + "'");
                _i += word.Length;
            }

            private JsonValue ParseObject()
            {
                _i++; // '{'
                var value = new JsonValue { Type = JsonValue.Kind.Object, Fields = new List<KeyValuePair<string, JsonValue>>() };
                SkipWs();
                if (!AtEnd && Current == '}')
                {
                    _i++;
                    return value;
                }
                while (true)
                {
                    SkipWs();
                    if (AtEnd || Current != '"') throw Error("expected object key");
                    string key = ParseStringRaw();
                    SkipWs();
                    if (AtEnd || Current != ':') throw Error("expected ':'");
                    _i++;
                    SkipWs();
                    var field = ParseValue();
                    value.Fields.Add(new KeyValuePair<string, JsonValue>(key, field));
                    SkipWs();
                    if (AtEnd) throw Error("unterminated object");
                    if (Current == ',') { _i++; continue; }
                    if (Current == '}') { _i++; return value; }
                    throw Error("expected ',' or '}'");
                }
            }

            private JsonValue ParseArray()
            {
                _i++; // '['
                var value = new JsonValue { Type = JsonValue.Kind.Array, Items = new List<JsonValue>() };
                SkipWs();
                if (!AtEnd && Current == ']')
                {
                    _i++;
                    return value;
                }
                while (true)
                {
                    SkipWs();
                    var item = ParseValue();
                    value.Items.Add(item);
                    SkipWs();
                    if (AtEnd) throw Error("unterminated array");
                    if (Current == ',') { _i++; continue; }
                    if (Current == ']') { _i++; return value; }
                    throw Error("expected ',' or ']'");
                }
            }

            private string ParseStringRaw()
            {
                _i++; // opening '"'
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("unterminated string");
                    char c = _s[_i];
                    if (c == '"')
                    {
                        _i++;
                        return sb.ToString();
                    }
                    if (c == '\\')
                    {
                        _i++;
                        if (AtEnd) throw Error("unterminated escape");
                        char e = _s[_i];
                        switch (e)
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
                                if (_i + 5 > _s.Length) throw Error("bad \\u escape");
                                sb.Append((char)Convert.ToInt32(_s.Substring(_i + 1, 4), 16));
                                _i += 4;
                                break;
                            default:
                                throw Error("bad escape '\\" + e + "'");
                        }
                        _i++;
                    }
                    else
                    {
                        sb.Append(c);
                        _i++;
                    }
                }
            }

            private JsonValue ParseNumber()
            {
                int start = _i;
                if (Current == '-') _i++;
                while (!AtEnd)
                {
                    char c = Current;
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') _i++;
                    else break;
                }
                string text = _s.Substring(start, _i - start);
                double num;
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out num))
                    throw Error("bad number '" + text + "'");
                return new JsonValue { Type = JsonValue.Kind.Number, Num = num };
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CpqSystemTool
{
    /// <summary>
    /// 极简零依赖 JSON 工具（R4b 收编）。
    /// 合并了以下两处重复实现：
    ///  - 原 Modules/ConfigBackup.cs 的 MiniJson / MiniJsonParser（ToolConfig 定向序列化/解析）；
    ///  - 原 MainWindow.Probe.cs 的私有 MiniJson（通用递归下降解析）。
    /// 注意：本文件刻意不引用 System.Web（WPF XAML 编译器无法解析该程序集，会导致 MC1000）。
    /// </summary>
    internal static class MiniJson
    {
        // ===================== 通用 JSON 值解析（原 MainWindow.Probe.cs 私有 MiniJson） =====================
        // 支持对象 / 数组 / 字符串(含转义) / 数字 / true|false|null。
        // 返回值：Dictionary<string,object>（对象）、List<object>（数组）、string、long、double、bool、null。
        public static object Parse(string json)
        {
            int i = 0;
            SkipWs(json, ref i);
            return ParseValue(json, ref i);
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't' || c == 'f') return ParseBool(s, ref i);
            if (c == 'n') { i += 4; return null; }       // null
            if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(s, ref i);
            i++;
            return null;
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var dict = new Dictionary<string, object>();
            i++; // 跳过 {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return dict; }
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') break;
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                object val = ParseValue(s, ref i);
                dict[key] = val;
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return dict;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // 跳过 [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (i < s.Length)
            {
                object val = ParseValue(s, ref i);
                list.Add(val);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return list;
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // 跳过开头的 "
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\')
                {
                    if (i >= s.Length) break;
                    char e = s[i++];
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
                            if (i + 4 <= s.Length)
                            {
                                string hex = s.Substring(i, 4);
                                i += 4;
                                sb.Append((char)int.Parse(hex, NumberStyles.HexNumber));
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && s[i] == '-') i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
            string num = s.Substring(start, i - start);
            if (num.IndexOf('.') < 0 && num.IndexOf('e') < 0 && num.IndexOf('E') < 0)
            {
                if (long.TryParse(num, out long l)) return l;
            }
            if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                return d;
            return num;
        }

        private static bool ParseBool(string s, ref int i)
        {
            if (i + 4 <= s.Length && s.Substring(i, 4) == "true") { i += 4; return true; }
            i += 5; // false
            return false;
        }

        // ===================== ToolConfig 定向序列化/解析（原 Modules/ConfigBackup.cs MiniJson / MiniJsonParser） =====================
        // 只覆盖 ToolConfig 形状：字符串数组 + 字符串字典。

        public static string Serialize(ToolConfig c)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            WriteArray(sb, "EnabledTweaks", c.EnabledTweaks);
            sb.Append(',');
            WriteArray(sb, "SelectedAppx", c.SelectedAppx);
            sb.Append(',');
            WriteArray(sb, "SelectedCleanup", c.SelectedCleanup);
            sb.Append(',');
            WriteDict(sb, "Flags", c.Flags);
            sb.Append(',');
            WriteDict(sb, "TweakStates", c.TweakStates);
            sb.Append('}');
            return sb.ToString();
        }

        private static void WriteArray(StringBuilder sb, string name, List<string> items)
        {
            sb.Append('"').Append(name).Append("\":[");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                WriteString(sb, items[i]);
            }
            sb.Append(']');
        }

        private static void WriteDict(StringBuilder sb, string name, Dictionary<string, string> items)
        {
            sb.Append('"').Append(name).Append("\":{");
            int i = 0;
            foreach (var kv in items)
            {
                if (i > 0) sb.Append(',');
                WriteString(sb, kv.Key);
                sb.Append(':');
                WriteString(sb, kv.Value);
                i++;
            }
            sb.Append('}');
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            if (s == null) { sb.Append("null"); return; }
            sb.Append('"');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (ch < 0x20)
                            sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else
                            sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>解析本工具序列化的 ToolConfig（未知字段跳过，缺省字段保持默认值）。</summary>
        public static ToolConfig ParseToolConfig(string json)
        {
            var cfg = new ToolConfig();
            if (string.IsNullOrWhiteSpace(json)) return cfg;
            int p = 0;
            SkipWs(json, ref p);
            if (Peek(json, p) != '{') return cfg;
            p++;
            while (true)
            {
                SkipWs(json, ref p);
                if (Peek(json, p) == '}') { p++; break; }
                var key = ReadString(json, ref p);
                SkipWs(json, ref p);
                Expect(json, ref p, ':');
                if (key == "EnabledTweaks") cfg.EnabledTweaks = ReadStringArray(json, ref p);
                else if (key == "SelectedAppx") cfg.SelectedAppx = ReadStringArray(json, ref p);
                else if (key == "SelectedCleanup") cfg.SelectedCleanup = ReadStringArray(json, ref p);
                else if (key == "Flags") cfg.Flags = ReadStringDict(json, ref p);
                else if (key == "TweakStates") cfg.TweakStates = ReadStringDict(json, ref p);
                else SkipValue(json, ref p);
                SkipWs(json, ref p);
                if (Peek(json, p) == ',') { p++; continue; }
                if (Peek(json, p) == '}') { p++; break; }
                break;
            }
            return cfg;
        }

        private static List<string> ReadStringArray(string json, ref int p)
        {
            var list = new List<string>();
            SkipWs(json, ref p);
            if (Peek(json, p) != '[') return list;
            p++;
            while (true)
            {
                SkipWs(json, ref p);
                if (Peek(json, p) == ']') { p++; break; }
                list.Add(ReadString(json, ref p));
                SkipWs(json, ref p);
                if (Peek(json, p) == ',') { p++; continue; }
                if (Peek(json, p) == ']') { p++; break; }
            }
            return list;
        }

        private static Dictionary<string, string> ReadStringDict(string json, ref int p)
        {
            var d = new Dictionary<string, string>();
            SkipWs(json, ref p);
            if (Peek(json, p) != '{') return d;
            p++;
            while (true)
            {
                SkipWs(json, ref p);
                if (Peek(json, p) == '}') { p++; break; }
                var k = ReadString(json, ref p);
                SkipWs(json, ref p);
                Expect(json, ref p, ':');
                var v = ReadString(json, ref p);
                d[k] = v;
                SkipWs(json, ref p);
                if (Peek(json, p) == ',') { p++; continue; }
                if (Peek(json, p) == '}') { p++; break; }
            }
            return d;
        }

        private static void SkipValue(string json, ref int p)
        {
            SkipWs(json, ref p);
            char c = Peek(json, p);
            if (c == '"') { ReadString(json, ref p); return; }
            if (c == '[') { p++; int depth = 1; while (p < json.Length && depth > 0) { if (json[p] == '[') depth++; else if (json[p] == ']') depth--; p++; } return; }
            if (c == '{') { p++; int depth = 1; while (p < json.Length && depth > 0) { if (json[p] == '{') depth++; else if (json[p] == '}') depth--; p++; } return; }
            while (p < json.Length && json[p] != ',' && json[p] != '}' && json[p] != ']') p++;
        }

        private static string ReadString(string json, ref int p)
        {
            SkipWs(json, ref p);
            if (Peek(json, p) != '"') return "";
            p++;
            var sb = new StringBuilder();
            while (p < json.Length)
            {
                char ch = json[p++];
                if (ch == '"') break;
                if (ch == '\\')
                {
                    char esc = json[p++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (p + 4 > json.Length) break; // 截断的 \u 转义，跳过避免越界
                            string hex = json.Substring(p, 4);
                            p += 4;
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code)) break;
                            sb.Append((char)code);
                            break;
                    }
                }
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        private static char Peek(string json, int p) => p < json.Length ? json[p] : '\0';
        private static void Expect(string json, ref int p, char c) { if (Peek(json, p) == c) p++; }
    }
}

﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CpqSystemTool
{
    /// <summary>
    /// 配置导入/导出（含优化后自动保存）。
    /// 对应 ZyperWin++ 的核心卖点「配置还原选项」——把当前开关/勾选状态序列化为 JSON，
    /// 支持导出分享、导入还原、应用后自动落盘。零风险、纯序列化。
    /// 注意：本文件刻意不引用 System.Web（WPF XAML 编译器无法解析该程序集，会导致 MC1000），
    /// 改用下方零依赖的 MiniJson / MiniJsonParser。
    /// </summary>
    public class ToolConfig
    {
        public List<string> EnabledTweaks = new List<string>();
        public List<string> SelectedAppx = new List<string>();
        public List<string> SelectedCleanup = new List<string>();
        public Dictionary<string, string> Flags = new Dictionary<string, string>();
        // 三态优化项状态：id -> "On"/"Off"/"Default"（旧 EnabledTweaks 仅存二态"已启用"项，保留向后兼容）
        public Dictionary<string, string> TweakStates = new Dictionary<string, string>();
    }

    /// <summary>
    /// 极简零依赖 JSON 序列化（只覆盖 ToolConfig 形状：字符串数组 + 字符串字典）。
    /// </summary>
    internal static class MiniJson
    {
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
    }

    /// <summary>
    /// 极简零依赖 JSON 解析（只解析本工具生成的简单结构：对象 / 字符串数组 / 字符串字典）。
    /// </summary>
    internal static class MiniJsonParser
    {
        public static ToolConfig Parse(string json)
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
                            string hex = json.Substring(p, 4);
                            p += 4;
                            sb.Append((char)int.Parse(hex, NumberStyles.HexNumber));
                            break;
                    }
                }
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        private static char Peek(string json, int p) => p < json.Length ? json[p] : '\0';
        private static void Expect(string json, ref int p, char c) { if (Peek(json, p) == c) p++; }
        private static void SkipWs(string json, ref int p)
        {
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
        }
    }

    public static class ConfigBackup
    {
        private static string _configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
        public static string ConfigDir { get => _configDir; set => _configDir = value; }
        public static string AutoPath => Path.Combine(ConfigDir, "autosave.json");

        public static void Save(string path, ToolConfig cfg, Action<string> log)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                var json = MiniJson.Serialize(cfg);
                File.WriteAllText(path, json, Encoding.UTF8);
                log("[OK] 已导出配置: " + path);
            }
            catch (Exception ex) { log("[!] 导出失败: " + ex.Message); }
        }

        public static ToolConfig Load(string path, Action<string> log)
        {
            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                var cfg = MiniJsonParser.Parse(json);
                log("[OK] 已读取配置: " + path);
                return cfg ?? new ToolConfig();
            }
            catch (Exception ex) { log("[!] 读取失败: " + ex.Message); return new ToolConfig(); }
        }

        public static void AutoSave(ToolConfig cfg, Action<string> log) => Save(AutoPath, cfg, log);

        public static List<string> ListConfigs()
        {
            try
            {
                if (!Directory.Exists(ConfigDir)) return new List<string>();
                var list = new List<string>(Directory.GetFiles(ConfigDir, "*.json"));
                return list;
            }
            catch (Exception caughtEx) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + caughtEx.Message);  return new List<string>(); }
        }
    }
}

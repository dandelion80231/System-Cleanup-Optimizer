using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// 注册表与命令执行辅助：普通读写走 Microsoft.Win32.Registry；外部命令走 RunCommand。
    /// 普通读写走 Microsoft.Win32.Registry；外部命令走 RunCommand。
    /// </summary>
    internal static class RegistryHelper
    {
        public static bool SetDword(RegistryKey hive, string path, string name, int value, Action<string> log, bool create = true)
        {
            try
            {
                using (var k = create ? hive.CreateSubKey(path, true) : hive.OpenSubKey(path, true))
                {
                    if (k == null) { log("  [!] 无法打开(写) " + path); return false; }
                    k.SetValue(name, value, RegistryValueKind.DWord);
                }
                return true;
            }
            catch (Exception ex) { log("  [!] 写 " + path + "\\" + name + " 失败: " + ex.Message); return false; }
        }

        public static bool SetSz(RegistryKey hive, string path, string name, string value, Action<string> log)
        {
            try
            {
                using (var k = hive.CreateSubKey(path, true))
                    k.SetValue(name, value, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex) { log("  [!] 写 " + path + "\\" + name + " 失败: " + ex.Message); return false; }
        }

        public static void DeleteValue(RegistryKey hive, string path, string name, Action<string> log)
        {
            try
            {
                using (var k = hive.OpenSubKey(path, true))
                {
                    if (k == null) return;
                    k.DeleteValue(name, false);
                }
            }
            catch (Exception ex) { log("  [!] 删 " + path + "\\" + name + " 失败: " + ex.Message); }
        }

        /// <summary>删键。返回是否真的成功（Win11 24H2+ Policies 路径 DACL 可能拒绝 DELETE，admin 删不动）。</summary>
        public static bool DeleteKeyTree(RegistryKey hive, string path, Action<string> log)
        {
            try
            {
                hive.DeleteSubKeyTree(path, false);
                // 二次验证：检查 key 是否真没了
                using (var k = hive.OpenSubKey(path, false))
                {
                    if (k != null)
                    {
                        log("  [!] 删键 " + path + " 调用成功但键仍在（可能被 DACL/所有者拒绝）");
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex) { log("  [!] 删键 " + path + " 失败: " + ex.Message); return false; }
        }

        public static int GetDword(RegistryKey hive, string path, string name, int def = 0)
        {
            try
            {
                using (var k = hive.OpenSubKey(path, false))
                {
                    if (k == null) return def;
                    var v = k.GetValue(name);
                    if (v == null) return def;
                    return Convert.ToInt32(v);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("RegistryHelper 读取失败: " + ex.Message); return def; }
        }

        public static string GetSz(RegistryKey hive, string path, string name, string def = "")
        {
            try
            {
                using (var k = hive.OpenSubKey(path, false))
                {
                    if (k == null) return def;
                    var v = k.GetValue(name);
                    return v == null ? def : v.ToString();
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("RegistryHelper 读取失败: " + ex.Message); return def; }
        }

        public static bool ClsIdDefaultEmpty(RegistryKey hive, string path)
        {
            try
            {
                using (var k = hive.OpenSubKey(path, false))
                {
                    if (k == null) return false;
                    var v = k.GetValue("");
                    return v is string && ((string)v) == "";
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("RegistryHelper.ClsIdDefaultEmpty 失败: " + ex.Message); return false; }
        }

        public static byte[] GetBinary(RegistryKey hive, string path, string name)
        {
            try
            {
                using (var k = hive.OpenSubKey(path, false))
                {
                    if (k == null) return null;
                    return k.GetValue(name) as byte[];
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("RegistryHelper 读取失败: " + ex.Message); return null; }
        }

        public static void SetBinary(RegistryKey hive, string path, string name, byte[] data, Action<string> log)
        {
            try
            {
                using (var k = hive.CreateSubKey(path, true))
                    k.SetValue(name, data, RegistryValueKind.Binary);
            }
            catch (Exception ex) { log("  [!] 写 " + path + "\\" + name + " 失败: " + ex.Message); }
        }

        /// <summary>重启 Windows 资源管理器使 Explorer 相关设置生效（对应 restart_explorer）。</summary>
        public static void RestartExplorer(Action<string> log)
        {
            log("  [*] 重启 Windows 资源管理器以生效...");
            try
            {
                RunCommand("taskkill", "/f /im explorer.exe", log, 8000);
            }
            catch (Exception ex) { log("  [!] " + ex.Message); }
            try { Process.Start("explorer.exe"); log("  [OK] 资源管理器已重启。"); }
            catch (Exception ex) { log("  [!] 重启资源管理器失败，请手动重启: " + ex.Message); }
        }

        /// <summary>执行外部命令（sc / netsh / powercfg / schtasks / powershell 等）。</summary>
        public static int RunCommand(string exe, string args, Action<string> log, int timeoutMs = 20000)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) { log("  [!] 无法启动: " + exe); return -1; }
                    p.WaitForExit(timeoutMs);
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { log("  [!] 执行 " + exe + " 失败: " + ex.Message); return -1; }
        }

        /// <summary>执行外部命令（无日志回调，用于 State 查询等静默场景）。</summary>
        public static int RunCommand(string exe, string args, int timeoutMs = 20000)
        {
            return RunCommand(exe, args, _ => { }, timeoutMs);
        }
    }
}

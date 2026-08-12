using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// 注册表与命令执行辅助：普通读写走 Microsoft.Win32.Registry；外部命令走 RunCommand。
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
                // 键不存在 = 无需删除（常见于 HKCU 下没有 Edge 组策略键）。直接返回成功，避免误报"[!] 删键失败"。
                using (var probe = hive.OpenSubKey(path, false))
                    if (probe == null) return true;
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

        // ============================================================
        //  Edge 组策略：同时作用于 HKCU 与 HKLM
        //  背景：Edge 读取 HKLM 与 HKCU 的 Policies\Microsoft\Edge；
        //  仅清 HKLM 会因 HKCU 残留而清不掉「由组织管理」状态。
        //  这里统一在双 hive 上操作，使「禁用/恢复」能彻底清除。
        // ============================================================
        private static readonly RegistryKey[] EdgePolicyHives = { Registry.CurrentUser, Registry.LocalMachine };

        public static void SetEdgePolicy(string name, int value, Action<string> log)
        {
            foreach (var hive in EdgePolicyHives)
                SetDword(hive, @"SOFTWARE\Policies\Microsoft\Edge", name, value, log);
        }

        public static void SetEdgePolicyRecommended(string name, int value, Action<string> log)
        {
            foreach (var hive in EdgePolicyHives)
                SetDword(hive, @"SOFTWARE\Policies\Microsoft\Edge\Recommended", name, value, log);
        }

        public static void DeleteEdgePolicy(string name, Action<string> log)
        {
            foreach (var hive in EdgePolicyHives)
                DeleteValue(hive, @"SOFTWARE\Policies\Microsoft\Edge", name, log);
        }

        public static void DeleteEdgePolicyRecommended(string name, Action<string> log)
        {
            foreach (var hive in EdgePolicyHives)
                DeleteValue(hive, @"SOFTWARE\Policies\Microsoft\Edge\Recommended", name, log);
        }

        /// <summary>删除 Edge 组策略键（含 Recommended 子键）。两 hive 都清，确保彻底移除。</summary>
        public static bool DeleteEdgePolicyTree(Action<string> log)
        {
            bool ok = true;
            foreach (var hive in EdgePolicyHives)
            {
                ok &= DeleteKeyTree(hive, @"SOFTWARE\Policies\Microsoft\Edge", log);
                DeleteKeyTree(hive, @"SOFTWARE\Policies\Microsoft\Edge\Recommended", log);
            }
            return ok;
        }

        /// <summary>任一 hive 中该 Edge 策略值等于 onValue 即视为「开」。</summary>
        public static bool GetEdgePolicyState(string name, int onValue)
        {
            foreach (var hive in EdgePolicyHives)
                if (GetDwordState(hive, @"SOFTWARE\Policies\Microsoft\Edge", name, onValue)) return true;
            return false;
        }

        public static bool GetEdgePolicyRecommendedState(string name, int onValue)
        {
            foreach (var hive in EdgePolicyHives)
                if (GetDwordState(hive, @"SOFTWARE\Policies\Microsoft\Edge\Recommended", name, onValue)) return true;
            return false;
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

        /// <summary>读取注册表 Dword 并判断是否等于 onValue（用于 Tweaks 等开关状态查询）。键/值不存在或读取异常均返回 false。</summary>
        public static bool GetDwordState(RegistryKey hive, string subPath, string name, int onValue)
        {
            try
            {
                using (var k = hive.OpenSubKey(subPath))
                {
                    if (k != null && k.GetValue(name) is int v) return v == onValue;
                }
            }
            catch { }
            return false;
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
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } }
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

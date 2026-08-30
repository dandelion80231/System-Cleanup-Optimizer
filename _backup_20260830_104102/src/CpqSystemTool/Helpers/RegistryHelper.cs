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
        // ============================================================
        //  注册表视图（RegistryView）处理
        //  背景：本程序是 AnyCPU，在 64 位 Windows 上以 32 位(WOW64)进程运行。
        //  此时 Registry.LocalMachine 默认打开 32 位视图，HKLM\SOFTWARE 的写入
        //  会被重定向到 Wow6432Node —— 导致 Edge/Defender 组策略对 64 位程序完全不生效。
        //  策略：
        //    · 写入 → 显式用 64 位视图（策略才真正生效）
        //    · 删除 → 64 位与 32 位视图都清（旧版本写入 Wow6432Node 的残留也要清掉）
        //    · 读取 → 先查 64 位视图，查不到再回退 32 位视图（加法式，保证原本能读到的仍读到）
        // ============================================================

        /// <summary>把 RegistryKey 映射回 RegistryHive 枚举，用于 OpenBaseKey。</summary>
        private static RegistryHive ToHive(RegistryKey key)
        {
            var n = key == null ? "" : (key.Name ?? "");
            if (n.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase)) return RegistryHive.LocalMachine;
            if (n.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase)) return RegistryHive.CurrentUser;
            if (n.StartsWith("HKEY_CLASSES_ROOT", StringComparison.OrdinalIgnoreCase)) return RegistryHive.ClassesRoot;
            if (n.StartsWith("HKEY_CURRENT_CONFIG", StringComparison.OrdinalIgnoreCase)) return RegistryHive.CurrentConfig;
            if (n.StartsWith("HKEY_USERS", StringComparison.OrdinalIgnoreCase)) return RegistryHive.Users;
            return RegistryHive.LocalMachine;
        }

        /// <summary>
        /// 按指定视图打开 hive 根。
        /// 注意：本方法**总是**返回一个新建的 RegistryKey（绝不会返回传入的静态 hive 单例），
        /// 因此调用方可以安全地使用 using 释放；但也绝不能去 dispose 传入的 hive 参数本身。
        /// </summary>
        private static RegistryKey OpenView(RegistryKey hive, RegistryView view)
        {
            // 32 位系统只有单一视图，指定 Registry32/Registry64 都无意义，用 Default。
            var actual = Environment.Is64BitOperatingSystem ? view : RegistryView.Default;
            return RegistryKey.OpenBaseKey(ToHive(hive), actual);
        }

        private static readonly RegistryView[] WriteViews = { RegistryView.Registry64 };
        private static readonly RegistryView[] BothViews = { RegistryView.Registry64, RegistryView.Registry32 };

        public static bool SetDword(RegistryKey hive, string path, string name, int value, Action<string> log, bool create = true)
        {
            try
            {
                // 显式写 64 位视图，避免被 WOW64 重定向到 Wow6432Node 而策略不生效
                using (var root = OpenView(hive, RegistryView.Registry64))
                using (var k = create ? root.CreateSubKey(path, true) : root.OpenSubKey(path, true))
                {
                    if (k == null) { log?.Invoke("  [!] 无法打开(写) " + path); return false; }
                    k.SetValue(name, value, RegistryValueKind.DWord);
                }
                return true;
            }
            catch (Exception ex) { log?.Invoke("  [!] 写 " + path + "\\" + name + " 失败: " + ex.Message); return false; }
        }

        public static bool SetSz(RegistryKey hive, string path, string name, string value, Action<string> log)
        {
            try
            {
                using (var root = OpenView(hive, RegistryView.Registry64))
                using (var k = root.CreateSubKey(path, true))
                {
                    if (k == null) { log?.Invoke("  [!] 无法打开(写) " + path); return false; }
                    k.SetValue(name, value, RegistryValueKind.String);
                }
                return true;
            }
            catch (Exception ex) { log?.Invoke("  [!] 写 " + path + "\\" + name + " 失败: " + ex.Message); return false; }
        }

        public static void DeleteValue(RegistryKey hive, string path, string name, Action<string> log)
        {
            // 两个视图都删：旧版本写入 Wow6432Node 的值也要一并清除，否则策略残留
            foreach (var view in BothViews)
            {
                try
                {
                    using (var root = OpenView(hive, view))
                    using (var k = root.OpenSubKey(path, true))
                    {
                        if (k == null) continue;
                        k.DeleteValue(name, false);
                    }
                }
                catch (Exception ex) { log?.Invoke("  [!] 删 " + path + "\\" + name + " 失败: " + ex.Message); }
            }
        }

        /// <summary>在指定视图内删键。返回是否真的成功（Win11 24H2+ Policies 路径 DACL 可能拒绝 DELETE，admin 删不动）。</summary>
        private static bool DeleteKeyTreeInView(RegistryKey hive, string path, RegistryView view, Action<string> log)
        {
            try
            {
                using (var root = OpenView(hive, view))
                {
                    // 键不存在 = 无需删除（常见于 HKCU 下没有 Edge 组策略键）。直接返回成功，避免误报"[!] 删键失败"。
                    using (var probe = root.OpenSubKey(path, false))
                        if (probe == null) return true;
                    root.DeleteSubKeyTree(path, false);
                    // 二次验证：检查 key 是否真没了
                    using (var k = root.OpenSubKey(path, false))
                    {
                        if (k != null)
                        {
                            log?.Invoke("  [!] 删键 " + path + " 调用成功但键仍在（可能被 DACL/所有者拒绝）");
                            return false;
                        }
                    }
                    return true;
                }
            }
            catch (Exception ex) { log?.Invoke("  [!] 删键 " + path + " 失败: " + ex.Message); return false; }
        }

        public static bool DeleteKeyTree(RegistryKey hive, string path, Action<string> log)
        {
            // 同时清理 64 位与 32 位视图，避免旧版残留
            bool ok64 = DeleteKeyTreeInView(hive, path, RegistryView.Registry64, log);
            bool ok32 = DeleteKeyTreeInView(hive, path, RegistryView.Registry32, log);
            return ok64 && ok32;
        }

        /// <summary>读取 Dword；键或值不存在返回 null（视图：先 64 位再回退 32 位）。</summary>
        public static int? GetDwordOrNull(RegistryKey hive, string path, string name)
        {
            foreach (var view in BothViews)
            {
                try
                {
                    using (var root = OpenView(hive, view))
                    using (var k = root.OpenSubKey(path, false))
                    {
                        if (k == null) continue;
                        var v = k.GetValue(name);
                        if (v == null) continue;
                        return Convert.ToInt32(v);
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("RegistryHelper.GetDwordOrNull 失败: " + ex.Message); }
            }
            return null;
        }

        /// <summary>判断键是否存在（64/32 任一视图存在即视为存在）。</summary>
        public static bool KeyExists(RegistryKey hive, string path)
        {
            foreach (var view in BothViews)
            {
                try
                {
                    using (var root = OpenView(hive, view))
                    using (var k = root.OpenSubKey(path, false))
                        if (k != null) return true;
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("RegistryHelper.KeyExists 失败: " + ex.Message); }
            }
            return false;
        }

        /// <summary>删除指定值（64/32 双视图）。返回是否成功；键或值不存在视为无需清理，返回 true。</summary>
        public static bool DeleteValueChecked(RegistryKey hive, string path, string name, Action<string> log)
        {
            bool ok = true;
            foreach (var view in BothViews)
            {
                try
                {
                    using (var root = OpenView(hive, view))
                    using (var k = root.OpenSubKey(path, true))
                    {
                        if (k == null) continue;                 // 键不存在 = 没有残留
                        if (k.GetValue(name) == null) continue;  // 值不存在 = 没有残留
                        k.DeleteValue(name, false);
                    }
                }
                catch (Exception ex)
                {
                    ok = false;
                    log?.Invoke("  [!] 删 " + path + "\\" + name + " 失败: " + ex.Message);
                }
            }
            return ok;
        }

        /// <summary>仅当值已存在时才改写（在存在该值的每个视图中更新），返回是否至少更新了一处。</summary>
        public static bool SetDwordIfExists(RegistryKey hive, string path, string name, int value, Action<string> log)
        {
            bool updated = false, ok = true;
            foreach (var view in BothViews)
            {
                try
                {
                    using (var root = OpenView(hive, view))
                    using (var k = root.OpenSubKey(path, true))
                    {
                        if (k == null) continue;
                        if (k.GetValue(name) == null) continue;  // 不存在则不新建
                        k.SetValue(name, value, RegistryValueKind.DWord);
                        updated = true;
                    }
                }
                catch (Exception ex)
                {
                    ok = false;
                    log?.Invoke("  [!] 改 " + path + "\\" + name + " 失败: " + ex.Message);
                }
            }
            return updated && ok;
        }

        /// <summary>判断指定值是否存在（64/32 任一视图存在即 true）。</summary>
        public static bool ValueExists(RegistryKey hive, string path, string name)
        {
            foreach (var view in BothViews)
            {
                try
                {
                    using (var root = OpenView(hive, view))
                    using (var k = root.OpenSubKey(path, false))
                        if (k != null && k.GetValue(name) != null) return true;
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("RegistryHelper.ValueExists 失败: " + ex.Message); }
            }
            return false;
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

        // 读取统一策略：先查 64 位视图，未找到再回退 32 位视图。
        // 这样既新增了 64 位可见性（修复软件枚举漏项），又保证原本能从 32 位视图读到的条目依然能读到（不回归）。

        public static int GetDword(RegistryKey hive, string path, string name, int def = 0)
        {
            foreach (var view in BothViews)
            {
                try
                {
                    using (var root = OpenView(hive, view))
                    using (var k = root.OpenSubKey(path, false))
                    {
                        if (k == null) continue;
                        var v = k.GetValue(name);
                        if (v == null) continue;
                        return Convert.ToInt32(v);
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("RegistryHelper 读取失败: " + ex.Message); }
            }
            return def;
        }

        /// <summary>读取注册表 Dword 并判断是否等于 onValue（用于 Tweaks 等开关状态查询）。键/值不存在或读取异常均返回 false。</summary>
        public static bool GetDwordState(RegistryKey hive, string subPath, string name, int onValue)
        {
            foreach (var view in BothViews)
            {
                try
                {
                    using (var root = OpenView(hive, view))
                    using (var k = root.OpenSubKey(subPath))
                    {
                        if (k == null) continue;
                        var v = k.GetValue(name);
                        if (v == null) continue;
                        // 统一用 Convert.ToInt32：原实现 `is int` 对 REG_SZ "1" 判不出，与 GetDword 行为不一致
                        return Convert.ToInt32(v) == onValue;
                    }
                }
                catch { }
            }
            return false;
        }

        public static string GetSz(RegistryKey hive, string path, string name, string def = "")
        {
            foreach (var view in BothViews)
            {
                try
                {
                    using (var root = OpenView(hive, view))
                    using (var k = root.OpenSubKey(path, false))
                    {
                        if (k == null) continue;
                        var v = k.GetValue(name);
                        if (v == null) continue;
                        return v.ToString();
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("RegistryHelper 读取失败: " + ex.Message); }
            }
            return def;
        }

        public static bool ClsIdDefaultEmpty(RegistryKey hive, string path)
        {
            foreach (var view in BothViews)
            {
                try
                {
                    using (var root = OpenView(hive, view))
                    using (var k = root.OpenSubKey(path, false))
                    {
                        if (k == null) continue;
                        var v = k.GetValue("");
                        if (v == null) continue;
                        return v is string && ((string)v) == "";
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("RegistryHelper.ClsIdDefaultEmpty 失败: " + ex.Message); }
            }
            return false;
        }

        public static byte[] GetBinary(RegistryKey hive, string path, string name)
        {
            foreach (var view in BothViews)
            {
                try
                {
                    using (var root = OpenView(hive, view))
                    using (var k = root.OpenSubKey(path, false))
                    {
                        if (k == null) continue;
                        var v = k.GetValue(name);
                        if (v == null) continue;
                        return v as byte[];
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("RegistryHelper 读取失败: " + ex.Message); }
            }
            return null;
        }

        public static void SetBinary(RegistryKey hive, string path, string name, byte[] data, Action<string> log)
        {
            try
            {
                // 显式写 64 位视图，避免被 WOW64 重定向
                using (var root = OpenView(hive, RegistryView.Registry64))
                using (var k = root.CreateSubKey(path, true))
                {
                    if (k == null) { log?.Invoke("  [!] 无法打开(写) " + path); return; }
                    k.SetValue(name, data, RegistryValueKind.Binary);
                }
            }
            catch (Exception ex) { log?.Invoke("  [!] 写 " + path + "\\" + name + " 失败: " + ex.Message); }
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

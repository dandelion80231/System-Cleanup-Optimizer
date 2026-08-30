using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// Defender 管理：5 个独立开关 + 一键禁用/恢复。
    /// 双路同步：Set-MpPreference (Defender runtime Preferences) + 注册表 (Policies)，
    /// 否则安全中心 UI 会显示"此设置由管理员进行管理"+ 关，导致 Get-MpPreference 返回旧值、UI 联动失败。
    /// 立即生效、不需要重启、不需要 TI 提权、不动服务/驱动。
    /// TP 开启时 Set-MpPreference 可能被拦（Preferences 受 TP 保护），但 Policies 路径不受 TP 保护。
    /// </summary>
    public static class Defender
    {
        // 5 个 Policies 注册表路径（与 Set-MpPreference 一一对应）
        private const string DEFENDER_POLICY = @"SOFTWARE\Policies\Microsoft\Windows Defender";
        private const string DEFENDER_RT_POLICY = @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection";
        private const string SPYNET_POLICY = @"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet";
        private const string DEFENDER_FEATURES = @"SOFTWARE\Microsoft\Windows Defender\Features";
        // MDM PolicyManager 路径（之前复杂版写过 Allow*=0，简化版未清 → 安全中心"管理员管理"的真正来源）
        private const string MDM_POLICY = @"SOFTWARE\Microsoft\PolicyManager\default\Defender";

        private static readonly string[] MDM_ALLOW_NAMES =
        {
            "AllowRealtimeMonitoring", "AllowBehaviorMonitoring", "AllowIOAVProtection",
            "AllowOnAccessProtection", "AllowCloudProtection", "AllowSampleSharing",
            "AllowTamperProtection", "AllowArchiveScanning", "AllowScanningNetworkFiles",
            "AllowFullScanRealtimeProtection", "AllowScriptScanning"
        };

        // ===================== 缓存：避免 BuildSecurity 启动 9 次 PowerShell =====================
        // 5 个 Get-MpPreference 值一次性取回缓存，Get* 全部读内存字段（O(1)），
        // 不再每次访问触发 powershell.exe 子进程。
        private static volatile int _cacheRealtime = 0, _cacheBehavior = 0, _cacheCloud = 2, _cacheSample = 1, _cacheTamper = 5;
        private static volatile bool _cacheValid = false;
        private static readonly object _cacheLock = new object();

        /// <summary>一次 PowerShell 调用拿全部 5 个值，写入缓存。BuildSecurity 入口 + onDone 回调各调一次。</summary>
        public static void RefreshStatusCache()
        {
            lock (_cacheLock)
            {
                try
                {
                    // PowerShell 用 -f 把 5 个值格式化成 pipe 分隔字符串，避开 ConvertTo-Json 的引号转义问题
                    var s = Exec.RunPowerShellGet(
                        "$ErrorActionPreference='SilentlyContinue'; " +
                        "$p = Get-MpPreference; " +
                        "'{0}|{1}|{2}|{3}|{4}' -f " +
                        "[int]$p.DisableRealtimeMonitoring, " +
                        "[int]$p.DisableBehaviorMonitoring, " +
                        "[int]$p.MAPSReporting, " +
                        "[int]$p.SubmitSamplesConsent, " +
                        "[int]$p.TamperProtection", null);
                    var t = s.Trim();
                    var parts = t.Split('|');
                    if (parts.Length >= 5)
                    {
                        if (int.TryParse(parts[0], out int r)) _cacheRealtime = r;
                        if (int.TryParse(parts[1], out int b)) _cacheBehavior = b;
                        if (int.TryParse(parts[2], out int c)) _cacheCloud = c;
                        if (int.TryParse(parts[3], out int sm)) _cacheSample = sm;
                        if (int.TryParse(parts[4], out int tm)) _cacheTamper = tm;
                        _cacheValid = true;
                    }
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  _cacheValid = false; }
            }
        }

        private static void EnsureCache()
        {
            lock (_cacheLock)
            {
                if (!_cacheValid) RefreshStatusCache();
            }
        }

        public static bool LastOperationFullSuccess { get; private set; } = true;

        /// <summary>4 个核心保护项都关 = 整体禁用（按 Get* 实际状态判断，不再依赖没设的 DisableAntiSpyware 键）。</summary>
        public static bool IsDisabled()
        {
            try
            {
                return !GetRealtime() && !GetBehavior() && !GetCloud() && !GetSampleSubmit();
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

        // ===================== 底层：Get-MpPreference / Set-MpPreference =====================

        // (ReadMpPref 已删除：无调用方，GetXxx 现已直接读注册表/Preference；SetXxx 同步刷新缓存字段)

        /// <summary>
        /// 读 Policies 注册表值（无值返回 null）。
        /// ★ 必须走 RegistryHelper：本程序是 32 位进程，裸 Registry.LocalMachine 只会读 32 位视图
        /// （Wow6432Node），而 Policies 的写入已统一写 64 位视图，直读会导致"设置已生效但状态读不到"。
        /// </summary>
        private static int? ReadPolicyDword(string keyPath, string valName)
        {
            return RegistryHelper.GetDwordOrNull(Registry.LocalMachine, keyPath, valName);
        }

        private static bool SetBool(string label, string cmdletParam, bool enable, Action<string> log)
        {
            log("[API] " + label + " -> " + (enable ? "启用" : "禁用") + "...");
            int r = Exec.RunPowerShell("Set-MpPreference -" + cmdletParam + " $" + enable, log);
            if (r != 0) log("   [!] Set-MpPreference 退出 " + r + "（可能被 TP 拦截）");
            else log("   [OK] Preferences 已更新");
            return r == 0;
        }

        private static bool SetInt(string label, string cmdletParam, int val, Action<string> log)
        {
            log("[API] " + label + " -> " + val + "...");
            int r = Exec.RunPowerShell("Set-MpPreference -" + cmdletParam + " " + val, log);
            if (r != 0) log("   [!] Set-MpPreference 退出 " + r + "（可能被 TP 拦截）");
            else log("   [OK] Preferences 已更新");
            return r == 0;
        }

        /// <summary>
        /// 精确删除本工具自己写入的那一个策略值（恢复/启用时使用）。
        /// ★ 修复：旧代码恢复时调用 DeleteKeyTree 删除整个 Real-Time Protection / Spynet 键，
        ///   会连带清掉本工具从未写过的其它值（DisableIOAVProtection、DisableScriptScanning、
        ///   SpynetReporting、SubmitSamplesConsent 等），破坏用户既有策略且回滚不完整。
        ///   这里只删指定值名，键与其它的值原样保留。键/值不存在视为无需清理（返回 true）。
        ///   删除走 RegistryHelper.DeleteValueChecked，会同时清理 64/32 两个视图，
        ///   避免"写入在 64 位视图、恢复只删 32 位视图"导致策略残留、Defender 卡在禁用状态。
        /// </summary>
        private static bool DeletePolicyValue(string keyPath, string valName, Action<string> log)
        {
            return RegistryHelper.DeleteValueChecked(Registry.LocalMachine, keyPath, valName, log);
        }

        // ===================== 5 个独立设置（Get/Set，双路同步） =====================

        /// <summary>1. 实时保护（含"开发人员驱动的保护"——Dev Drive 保护是其实时保护子集）。</summary>
        public static bool GetRealtime()
        {
            var policy = ReadPolicyDword(DEFENDER_RT_POLICY, "DisableRealtimeMonitoring");
            if (policy.HasValue) return policy.Value == 0;
            EnsureCache(); return _cacheRealtime == 0;
        }
        public static bool SetRealtime(bool enable, Action<string> log)
        {
            bool ok = SetBool("实时保护", "DisableRealtimeMonitoring", enable, log);
            if (ok) { _cacheRealtime = enable ? 0 : 1; _cacheValid = true; }
            try
            {
                bool policyOk;
                // 恢复时只删本工具写过的 DisableRealtimeMonitoring，不删整个 Real-Time Protection 键
                if (enable) policyOk = DeletePolicyValue(DEFENDER_RT_POLICY, "DisableRealtimeMonitoring", log);
                else { RegistryHelper.SetDword(Registry.LocalMachine, DEFENDER_RT_POLICY, "DisableRealtimeMonitoring", 1, log); policyOk = true; }
                log(policyOk ? "   [OK] Policies 已" + (enable ? "清理（仅删本工具写入的值）" : "写入") : "   [!!] Policies 失败，安全中心可能仍显示陈旧状态");
            }
            catch (Exception ex) { log("   [!!] Policies 失败: " + ex.Message); }
            return ok;
        }

        /// <summary>2. 行为监控（不受 TP 保护）。</summary>
        public static bool GetBehavior()
        {
            var policy = ReadPolicyDword(DEFENDER_RT_POLICY, "DisableBehaviorMonitoring");
            if (policy.HasValue) return policy.Value == 0;
            EnsureCache(); return _cacheBehavior == 0;
        }
        public static bool SetBehavior(bool enable, Action<string> log)
        {
            bool ok = SetBool("行为监控", "DisableBehaviorMonitoring", enable, log);
            if (ok) { _cacheBehavior = enable ? 0 : 1; _cacheValid = true; }
            try
            {
                bool policyOk;
                // 恢复时只删本工具写过的 DisableBehaviorMonitoring，不删整个 Real-Time Protection 键
                if (enable) policyOk = DeletePolicyValue(DEFENDER_RT_POLICY, "DisableBehaviorMonitoring", log);
                else { RegistryHelper.SetDword(Registry.LocalMachine, DEFENDER_RT_POLICY, "DisableBehaviorMonitoring", 1, log); policyOk = true; }
                log(policyOk ? "   [OK] Policies 已" + (enable ? "清理" : "写入") : "   [!!] Policies 失败");
            }
            catch (Exception ex) { log("   [!!] Policies 失败: " + ex.Message); }
            return ok;
        }

        /// <summary>3. 云提供的保护 (MAPSReporting: 0=Disabled, 1=Basic, 2=Advanced)。</summary>
        public static bool GetCloud()
        {
            var policy = ReadPolicyDword(SPYNET_POLICY, "SpynetReporting");
            if (policy.HasValue) return policy.Value != 0;  // 0=管理员强制关；1/2=启用
            EnsureCache(); return _cacheCloud > 0;
        }
        public static bool SetCloud(bool enable, Action<string> log)
        {
            bool ok = SetInt("云保护", "MAPSReporting", enable ? 2 : 0, log);
            if (ok) { _cacheCloud = enable ? 2 : 0; _cacheValid = true; }
            try
            {
                bool policyOk;
                if (enable)
                {
                    // 只删本工具写过的 SpynetReporting，保留用户/其它程序的 SubmitSamplesConsent 等值
                    policyOk = DeletePolicyValue(SPYNET_POLICY, "SpynetReporting", log);
                    log(policyOk ? "   [OK] Policies\\Spynet\\SpynetReporting 已删（解除管理员管理）" : "   [!!] Policies\\Spynet 删值失败，安全中心仍显示\"由管理员管理+关\"。需 TI 提权。");
                }
                else
                {
                    RegistryHelper.SetDword(Registry.LocalMachine, SPYNET_POLICY, "SpynetReporting", 0, log);
                    policyOk = true;
                    log("   [OK] Policies\\Spynet\\SpynetReporting=0");
                }
            }
            catch (Exception ex) { log("   [!!] Policies 失败: " + ex.Message); }
            return ok;
        }

        /// <summary>4. 自动提交样本 (SubmitSamplesConsent: 1=SendSafeSamples, 2=NeverSend)。</summary>
        public static bool GetSampleSubmit()
        {
            var policy = ReadPolicyDword(SPYNET_POLICY, "SubmitSamplesConsent");
            if (policy.HasValue) return policy.Value == 1 || policy.Value == 3;
            EnsureCache(); return _cacheSample == 1 || _cacheSample == 3;
        }
        public static bool SetSampleSubmit(bool enable, Action<string> log)
        {
            bool ok = SetInt("样本提交", "SubmitSamplesConsent", enable ? 1 : 2, log);
            if (ok) { _cacheSample = enable ? 1 : 2; _cacheValid = true; }
            try
            {
                bool policyOk;
                if (enable)
                {
                    // 只删本工具写过的 SubmitSamplesConsent，保留用户/其它程序的 SpynetReporting 等值
                    policyOk = DeletePolicyValue(SPYNET_POLICY, "SubmitSamplesConsent", log);
                    log(policyOk ? "   [OK] Policies\\Spynet\\SubmitSamplesConsent 已删（解除管理员管理）" : "   [!!] Policies\\Spynet 删值失败，安全中心仍显示\"由管理员管理+关\"。需 TI 提权。");
                }
                else
                {
                    RegistryHelper.SetDword(Registry.LocalMachine, SPYNET_POLICY, "SubmitSamplesConsent", 2, log);
                    policyOk = true;
                    log("   [OK] Policies\\Spynet\\SubmitSamplesConsent=2");
                }
            }
            catch (Exception ex) { log("   [!!] Policies 失败: " + ex.Message); }
            return ok;
        }

        /// <summary>5. 篡改防护 (TamperProtection: 0/4=关, 1/5=开)。受自己保护。Features 键受 TP 保护。Policies 没这键（TP 只能用 Features 路径）。</summary>
        public static bool GetTamper()
        {
            EnsureCache(); return _cacheTamper == 1 || _cacheTamper == 5;
        }
        public static bool SetTamper(bool enable, Action<string> log)
        {
            // Features 键受 TP 保护（TP 开时 Set-MpPreference 也会拦）——只能尽力
            return SetInt("篡改防护", "TamperProtection", enable ? 5 : 4, log);
        }

        // ===================== 一键禁用/恢复（批量调用前 4 个，不含 TP） =====================

        /// <summary>清理策略残留（解除"此设置由管理员进行管理"）。
        /// 三个来源：① GP 根键 Disable* 值 ② MDM PolicyManager Allow* 值（最常见残留）③ GP 子键删除。
        /// MDM/GP 值 admin 可写可删（有 KEY_SET_VALUE），只有删 GP 子键受 DACL 限制——值清掉即够，键删不掉可接受。</summary>
        public static int ClearAllPolicies(Action<string> log)
        {
            log("=== 清理策略残留（解除管理员管理）===");
            int ok = 0, total = 0;

            // 1. GP 根键 Disable* 值（DisableAntiSpyware=1 等会触发"管理员管理"标记）
            //    统一走 RegistryHelper：双视图清理，避免只清 32 位视图导致策略残留
            total++;
            try
            {
                if (!RegistryHelper.KeyExists(Registry.LocalMachine, DEFENDER_POLICY))
                {
                    log("   [-] GP 根键不存在: " + DEFENDER_POLICY);
                }
                else
                {
                    int cleared = 0;
                    foreach (var name in new[] { "DisableAntiSpyware", "DisableAntiVirus" })
                        if (RegistryHelper.ValueExists(Registry.LocalMachine, DEFENDER_POLICY, name)
                            && RegistryHelper.DeleteValueChecked(Registry.LocalMachine, DEFENDER_POLICY, name, log))
                            cleared++;
                    if (cleared > 0) { log("   [OK] 删 GP 根键 " + cleared + " 个 Disable* 值"); ok++; }
                    else log("   [-] GP 根键无 Disable* 残留");
                }
            }
            catch (Exception ex) { log("   [!!] GP 根键: " + ex.Message); }

            // 2. MDM PolicyManager Allow* 值重置为 1（安全中心"管理员管理"最常见来源）
            //    SetDwordIfExists：在存在该值的每个视图中重置为 1，不存在则不新建
            total++;
            try
            {
                if (!RegistryHelper.KeyExists(Registry.LocalMachine, MDM_POLICY))
                {
                    log("   [-] MDM 键不存在: " + MDM_POLICY);
                }
                else
                {
                    int fixedCount = 0;
                    foreach (var name in MDM_ALLOW_NAMES)
                        if (RegistryHelper.SetDwordIfExists(Registry.LocalMachine, MDM_POLICY, name, 1, log))
                            fixedCount++;
                    if (fixedCount > 0) { log("   [OK] MDM Allow* 重置 " + fixedCount + " 项为 1（解除强制关）"); ok++; }
                    else log("   [-] MDM 无 Allow* 残留");
                }
            }
            catch (Exception ex) { log("   [!!] MDM: " + ex.Message); }

            // 3. 尝试删 GP 子键（RT_POLICY / SPYNET_POLICY / 根键）——DACL 限制下可失败，值已清即够
            foreach (var path in new[] { DEFENDER_RT_POLICY, SPYNET_POLICY, DEFENDER_POLICY })
            {
                total++;
                if (!RegistryHelper.KeyExists(Registry.LocalMachine, path))
                {
                    log("   [-] 不存在: " + path);
                    continue;
                }
                bool deleted = RegistryHelper.DeleteKeyTree(Registry.LocalMachine, path, log);
                if (deleted) { log("   [OK] 已删: " + path); ok++; }
                else { log("   [!!] 删失败: " + path + "（DACL 限制，可接受——值已清）"); }
            }

            log(ok >= total ? "=== 完成 ===" : "=== 部分成功 " + ok + "/" + total + " ===");
            log("提示：重新打开 Windows 安全中心验证（切到别的页再回来刷新）。若仍显示管理员管理，把日志发我。");
            return ok;
        }

        // ===================== 诊断：读 Get-MpComputerStatus（runtime 实际状态） =====================

        /// <summary>
        /// 诊断 Defender runtime 实际状态（不是注册表，是 Defender 服务内存中的真实状态）。
        /// AMRunningMode=Passive → Defender 不主动扫描（禁用已生效）；Normal → 完全运行（改动被 runtime 拒）。
        /// </summary>
        public static void DiagnoseRuntime(Action<string> log)
        {
            log("=== Defender Runtime 诊断（Get-MpComputerStatus）===");
            try
            {
                var s = Exec.RunPowerShellGet(
                    "$ErrorActionPreference='SilentlyContinue'; " +
                    "$s = Get-MpComputerStatus; " +
                    "'AMRunningMode=' + $s.AMRunningMode + '; RealTimeProtectionEnabled=' + $s.RealTimeProtectionEnabled + '; IsTamperProtected=' + $s.IsTamperProtected + '; AntivirusEnabled=' + $s.AntivirusEnabled", null);
                log("   " + s.Trim());
                log("   ---------- 判读 ----------");
                if (s.Contains("AMRunningMode=Passive") || s.Contains("AMRunningMode=SxS Passive"))
                    log("   ✅ AMRunningMode=Passive → Defender 不主动扫描，禁用已生效（安全中心 UI 只是陈旧）");
                else
                    log("   ⚠ AMRunningMode=Normal → Defender 仍在完全运行，改动被 runtime 自我保护拒绝");
                log("   RealTimeProtectionEnabled=False → 实时保护真关了");
                log("   RealTimeProtectionEnabled=True → 实时保护还在跑");
                log("   IsTamperProtected=True → TP 开着（自我保护拦截了 Set-MpPreference）");
            }
            catch (Exception ex) { log("   [!!] 诊断异常: " + ex.Message); }
        }

        public static void Disable(Action<string> log)
        {
            log("=== 一键禁用 Defender（前 4 项，不含 TP）===");
            int ok = 0, fail = 0;
            if (SetRealtime(false, log)) ok++; else fail++;
            if (SetBehavior(false, log)) ok++; else fail++;
            if (SetCloud(false, log)) ok++; else fail++;
            if (SetSampleSubmit(false, log)) ok++; else fail++;
            LastOperationFullSuccess = (fail == 0);
            log("=== 完成: " + ok + " 成功, " + fail + " 失败（被 TP 拦截的需先关篡改防护） ===");
        }

        public static void Enable(Action<string> log)
        {
            log("=== 一键恢复 Defender（前 4 项，不含 TP）===");
            // 先清 Policies 残留（解决"管理员管理"问题）——幂等：清完再写新的
            ClearAllPolicies(log);
            log("");
            int ok = 0, fail = 0;
            if (SetRealtime(true, log)) ok++; else fail++;
            if (SetBehavior(true, log)) ok++; else fail++;
            if (SetCloud(true, log)) ok++; else fail++;
            if (SetSampleSubmit(true, log)) ok++; else fail++;
            LastOperationFullSuccess = (fail == 0);
            log("=== 完成: " + ok + " 成功, " + fail + " 失败（被 TP 拦截的需先关篡改防护） ===");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        // ClearAllPolicies 退路清理：删 GP 子键被 DACL 限制失败时，逐个删这些本工具可能写入的 Disable*/Spynet 值
        private static readonly string[] RT_DISABLE_VALUE_NAMES =
        {
            "DisableRealtimeMonitoring", "DisableBehaviorMonitoring", "DisableIOAVProtection",
            "DisableOnAccessProtection", "DisableScanOnRealtimeEnable"
        };
        private static readonly string[] SPYNET_VALUE_NAMES =
        {
            "SpynetReporting", "SubmitSamplesConsent"
        };

        // ===================== 缓存：避免 BuildSecurity 启动 9 次 PowerShell =====================
        // 5 个 Get-MpPreference 值一次性取回缓存，Get* 全部读内存字段（O(1)），
        // 不再每次访问触发 powershell.exe 子进程。
        private static volatile int _cacheRealtime = 0, _cacheBehavior = 0, _cacheCloud = 2, _cacheSample = 1;
        private static volatile bool _cacheValid = false;
        private static readonly object _cacheLock = new object();

        /// <summary>一次 PowerShell 调用拿全部 4 个值，写入缓存。BuildSecurity 入口 + onDone 回调各调一次。
        /// 注：TP 不走 Get-MpPreference（该字段本机实测常为空），改由 IsTamperProtectionEnabled() 读 Get-MpComputerStatus。</summary>
        public static void RefreshStatusCache()
        {
            lock (_cacheLock)
            {
                try
                {
                    // PowerShell 用 -f 把 4 个值格式化成 pipe 分隔字符串，避开 ConvertTo-Json 的引号转义问题
                    var s = Exec.RunPowerShellGet(
                        "$ErrorActionPreference='SilentlyContinue'; " +
                        "$p = Get-MpPreference; " +
                        "'{0}|{1}|{2}|{3}' -f " +
                        "[int]$p.DisableRealtimeMonitoring, " +
                        "[int]$p.DisableBehaviorMonitoring, " +
                        "[int]$p.MAPSReporting, " +
                        "[int]$p.SubmitSamplesConsent", null);
                    var t = s.Trim();
                    var parts = t.Split('|');
                    if (parts.Length >= 4)
                    {
                        if (int.TryParse(parts[0], out int r)) _cacheRealtime = r;
                        if (int.TryParse(parts[1], out int b)) _cacheBehavior = b;
                        if (int.TryParse(parts[2], out int c)) _cacheCloud = c;
                        if (int.TryParse(parts[3], out int sm)) _cacheSample = sm;
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

        /// <summary>本会话内是否已用 PowerShell 刷新过状态缓存（O(1) 判断，供 UI 决定"先同步填缓存 / 后台再刷新"）。
        /// false = 首次进页或刷新失败，此时 Get* 会触发同步刷新；true = 缓存已是最新或上次成功。</summary>
        public static bool CacheValid => _cacheValid;

        // Prefs 注册表真实键（Get-MpPreference 的 runtime 底层值）：本机实测存在且 O(1) 可读，无需 PowerShell。
        // 用途：首次进页时同步读这两项给首屏初值，让"实时保护已禁用"立刻显示，再后台 PowerShell 校正全部 4 值。
        private const string PREFS_RT_BASE = @"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection";
        private const string PREFS_SPYNET = @"SOFTWARE\Microsoft\Windows Defender\Spynet";

        /// <summary>用注册表 Prefs 同步"种子"缓存（O(1)，UI 线程可安全调用）：
        /// 读 Real-Time Protection\DisableRealtimeMonitoring / DisableBehaviorMonitoring（1=禁用）
        /// 与 Spynet\SpynetReporting / SubmitSamplesConsent（与 runtime 实测一致：SpynetReporting 0=关 1/2=启用，
        /// SubmitSamplesConsent 1=启用 2/0=关闭）给全部 4 值首屏初值。
        /// 种子成功后 _cacheValid=true，使 Get* 读缓存而不再同步 spawn PowerShell；首次进页"实时保护已禁用"即可立即渲染。
        /// 仅当 Prefs 实时/行为两条都读不到（Defender 从未被改过、值全走 runtime 默认）时返回 false，调用方退回后台加载。</summary>
        public static bool SeedCacheFromPrefs()
        {
            lock (_cacheLock)
            {
                bool rtFound = TryReadPrefsDword(PREFS_RT_BASE, "DisableRealtimeMonitoring", out int rtVal);
                bool behFound = TryReadPrefsDword(PREFS_RT_BASE, "DisableBehaviorMonitoring", out int behVal);
                if (!rtFound && !behFound)
                    return false; // 实时/行为 Prefs 都读不到 → 无种子价值，走后台 PowerShell
                if (rtFound) _cacheRealtime = rtVal;          // 1=禁用 0=启用
                if (behFound) _cacheBehavior = behVal;
                // 云保护：SpynetReporting 0=Disabled 1=Basic 2=Advanced（与 runtime MAPSReporting 语义一致）
                if (TryReadPrefsDword(PREFS_SPYNET, "SpynetReporting", out int cloudVal))
                    _cacheCloud = cloudVal;
                // 样本提交：SubmitSamplesConsent 1=启用 其余=关闭（GetSampleSubmit 判 ==1）
                if (TryReadPrefsDword(PREFS_SPYNET, "SubmitSamplesConsent", out int sampleVal))
                    _cacheSample = sampleVal;
                _cacheValid = true;
                return true;
            }
        }

        private static bool TryReadPrefsDword(string keyPath, string valueName, out int val)
        {
            val = 0;
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(keyPath);
                if (k == null) return false;
                object o = k.GetValue(valueName);
                if (o is int i) { val = i; return true; }
                if (o is byte[] b && b.Length >= 4) { val = BitConverter.ToInt32(b, 0); return true; }
                return false;
            }
            catch (Exception ex) { DebugLog.Ignore(ex); return false; }
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

        // (ReadMpPref / ReadPolicyDword 已删除：Get* 现直接读 Get-MpPreference 缓存，无注册表 Policy 优先逻辑，故无调用方)

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
            // ponytail: 以 runtime 真实状态为准（Get-MpPreference 缓存），不再优先读 Policies 注册表——
            // TP 开启时 Policies 残留值(=1)被 Defender 无视，据此判"已禁用"会与 runtime 实际"在跑"脱节，造成假象。
            EnsureCache();
            return _cacheRealtime == 0;
        }
        public static bool SetRealtime(bool enable, Action<string> log)
        {
            bool ok = SetInt("实时保护", "DisableRealtimeMonitoring", enable ? 0 : 1, log);
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
            EnsureCache();
            return _cacheBehavior == 0;
        }
        public static bool SetBehavior(bool enable, Action<string> log)
        {
            bool ok = SetInt("行为监控", "DisableBehaviorMonitoring", enable ? 0 : 1, log);
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
            EnsureCache();
            return _cacheCloud > 0;   // 0=管理员强制关；1/2=启用
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
            EnsureCache();
            return _cacheSample == 1 || _cacheSample == 3;
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
        // ===================== 篡改防护(TP)：只读，不可经脚本改 =====================
        // Windows 11 下 TP 开启时仅 Windows 安全中心 GUI 能切换；任何脚本/工具/注册表写都被 TP 拦截。
        // 因此本工具不提供 SetTamper——只提供 IsTamperProtectionEnabled() 读 + OpenSecurityCenter() 跳转手动开关。

        /// <summary>读 Get-MpComputerStatus.IsTamperProtected（runtime 真实状态，与诊断一致）。
        /// 不要用 Get-MpPreference.TamperProtection 判断——本机实测该字段为空，会误判 TP 关闭，导致禁用操作漏报。</summary>
        public static bool IsTamperProtectionEnabled() => IsTamperProtectionEnabled(out _);

        /// <summary>Q13：区分「TP 确实开启」与「查询失败/无法确定」。known=false 表示无法确认（保守仍视为开启，避免漏报）。</summary>
        public static bool IsTamperProtectionEnabled(out bool known)
        {
            known = true;
            try
            {
                var s = Exec.RunPowerShellGet(
                    "$ErrorActionPreference='SilentlyContinue'; " +
                    "if ((Get-MpComputerStatus).IsTamperProtected) { '1' } else { '0' }", null);
                s = (s ?? "").Trim();
                if (s == "1") return true;
                if (s == "0") return false;
                known = false; // 既非 '1' 也非 '0'：Get-MpComputerStatus 查询失败/无结果
            }
            catch (Exception ex) { known = false; DebugLog.Ignore(ex); }
            // 解析失败回退：默认视为开启（保守——让用户先手动关 TP 再操作，避免漏报）
            return true;
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

            // 3. 尝试删 GP 子键（RT_POLICY / SPYNET_POLICY / 根键）——DACL 限制下删键会失败，
            //    此时退路逐个删具体 Disable*/Spynet 值，避免残留遗留（实测本机会遗留 DisableRealtimeMonitoring=1 等死值）。
            foreach (var path in new[] { DEFENDER_RT_POLICY, SPYNET_POLICY, DEFENDER_POLICY })
            {
                total++;
                if (!RegistryHelper.KeyExists(Registry.LocalMachine, path))
                {
                    log("   [-] 不存在: " + path);
                    continue;
                }
                bool deleted = RegistryHelper.DeleteKeyTree(Registry.LocalMachine, path, log);
                if (deleted) { log("   [OK] 已删: " + path); ok++; continue; }
                // ponytail: 删键被 DACL 限制失败 → 不放弃，退路清具体值（死值残留会骗 UI 显示"已禁用"）
                log("   [!] 删子键失败（DACL 限制），退路清理具体值…");
                var names = path == SPYNET_POLICY ? SPYNET_VALUE_NAMES
                          : path == DEFENDER_POLICY ? new[] { "DisableAntiSpyware", "DisableAntiVirus" }
                          : RT_DISABLE_VALUE_NAMES;
                int cleared = 0;
                foreach (var name in names)
                    if (RegistryHelper.DeleteValueChecked(Registry.LocalMachine, path, name, log))
                        cleared++;
                if (cleared > 0) { log("   [OK] 退路清掉 " + cleared + " 个值"); ok++; }
                else log("   [!!] 无具体值可清（可能已被其它程序占用 DACL）");
            }

            log(ok >= total ? "=== 完成 ===" : "=== 部分成功 " + ok + "/" + total + " ===");
            log("提示：重新打开 Windows 安全中心验证（切到别的页再回来刷新）。若仍显示管理员管理，把日志发我。");
            return ok;
        }

        // ===================== 临时禁用/启用（03/04 逻辑，仅 Set-MpPreference，不写 Policies） =====================
        // 定位：与「一键禁用/恢复」并列的轻量路径——只动 Defender runtime Preferences，
        // 不碰注册表 Policies、不触发 ClearAllPolicies，适合"临时让位给某安装程序"场景。
        // TP 开启时 Set-MpPreference 会被拦，故入口先查 TP；TP 开则提示 + 跳转安全中心，不执行。

        /// <summary>临时禁用：实时保护=关、云保护=关、样本=不提交。不动 Policies 注册表。TP 开则提示并跳转，不执行。</summary>
        public static void TemporaryDisable(Action<string> log)
        {
            log("=== 临时禁用 Defender（仅 Preferences，不动 Policies）===");
              if (IsTamperProtectionEnabled(out bool tpKnown))
              {
                  if (tpKnown)
                  {
                      log("   ⚠ 篡改防护(TP)已开启：Windows 会拦截 Set-MpPreference，临时禁用不会真正生效。");
                      log("   请先在「Windows 安全中心 → 病毒和威胁防护 → 管理设置」关闭「篡改防护」，再重试。");
                  }
                  else
                  {
                      log("   ⚠ 无法确认篡改防护(TP)状态（Get-MpComputerStatus 查询失败/无结果）：保守起见先暂停，请人工在安全中心确认 TP 已关闭后重试。");
                  }
                  log("   正在跳转到安全中心设置页…");
                  OpenSecurityCenter();
                  return;
              }
            int ok = 0, fail = 0;
            // 临时禁用走"直接 Set-MpPreference"（不写 Policies），与一键禁用（双路同步）区分
            if (SetPrefOnly("实时保护", "DisableRealtimeMonitoring", 1, log)) ok++; else fail++;
            if (SetPrefOnly("行为监控", "DisableBehaviorMonitoring", 1, log)) ok++; else fail++;
            if (SetPrefOnly("云保护", "MAPSReporting", 0, log)) ok++; else fail++;
            if (SetPrefOnly("样本提交", "SubmitSamplesConsent", 2, log)) ok++; else fail++;
            LastOperationFullSuccess = (fail == 0);
            log("=== 临时禁用完成: " + ok + " 成功, " + fail + " 失败 ===");
            log("   提示：Windows 重启后 Defender 配置会被还原，临时禁用不需手动恢复。");
        }

        /// <summary>临时恢复：实时保护=开、云保护=开、样本=发送安全样本。仅 Preferences。TP 开则提示并跳转。</summary>
        public static void TemporaryEnable(Action<string> log)
        {
            log("=== 临时恢复 Defender（仅 Preferences，不动 Policies）===");
            if (IsTamperProtectionEnabled())
            {
                log("   ⚠ 篡改防护(TP)已开启：Windows 会拦截 Set-MpPreference，临时恢复不会真正生效。");
                log("   请先在「Windows 安全中心 → 病毒和威胁防护 → 管理设置」关闭「篡改防护」，再重试。");
                log("   正在跳转到安全中心设置页…");
                OpenSecurityCenter();
                return;
            }
            int ok = 0, fail = 0;
            if (SetPrefOnly("实时保护", "DisableRealtimeMonitoring", 0, log)) ok++; else fail++;
            if (SetPrefOnly("行为监控", "DisableBehaviorMonitoring", 0, log)) ok++; else fail++;
            if (SetPrefOnly("云保护", "MAPSReporting", 2, log)) ok++; else fail++;
            if (SetPrefOnly("样本提交", "SubmitSamplesConsent", 1, log)) ok++; else fail++;
            LastOperationFullSuccess = (fail == 0);
            log("=== 临时恢复完成: " + ok + " 成功, " + fail + " 失败 ===");
        }

        /// <summary>只写 Set-MpPreference（runtime Preferences），不碰 Policies 注册表。供临时禁用/恢复使用。</summary>
        private static bool SetPrefOnly(string label, string cmdletParam, int val, Action<string> log)
        {
            log("[API] " + label + " -> " + val + " (Preferences only)…");
            int r = Exec.RunPowerShell("Set-MpPreference -" + cmdletParam + " " + val, log);
            if (r != 0) log("   [!] Set-MpPreference 退出 " + r + "（可能被 TP 拦截）");
            else log("   [OK] Preferences 已更新");
            if (r == 0)
            {
                // 同步内存缓存，避免后续 Get* 读到旧值
                switch (cmdletParam)
                {
                    case "DisableRealtimeMonitoring": _cacheRealtime = val; break;
                    case "DisableBehaviorMonitoring": _cacheBehavior = val; break;
                    case "MAPSReporting": _cacheCloud = val; break;
                    case "SubmitSamplesConsent": _cacheSample = val; break;
                }
                _cacheValid = true;
            }
            return r == 0;
        }

        // ===================== 安全中心跳转（06 逻辑：直达 病毒和威胁防护 → 管理设置 页） =====================

        /// <summary>直达 Windows 安全中心「病毒和威胁防护 → 管理设置」页（TP 开关所在页，截图页）。
        /// 三层跳转（按可靠性排序）：
        ///   1. 主路径 windowsdefender://threatsettings（系统内置协议 + 子路径，Win10 1903+/Win11 通用，
        ///      直达管理设置页；Win+R 实测可用）。HKCU\Software\Classes\windowsdefender 注册存在性预检通过即用。
        ///   2. 静默 fallback ms-settings:windows-security-center（系统设置框架，主路径协议未注册/SecHealthUI 包缺失时兜底；
        ///      只到安全中心主入口不到管理设置子页，但不弹"需要新应用"）。
        ///   3. TODO（暂不实现）：ms-windows-defender:VirusThreatProtectionSettings——Win11 官方原生支持的 UWP 协议，
        ///      新装正常系统可用且能直达管理设置页；本机实测弹"需要新应用"是 SecHealthUI app 包被精简/损坏所致
        ///      （winhelponline：windowsdefender: 与 ms-windows-defender: 都依赖 SecHealthUI 包存在 + 服务已启动，
        ///      包缺失时两者都会弹错）。本机注册表实测 ms-windows-defender: 仍 FOUND，说明协议注册还在、是 app 包问题。
        ///      将来若 windowsdefender: 族被微软调整，此协议是比 ms-settings 更强的备选（直达子页）。
        /// 调用要点：
        ///   - Process.Start + UseShellExecute=true（默认）：让 Shell 原生接管协议处理；
        ///   - Task.Run 后台派单：UI 线程立刻返回，不卡 UI；
        ///   - 已知代价：跳转后 UWP 安全中心（SecHealthUI）冷启动渲染约 0.8-1.2s 期间桌面会"闪黑"——
        ///     Windows 系统固有行为（ms-settings 走同一宿主同样闪黑），代码层无法消除。
        /// </summary>
        public static void OpenSecurityCenter()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                // 预检：windowsdefender: 协议注册存在性（HKCU\Software\Classes\windowsdefender）
                bool wdOk = false;
                try
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\windowsdefender"))
                        wdOk = (k != null);
                }
                catch (Exception ex) { DebugLog.Ignore(ex); }

                if (wdOk)
                {
                    // 主路径：windowsdefender://threatsettings 直达管理设置页
                    try { Process.Start("explorer.exe", "windowsdefender://threatsettings"); return; }
                    catch (Exception ex) { DebugLog.Ignore(ex); }
                }
                // 静默 fallback：ms-settings:windows-security-center（主路径协议未注册或调用失败时兜底）
                try { Process.Start("explorer.exe", "ms-settings:windows-security-center"); }
                catch (Exception ex) { DebugLog.Ignore(ex); }
                // TODO：将来评估 ms-windows-defender:VirusThreatProtectionSettings（Win11 原生，直达子页；
                //       本机因 SecHealthUI 包精简弹错，新装正常系统可用）
            });
        }

        // ===================== 注册表快照：备份 / 列取 / 回滚（02/07 逻辑） =====================
        // 定位：改 Policies 前先 reg export 两个服务键 + Defender 策略键做快照；出问题 reg import 回滚。
        // 快照文件统一放 cpq-tool\安全防护\regbackup\（安全防护页自己的数据，不属于 Office 部署），文件名带时间戳（BEFORE/AFTER 配对）。

        /// <summary>快照目录：cpq-tool\安全防护\regbackup\（AppPaths.RegBackupDir）。</summary>
        private static string SnapDir()
        {
            var d = AppPaths.RegBackupDir;
            Directory.CreateDirectory(d);
            return d;
        }

        /// <summary>要纳入快照的 HKLM 键（含 Services 两键 + Defender 策略三键）。</summary>
        private static readonly string[] SNAP_KEYS =
        {
            @"SYSTEM\CurrentControlSet\Services\SecurityHealthService",
            @"SYSTEM\CurrentControlSet\Services\wscsvc",
            @"SOFTWARE\Policies\Microsoft\Windows Defender",
            @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection",
            @"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet",
            @"SOFTWARE\Microsoft\Windows Defender\Features"
        };

        private static string SnapStamp() => DateTime.Now.ToString("yyyyMMdd_HHmmss");

        /// <summary>改动前备份：reg export 各快照键 → 安全防护\regbackup\<tag>_<stamp>_<key>.reg。返回快照目录路径（空串=失败）。</summary>
        public static string BackupSnapshots(string tag, Action<string> log)
        {
            log("=== 备份注册表快照（" + tag + "）===");
            string dir;
            try { dir = SnapDir(); }
            catch (Exception ex) { log("   [!!] 无法创建快照目录: " + ex.Message); return ""; }
            string stamp = SnapStamp();
            int ok = 0;
            foreach (var key in SNAP_KEYS)
            {
                // 键不存在 reg export 会报错，跳过（只备份实际存在的键）
                if (!RegistryHelper.KeyExists(Registry.LocalMachine, key))
                {
                    log("   [-] 键不存在，跳过: " + key);
                    continue;
                }
                string file = Path.Combine(dir, tag + "_" + stamp + "_" + key.Replace('\\', '_') + ".reg");
                int r = Exec.RunCmd(new[] { "reg", "export", "HKEY_LOCAL_MACHINE\\" + key, file, "/y" }, log, false);
                if (r == 0) { ok++; log("   [OK] 已备份: " + Path.GetFileName(file)); }
                else log("   [!!] 备份失败(" + r + "): " + key);
            }
            log("=== 备份完成: " + ok + "/" + SNAP_KEYS.Length + " 个键 -> " + dir + " ===");
            return ok > 0 ? dir : "";
        }

        /// <summary>列出快照目录下的 .reg 文件（按文件名倒序，新在前）。文件名含 tag+时间戳+键名。</summary>
        public static List<string> ListSnapshots()
        {
            var list = new List<string>();
            try
            {
                var dir = SnapDir();
                foreach (var f in Directory.GetFiles(dir, "*.reg"))
                    list.Add(f);
                list.Sort((a, b) => string.CompareOrdinal(b, a));
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }
            return list;
        }

        /// <summary>回滚：把指定快照文件 reg import 回注册表。调用方必须先经 UI 确认（破坏性）。</summary>
        public static bool RestoreFromSnapshot(string regFile, Action<string> log)
        {
            log("=== 回滚注册表快照: " + Path.GetFileName(regFile) + " ===");
            if (string.IsNullOrEmpty(regFile) || !File.Exists(regFile))
            {
                log("   [!!] 快照文件不存在: " + regFile);
                return false;
            }
            int r = Exec.RunCmd(new[] { "reg", "import", regFile }, log, false);
            if (r == 0)
            {
                log("   [OK] 回滚完成。若涉及服务 Start 值，请重启或重启服务后生效。");
                return true;
            }
            log("   [!!] 回滚失败，退出码 " + r + "。");
            return false;
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
                    log("   ℹ AMRunningMode=Normal → Defender 引擎在运行（实时保护是否开启看下行 RealTimeProtectionEnabled，Normal≠一定在主动扫描）");
                if (s.Contains("RealTimeProtectionEnabled=False"))
                    log("   ✅ RealTimeProtectionEnabled=False → 实时保护真关了（禁用已生效）");
                else if (s.Contains("RealTimeProtectionEnabled=True"))
                    log("   ⚠ RealTimeProtectionEnabled=True → 实时保护还在跑（改动被 runtime 拒或未生效）");
                if (s.Contains("IsTamperProtected=True"))
                    log("   ⚠ IsTamperProtected=True → TP 开着（会拦截 Set-MpPreference，需先在安全中心关 TP）");
                else if (s.Contains("IsTamperProtected=False"))
                    log("   ✅ IsTamperProtected=False → TP 已关，外部脚本可正常改 Defender");
            }
            catch (Exception ex) { log("   [!!] 诊断异常: " + ex.Message); }
        }

        public static void Disable(Action<string> log)
        {
            log("=== 一键禁用 Defender（前 4 项，不含 TP）===");
            if (IsTamperProtectionEnabled())
                log("   ⚠ 检测到篡改防护(TP)已开启：Windows 会拦截对 Defender 的运行时修改，本操作很可能无法真正禁用，仅会留下策略残留。请先在 Windows 安全中心→病毒和威胁防护→管理设置→关闭「篡改防护」，再重试。");
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
            if (IsTamperProtectionEnabled())
                log("   ⚠ 检测到篡改防护(TP)已开启：恢复(启用)通常可生效，但 UI 状态请以诊断(RealTimeProtectionEnabled)为准；若仍异常，先手动关闭 TP 再重试。");
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

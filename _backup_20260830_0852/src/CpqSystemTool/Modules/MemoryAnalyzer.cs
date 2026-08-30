using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace CpqSystemTool
{
    /// <summary>
    /// 内存分析（镜像 RAMMap 只读视图）+ 内存优化（清 Standby / 空工作集）。
    ///
    /// 只读（Tier A/B）：
    ///   - GlobalMemoryStatusEx（kernel32，文档化）：总/可用物理、内存占用%。
    ///   - GetPerformanceInfo（psapi，文档化）：已提交、提交上限、内核分页/非分页池、页大小、进程数。
    ///   - WMI Win32_PerfFormattedData_PerfOS_Memory（文档化）：拆解 Active/Standby/Modified/(Free+Zero)
    ///     以及系统缓存、提交、池使用。选择 WMI 而非未文档化的 NtQuerySystemInformation 结构体，
    ///     是为了避免猜结构体偏移导致「静默假数据」——只读分析视图的价值正是数据准确。
    ///   - 逐进程 GetProcessMemoryInfo（psapi，文档化）：进程工作集 Top-N。
    ///
    /// 优化（Tier C，未文档化 + 需管理员 + 提权，UI 默认收起、非管理员禁用）：
    ///   - NtSetSystemInformation(SystemMemoryListInformation=0x50, cmd=2 purge standby)：清空备用列表。
    ///   - 逐进程 EmptyWorkingSet（psapi）：清空工作集。
    ///   常量 purge standby = 2 取自 Process Hacker / Windows Internals（网上有写 3/4 的错值）。
    ///   未文档化 API 跨版本稳定但微软不保证，须 try/catch 优雅降级。
    /// </summary>
    internal static class MemoryAnalyzer
    {
        // ===================== 只读：GlobalMemoryStatusEx =====================
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        // ===================== 只读：GetPerformanceInfo (psapi) =====================
        [StructLayout(LayoutKind.Sequential)]
        private struct PERFORMANCE_INFORMATION
        {
            public uint cb;
            public IntPtr CommitTotal;
            public IntPtr CommitLimit;
            public IntPtr CommitPeak;
            public IntPtr PhysicalTotal;
            public IntPtr PhysicalAvailable;
            public IntPtr PhysicalUsed;
            public IntPtr KernelTotal;
            public IntPtr KernelPaged;
            public IntPtr KernelNonpaged;
            public IntPtr PageSize;
            public uint HandleCount;
            public uint ProcessCount;
            public uint ThreadCount;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetPerformanceInfo(ref PERFORMANCE_INFORMATION pPerformanceInformation, uint cb);

        // ===================== 只读：PDH 性能计数器回退（WMI 不可用时）=====================
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhOpenQuery(string szDataSource, IntPtr dwUserData, out IntPtr phQuery);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhAddEnglishCounter(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

        [DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(IntPtr hQuery);

        [DllImport("pdh.dll")]
        private static extern uint PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, out uint lpdwType, out PDH_FMT_COUNTERVALUE pValue);

        [DllImport("pdh.dll")]
        private static extern uint PdhRemoveCounter(IntPtr hCounter);

        [DllImport("pdh.dll")]
        private static extern uint PdhCloseQuery(IntPtr hQuery);

        // PDH 格式常量（权威值，来自 winperf.h / pdh.h）：
        //   PDH_FMT_DOUBLE  = 0x00000200  // 返回 double
        //   PDH_FMT_LARGE   = 0x00000400  // 返回 64 位整数（LONGLONG）—— 内存字节计数器应用此格式
        //   PDH_FMT_NOCAP100= 0x00008000  // 百分比计数器不封顶 100%
        // 历史 bug：PDH_FMT_LARGE 曾被误写成 0x200（实为 DOUBLE），导致 fmt 实际请求 DOUBLE 格式，
        // 而读取走 cv.longValue —— 把 IEEE-754 double 的二进制位当成 64 位整数读，出现天文数字 GB，
        // 进而 (Available+Modified) >> Total 使 InUse 被钳为 0。
        private const uint PDH_FMT_DOUBLE = 0x00000200;
        private const uint PDH_FMT_LARGE = 0x00000400;
        private const uint PDH_FMT_NOCAP100 = 0x00008000;

        [StructLayout(LayoutKind.Explicit, Size = 16)]
        private struct PDH_FMT_COUNTERVALUE
        {
            [FieldOffset(0)] public uint CStatus;
            [FieldOffset(8)] public long longValue;
            [FieldOffset(8)] public double doubleValue;
        }

        // ===================== 优化：NtSetSystemInformation(80, cmd) =====================
        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int SystemInformationClass, ref int SystemInformation, int SystemInformationLength);

        private const int SystemMemoryListInformation = 0x50;
        // SYSTEM_MEMORY_LIST_COMMAND（权威值，非网上流传的 3/4）：
        //   MemoryEmptyWorkingSets = 0
        //   MemoryFlushModifiedList = 1
        //   MemoryPurgeStandbyList = 2
        //   MemoryPurgeLowPriorityStandbyList = 3
        private const int MemoryPurgeStandbyList = 2;

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        // ===================== 提权 =====================
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID Luid;
            public uint Attributes;
        }

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

        // ===================== 进程枚举（Top-N 工作集）=====================
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EnumProcesses([Out] uint[] lpidProcess, int cb, out int cbNeeded);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(IntPtr hProcess, out PROCESS_MEMORY_COUNTERS_EX counters, uint size);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_MEMORY_COUNTERS_EX
        {
            public uint cb;
            public uint PageFaultCount;
            public IntPtr PeakWorkingSetSize;
            public IntPtr WorkingSetSize;
            public IntPtr QuotaPeakPagedPoolUsage;
            public IntPtr QuotaPagedPoolUsage;
            public IntPtr QuotaPeakNonPagedPoolUsage;
            public IntPtr QuotaNonPagedPoolUsage;
            public IntPtr PagefileUsage;
            public IntPtr PeakPagefileUsage;
            public IntPtr PrivateUsage;
        }

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_SET_QUOTA = 0x0100;
        private const uint PROCESS_VM_READ = 0x0010;

        // ===================== 数据模型 =====================
        public class MemoryOverview
        {
            public ulong TotalPhys;      // bytes
            public ulong AvailPhys;      // bytes
            public uint MemoryLoad;      // %
            public ulong CommitTotal;    // bytes
            public ulong CommitLimit;    // bytes
            public ulong CommitPeak;     // bytes
            public ulong KernelPaged;    // bytes
            public ulong KernelNonpaged; // bytes
            public ulong PageSize;       // bytes
            public uint ProcessCount;
        }

        public class MemoryUseCounts
        {
            public ulong Total;        // bytes（= 总物理）
            public ulong InUse;        // Active（使用中）
            public ulong Standby;      // 备用
            public ulong Modified;     // 已修改
            public ulong FreeZero;     // 空闲 + 零页（WMI 不区分，合并展示）
            public ulong Available;    // 可用（= Standby + Free + Zero）
            public ulong Cache;        // 系统缓存
            public ulong Committed;    // 已提交
            public ulong CommitLimit;  // 提交上限
            public ulong PoolPaged;    // 分页池
            public ulong PoolNonpaged; // 非分页池
            public bool IsDegraded;    // true = 使用总览数据降级构造，备用/已修改/缓存无法细分
        }

        public class ProcessMemInfo
        {
            public int Pid;
            public string Name;
            public ulong WorkingSet;  // bytes
            public ulong PrivateBytes; // bytes
        }

        // WMI 属性名 ↔ PDH 计数器名的统一映射，避免两份列表漂移。
        private static readonly (string Wmi, string Pdh)[] MemoryCounterMap = new[]
        {
            ("AvailableBytes", "Available Bytes"),
            ("StandbyCacheNormalPriorityBytes", "Standby Cache Normal Priority Bytes"),
            ("StandbyCacheReserveBytes", "Standby Cache Reserve Bytes"),
            ("StandbyCacheCoreBytes", "Standby Cache Core Bytes"),
            ("ModifiedPageListBytes", "Modified Page List Bytes"),
            ("FreeAndZeroPageListBytes", "Free & Zero Page List Bytes"),
            ("CacheBytes", "Cache Bytes"),
            ("CommittedBytes", "Committed Bytes"),
            ("CommitLimitBytes", "Commit Limit"),
            ("PoolPagedBytes", "Pool Paged Bytes"),
            ("PoolNonpagedBytes", "Pool Nonpaged Bytes")
        };

        // ===================== 公开方法 =====================
        public static bool IsAdministrator()
        {
            try
            {
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("IsAdministrator 异常(已忽略): " + ex.Message);
                return false;
            }
        }

        public static string FormatBytes(ulong bytes)
        {
            if (bytes >= 1UL << 30)
            {
                double gb = bytes / (1024.0 * 1024.0 * 1024.0);
                return gb.ToString("F2") + " GB";
            }
            if (bytes >= 1UL << 20)
            {
                double mb = bytes / (1024.0 * 1024.0);
                return mb.ToString("F1") + " MB";
            }
            if (bytes >= 1UL << 10)
            {
                double kb = bytes / 1024.0;
                return kb.ToString("F1") + " KB";
            }
            return bytes.ToString() + " B";
        }

        public static MemoryOverview GetOverview()
        {
            var o = new MemoryOverview();
            try
            {
                var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
                if (GlobalMemoryStatusEx(ref ms))
                {
                    o.TotalPhys = ms.ullTotalPhys;
                    o.AvailPhys = ms.ullAvailPhys;
                    o.MemoryLoad = ms.dwMemoryLoad;
                }
            }
            catch (Exception ex) { Debug.WriteLine("GetOverview GlobalMemoryStatusEx: " + ex.Message); }

            try
            {
                var pi = new PERFORMANCE_INFORMATION { cb = (uint)Marshal.SizeOf(typeof(PERFORMANCE_INFORMATION)) };
                if (GetPerformanceInfo(ref pi, pi.cb))
                {
                    ulong ps = (ulong)pi.PageSize.ToInt64();
                    o.PageSize = ps;
                    o.CommitTotal = (ulong)pi.CommitTotal.ToInt64() * ps;
                    o.CommitLimit = (ulong)pi.CommitLimit.ToInt64() * ps;
                    o.CommitPeak = (ulong)pi.CommitPeak.ToInt64() * ps;
                    o.KernelPaged = (ulong)pi.KernelPaged.ToInt64() * ps;
                    o.KernelNonpaged = (ulong)pi.KernelNonpaged.ToInt64() * ps;
                    o.ProcessCount = pi.ProcessCount;
                }
            }
            catch (Exception ex) { Debug.WriteLine("GetOverview GetPerformanceInfo: " + ex.Message); }

            return o;
        }

        public static MemoryUseCounts GetUseCounts(ulong totalPhys, MemoryOverview overview)
        {
            var u = new MemoryUseCounts { Total = totalPhys };
            bool ok = false;
            try
            {
                // 1) WMI 格式化计数器（首选）。
                u = QueryUseCounts("Win32_PerfFormattedData_PerfOS_Memory", totalPhys, u);
                // WMI 格式化性能计数器首次查询常返回全 0（计数器尚未"cook"），重试一次以取到真实值。
                if (IsBreakdownEmpty(u))
                {
                    System.Threading.Thread.Sleep(80);
                    u = QueryUseCounts("Win32_PerfFormattedData_PerfOS_Memory", totalPhys, u);
                }
                ok = !IsBreakdownEmpty(u);

                // 2) WMI 原始计数器回退。
                if (!ok)
                {
                    Debug.WriteLine("GetUseCounts: WMI formatted class returned empty, trying raw class.");
                    u = QueryUseCounts("Win32_PerfRawData_PerfOS_Memory", totalPhys, u);
                    ok = !IsBreakdownEmpty(u);
                }

                // 3) PDH 直接读取性能计数器回退（绕过 WMI）。
                if (!ok)
                {
                    Debug.WriteLine("GetUseCounts: WMI raw class also empty, falling back to PDH.");
                    ok = TryQueryUseCountsPdh(totalPhys, u);
                }

                // 4) 最终降级：从 GetOverview 可靠数据构造一个简化的拆解视图。
                if (!ok && overview != null)
                {
                    Debug.WriteLine("GetUseCounts: 所有计数器源均失败，使用基于总览数据的降级视图。");
                    u.Available = overview.AvailPhys;
                    u.FreeZero = overview.AvailPhys;      // 把全部可用内存归入 Free+Zero 用于占比条（InUse 由下方统一推导）
                    u.Standby = 0;
                    u.Modified = 0;
                    u.Committed = overview.CommitTotal;
                    u.CommitLimit = overview.CommitLimit;
                    u.PoolPaged = overview.KernelPaged;
                    u.PoolNonpaged = overview.KernelNonpaged;
                    // Cache 无可靠替代源，保持 0（UI 会显示 N/A）。
                    u.IsDegraded = true;
                    ok = true;
                }

                // 使用中(Active) = 总 − 可用 − 已修改（可用 = 备用 + 空闲 + 零页）。
                u.InUse = totalPhys > (u.Available + u.Modified) ? totalPhys - u.Available - u.Modified : 0;
            }
            catch (Exception ex)
            {
                // 任何未预期异常都落到降级路径，绝不让全 0 被当成真实数据渲染（占比条消失 / 明细显示 0 B）。
                Debug.WriteLine("GetUseCounts 异常(已降级): " + ex.Message);
                ok = false;
            }

            // 全部回退失败时标记降级：UI 据此走灰色占位 + 清晰提示，避免静默假数据。
            // 即便 overview == null 连降级视图都构造不出，至少让 UI 显示「数据不可用」而非 0 B 真值。
            if (!ok)
            {
                u.IsDegraded = true;
            }
            return u;
        }

        // 判断拆解数据是否"无真实数据"：正常运行的 Windows 必然存在 Standby/Free/Zero，四项全 0 是可靠的不可用信号。
        public static bool IsBreakdownEmpty(MemoryUseCounts u)
        {
            return u != null && u.Available == 0 && u.Standby == 0 && u.Modified == 0 && u.FreeZero == 0;
        }

        // 将计数器数组按 MemoryCounterMap 顺序填充到 MemoryUseCounts。
        // 顺序：[0]Available [1]StandbyNormal [2]StandbyReserve [3]StandbyCore [4]Modified [5]FreeZero
        //       [6]Cache [7]Committed [8]CommitLimit [9]PoolPaged [10]PoolNonpaged
        private static void ApplyCounterValues(ulong[] v, MemoryUseCounts u)
        {
            u.Available = v[0];
            u.Standby = v[1] + v[2] + v[3];
            u.Modified = v[4];
            u.FreeZero = v[5];
            u.Cache = v[6];
            u.Committed = v[7];
            u.CommitLimit = v[8];
            u.PoolPaged = v[9];
            u.PoolNonpaged = v[10];
        }

        // 通过 PDH.dll 直接读取性能计数器，绕过 WMI。
        // 逐计数器容错：某个计数器在本 Windows 版本不存在时仅跳过该计数器（零值填充），
        // 只要关键计数器（可用内存）成功即采用真实数据，避免"全有或全无"地丢弃其余有效值。
        private static bool TryQueryUseCountsPdh(ulong totalPhys, MemoryUseCounts u)
        {
            const uint fmt = PDH_FMT_LARGE | PDH_FMT_NOCAP100;
            const uint PDH_CSTATUS_NEW_DATA = 1; // 新数据，视为有效
            IntPtr query = IntPtr.Zero;
            int n = MemoryCounterMap.Length;
            var handles = new IntPtr[n];
            var values = new ulong[n];
            try
            {
                uint r = PdhOpenQuery(null, IntPtr.Zero, out query);
                if (r != 0) { Debug.WriteLine("TryQueryUseCountsPdh PdhOpenQuery failed: 0x" + r.ToString("X8")); return false; }

                // 逐计数器添加；某计数器名在本系统不存在时仅跳过，不影响其余。
                int added = 0;
                for (int i = 0; i < n; i++)
                {
                    string path = "\\Memory\\" + MemoryCounterMap[i].Pdh;
                    r = PdhAddEnglishCounter(query, path, IntPtr.Zero, out handles[i]);
                    if (r != 0)
                    {
                        Debug.WriteLine("TryQueryUseCountsPdh PdhAddEnglishCounter failed for " + MemoryCounterMap[i].Pdh + ": 0x" + r.ToString("X8") + "（跳过该计数器）");
                        handles[i] = IntPtr.Zero; // 标记不可用
                        continue;
                    }
                    added++;
                }
                if (added == 0) { Debug.WriteLine("TryQueryUseCountsPdh: 所有计数器添加失败，放弃 PDH。"); return false; }

                // 收集两次：第一次初始化（返回值可能为 PDH_RETRY，忽略），第二次取当前值。
                PdhCollectQueryData(query);
                System.Threading.Thread.Sleep(80);
                r = PdhCollectQueryData(query);
                if (r != 0) return false;

                int got = 0;
                for (int i = 0; i < n; i++)
                {
                    if (handles[i] == IntPtr.Zero) continue; // 该计数器本就不可用，零值填充
                    uint hr = PdhGetFormattedCounterValue(handles[i], fmt, out _, out PDH_FMT_COUNTERVALUE cv);
                    bool ok = hr == 0 && (cv.CStatus == 0 || cv.CStatus == PDH_CSTATUS_NEW_DATA);
                    if (!ok)
                    {
                        Debug.WriteLine("TryQueryUseCountsPdh PdhGetFormattedCounterValue failed for " + MemoryCounterMap[i].Pdh + ": hr=0x" + hr.ToString("X8") + " CStatus=" + cv.CStatus);
                        continue;
                    }
                    values[i] = cv.longValue > 0 ? (ulong)cv.longValue : 0;
                    got++;
                }

                // 关键计数器（可用内存 [0]）必须成功，否则整体放弃并回退到降级视图；
                // 其余计数器缺失则零值填充（如旧版 Windows 无 Standby 细分），仍是有价值的真实数据。
                if (values[0] == 0) { Debug.WriteLine("TryQueryUseCountsPdh: 关键计数器 Available 不可用，放弃 PDH 数据。"); return false; }
                if (got == 0) return false; // 防御性：所有计数器读取均失败

                ApplyCounterValues(values, u);

                return !IsBreakdownEmpty(u);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TryQueryUseCountsPdh exception: " + ex.Message);
                return false;
            }
            finally
            {
                for (int i = 0; i < n; i++) { if (handles[i] != IntPtr.Zero) PdhRemoveCounter(handles[i]); }
                if (query != IntPtr.Zero) PdhCloseQuery(query);
            }
        }

        private static MemoryUseCounts QueryUseCounts(string tableName, ulong totalPhys, MemoryUseCounts u)
        {
            try
            {
                var sb = new StringBuilder("SELECT ");
                for (int i = 0; i < MemoryCounterMap.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(MemoryCounterMap[i].Wmi);
                }
                sb.Append(" FROM ").Append(tableName);
                using (var searcher = new ManagementObjectSearcher(sb.ToString()))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        var v = new ulong[MemoryCounterMap.Length];
                        for (int i = 0; i < MemoryCounterMap.Length; i++)
                            v[i] = ToUlong(mo[MemoryCounterMap[i].Wmi]);
                        ApplyCounterValues(v, u);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("QueryUseCounts WMI (" + tableName + "): " + ex.Message);
            }
            return u;
        }

        public static List<ProcessMemInfo> GetProcessWorkingSets(int topN)
        {
            var list = new List<ProcessMemInfo>();
            try
            {
                uint[] ids = new uint[4096];
                if (EnumProcesses(ids, ids.Length * 4, out int needed) && needed > 0)
                {
                    int count = Math.Min(needed / 4, ids.Length);
                    for (int i = 0; i < count; i++)
                    {
                        uint pid = ids[i];
                        if (pid == 0) continue; // System Idle Process
                        IntPtr h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
                        if (h == IntPtr.Zero) continue;
                        try
                        {
                            var c = new PROCESS_MEMORY_COUNTERS_EX { cb = (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS_EX)) };
                            if (GetProcessMemoryInfo(h, out c, c.cb))
                            {
                                list.Add(new ProcessMemInfo
                                {
                                    Pid = (int)pid,
                                    Name = SafeProcessName(pid),
                                    WorkingSet = (ulong)c.WorkingSetSize.ToInt64(),
                                    PrivateBytes = (ulong)c.PrivateUsage.ToInt64()
                                });
                            }
                        }
                        catch (Exception ex) { Debug.WriteLine("GetProcessWorkingSets(pid=" + pid + "): " + ex.Message); }
                        finally { CloseHandle(h); }
                    }
                }
                list.Sort((a, b) => b.WorkingSet.CompareTo(a.WorkingSet));
                if (list.Count > topN) list = list.GetRange(0, topN);
            }
            catch (Exception ex) { Debug.WriteLine("GetProcessWorkingSets: " + ex.Message); }
            return list;
        }

        // ===================== 优化（Tier C）=====================
        public static string OptimizePurgeStandby()
        {
            var sb = new StringBuilder();
            try
            {
                if (!IsAdministrator())
                {
                    sb.AppendLine("[!] 需要以管理员身份运行本工具才能执行内存优化。");
                    return sb.ToString();
                }
                bool p1 = EnablePrivilege("SeProfileSingleProcessPrivilege");
                bool p2 = EnablePrivilege("SeIncreaseQuotaPrivilege");
                if (!p1 && !p2)
                    sb.AppendLine("[!] 提权失败（SeProfileSingleProcessPrivilege / SeIncreaseQuotaPrivilege），操作可能被系统拒绝。");

                int cmd = MemoryPurgeStandbyList; // 2 = purge standby
                int r = NtSetSystemInformation(SystemMemoryListInformation, ref cmd, Marshal.SizeOf(typeof(int)));
                if (r == 0)
                    sb.AppendLine("[OK] 已清空备用(Standby)列表：原缓存页转为可用内存（效果为临时，重新访问文件会再次缓存）。");
                else
                    sb.AppendLine("[!] NtSetSystemInformation 返回 " + r + "（0 = 成功）。可能系统拒绝或当前 Windows 版本不支持。");
            }
            catch (Exception ex) { sb.AppendLine("[!] 异常: " + ex.Message); }
            return sb.ToString();
        }

        public static string OptimizeEmptyWorkingSets()
        {
            var sb = new StringBuilder();
            try
            {
                if (!IsAdministrator())
                {
                    sb.AppendLine("[!] 需要以管理员身份运行本工具才能执行内存优化。");
                    return sb.ToString();
                }
                bool p1 = EnablePrivilege("SeProfileSingleProcessPrivilege");
                bool p2 = EnablePrivilege("SeIncreaseQuotaPrivilege");
                if (!p1 && !p2)
                    sb.AppendLine("[!] 提权失败，部分进程工作集可能无法清空。");

                uint[] ids = new uint[4096];
                if (EnumProcesses(ids, ids.Length * 4, out int needed) && needed > 0)
                {
                    int count = Math.Min(needed / 4, ids.Length);
                    int ok = 0, skip = 0;
                    for (int i = 0; i < count; i++)
                    {
                        uint pid = ids[i];
                        if (pid == 0) continue;
                        IntPtr h = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION, false, pid);
                        if (h == IntPtr.Zero) { skip++; continue; }
                        try
                        {
                            if (EmptyWorkingSet(h)) ok++; else skip++;
                        }
                        catch (Exception ex) { Debug.WriteLine("EmptyWorkingSet(pid=" + pid + "): " + ex.Message); skip++; }
                        finally { CloseHandle(h); }
                    }
                    sb.AppendLine("[OK] 已尝试清空 " + ok + " 个进程的工作集（跳过 " + skip + " 个，多为系统/受保护进程）。");
                    sb.AppendLine("[!] 注意：清空工作集会让相关进程下次访问内存时产生缺页，可能引起短暂卡顿。");
                }
                else
                {
                    sb.AppendLine("[!] 枚举进程失败。");
                }
            }
            catch (Exception ex) { sb.AppendLine("[!] 异常: " + ex.Message); }
            return sb.ToString();
        }

        // ===================== 内部辅助 =====================
        private static ulong ToUlong(object v)
        {
            try
            {
                if (v == null) return 0;
                if (v is ulong u) return u;
                if (v is long l) return (ulong)l;
                if (v is uint ui) return ui;
                if (v is int i) return (ulong)i;
                return Convert.ToUInt64(v);
            }
            catch (Exception ex)
            {
                // 字段转换失败不应静默记为 0 而制造假数据；写入调试输出留痕，便于排查 WMI 计数器类型变化。
                Debug.WriteLine("MemoryAnalyzer.ToUlong: 转换失败，已降级为 0。值=" + (v?.ToString() ?? "null") + " 原因=" + ex.Message);
                return 0;
            }
        }

        private static string SafeProcessName(uint pid)
        {
            try
            {
                using (var p = Process.GetProcessById((int)pid))
                {
                    if (!string.IsNullOrEmpty(p.ProcessName)) return p.ProcessName + ".exe";
                }
            }
            catch (Exception ex) { Debug.WriteLine("SafeProcessName(pid=" + pid + "): " + ex.Message); }
            return "PID " + pid;
        }

        private static bool EnablePrivilege(string name)
        {
            try
            {
                IntPtr token;
                if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token))
                    return false;
                try
                {
                    if (!LookupPrivilegeValue(null, name, out LUID luid))
                        return false;
                    var tp = new TOKEN_PRIVILEGES
                    {
                        PrivilegeCount = 1,
                        Luid = luid,
                        Attributes = SE_PRIVILEGE_ENABLED
                    };
                    return AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                }
                finally { CloseHandle(token); }
            }
            catch (Exception ex) { Debug.WriteLine("EnablePrivilege(" + name + "): " + ex.Message); return false; }
        }
    }
}

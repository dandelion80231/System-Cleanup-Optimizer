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
        }

        public class ProcessMemInfo
        {
            public int Pid;
            public string Name;
            public ulong WorkingSet;  // bytes
            public ulong PrivateBytes; // bytes
        }

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

        public static MemoryUseCounts GetUseCounts(ulong totalPhys)
        {
            var u = new MemoryUseCounts { Total = totalPhys };
            try
            {
                // 文档化 WMI 计数器（单位字节），稳定且不会踩未文档化偏移。
                const string q = "SELECT AvailableBytes,StandbyCacheNormalPriorityBytes,StandbyCacheReserveBytes,"
                    + "StandbyCacheCoreBytes,ModifiedPageListBytes,FreeAndZeroPageListBytes,CacheBytes,"
                    + "CommittedBytes,CommitLimitBytes,PoolPagedBytes,PoolNonpagedBytes "
                    + "FROM Win32_PerfFormattedData_PerfOS_Memory";
                u = QueryUseCounts(q, totalPhys, u);
                // WMI 格式化性能计数器首次查询常返回全 0（计数器尚未"cook"），重试一次以取到真实值。
                // 正常运行的 Windows 必然存在 Standby/Free/Zero，四项全 0 是可靠的"无真实数据"信号。
                if (IsBreakdownEmpty(u))
                {
                    System.Threading.Thread.Sleep(80);
                    u = QueryUseCounts(q, totalPhys, u);
                }
                // 使用中(Active) = 总 − 可用 − 已修改（可用 = 备用 + 空闲 + 零页）。
                u.InUse = totalPhys > (u.Available + u.Modified) ? totalPhys - u.Available - u.Modified : 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GetUseCounts WMI: " + ex.Message);
            }
            return u;
        }

        // 判断拆解数据是否"无真实数据"：正常运行的 Windows 必然存在 Standby/Free/Zero，四项全 0 是可靠的不可用信号。
        public static bool IsBreakdownEmpty(MemoryUseCounts u)
        {
            return u != null && u.Available == 0 && u.Standby == 0 && u.Modified == 0 && u.FreeZero == 0;
        }

        private static MemoryUseCounts QueryUseCounts(string q, ulong totalPhys, MemoryUseCounts u)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(q))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        u.Available = ToUlong(mo["AvailableBytes"]);
                        u.Standby = ToUlong(mo["StandbyCacheNormalPriorityBytes"])
                            + ToUlong(mo["StandbyCacheReserveBytes"])
                            + ToUlong(mo["StandbyCacheCoreBytes"]);
                        u.Modified = ToUlong(mo["ModifiedPageListBytes"]);
                        u.FreeZero = ToUlong(mo["FreeAndZeroPageListBytes"]);
                        u.Cache = ToUlong(mo["CacheBytes"]);
                        u.Committed = ToUlong(mo["CommittedBytes"]);
                        u.CommitLimit = ToUlong(mo["CommitLimitBytes"]);
                        u.PoolPaged = ToUlong(mo["PoolPagedBytes"]);
                        u.PoolNonpaged = ToUlong(mo["PoolNonpagedBytes"]);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("QueryUseCounts WMI: " + ex.Message);
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

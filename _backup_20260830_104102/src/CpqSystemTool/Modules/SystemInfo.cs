using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Management;
using System.Text;
using System.Windows.Forms;

namespace CpqSystemTool
{
    /// <summary>
    /// 系统信息收集模块 —— 对应原版 Win11 轻松设置「系统信息」标签页。
    /// 通过 WMI / 注册表 / Environment / Win32 P/Invoke 收集真实的 OS/CPU/RAM/GPU/DPI/分辨率等信息。
    /// </summary>
    internal static class SystemInfo
    {
        // Issue 18 + 20: 增加 NVIDIA 风格详细报告（多 GPU / 主板 / 硬盘 / 网络 / 安装日期）
        // Issue 23: 分两列收集 — 返回 left/right 两个字符串
        public class DualReport
        {
            public string Left = "";
            public string Right = "";
        }

        public static DualReport CollectDual()
        {
            var left = new StringBuilder();
            var right = new StringBuilder();
            try
            {
                // ====== 左侧：硬件信息（CPU + 内存）======
                // CPU
                string cpuName = "";
                int cores = 0, threads = 0;
                double cpuMaxClock = 0;
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
                    using (var moc = searcher.Get())
                        foreach (ManagementObject mo in moc)
                        {
                            cpuName = mo["Name"] != null ? mo["Name"].ToString().Trim() : "";
                            cores += mo["NumberOfCores"] != null ? Convert.ToInt32(mo["NumberOfCores"]) : 0;
                            threads += mo["NumberOfLogicalProcessors"] != null ? Convert.ToInt32(mo["NumberOfLogicalProcessors"]) : 0;
                            if (mo["MaxClockSpeed"] != null) cpuMaxClock = Math.Max(cpuMaxClock, Convert.ToDouble(mo["MaxClockSpeed"]) / 1000.0);
                        }
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
                left.AppendLine("【CPU】");
                left.AppendLine((string.IsNullOrEmpty(cpuName) ? "未知" : cpuName));
                left.AppendLine("核心：" + (cores > 0 ? cores : Environment.ProcessorCount) +
                                "  线程：" + (threads > 0 ? threads : Environment.ProcessorCount) +
                                (cpuMaxClock > 0 ? "  频率：" + cpuMaxClock.ToString("F2") + " GHz" : ""));

                // 内存
                long totalPhys = GetTotalPhysicalMemoryBytes();
                int dimmCount = 0;
                if (totalPhys <= 0) totalPhys = 8L * 1024 * 1024 * 1024;
                long gb = totalPhys / (1024 * 1024 * 1024);
                left.AppendLine("【内存】");
                left.AppendLine("总容量：" + gb + " G");
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT Capacity, Manufacturer, Speed FROM Win32_PhysicalMemory"))
                    using (var moc = searcher.Get())
                        foreach (ManagementObject mo in moc)
                        {
                            dimmCount++;
                            long cap = mo["Capacity"] != null ? Convert.ToInt64(mo["Capacity"]) : 0;
                            string mfr = mo["Manufacturer"] != null ? mo["Manufacturer"].ToString().Trim() : "未知";
                            uint speed = mo["Speed"] != null ? Convert.ToUInt32(mo["Speed"]) : 0;
                            left.AppendLine("  内存条" + dimmCount + "：" + (cap / (1024L * 1024 * 1024)) + " G  " + mfr + (speed > 0 ? "  " + speed + " MHz" : ""));
                        }
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }

                // ====== 右侧：系统 + 网络 + 显卡 + 主板 + 硬盘 + 显示 ======
                // 系统
                string osName = Environment.OSVersion.VersionString.Trim();
                string productName = GetRegSz(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "");
                string edition = GetRegSz(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "");
                string displayVer = GetRegSz(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", "");
                string build = GetRegSz(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild", "");
                string ubr = GetRegSz(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR", "0");
                string installDate = GetRegSz(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "InstallDate", "");
                string installDateHuman = ParseInstallDate(installDate);
                right.AppendLine("【系统】");
                // Issue: 系统信息版本字符串应显示为中文。注册表 ProductName 在部分系统（尤其 Windows 11）仍为英文
                // （如 "Windows 10 Pro for Workstations"），因此按 CurrentBuild 判断 Windows 代际，再按 EditionID/ProductName
                // 中的版本片段做中文映射，得到如 "Windows 11 专业工作站版 25H2"。
                string winVer = GetWindowsVersionName(build);
                string editionCn = LocalizeEdition(edition, productName);
                string osDisplay = winVer;
                if (!string.IsNullOrEmpty(editionCn)) osDisplay += " " + editionCn;
                if (!string.IsNullOrEmpty(displayVer)) osDisplay += " " + displayVer;
                right.AppendLine(osDisplay.Trim());
                right.AppendLine("版本号：" + build + "." + ubr);
                if (!string.IsNullOrEmpty(installDateHuman))
                    right.AppendLine("安装日期：" + installDateHuman);
                right.AppendLine("当前用户：" + Environment.UserName);
                right.AppendLine("计算机：" + Environment.MachineName);

                // 网络适配器 — 紧凑显示（每行 2 个网卡）
                var nicList = new List<(string name, string mac)>();
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT Name, MACAddress FROM Win32_NetworkAdapter WHERE MACAddress IS NOT NULL"))
                    using (var moc = searcher.Get())
                        foreach (ManagementObject mo in moc)
                        {
                            string name = mo["Name"] != null ? mo["Name"].ToString().Trim() : "";
                            string mac = mo["MACAddress"] != null ? mo["MACAddress"].ToString().Trim() : "";
                            nicList.Add((name, mac));
                        }
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
                right.AppendLine("【网络适配器】");
                if (nicList.Count == 0)
                {
                    right.AppendLine("  无网络适配器");
                }
                else
                {
                    // Issue 26: 一行网卡一行 MAC（如太长则 MAC 换行）
                    for (int i = 0; i < nicList.Count; i++)
                    {
                        var n = nicList[i];
                        right.AppendLine("  网卡" + (i + 1) + "：" + n.Item1);
                        right.AppendLine("        MAC：" + n.Item2);
                    }
                }

                                // 显卡 — 移到左侧
                left.AppendLine("【显卡】");
                try
                {
                    int gpuIdx = 0;
                    // VRAM 三级探测：注册表 qwMemorySize（全厂商 >4GB）> nvidia-smi > WMI
                    var vramRegistry = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
                    // HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\000X\HardwareInformation.qwMemorySize
                    try
                    {
                        string baseKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
                        using (var root = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(baseKey))
                        {
                            if (root != null)
                            {
                                foreach (var sub in root.GetSubKeyNames())
                                {
                                    if (sub.Length != 4 || !int.TryParse(sub, out _)) continue;
                                    using (var gpuKey = root.OpenSubKey(sub + @"\HardwareInformation"))
                                    {
                                        if (gpuKey == null) continue;
                                        string name = gpuKey.GetValue("AdapterString") as string;
                                        object qw = gpuKey.GetValue("qwMemorySize");
                                        if (name != null && qw != null)
                                        {
                                            try { vramRegistry[name.Trim()] = Convert.ToUInt64(qw); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }

                    // nvidia-smi 备选
                    var vramNVIDIA = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        string nvidiaOut = Exec.RunCmdGet(new[] { "nvidia-smi", "--query-gpu=name,memory.total", "--format=csv,noheader,nounits" }, null);
                        if (!string.IsNullOrWhiteSpace(nvidiaOut))
                        {
                            foreach (var line in nvidiaOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                var parts = line.Split(',');
                                if (parts.Length >= 2 && ulong.TryParse(parts[1].Trim(), out ulong mb))
                                    vramNVIDIA[parts[0].Trim().ToLowerInvariant()] = mb * 1024UL * 1024UL;
                            }
                        }
                    }
                    catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }

                    using (var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterRAM, VideoProcessor, VideoModeDescription FROM Win32_VideoController"))
                    using (var moc = searcher.Get())
                        foreach (ManagementObject mo in moc)
                        {
                            gpuIdx++;
                            string n = mo["Name"] != null ? mo["Name"].ToString().Trim() : "未知";
                            string drv = mo["DriverVersion"] != null ? mo["DriverVersion"].ToString().Trim() : "未知";
                            string proc = mo["VideoProcessor"] != null ? mo["VideoProcessor"].ToString().Trim() : "未知";
                            // 显存优先级：注册表 qwMemorySize > nvidia-smi > WMI(uint32)
                            ulong ram = 0;
                            bool found = false;
                            string nKey = n.ToLowerInvariant();
                            // 注册表
                            foreach (var kv in vramRegistry)
                            { if (nKey.Contains(kv.Key.ToLowerInvariant()) || kv.Key.ToLowerInvariant().Contains(nKey)) { ram = kv.Value; found = true; break; } }
                            // nvidia-smi
                            if (!found) foreach (var kv in vramNVIDIA)
                            { if (nKey.Contains(kv.Key) || kv.Key.Contains(nKey)) { ram = kv.Value; found = true; break; } }
                            // WMI
                            if (!found && mo["AdapterRAM"] != null)
                            { try { ram = Convert.ToUInt32(mo["AdapterRAM"]); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  ram = 0; } }
                            string ramStr = ram > 0 ? "  显存：" + (ram / (1024UL * 1024 * 1024)) + " GB" : "";
                            string modeStr = "";
                            try { var mode = mo["VideoModeDescription"]; if (mode != null) modeStr = mode.ToString(); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
                            left.AppendLine("显卡" + gpuIdx + "：" + n);
                            left.AppendLine("  驱动：" + drv + ramStr);
                            left.AppendLine("  处理器：" + proc);
                            if (!string.IsNullOrEmpty(modeStr))
                                left.AppendLine("  分辨率：" + modeStr);
                        }
                    if (gpuIdx == 0) left.AppendLine("无显卡");
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  left.AppendLine("（获取失败）"); }

                // 主板 — 移到左侧
                left.AppendLine("【主板】");
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product, SerialNumber FROM Win32_BaseBoard"))
                    using (var moc = searcher.Get())
                        foreach (ManagementObject mo in moc)
                        {
                            string mfr = mo["Manufacturer"] != null ? mo["Manufacturer"].ToString().Trim() : "未知";
                            string prod = mo["Product"] != null ? mo["Product"].ToString().Trim() : "未知";
                            string serial = mo["SerialNumber"] != null ? mo["SerialNumber"].ToString().Trim() : "";
                            left.AppendLine("制造商：" + mfr);
                            left.AppendLine("型号：" + prod);
                            if (!string.IsNullOrEmpty(serial))
                                left.AppendLine("序列号：" + serial);
                            break;
                        }
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }

                // 硬盘 — 移到左侧
                left.AppendLine("【硬盘】");
                try
                {
                    int diskIdx = 0;
                    using (var searcher = new ManagementObjectSearcher("SELECT Caption, Size, InterfaceType FROM Win32_DiskDrive"))
                    using (var moc = searcher.Get())
                        foreach (ManagementObject mo in moc)
                        {
                            diskIdx++;
                            string cap = mo["Caption"] != null ? mo["Caption"].ToString().Trim() : "未知";
                            long size = mo["Size"] != null ? Convert.ToInt64(mo["Size"]) : 0;
                            string iface = mo["InterfaceType"] != null ? mo["InterfaceType"].ToString().Trim() : "未知";
                            left.AppendLine("磁盘" + diskIdx + "：" + cap);
                            left.AppendLine("  接口：" + iface + (size > 0 ? "  容量：" + (size / (1024L * 1024 * 1024)) + " GB" : ""));
                        }
                    if (diskIdx == 0) left.AppendLine("无磁盘");
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }

                // 显示 — 移到左侧
                left.AppendLine("【显示】");
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    float dpiX = g.DpiX;
                    int pct = (int)Math.Round(dpiX / 96.0 * 100);
                    left.AppendLine("DPI：" + ((int)dpiX).ToString() + "  缩放：" + pct + "%");
                    left.AppendLine("分辨率：" + Screen.PrimaryScreen.Bounds.Width + " X " + Screen.PrimaryScreen.Bounds.Height);
                }
            }
            catch (Exception ex)
            {
                left.AppendLine("错误：" + ex.Message);
            }
            return new DualReport { Left = left.ToString().TrimEnd('\r', '\n'), Right = right.ToString().TrimEnd('\r', '\n') };
        }

        /// <summary>
        /// 解析注册表 InstallDate。Win10/11 多数存为 REG_DWORD 十进制 yyyyMMdd（如 20231015），
        /// 也兼容字符串日期(yyyy-MM-dd / yyyy/M/d)、Unix 秒/毫秒时间戳，以及超大数值的 FILETIME 兜底。
        /// 返回 "" 表示无法解析（不再显示乱值）。
        /// </summary>
        private static string ParseInstallDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            raw = raw.Trim();
            // 1) yyyyMMdd（最常见，8 位纯数字）
            if (raw.Length == 8 && long.TryParse(raw, out long ymd) && ymd >= 19000101 && ymd <= 29991231)
            {
                if (DateTime.TryParseExact(raw, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime d))
                    return d.ToString("yyyy-MM-dd");
            }
            // 2) 标准日期字符串
            if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime ds))
                return ds.ToString("yyyy-MM-dd");
            // 3) Unix 时间戳（秒 ~1e9 / 毫秒 ~1e12）
            if (long.TryParse(raw, out long unix))
            {
                try
                {
                    if (unix > 1_000_000_000_000L) return DateTimeOffset.FromUnixTimeMilliseconds(unix).LocalDateTime.ToString("yyyy-MM-dd");
                    if (unix > 1_000_000_000L) return DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime.ToString("yyyy-MM-dd");
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            }
            // 4) FILETIME 兜底（极大数值）
            if (long.TryParse(raw, out long ticks) && ticks > 1_000_000_000_000L)
            {
                try { return DateTime.FromFileTimeUtc(ticks).ToLocalTime().ToString("yyyy-MM-dd"); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            }
            return "";
        }

        // Issue: 系统信息版本字符串应显示为中文。注册表 ProductName 在 Windows 11 上仍可能是英文
        //（如 "Windows 10 Pro for Workstations"），因此按 EditionID / ProductName 中的版本片段做中文映射。
        // 映射数据源已收编至 Helpers/EditionMap.cs（EnglishToChinese / ToChinese）。
        // 覆盖项目支持的 Windows 10/11 全部主流版本（含 N / S(LTSC) / S 模式 / IoT 企业版 / 服务器 / 多会话）。

        private static string GetWindowsVersionName(string currentBuild)
        {
            if (int.TryParse(currentBuild, out int b))
            {
                if (b >= 22000) return "Windows 11";
                if (b >= 10240) return "Windows 10";
                if (b >= 9600) return "Windows 8.1";
                if (b >= 9200) return "Windows 8";
                if (b >= 7601) return "Windows 7";
            }
            // 兜底：从 Environment.OSVersion 取最友好的前缀
            string fallback = Environment.OSVersion.VersionString.Trim();
            if (fallback.StartsWith("Microsoft Windows ")) return fallback.Substring(18).Trim();
            return "Windows";
        }

        private static string LocalizeEdition(string edition, string productName)
        {
            if (!string.IsNullOrEmpty(edition) && EditionMap.EnglishToChinese.TryGetValue(edition, out string cn1))
                return cn1;
            // 从 ProductName 中匹配版本片段（如 "Windows 10 Pro for Workstations"）。
            // 按键长降序匹配，确保 "Pro for Workstations N" 优先于 "Pro" 等短片段，且不依赖字典枚举顺序。
            if (!string.IsNullOrEmpty(productName))
            {
                var keys = new List<string>(EditionMap.EnglishToChinese.Keys);
                keys.Sort((a, b) => b.Length.CompareTo(a.Length));
                foreach (var key in keys)
                {
                    if (productName.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                        return EditionMap.EnglishToChinese[key];
                }
            }
            // 未匹配到中文：优雅降级为英文原文，绝不报错。
            if (!string.IsNullOrEmpty(edition)) return edition;          // 优先回退 EditionID 英文
            if (!string.IsNullOrEmpty(productName))                       // 否则取 ProductName 去掉 "Windows X " 前缀后的英文片段
            {
                string stripped = productName;
                if (stripped.StartsWith("Windows ", StringComparison.OrdinalIgnoreCase))
                {
                    int sp = stripped.IndexOf(' ', 8);                    // 跳过 "Windows " 后找下一个空格
                    if (sp > 0) stripped = stripped.Substring(sp + 1).Trim();
                }
                return stripped;
            }
            return "";
        }

        private static string GetRegSz(string keyPath, string valueName, string def)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key != null)
                    {
                        var v = key.GetValue(valueName);
                        if (v != null) return v.ToString();
                    }
                }
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            return def;
        }

        // ---- 物理内存总量（Win32 P/Invoke，net48 可用，无需额外程序集）----
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
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

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        private static long GetTotalPhysicalMemoryBytes()
        {
            try
            {
                var ms = new MEMORYSTATUSEX();
                ms.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(ms);
                if (GlobalMemoryStatusEx(ref ms))
                    return (long)ms.ullTotalPhys;
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            return 0;
        }
    }
}

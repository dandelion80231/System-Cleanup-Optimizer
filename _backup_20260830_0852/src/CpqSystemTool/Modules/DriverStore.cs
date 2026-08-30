using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace CpqSystemTool
{
    /// <summary>
    /// 驱动存储管理模块（参考开源工具 Driver Store Explorer / RAPR 的核心能力）。
    /// 本质是对 Windows 自带 <c>pnputil.exe</c> 与 DISM 的封装：
    ///   - 枚举已安装驱动包（pnputil /enum-drivers 或 DISM Get-WindowsDriver）
    ///   - 通过 WMI（Win32_PnPSignedDriver）识别"在役"驱动，保护正在使用的设备
    ///   - 识别并保护"启动关键"驱动（Boot Critical），避免删除后系统无法启动
    ///   - 同系列（原始 INF 同名）下保留最新版，其余标记为"旧版冗余"
    ///   - 删除选中驱动包（/delete-driver，默认不带 /force；支持强制删除 /force）
    ///   - 导出备份选中驱动包（/export-driver）
    ///   - 添加驱动包（/add-driver &lt;inf&gt; /subdirs）、安装到设备（/install-driver &lt;oem.inf&gt;）
    /// 程序已以管理员身份运行（app.manifest requireAdministrator），无需额外提权。
    /// </summary>
    internal static class DriverStore
    {
        /// <summary>后端枚举引擎。</summary>
        internal enum DriverEngine
        {
            /// <summary>PnP 实用工具（pnputil /enum-drivers），默认且最安全，仅含第三方驱动。</summary>
            PnpUtil,
            /// <summary>系统映像管理（DISM Get-WindowsDriver -Online -All），会包含内置(inbox)驱动。</summary>
            Dism
        }

        /// <summary>单条驱动包信息。</summary>
        internal class DriverInfo
        {
            public string OemName { get; set; } = "";       // 发布名称，如 oem12.inf（删除/导出用它）
            public string OriginalName { get; set; } = "";  // 原始 INF 名，如 nv_dispig.inf
            public string Provider { get; set; } = "";      // 提供程序
            public string Class { get; set; } = "";         // 驱动类（原始英文 ClassName）
            public string ClassGuid { get; set; } = "";     // 类 GUID，用于读取本地化类名
            public string ClassDescription { get; set; } = ""; // 驱动类中文描述（本地化后的可读名称）
            public string Version { get; set; } = "";       // 驱动版本（原始字符串，如 31.0.15.1234）
            public DateTime? Date { get; set; }               // 驱动日期
            public DateTime? InstallDate { get; set; }        // 安装日期（PnPUtil：通过 INF 内容匹配 FileRepository 目录创建时间）
            public string Signer { get; set; } = "";        // 数字签名者
            public string WhcpLevel { get; set; } = "";     // WHCP 级别（未知/等）
            public string DeviceName { get; set; } = "";    // 设备名称（通过 WMI 匹配获取；可能为空）
            public bool IsOld { get; set; }                   // 同系列下的旧版本（可清理）
            public bool InUse { get; set; }                   // 当前有设备在使用（受保护，禁止删除）
            public bool BootCritical { get; set; }            // 启动关键驱动（受保护，默认不参与清理）
            public bool IsDism { get; set; }                  // 来自 DISM 后端（无 oemX.inf 发布名，不能执行依赖 oem 名的操作）
            public double SizeMB { get; set; }                // 该系列驱动包估算占用（MB，近似值）
            public bool Selected { get; set; }                // UI 勾选态（仅用于对话框选择；须为属性以支持 TwoWay 绑定）

            /// <summary>状态显示文本：在役/启动关键/旧版可清/当前。</summary>
            public string StatusText
            {
                get
                {
                    if (InUse) return "在役·保护";
                    if (BootCritical) return "启动关键";
                    if (IsOld) return "旧版可清";
                    return "当前";
                }
            }

            /// <summary>日期显示文本。</summary>
            public string DateText => Date.HasValue ? Date.Value.ToString("yyyy-MM-dd") : "";

            /// <summary>安装日期显示文本。</summary>
            public string InstallDateText => InstallDate.HasValue ? InstallDate.Value.ToString("yyyy-MM-dd") : "—";

            /// <summary>占用显示文本（近似值）。</summary>
            public string SizeText => SizeMB > 0 ? "约 " + FormatSize(SizeMB) : "—";

            /// <summary>设备名称显示文本（空时显示占位符）。</summary>
            public string DeviceNameText => string.IsNullOrWhiteSpace(DeviceName) ? "—" : DeviceName;
        }

        /// <summary>删除操作的结构化结果。</summary>
        internal class DeleteResult
        {
            public int Succeeded;
            public int SkippedProtected;    // 在役（InUse）
            public int SkippedBootCritical; // 启动关键且未勾选包含
            public int Failed;
            public bool AllOk => Failed == 0 && SkippedProtected == 0 && SkippedBootCritical == 0;
        }

        #region SetupAPI 设备名解析（net48 P/Invoke，实机验证通过）
        // 取数走经典 SetupDiGetDeviceRegistryPropertyW(SPDRP_*) API。
        // 经独立测试程序在 Windows 实机验证：设备描述命中 279/280、oem 发布名命中 261/280。
        // 注：早期尝试的 SetupDiGetDevicePropertyW(DEVPROPKEY) 在本机对所有设备返回
        //     ERROR_NOT_FOUND(1168)，故弃用；经典 SPDRP API 稳定可用。
        // 仅作设备名增强：任何异常都被吞掉返回空字典，调用方自动回退 WMI，不影响主流程。

        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_ALLCLASSES = 0x00000004;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;
        private const uint SPDRP_DEVICEDESC = 0x00000000;   // REG_SZ 设备描述
        private const uint SPDRP_FRIENDLYNAME = 0x0000000C; // REG_SZ 友好名
        private const uint SPDRP_DRIVER = 0x00000009;       // REG_SZ 驱动键（如 {guid}\nnnn）
        private static readonly IntPtr SETUP_INVALID_HANDLE = new IntPtr(-1);
        private static readonly IntPtr HKEY_LOCAL_MACHINE = new IntPtr(0x80000002);
        private const uint KEY_READ = 0x20019;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public int cbSize;       // 必须用 Marshal.SizeOf<SP_DEVINFO_DATA>() 初始化（兼容 x86/x64）
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;  // ULONG_PTR -> 平台相关大小
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(
            IntPtr ClassGuid,
            [MarshalAs(UnmanagedType.LPWStr)] string Enumerator,
            IntPtr hwndParent,
            uint Flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(
            IntPtr DeviceInfoSet,
            uint MemberIndex,
            ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceRegistryPropertyW(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData,
            uint Property,
            out uint PropertyRegDataType,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)] byte[] PropertyBuffer,
            uint PropertyBufferSize,
            out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegOpenKeyExW(
            IntPtr hKey, string lpSubKey, uint ulOptions, uint samDesired, out IntPtr phkResult);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegQueryValueExW(
            IntPtr hKey, string lpValueName, IntPtr lpReserved,
            out uint lpType, byte[] lpData, ref uint lpcbData);

        [DllImport("advapi32.dll")]
        private static extern int RegCloseKey(IntPtr hKey);

        /// <summary>
        /// 经 SetupAPI 构建设备名映射：键为驱动 INF 名（oemX.inf 或原始名，取决于系统）+ 驱动键本身，
        /// 值为该设备最可读的名称（优先 FriendlyName，其次 DeviceDesc）。任何异常都返回空字典。
        /// </summary>
        private static Dictionary<string, string> BuildDeviceNameMapViaSetupApi()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IntPtr devInfoSet = IntPtr.Zero;
            try
            {
                devInfoSet = SetupDiGetClassDevsW(
                    IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
                if (devInfoSet == SETUP_INVALID_HANDLE || devInfoSet == IntPtr.Zero)
                    return map;

                uint index = 0;
                var devInfo = new SP_DEVINFO_DATA();
                devInfo.cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>();

                while (SetupDiEnumDeviceInfo(devInfoSet, index, ref devInfo))
                {
                    index++;
                    try
                    {
                        string desc = GetRegistryPropertyString(devInfoSet, devInfo, SPDRP_DEVICEDESC);
                        string friendly = GetRegistryPropertyString(devInfoSet, devInfo, SPDRP_FRIENDLYNAME);
                        string driverKey = GetRegistryPropertyString(devInfoSet, devInfo, SPDRP_DRIVER);
                        string infName = !string.IsNullOrWhiteSpace(driverKey) ? ReadInfPathFromRegistry(driverKey) : null;

                        string name = !string.IsNullOrWhiteSpace(friendly) ? SanitizeText(friendly)
                                   : !string.IsNullOrWhiteSpace(desc) ? SanitizeText(desc) : null;
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        // 以 InfPath 作为唯一键：其值在本机可能是 oem 名(oemX.inf)或原始名(原 inf)，
                        // 经 ResolveDeviceNames 用 OemName/OriginalName 双键查找即可命中其一。
                        // 注意：不要再加驱动键({guid}\nnnn)——它永不等于 OemName/OriginalName，纯属噪声。
                        if (!string.IsNullOrWhiteSpace(infName) && !map.ContainsKey(infName))
                            map[infName] = name;
                    }
                    catch { /* 单个设备失败忽略，继续下一个 */ }
                }
            }
            catch { }
            finally
            {
                if (devInfoSet != IntPtr.Zero && devInfoSet != SETUP_INVALID_HANDLE)
                    SetupDiDestroyDeviceInfoList(devInfoSet);
            }
            return map;
        }

        private static string GetRegistryPropertyString(IntPtr devInfoSet, SP_DEVINFO_DATA data, uint property)
        {
            uint regType;
            uint requiredSize;
            if (!SetupDiGetDeviceRegistryPropertyW(devInfoSet, ref data, property, out regType, null, 0, out requiredSize))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != ERROR_INSUFFICIENT_BUFFER) return null;
            }
            if (requiredSize == 0) return null;

            var buf = new byte[requiredSize];
            uint actualSize;
            if (!SetupDiGetDeviceRegistryPropertyW(devInfoSet, ref data, property, out regType, buf, requiredSize, out actualSize))
                return null;

            return TrimNtString(buf, (int)requiredSize);
        }

        private static string ReadInfPathFromRegistry(string driverKey)
        {
            IntPtr hKey = IntPtr.Zero;
            try
            {
                string subKey = @"SYSTEM\CurrentControlSet\Control\Class\" + driverKey;
                int rc = RegOpenKeyExW(HKEY_LOCAL_MACHINE, subKey, 0, KEY_READ, out hKey);
                if (rc != 0) return null;
                uint type;
                uint size = 256;
                var data = new byte[size];
                rc = RegQueryValueExW(hKey, "InfPath", IntPtr.Zero, out type, data, ref size);
                if (rc == 234 /* ERROR_MORE_DATA */)
                {
                    data = new byte[size];
                    rc = RegQueryValueExW(hKey, "InfPath", IntPtr.Zero, out type, data, ref size);
                }
                if (rc != 0) return null;
                return TrimNtString(data, (int)size);
            }
            catch { return null; }
            finally { if (hKey != IntPtr.Zero) RegCloseKey(hKey); }
        }

        /// <summary>从 UTF-16 LE 字节缓冲（含结尾 null）中截取 null 终止符前的字符串。</summary>
        private static string TrimNtString(byte[] data, int len)
        {
            if (data == null || len <= 0) return "";
            string s = Encoding.Unicode.GetString(data, 0, Math.Min(len, data.Length));
            int nullIdx = s.IndexOf('\0');
            return nullIdx >= 0 ? s.Substring(0, nullIdx) : s;
        }
        #endregion

        // 字段标签（pnputil）：同时兼容中文 / 英文。
        // Windows 11 中文版 pnputil 输出字段名为：发布名称 / 原始名称 / 提供程序名称 / 类名 / 驱动程序版本 / 签名者姓名 / WHCP 版本。
        // 当控制台编码为 UTF-8 时 pnputil 会输出英文：Published Name / Original Name / Provider Name / Class Name / Driver Version / Signer Name / WHCP Version。
        // 关键：解析不依赖"按行"，而是全局定位标签 + 截断到下一个标签，
        // 以容错 pnputil 「WHCP 版本: 未知发布名称: oemX.inf」挤在同一行的现象。
        private static readonly Dictionary<string, string[]> LabelPatterns = new Dictionary<string, string[]>
        {
            { "PublishedName", new[] { "发布名称", "Published Name" } },
            { "OriginalName",  new[] { "原始名称", "Original Name" } },
            { "Provider",      new[] { "提供程序名称", "Provider Name" } },
            { "Class",         new[] { "类名", "Class Name", "类", "Class" } },
            { "DriverDate",    new[] { "驱动程序日期", "Driver Date" } },
            { "DriverVersion", new[] { "驱动程序版本", "Driver Version" } },
            { "Signer",        new[] { "签名者姓名", "Signer Name", "数字签名者", "Signer" } },
            { "ClassGuid",     new[] { "类 GUID", "Class GUID" } },
            { "WhcpLevel",     new[] { "WHCP 版本", "WHCP Version", "WHCP Level" } },
            { "Locale",        new[] { "区域设置", "Locale" } },
            { "BootCritical",  new[] { "启动关键信息", "Boot Critical" } },
        };

        /// <summary>
        /// 枚举全部驱动包（默认 PnpUtil 后端）。
        /// </summary>
        internal static List<DriverInfo> Enumerate(Action<string> log)
            => Enumerate(DriverEngine.PnpUtil, log);

        /// <summary>
        /// 枚举全部驱动包。engine 指定后端：PnpUtil 仅含第三方驱动（安全）；
        /// Dism 含内置(inbox)驱动，可能列出系统关键驱动，请谨慎操作。
        /// </summary>
        internal static List<DriverInfo> Enumerate(DriverEngine engine, Action<string> log)
        {
            if (engine == DriverEngine.Dism)
            {
                // 用 KEY|VALUE 格式输出，规避表格换行/截断，便于逐行解析。
                // 注意：Get-WindowsDriver 的属性名是 Driver / OriginalFileName / ProviderName / ClassName / Date / Version / BootCritical / DriverSignature。
                const string script = "Get-WindowsDriver -Online -All -ErrorAction SilentlyContinue | ForEach-Object { " +
                    "'DRIVER|' + $_.Driver; 'ORIGINALFILENAME|' + $_.OriginalFileName; 'PROVIDERNAME|' + $_.ProviderName; " +
                    "'CLASSNAME|' + $_.ClassName; 'CLASSDESCRIPTION|' + $_.ClassDescription; 'CLASSGUID|' + $_.ClassGuid; " +
                    "'DATE|' + $_.Date; 'VERSION|' + $_.Version; " +
                    "'BOOTCRITICAL|' + $_.BootCritical; 'DRIVERSIGNATURE|' + $_.DriverSignature }";
                var outp = Exec.RunPowerShellGet(script, log);
                if (string.IsNullOrWhiteSpace(outp))
                {
                    log?.Invoke("[!] DISM 未返回驱动信息（可能不支持或权限不足）。");
                    return new List<DriverInfo>();
                }
                var list = ParseDismOutput(outp, log);
                log?.Invoke($"[✓] DISM 枚举完成：共 {list.Count} 个驱动包。");
                return list;
            }

            // 通过 PowerShell 调用 pnputil：RunPS 已设置 [Console]::OutputEncoding = UTF8，
            // 可稳定得到英文输出并避免直接 CreateProcess 时的控制台代码页/编码猜测问题。
            var outp2 = Exec.RunPowerShellGet("pnputil /enum-drivers", log);
            if (string.IsNullOrWhiteSpace(outp2))
            {
                log?.Invoke("[!] 未获取到 pnputil 输出（可能无第三方驱动，或 pnputil 不可用）。");
                return new List<DriverInfo>();
            }

            var list2 = ParseEnumOutput(outp2, log);
            log?.Invoke("[→] 正在解析类描述、安装日期、设备名称…");
            ResolveClassDescriptions(list2);
            ResolveInstallDates(list2);
            ResolveDeviceNames(list2, log);
            log?.Invoke($"[✓] PnPUtil 枚举完成：共 {list2.Count} 个驱动包。");
            return list2;
        }

        /// <summary>把 pnputil /enum-drivers 的文本解析成 DriverInfo 列表（标签定位法，容错挤行）。</summary>
        private static List<DriverInfo> ParseEnumOutput(string text, Action<string> log)
        {
            var markers = new List<(int pos, string key, int valStart)>();
            foreach (var kv in LabelPatterns)
            {
                foreach (var pat in kv.Value)
                {
                    // 标签后必须跟半角/全角冒号（可选空白），避免误匹配正文中的字样
                    var re = new Regex(Regex.Escape(pat) + @"\s*[:：]\s*", RegexOptions.CultureInvariant);
                    foreach (Match m in re.Matches(text))
                        markers.Add((m.Index, kv.Key, m.Index + m.Length));
                }
            }
            if (markers.Count == 0)
            {
                log?.Invoke("[!] 未能从 pnputil 输出中识别任何驱动字段，请检查系统语言或 pnputil 版本。");
                return new List<DriverInfo>();
            }

            markers.Sort((a, b) => a.pos.CompareTo(b.pos));

            var list = new List<DriverInfo>();
            DriverInfo cur = null;
            for (int i = 0; i < markers.Count; i++)
            {
                var (pos, key, valStart) = markers[i];
                int valEnd = (i + 1 < markers.Count) ? markers[i + 1].pos : text.Length;
                var value = text.Substring(valStart, valEnd - valStart).Trim();

                if (key == "PublishedName")
                {
                    if (cur != null) list.Add(cur);
                    cur = new DriverInfo { OemName = CleanValue(value) };
                }
                else if (cur != null)
                {
                    switch (key)
                    {
                        case "OriginalName":  cur.OriginalName = CleanValue(value); break;
                        case "Provider":      cur.Provider = CleanValue(value); break;
                        case "Class":         cur.Class = CleanValue(value); cur.ClassDescription = cur.Class; break;
                        case "ClassGuid":     cur.ClassGuid = CleanValue(value); break;
                        case "DriverDate":
                            // 兼容旧版「驱动程序日期: 2016/2/25」单独字段
                            ApplyDateVersion(cur, CleanValue(value), takeDate: true);
                            break;
                        case "DriverVersion":
                            // Windows 11 pnputil 把「驱动程序日期 + 版本」放在同一行，如 "02/25/2016 6.2.2600.0"
                            ApplyDateVersion(cur, CleanValue(value), takeVersion: true);
                            break;
                        case "Signer":        cur.Signer = CleanValue(value); break;
                        case "WhcpLevel":     cur.WhcpLevel = CleanValue(value); break;
                        case "BootCritical":  cur.BootCritical = IsAffirmative(value); break;
                            // Locale 暂未使用，保留解析容错以避免干扰
                    }
                }
            }
            if (cur != null) list.Add(cur);

            // 丢弃没有发布名称的脏记录
            list.RemoveAll(d => string.IsNullOrWhiteSpace(d.OemName));

            return list;
        }

        /// <summary>
        /// 我们自己的英文→中文设备类兜底映射。
        /// 用途：某些设备类在中文 Windows 下没有官方本地化名称（如 Extension、Network Service、libusbK 等），
        /// 系统 API 与注册表都只能取到英文，此时用本字典给出可读的中文。
        /// 仅在系统已给出的 ClassDescription 恰好等于某英文键时才替换，避免覆盖系统已有的中文本地化名。
        /// </summary>
        private static readonly Dictionary<string, string> EnClassFallbackZh =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Extension", "扩展" },
                { "Network Service", "网络服务" },
                { "Printer", "打印机" },
                { "libusbK Usb Devices", "libusbK USB 设备" },
                { "libusb-win32 devices", "libusb-win32 设备" },
                { "DTS", "DTS 音频" },
                { "F5 Networks", "F5 网络" },
                { "Android Device", "安卓设备" },
                { "Usb Device", "USB 设备" },
                { "Usb Devices", "USB 设备" },
            };

        /// <summary>通过注册表 ClassName 获取本地化类名，失败再回退原 Class，最后用我们自己的英文→中文兜底映射（无 P/Invoke 依赖）。</summary>
        private static void ResolveClassDescriptions(List<DriverInfo> list)
        {
            // 纯 WMI + 注册表兜底：直接读注册表 ClassName（已移除 CfgMgr32/SetupAPI P/Invoke 依赖）。
            foreach (var d in list)
            {
                if (string.IsNullOrWhiteSpace(d.ClassGuid)) continue;
                string keyName = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\" + d.ClassGuid;
                try
                {
                    var className = SanitizeText(Registry.GetValue(keyName, "ClassName", null) as string ?? "");
                    if (!string.IsNullOrWhiteSpace(className))
                        d.ClassDescription = className;
                }
                catch { }
            }

            // 最终兜底：对仍保留英文 ClassDescription 的项套用我们自己的映射。
            // 只在当前 ClassDescription 与某英文键匹配时才替换，避免覆盖系统已有的中文本地化名。
            foreach (var d in list)
            {
                if (EnClassFallbackZh.TryGetValue(d.ClassDescription, out var cn))
                    d.ClassDescription = cn;
            }
        }

        /// <summary>
        /// 近似获取驱动安装日期。
        /// PnPUtil 发布名（oemX.inf）在 FileRepository 中没有同名目录；目录实际以
        /// 原始 INF 名（如 adafruitcircuitplayground.inf_amd64_... 或 ialpss2_gpio2_adl.inf_amd64_...）命名，
        /// 故按 OriginalName 前缀匹配。原始名中可能包含下划线，必须以 ".inf_" 为分隔符。
        /// </summary>
        private static void ResolveInstallDates(List<DriverInfo> list)
        {
            string repo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\DriverStore\FileRepository");
            if (!Directory.Exists(repo)) return;

            // 一次性缓存原始名前缀 -> 目录创建时间
            var prefixDates = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(repo))
                {
                    try
                    {
                        string name = Path.GetFileName(dir);
                        if (!TryExtractInfPrefix(name, out string prefix)) continue;
                        var di = new DirectoryInfo(dir);
                        if (!prefixDates.ContainsKey(prefix))
                            prefixDates[prefix] = di.CreationTime;
                    }
                    catch { }
                }
            }
            catch { return; }

            foreach (var d in list)
            {
                if (string.IsNullOrWhiteSpace(d.OriginalName)) continue;
                if (prefixDates.TryGetValue(d.OriginalName, out var dt))
                    d.InstallDate = dt;
            }
        }

        /// <summary>从 FileRepository 目录名中提取原始 INF 名前缀；原始名可含下划线，以 ".inf_" 为界。</summary>
        private static bool TryExtractInfPrefix(string dirName, out string prefix)
        {
            prefix = null;
            if (string.IsNullOrWhiteSpace(dirName)) return false;
            int idx = dirName.IndexOf(".inf_", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                prefix = dirName.Substring(0, idx + 4);
                return true;
            }
            if (dirName.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
            {
                prefix = dirName;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 解析设备名称（SetupAPI 为主、WMI 兜底，双保险）。
        ///   主源 SetupAPI：经 SetupDiGetDeviceRegistryPropertyW(SPDRP_*) 枚举在役设备，
        ///     取 DeviceDesc/FriendlyName，并以 SPDRP_DRIVER→注册表 InfPath 建 oem 名→设备名映射；
        ///     实机验证设备描述命中 279/280、oem 名 261/280。
        ///   兜底 WMI：Win32_PnPSignedDriver 按 InfName 取 DeviceName；Win32_PnPEntity 按 PNPDeviceID 末段兜底。
        /// 匹配优先级 SetupAPI(OemName) > SetupAPI(OriginalName) > WMI(OemName) > WMI(OriginalName)。
        /// 无法补充真实设备名的驱动包（旧版本/未加载驱动，确无活跃设备）再经 Provider+类描述 兜底，保证整列可读。
        /// </summary>
        private static void ResolveDeviceNames(List<DriverInfo> list, Action<string> log = null)
        {
            // 主源：SetupAPI（实机验证设备描述命中 279/280）。异常时返回空字典，自动回退 WMI（双保险）。
            Dictionary<string, string> setupApiMap;
            try { setupApiMap = BuildDeviceNameMapViaSetupApi(); }
            catch (Exception ex)
            {
                setupApiMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                log?.Invoke("[*] SetupAPI 取设备名失败，回退 WMI：" + ex.Message);
            }

            var wmiMap = BuildDeviceNameMapViaWmi();   // 键为 Win32_PnPSignedDriver.InfName（多为 oem 名，部分系统为原始名）+ PnPEntity 硬件 ID

            int fromSetup = 0, fromWmi = 0, fromFallback = 0;
            foreach (var d in list)
            {
                // 优先级：SetupAPI(OemName) > SetupAPI(OriginalName) > WMI(OemName) > WMI(OriginalName)。
                // 多键容错：InfName 在不同环境下可能是 oem 名或原始名，任一命中即采用设备名。
                string name = null;
                if (!string.IsNullOrWhiteSpace(d.OemName) && setupApiMap.TryGetValue(d.OemName, out name) && !string.IsNullOrWhiteSpace(name)) { d.DeviceName = name; fromSetup++; }
                else if (!string.IsNullOrWhiteSpace(d.OriginalName) && setupApiMap.TryGetValue(d.OriginalName, out name) && !string.IsNullOrWhiteSpace(name)) { d.DeviceName = name; fromSetup++; }
                else if (!string.IsNullOrWhiteSpace(d.OemName) && wmiMap.TryGetValue(d.OemName, out name) && !string.IsNullOrWhiteSpace(name)) { d.DeviceName = name; fromWmi++; }
                else if (!string.IsNullOrWhiteSpace(d.OriginalName) && wmiMap.TryGetValue(d.OriginalName, out name) && !string.IsNullOrWhiteSpace(name)) { d.DeviceName = name; fromWmi++; }
                else
                {
                    // 追加兜底：真实设备名缺失（多为无在役设备的旧版/未加载驱动包）时，
                    // 用驱动包自带的 提供程序 + 类描述 合成可读名称，避免整列显示"—"。
                    var fb = BuildDeviceFallbackName(d);
                    if (!string.IsNullOrWhiteSpace(fb)) { d.DeviceName = fb; fromFallback++; }
                }
            }

            int total = list.Count;
            int named = list.Count(d => !string.IsNullOrWhiteSpace(d.DeviceName));
            log?.Invoke($"[i] 设备名称补充(SetupAPI {fromSetup} + WMI {fromWmi} + 兜底 {fromFallback})：{named}/{total} 个驱动包已匹配到名称" +
                        (total - named > 0 ? $"（其中 {fromFallback} 个为提供程序/类描述兜底（非实时设备名），{total - named} 个仍无名称）。" : "。"));

            // 诊断：仅当匹配为 0 时打印样本，辅助确认键类型与驱动名是否一致（Release 下也输出，但只在失败时，避免噪音）。
            if (named == 0 && wmiMap.Count > 0)
            {
                var keysSample = string.Join(", ", wmiMap.Keys.Take(3));
                var drvSample = string.Join(" | ", list.Take(3).Select(d => $"OEM={d.OemName}/ORIG={d.OriginalName}"));
                log?.Invoke($"[diag] 设备名 0 匹配：wmiMap 样例键=[{keysSample}]；驱动样例=[{drvSample}]");
            }
        }

        /// <summary>用驱动包自带的 提供程序(Provider) + 类描述(ClassDescription) 合成兜底名称。
        /// 仅在前述 SetupAPI/WMI 真实设备名都缺失时调用，保证无在役设备的驱动包也有可读名称而非"—"。</summary>
        private static string BuildDeviceFallbackName(DriverInfo d)
        {
            var parts = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(d.Provider)) parts.Add(d.Provider);
            if (!string.IsNullOrWhiteSpace(d.ClassDescription)) parts.Add(d.ClassDescription);
            else if (!string.IsNullOrWhiteSpace(d.Class)) parts.Add(d.Class);
            return parts.Count == 0 ? null : string.Join(" ", parts);
        }

        /// <summary>WMI 兜底：Win32_PnPSignedDriver 按 InfName（多为 oem 名，部分系统为原始名）；Win32_PnPEntity 按 PNPDeviceID 末段硬件 ID。键类型由调用方（ResolveDeviceNames）用多键容错匹配。</summary>
        private static Dictionary<string, string> BuildDeviceNameMapViaWmi()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceName, InfName FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        string inf = mo["InfName"]?.ToString() ?? "";
                        string dev = SanitizeText(mo["DeviceName"]?.ToString() ?? "");
                        if (!string.IsNullOrWhiteSpace(inf) && !string.IsNullOrWhiteSpace(dev) && !map.ContainsKey(inf))
                            map[inf] = dev;
                    }
                }

                try
                {
                    using (var searcher = new ManagementObjectSearcher(
                        "SELECT Name, Description, FriendlyName, PNPDeviceID FROM Win32_PnPEntity WHERE Name IS NOT NULL"))
                    {
                        foreach (ManagementObject mo in searcher.Get())
                        {
                            string pnpId = mo["PNPDeviceID"]?.ToString() ?? "";
                            string name = mo["Name"]?.ToString() ?? "";
                            string friendly = mo["FriendlyName"]?.ToString() ?? "";
                            string desc = mo["Description"]?.ToString() ?? "";
                            string chosen = SanitizeText(!string.IsNullOrWhiteSpace(friendly) ? friendly :
                                                         (!string.IsNullOrWhiteSpace(name) ? name : desc));
                            if (!string.IsNullOrWhiteSpace(pnpId) && !string.IsNullOrWhiteSpace(chosen))
                            {
                                int lastSep = pnpId.LastIndexOf('\\');
                                string infHint = lastSep >= 0 ? pnpId.Substring(lastSep + 1) : pnpId;
                                if (!string.IsNullOrWhiteSpace(infHint) && !map.ContainsKey(infHint))
                                    map[infHint] = chosen;
                            }
                        }
                    }
                }
                catch { }
            }
            catch { }
            return map;
        }

        /// <summary>把 DISM 的 KEY|VALUE 文本解析成 DriverInfo 列表（按行解析，鲁棒）。</summary>
        private static List<DriverInfo> ParseDismOutput(string text, Action<string> log)
        {
            var list = new List<DriverInfo>();
            DriverInfo cur = null;
            foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                int idx = line.IndexOf('|');
                if (idx <= 0) continue;
                var key = line.Substring(0, idx).Trim().ToUpperInvariant();
                var value = line.Substring(idx + 1).Trim();
                switch (key)
                {
                    case "DRIVER":
                        // Get-WindowsDriver 的 Driver 字段是原始 INF 名（如 1394.inf），不是 oemX.inf
                        if (cur != null) list.Add(cur);
                        cur = new DriverInfo { IsDism = true, OriginalName = CleanValue(value) };
                        break;
                    case "ORIGINALFILENAME":
                        // 取路径中的文件名作为原始名 fallback
                        if (cur != null)
                        {
                            var fn = Path.GetFileName(CleanValue(value));
                            if (!string.IsNullOrWhiteSpace(fn)) cur.OriginalName = fn;
                        }
                        break;
                    case "PROVIDERNAME": if (cur != null) cur.Provider = CleanValue(value); break;
                    case "CLASSNAME":    if (cur != null) { cur.Class = CleanValue(value); if (string.IsNullOrWhiteSpace(cur.ClassDescription)) cur.ClassDescription = cur.Class; } break;
                    case "CLASSDESCRIPTION": if (cur != null) cur.ClassDescription = CleanValue(value); break;
                    case "CLASSGUID":    if (cur != null) cur.ClassGuid = CleanValue(value); break;
                    case "DATE":         if (cur != null) cur.Date = ParseDate(CleanValue(value)); break;
                    case "VERSION":      if (cur != null) cur.Version = CleanValue(value); break;
                    case "BOOTCRITICAL": if (cur != null) cur.BootCritical = IsAffirmative(value); break;
                    case "DRIVERSIGNATURE": if (cur != null) cur.Signer = CleanValue(value); break;
                }
            }
            if (cur != null) list.Add(cur);

            // 丢弃没有原始名称的脏记录（DISM 没有 oem 发布名）
            list.RemoveAll(d => string.IsNullOrWhiteSpace(d.OriginalName));
            return list;
        }

        /// <summary>
        /// 处理 pnputil 的「驱动程序版本」字段同时包含日期和版本的现象（"02/25/2016 6.2.2600.0"）。
        /// 优先从第一个空格拆分：前面是日期，后面是版本。若只有日期/版本则只取对应部分。
        /// </summary>
        private static void ApplyDateVersion(DriverInfo d, string value, bool takeDate = false, bool takeVersion = false)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            int sp = value.IndexOf(' ');
            if (sp > 0)
            {
                var datePart = value.Substring(0, sp).Trim();
                var verPart = value.Substring(sp + 1).Trim();
                if (takeDate || !d.Date.HasValue) d.Date = ParseDate(datePart);
                if (takeVersion || string.IsNullOrWhiteSpace(d.Version)) d.Version = verPart;
            }
            else
            {
                if (takeDate) d.Date = ParseDate(value);
                if (takeVersion) d.Version = value;
            }
        }

        /// <summary>判断值是肯定（是/Yes/True/1）。</summary>
        private static bool IsAffirmative(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return false;
            v = v.Trim();
            return v == "是"
                || string.Equals(v, "Yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "True", StringComparison.OrdinalIgnoreCase)
                || v == "1";
        }

        private static string CleanValue(string v)
        {
            if (v == null) return "";
            // 去掉可能混入的换行（挤行场景下值内不应有换行，但保险起见归一）
            return SanitizeText(v.Replace("\r", "").Replace("\n", " ").Trim());
        }

        /// <summary>清理外部来源字符串：过滤控制字符、替换字符和私有区乱码，减少在 WPF 中显示为菱形问号的情况。</summary>
        private static string SanitizeText(string v)
        {
            if (string.IsNullOrEmpty(v)) return v;
            var sb = new System.Text.StringBuilder(v.Length);
            int suspicious = 0;
            foreach (char c in v)
            {
                // Unicode 替换字符（U+FFFD）或 C0/C1 控制字符（除常见空白外）视为乱码
                if (c == '\uFFFD' || (char.IsControl(c) && c != '\t' && c != '\n' && c != '\r'))
                { sb.Append(' '); suspicious++; continue; }
                // 私有使用区字符在设备名/类名中通常是编码错误的产物
                if (c >= '\uE000' && c <= '\uF8FF')
                { sb.Append(' '); suspicious++; continue; }
                sb.Append(c);
            }
            // 若可疑字符占比过高，直接返回空，由调用方显示占位符
            if (suspicious > 0 && (double)suspicious / v.Length > 0.25)
                return "";
            return sb.ToString().Trim();
        }

        private static DateTime? ParseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            // 兼容 2023/10/17、2023-10-17、10/17/2023 等
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1)) return d1;
            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d2)) return d2;
            return null;
        }

        /// <summary>
        /// 通过 WMI（Win32_PnPSignedDriver）取得当前"在役"设备的 INF 名集合。
        /// 注意：WMI 的 InfName 在本机通常返回 oem 发布名（oemX.inf），但为兼容也保留原始名匹配；
        /// 调用方用 (OemName || OriginalName) 双键判断在役，避免"在役保护"失效。
        /// queryOk 指示"查询本身是否成功"（退出码 0 且无 stderr 且无异常）；
        /// 结果为空但查询成功时 queryOk 仍为 true，由调用方按 fail-closed 规则决定如何保护。
        /// </summary>
        internal static HashSet<string> GetActiveInfNames(Action<string> log, out bool queryOk)
        {
            queryOk = false;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var script = "(Get-CimInstance -ClassName Win32_PnPSignedDriver -ErrorAction SilentlyContinue).InfName | Where-Object { $_ }";
                var (exitCode, stdout, stderr) = Exec.RunPowerShellGetFull(script, log);
                // 修复：区分"查询成功但结果为空"与"查询失败/异常"——旧版只看输出是否为空，
                // 查询失败时无法区分，导致在役保护被整体跳过（fail-open）。
                // 退出码非 0 或 stderr 非空（或抛异常）均视为查询失败。
                queryOk = exitCode == 0 && string.IsNullOrWhiteSpace(stderr);
                if (!queryOk)
                    log?.Invoke($"[!] 在役驱动查询失败（退出码 {exitCode}），将按在役保护处理。");
                var outp = stdout ?? "";
                foreach (var line in outp.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var name = line.Trim();
                    if (name.Length > 0) set.Add(name);
                }
            }
            catch (Exception ex)
            {
                queryOk = false;
                log?.Invoke("[!] 在役驱动查询异常，将按在役保护处理：" + ex.Message);
            }
            return set;
        }

        /// <summary>
        /// 标记在役驱动：把在役设备对应的原始名系列中"最新版"标记为 InUse（受保护）。
        /// 若某系列在役，仅最新版不可删；其余旧版仍标记为可清理（与 RAPR 行为一致，安全）。
        /// ★ 修复：改为 fail-closed（失败即保护）。查询失败或结果为空时，一律把所有驱动
        ///   标记为 InUse=true 并阻断删除——空结果极可能是查询失败而非真的没有在役驱动，
        ///   宁可不让删，也不能让用户删掉正在使用的驱动（配合 /force 更危险）。
        /// </summary>
        internal static void DetectInUse(List<DriverInfo> list, Action<string> log)
        {
            if (list == null || list.Count == 0) return;

            bool queryOk;
            var active = GetActiveInfNames(log, out queryOk);
            if (!queryOk || active.Count == 0)
            {
                // 查询失败 / 结果为空：全部按在役保护处理，且不进入可删除列表
                foreach (var d in list)
                {
                    d.InUse = true;
                    d.IsOld = false;
                    d.Selected = false;
                }
                log?.Invoke(queryOk
                    ? "[!] 在役驱动查询结果为空（正常情况下至少有一个在役驱动，结果可疑）→ 已按在役保护处理：本次全部驱动禁止删除。请重新枚举后再试。"
                    : "[!] 在役驱动查询失败 → 已按在役保护处理：本次全部驱动禁止删除（宁可不让删，也不能误删在役驱动）。");
                return;
            }

            // 先标记同系列最新版
            MarkNewestPerFamily(list);

            int protectedCount = 0;
            foreach (var d in list)
            {
                // 与 ResolveDeviceNames 同样的多键匹配：WMI InfName 可能为 oem 名或原始名，
                // 故 OemName、OriginalName 任一命中即视为在役（受保护）。仅增条件，不会减少保护，更安全。
                if ((active.Contains(d.OemName) || active.Contains(d.OriginalName)) && !d.IsOld)
                {
                    d.InUse = true;
                    d.Selected = false;   // 在役驱动不进入可删除勾选集合
                    protectedCount++;
                }
            }
            log?.Invoke($"[✓] 在役检测：{protectedCount} 个驱动包正被设备使用，已设为保护（不可删除）。");
        }

        /// <summary>
        /// 同原始名系列下，仅最新版标记为非旧（IsOld=false），其余标记 IsOld=true。
        /// 比较规则：先比版本号（按点分整数），再比日期；都无法判定时按发布名称排序兜底。
        /// </summary>
        internal static void MarkOldVersions(List<DriverInfo> list)
        {
            MarkNewestPerFamily(list);
        }

        private static void MarkNewestPerFamily(List<DriverInfo> list)
        {
            var groups = list.GroupBy(d => d.OriginalName, StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                var items = g.ToList();
                if (items.Count <= 1) { items[0].IsOld = false; continue; }
                DriverInfo best = items[0];
                foreach (var it in items.Skip(1))
                {
                    if (IsNewer(it, best)) best = it;
                }
                foreach (var it in items) it.IsOld = !ReferenceEquals(it, best);
            }
        }

        /// <summary>判断 a 是否比 b 更新（版本号优先，其次日期）。</summary>
        private static bool IsNewer(DriverInfo a, DriverInfo b)
        {
            int vc = VersionUtil.CompareVersion(a.Version, b.Version);
            if (vc != 0) return vc > 0;
            if (a.Date.HasValue && b.Date.HasValue) return a.Date.Value > b.Date.Value;
            if (a.Date.HasValue != b.Date.HasValue) return a.Date.HasValue; // 有日期者更新
            // 兜底：发布名称字符串比较（oem 序号越大通常越新）
            return string.Compare(a.OemName, b.OemName, StringComparison.OrdinalIgnoreCase) > 0;
        }

        /// <summary>
        /// 估算每个驱动包（按原始名系列）在 DriverStore\FileRepository 中的占用。
        /// 注意：同系列多版本共享原始名前缀，此处返回的是"该系列合计占用"（近似值），
        /// 并非单版本精确值——精确值需 SetupAPI，超出本工具范围；UI 中以"约"标注。
        /// </summary>
        internal static void EstimateSizes(List<DriverInfo> list, Action<string> log)
        {
            string repo = null;
            try
            {
                var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                repo = Path.Combine(win, "System32", "DriverStore", "FileRepository");
            }
            catch { }

            if (string.IsNullOrEmpty(repo) || !Directory.Exists(repo))
            {
                log?.Invoke("[*] 未找到驱动存储目录，跳过体积估算。");
                return;
            }

            // 一次扫描，按原始名前缀（foo.inf）累加各版本文件夹大小
            var prefixSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var dir in Directory.GetDirectories(repo))
                {
                    var name = Path.GetFileName(dir);
                    if (!TryExtractInfPrefix(name, out var prefix)) continue;
                    try { prefixSizes[prefix] = prefixSizes.TryGetValue(prefix, out var s) ? s + DirSize(dir) : DirSize(dir); }
                    catch { }
                }
            }
            catch (Exception ex) { log?.Invoke("[!] 扫描驱动存储失败：" + ex.Message); }

            foreach (var d in list)
            {
                if (prefixSizes.TryGetValue(d.OriginalName, out var bytes))
                    d.SizeMB = bytes / (1024.0 * 1024.0);
            }
        }

        private static long DirSize(string dir)
        {
            long total = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; }
                    catch { }
                }
            }
            catch { }
            return total;
        }

        /// <summary>
        /// 删除一组驱动包。
        /// 护栏：在役（InUse）始终跳过；启动关键（BootCritical）除非 includeBootCritical 否则跳过。
        /// force=true 时使用 /delete-driver &lt;oem&gt; /force（可删除仍在引用的包，危险）。
        /// 返回结构化结果（成功 / 跳过 / 失败计数）。
        /// </summary>
        internal static DeleteResult Delete(List<DriverInfo> items, Action<string> log, bool force = false, bool includeBootCritical = false)
        {
            var res = new DeleteResult();
            if (items == null || items.Count == 0) { log?.Invoke("[!] 没有可删除的驱动。"); return res; }

            foreach (var d in items)
            {
                // DISM 后端来源驱动没有 oemX.inf 发布名，依赖 oem 名的删除命令会失败，提前跳过
                if (d.IsDism || string.IsNullOrWhiteSpace(d.OemName))
                {
                    log?.Invoke($"[!] 跳过（DISM 驱动不支持该操作）：{d.OriginalName}");
                    res.Failed++;
                    continue;
                }
                if (d.InUse)
                {
                    log?.Invoke($"[!] 跳过（在役/受保护）：{d.OemName}（{d.OriginalName}）— 正在被设备使用，删除可能导致设备失效。");
                    res.SkippedProtected++;
                    continue;
                }
                if (d.BootCritical && !includeBootCritical)
                {
                    log?.Invoke($"[!] 跳过（启动关键）：{d.OemName}（{d.OriginalName}）— 删除可能导致系统无法启动。");
                    res.SkippedBootCritical++;
                    continue;
                }
                log?.Invoke($"[*] 删除 {d.OemName}（{d.OriginalName} {d.Version}）…");
                int code = Exec.RunCmd(
                    force
                        ? new[] { "pnputil", "/delete-driver", d.OemName, "/force" }
                        : new[] { "pnputil", "/delete-driver", d.OemName },
                    log);
                if (code == 0) { log?.Invoke($"    [✓] 已删除 {d.OemName}。"); res.Succeeded++; }
                else { log?.Invoke($"    [!] 删除失败（退出码 {code}）：{d.OemName}。"); res.Failed++; }
            }

            log?.Invoke($"[✓] 删除结束：成功 {res.Succeeded}，跳过（在役保护）{res.SkippedProtected}，跳过（启动关键）{res.SkippedBootCritical}，失败 {res.Failed}。");
            return res;
        }

        /// <summary>
        /// 添加驱动包（pnputil /add-driver &lt;inf&gt; /subdirs）。infPath 为 .inf 文件路径，
        /// /subdirs 会一并添加与 .inf 同目录内的相关驱动文件。
        /// </summary>
        internal static bool AddDriver(string infPath, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(infPath)) { log?.Invoke("[!] 未指定驱动 INF 文件。"); return false; }
            if (!File.Exists(infPath)) { log?.Invoke("[!] 文件不存在：" + infPath); return false; }
            log?.Invoke($"[*] 添加驱动包：{infPath}（含子目录）…");
            int code = Exec.RunCmd(new[] { "pnputil", "/add-driver", infPath, "/subdirs" }, log);
            if (code == 0) { log?.Invoke($"[✓] 已添加 {Path.GetFileName(infPath)}。"); return true; }
            log?.Invoke($"[!] 添加失败（退出码 {code}）。"); return false;
        }

        /// <summary>
        /// 把已添加的驱动包安装到匹配的设备（pnputil /install-driver &lt;oem.inf&gt;）。
        /// 仅对系统中存在匹配硬件的驱动有效；无匹配设备时会报错，失败时返回 false。
        /// </summary>
        internal static bool InstallDriver(string oemName, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(oemName)) { log?.Invoke("[!] 未指定驱动包。"); return false; }
            log?.Invoke($"[*] 安装驱动到匹配设备：{oemName}…");
            int code = Exec.RunCmd(new[] { "pnputil", "/install-driver", oemName }, log);
            if (code == 0) { log?.Invoke($"[✓] 已触发安装 {oemName}。"); return true; }
            log?.Invoke($"[!] 安装失败（退出码 {code}）：可能无匹配设备，或驱动尚未正确添加。"); return false;
        }

        /// <summary>
        /// 导出备份一组驱动包到目标目录（pnputil /export-driver）。
        /// 逐个导出以便逐条报告结果；目标目录不存在时尝试创建。
        /// </summary>
        internal static void Export(List<DriverInfo> items, string dir, Action<string> log)
        {
            if (items == null || items.Count == 0) { log?.Invoke("[!] 没有可导出的驱动。"); return; }
            if (string.IsNullOrWhiteSpace(dir)) { log?.Invoke("[!] 未指定导出目录。"); return; }

            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception ex) { log?.Invoke("[!] 无法创建导出目录：" + ex.Message); return; }

            int ok = 0, fail = 0;
            foreach (var d in items)
            {
                // DISM 后端来源驱动没有 oemX.inf 发布名，依赖 oem 名的导出命令会失败，提前跳过
                if (d.IsDism || string.IsNullOrWhiteSpace(d.OemName))
                {
                    log?.Invoke($"[!] 跳过（DISM 驱动不支持该操作）：{d.OriginalName}");
                    fail++;
                    continue;
                }
                log?.Invoke($"[*] 导出 {d.OemName}（{d.OriginalName}） → {dir}");
                int code = Exec.RunCmd(new[] { "pnputil", "/export-driver", d.OemName, dir }, log);
                if (code == 0) { log?.Invoke($"    [✓] 已导出 {d.OemName}。"); ok++; }
                else { log?.Invoke($"    [!] 导出失败（退出码 {code}）：{d.OemName}。"); fail++; }
            }
            log?.Invoke($"[✓] 导出结束：成功 {ok}，失败 {fail}。备份位于：{dir}");
        }

        /// <summary>格式化占用大小为友好字符串。</summary>
        internal static string FormatSize(double mb)
        {
            if (mb >= 1024) return (mb / 1024.0).ToString("F2") + " GB";
            if (mb >= 1) return mb.ToString("F0") + " MB";
            return (mb * 1024.0).ToString("F0") + " KB";
        }

    }
}

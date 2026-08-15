using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace StandaloneSetupApiTester
{
    /// <summary>
    /// Minimal, isolated SetupAPI verification program.
    /// Enumerates present devices (DIGCF_ALLCLASSES | DIGCF_PRESENT) and prints,
    /// per device:  instance id | oem publish name (oemX.inf) | device desc | friendly name.
    /// Goal: confirm the SetupAPI P/Invoke can retrieve device descriptions + oem inf on this
    /// Windows box, before re-integrating into the main CpqSystemTool project.
    ///
    /// v2: the modern SetupDiGetDevicePropertyW (DEVPROPKEY) path returned 0/277 on real
    /// hardware (every property, incl. DeviceDesc, came back null). This build collects the
    /// data via the classic, proven SetupDiGetDeviceRegistryPropertyW (SPDRP_*) API instead,
    /// and keeps a first-device diagnostic that prints the exact error code of BOTH APIs so
    /// the failure mode is proven rather than assumed.
    ///
    /// Output is teed to the console and a result .txt next to the exe; the console waits for
    /// a keypress so a double-click does not flash-and-close.
    /// </summary>
    class Program
    {
        // ---- SetupAPI flags ----
        private const uint DIGCF_DEFAULT = 0x00000001;
        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_ALLCLASSES = 0x00000004;
        private const uint DIGCF_PROFILE = 0x00000008;
        private const uint DIGCF_DEVICEINTERFACE = 0x00000010;

        // ---- Win32 error codes ----
        private const int ERROR_NO_MORE_ITEMS = 259;        // 0x103
        private const int ERROR_INSUFFICIENT_BUFFER = 122;  // 0x7A
        private const int ERROR_NOT_FOUND = 1168;           // 0x490

        // ---- SPDRP property ids (setupapi.h) ----
        private const uint SPDRP_DEVICEDESC = 0x00000000;   // REG_SZ device description
        private const uint SPDRP_FRIENDLYNAME = 0x0000000C; // REG_SZ friendly name
        private const uint SPDRP_DRIVER = 0x00000009;       // REG_SZ driver key (e.g. {guid}\nnnn)

        // ---- Registry access ----
        private static readonly IntPtr HKEY_LOCAL_MACHINE = new IntPtr(0x80000002);
        private const uint KEY_READ = 0x20019;

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        // ===== Native structures =====

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public int cbSize;       // MUST be Marshal.SizeOf<SP_DEVINFO_DATA>()
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;  // ULONG_PTR -> platform sized
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVPKEY
        {
            public Guid fmtid;  // 16 bytes
            public uint pid;    // 4 bytes
        }

        // ===== Exact DEVPKEY constants (from devpkey.h) — kept for diagnostic only =====
        private static readonly DEVPKEY DEVPKEY_Device_DeviceDesc =
            new DEVPKEY { fmtid = new Guid("A45C254E-DF1C-4EF2-BC69-99F71A2A1A2F"), pid = 2 };

        // ===== P/Invoke =====

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
        private static extern bool SetupDiGetDeviceInstanceIdW(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder DeviceInstanceId,
            uint DeviceInstanceIdSize,
            out uint RequiredSize);

        // Modern DEVPROPKEY-based API (diagnostic only; proved unreliable on this box)
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDevicePropertyW(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData,
            ref DEVPKEY PropertyKey,
            out uint PropertyType,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)] byte[] PropertyBuffer,
            uint PropertyBufferSize,
            out uint RequiredSize,
            uint Flags);

        // Classic, proven registry-property API (used for real data collection)
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

        // ===== Helpers =====

        private static string GetDeviceInstanceId(IntPtr devInfoSet, ref SP_DEVINFO_DATA data)
        {
            uint requiredSize;
            SetupDiGetDeviceInstanceIdW(devInfoSet, ref data, null, 0, out requiredSize);
            int err = Marshal.GetLastWin32Error();
            if (requiredSize == 0 || (err != 0 && err != ERROR_INSUFFICIENT_BUFFER))
                return null;
            var sb = new StringBuilder((int)requiredSize);
            if (!SetupDiGetDeviceInstanceIdW(devInfoSet, ref data, sb, requiredSize, out _))
                return null;
            return sb.ToString();
        }

        /// <summary>Read a REG_SZ device property via the classic SPDRP API.</summary>
        private static string GetRegistryPropertyString(IntPtr devInfoSet, ref SP_DEVINFO_DATA data, uint property)
        {
            uint regType;
            uint requiredSize;
            bool first = SetupDiGetDeviceRegistryPropertyW(devInfoSet, ref data, property, out regType, null, 0, out requiredSize);
            int err = Marshal.GetLastWin32Error();
            if (!first && err != ERROR_INSUFFICIENT_BUFFER)
                return null;
            if (requiredSize == 0)
                return null;

            var buf = new byte[requiredSize];
            uint actualSize;
            if (!SetupDiGetDeviceRegistryPropertyW(devInfoSet, ref data, property, out regType, buf, requiredSize, out actualSize))
                return null;

            string s = Encoding.Unicode.GetString(buf, 0, (int)requiredSize);
            int nullIdx = s.IndexOf('\0');
            return nullIdx >= 0 ? s.Substring(0, nullIdx) : s;
        }

        /// <summary>SPDRP_DRIVER returns a driver key like "{guid}\nnnn"; read its InfPath value.</summary>
        private static string ReadInfPathFromRegistry(string driverKey)
        {
            IntPtr hKey = IntPtr.Zero;
            try
            {
                string subKey = @"SYSTEM\CurrentControlSet\Control\Class\" + driverKey;
                int rc = RegOpenKeyExW(HKEY_LOCAL_MACHINE, subKey, 0, KEY_READ, out hKey);
                if (rc != 0)
                    return null;
                uint type;
                uint size = 256;
                var data = new byte[size];
                rc = RegQueryValueExW(hKey, "InfPath", IntPtr.Zero, out type, data, ref size);
                if (rc != 0)
                    return null;
                string s = Encoding.Unicode.GetString(data, 0, (int)size);
                int idx = s.IndexOf('\0');
                return idx >= 0 ? s.Substring(0, idx) : s;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (hKey != IntPtr.Zero)
                    RegCloseKey(hKey);
            }
        }

        // ===== First-device diagnostic: prove which API works =====

        private static void DiagFirstDevice(IntPtr devInfoSet, ref SP_DEVINFO_DATA data)
        {
            Console.WriteLine("---- [DIAG] 首设备：对比两套属性读取 API ----");
            // Modern DEVPROPKEY API
            {
                DEVPKEY k = DEVPKEY_Device_DeviceDesc;
                uint pt; uint req;
                bool f1 = SetupDiGetDevicePropertyW(devInfoSet, ref data, ref k, out pt, null, 0, out req, 0);
                int e1 = Marshal.GetLastWin32Error();
                Console.WriteLine("  SetupDiGetDevicePropertyW(DeviceDesc): first=" + f1 + " err1=" + e1 + " reqSize=" + req);
                if (f1 || e1 == ERROR_INSUFFICIENT_BUFFER)
                {
                    var buf = new byte[req == 0 ? 2 : req];
                    uint act;
                    bool f2 = SetupDiGetDevicePropertyW(devInfoSet, ref data, ref k, out pt, buf, (uint)buf.Length, out act, 0);
                    int e2 = Marshal.GetLastWin32Error();
                    Console.WriteLine("    second=" + f2 + " err2=" + e2);
                }
            }
            // Classic SPDRP API
            {
                uint rt; uint req;
                bool f1 = SetupDiGetDeviceRegistryPropertyW(devInfoSet, ref data, SPDRP_DEVICEDESC, out rt, null, 0, out req);
                int e1 = Marshal.GetLastWin32Error();
                Console.WriteLine("  SetupDiGetDeviceRegistryPropertyW(SPDRP_DEVICEDESC): first=" + f1 + " err1=" + e1 + " reqSize=" + req + " regType=" + rt);
                if (f1 || e1 == ERROR_INSUFFICIENT_BUFFER)
                {
                    var buf = new byte[req == 0 ? 2 : req];
                    uint act;
                    bool f2 = SetupDiGetDeviceRegistryPropertyW(devInfoSet, ref data, SPDRP_DEVICEDESC, out rt, buf, (uint)buf.Length, out act);
                    int e2 = Marshal.GetLastWin32Error();
                    string val = act > 0 ? Encoding.Unicode.GetString(buf, 0, (int)act).TrimEnd('\0') : "";
                    Console.WriteLine("    second=" + f2 + " err2=" + e2 + " value=\"" + val + "\"");
                }
            }
            Console.WriteLine("---- [DIAG] end ----");
        }

        // ===== Tee writer =====

        private sealed class TeeWriter : TextWriter
        {
            private readonly TextWriter _console;
            private readonly TextWriter _file;
            public TeeWriter(TextWriter console, TextWriter file) { _console = console; _file = file; }
            public override Encoding Encoding => _console.Encoding;
            public override void Write(char value) { _console.Write(value); _file.Write(value); }
            public override void Write(string value) { _console.Write(value); _file.Write(value); }
            public override void WriteLine(string value) { _console.WriteLine(value); _file.WriteLine(value); }
            public override void WriteLine() { _console.WriteLine(); _file.WriteLine(); }
        }

        // ===== Core enumeration =====

        private static void RunCore()
        {
            Console.WriteLine("============================================================");
            Console.WriteLine(" Standalone SetupAPI Tester v2  (net48 / SetupAPI P/Invoke check)");
            Console.WriteLine(" 枚举当前存在设备: 实例ID | oem发布名 | 设备描述 | 友好名");
            Console.WriteLine("============================================================");
            Console.WriteLine("[INFO] SP_DEVINFO_DATA size=" + Marshal.SizeOf<SP_DEVINFO_DATA>() +
                              ", DEVPKEY size=" + Marshal.SizeOf<DEVPKEY>());

            IntPtr devInfoSet = IntPtr.Zero;
            try
            {
                devInfoSet = SetupDiGetClassDevsW(
                    IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);

                if (devInfoSet == INVALID_HANDLE_VALUE || devInfoSet == IntPtr.Zero)
                {
                    Console.WriteLine("[ERROR] SetupDiGetClassDevsW failed. LastError=" + Marshal.GetLastWin32Error());
                    return;
                }

                int total = 0;
                int gotOem = 0;
                int gotDesc = 0;
                uint index = 0;

                var devInfo = new SP_DEVINFO_DATA();
                devInfo.cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>();

                while (SetupDiEnumDeviceInfo(devInfoSet, index, ref devInfo))
                {
                    total++;

                    if (total == 1)
                        DiagFirstDevice(devInfoSet, ref devInfo);

                    string instanceId = GetDeviceInstanceId(devInfoSet, ref devInfo);
                    string desc = GetRegistryPropertyString(devInfoSet, ref devInfo, SPDRP_DEVICEDESC);
                    string friendly = GetRegistryPropertyString(devInfoSet, ref devInfo, SPDRP_FRIENDLYNAME);
                    string oemName = null;
                    string driverKey = GetRegistryPropertyString(devInfoSet, ref devInfo, SPDRP_DRIVER);
                    if (!string.IsNullOrEmpty(driverKey))
                        oemName = ReadInfPathFromRegistry(driverKey);

                    if (!string.IsNullOrEmpty(oemName)) gotOem++;
                    if (!string.IsNullOrEmpty(desc)) gotDesc++;

                    if (total <= 60)
                    {
                        Console.WriteLine(
                            (instanceId ?? "(无)") + " | " +
                            (oemName ?? "(无)") + " | " +
                            (desc ?? "(无)") + " | " +
                            (friendly ?? "(无)"));
                    }

                    index++;
                }

                int lastErr = Marshal.GetLastWin32Error();
                if (lastErr != ERROR_NO_MORE_ITEMS)
                    Console.WriteLine("[WARN] Enumeration stopped early. LastError=" + lastErr);

                Console.WriteLine("------------------------------------------------------------");
                Console.WriteLine("共枚举 " + total + " 个设备");
                Console.WriteLine("  经注册表 API 取到设备描述(DeviceDesc): " + gotDesc + " (" + gotDesc + "/" + total + ")");
                Console.WriteLine("  经驱动键值取到 oem 发布名: " + gotOem + " (" + gotOem + "/" + total + ")");
                if (total > 60)
                    Console.WriteLine("(仅显示前 60 个, 共 " + total + " 个)");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[EXCEPTION] " + ex.GetType().FullName + ": " + ex.Message);
            }
            finally
            {
                if (devInfoSet != IntPtr.Zero && devInfoSet != INVALID_HANDLE_VALUE)
                    SetupDiDestroyDeviceInfoList(devInfoSet);
            }
        }

        // ===== Main =====

        private static int Main()
        {
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            string logPath = Path.Combine(exeDir, "StandaloneSetupApiTester_result.txt");

            TextWriter fileWriter = null;
            try
            {
                fileWriter = new StreamWriter(logPath, false, Encoding.UTF8) { AutoFlush = true };
                Console.SetOut(new TeeWriter(Console.Out, fileWriter));

                RunCore();

                Console.WriteLine();
                Console.WriteLine("============================================================");
                Console.WriteLine("结果已保存到: " + logPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[EXCEPTION] " + ex.GetType().FullName + ": " + ex.Message);
                try { File.AppendAllText(logPath, "[EXCEPTION] " + ex + Environment.NewLine); } catch { }
            }
            finally
            {
                if (fileWriter != null)
                {
                    try { fileWriter.Flush(); } catch { }
                }
            }

            try
            {
                Console.WriteLine("按任意键退出...");
                Console.ReadKey(true);
            }
            catch
            {
                // no interactive console -> just exit
            }

            return 0;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SetupApiMatchTester
{
    /// <summary>
    /// 复刻主程序 DriverStore.BuildDeviceNameMapViaSetupApi 的取数 + 键映射逻辑，
    /// 并与 pnputil /enum-drivers 解析出的 oem 发布名求交集，定位「设备名仍为 40/94」的根因。
    /// 分别用 STA（模拟 WPF UI 线程）与 MTA（模拟后台线程/控制台）运行，对比是否公寓态相关。
    /// </summary>
    class Program
    {
        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_ALLCLASSES = 0x00000004;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;
        private const uint SPDRP_DEVICEDESC = 0x00000000;
        private const uint SPDRP_FRIENDLYNAME = 0x0000000C;
        private const uint SPDRP_DRIVER = 0x00000009;
        private static readonly IntPtr SETUP_INVALID_HANDLE = new IntPtr(-1);
        private static readonly IntPtr HKEY_LOCAL_MACHINE = new IntPtr(0x80000002);
        private const uint KEY_READ = 0x20019;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(IntPtr ClassGuid, [MarshalAs(UnmanagedType.LPWStr)] string Enumerator, IntPtr hwndParent, uint Flags);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, uint Property, out uint PropertyRegDataType, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)] byte[] PropertyBuffer, uint PropertyBufferSize, out uint RequiredSize);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegOpenKeyExW(IntPtr hKey, string lpSubKey, uint ulOptions, uint samDesired, out IntPtr phkResult);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegQueryValueExW(IntPtr hKey, string lpValueName, IntPtr lpReserved, out uint lpType, byte[] lpData, ref uint lpcbData);
        [DllImport("advapi32.dll")]
        private static extern int RegCloseKey(IntPtr hKey);

        private static Dictionary<string, string> BuildMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IntPtr devInfoSet = IntPtr.Zero;
            try
            {
                devInfoSet = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
                if (devInfoSet == SETUP_INVALID_HANDLE || devInfoSet == IntPtr.Zero) return map;
                uint index = 0;
                var devInfo = new SP_DEVINFO_DATA();
                devInfo.cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>();
                int enumCount = 0;
                while (SetupDiEnumDeviceInfo(devInfoSet, index, ref devInfo))
                {
                    index++; enumCount++;
                    try
                    {
                        string desc = GetReg(devInfoSet, devInfo, SPDRP_DEVICEDESC);
                        string friendly = GetReg(devInfoSet, devInfo, SPDRP_FRIENDLYNAME);
                        string driverKey = GetReg(devInfoSet, devInfo, SPDRP_DRIVER);
                        string infName = !string.IsNullOrWhiteSpace(driverKey) ? ReadInfPath(driverKey) : null;
                        string name = !string.IsNullOrWhiteSpace(friendly) ? friendly : (!string.IsNullOrWhiteSpace(desc) ? desc : null);
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (!string.IsNullOrWhiteSpace(infName) && !map.ContainsKey(infName)) map[infName] = name;
                    }
                    catch { }
                }
                if (enumCount == 0) Console.WriteLine("  [!] SetupDiEnumDeviceInfo 枚举到 0 个设备（可能 SetupDiGetClassDevsW 在 STA 下异常）");
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [EXCEPTION in BuildMap] " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (devInfoSet != IntPtr.Zero && devInfoSet != SETUP_INVALID_HANDLE) SetupDiDestroyDeviceInfoList(devInfoSet);
            }
            return map;
        }

        private static string GetReg(IntPtr ds, SP_DEVINFO_DATA d, uint p)
        {
            uint rt; uint req;
            if (!SetupDiGetDeviceRegistryPropertyW(ds, ref d, p, out rt, null, 0, out req))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != ERROR_INSUFFICIENT_BUFFER) return null;
            }
            if (req == 0) return null;
            var buf = new byte[req]; uint act;
            if (!SetupDiGetDeviceRegistryPropertyW(ds, ref d, p, out rt, buf, req, out act)) return null;
            string s = Encoding.Unicode.GetString(buf, 0, Math.Min((int)req, buf.Length));
            int i = s.IndexOf('\0');
            return i >= 0 ? s.Substring(0, i) : s;
        }

        private static string ReadInfPath(string driverKey)
        {
            IntPtr hKey = IntPtr.Zero;
            try
            {
                string sub = @"SYSTEM\CurrentControlSet\Control\Class\" + driverKey;
                int rc = RegOpenKeyExW(HKEY_LOCAL_MACHINE, sub, 0, KEY_READ, out hKey);
                if (rc != 0) return null;
                uint type; uint size = 256; var data = new byte[size];
                rc = RegQueryValueExW(hKey, "InfPath", IntPtr.Zero, out type, data, ref size);
                if (rc == 234) { data = new byte[size]; rc = RegQueryValueExW(hKey, "InfPath", IntPtr.Zero, out type, data, ref size); }
                if (rc != 0) return null;
                string s = Encoding.Unicode.GetString(data, 0, Math.Min((int)size, data.Length));
                int i = s.IndexOf('\0');
                return i >= 0 ? s.Substring(0, i) : s;
            }
            catch { return null; }
            finally { if (hKey != IntPtr.Zero) RegCloseKey(hKey); }
        }

        private static List<(string oem, string orig)> GetPnpUtilDrivers()
        {
            var list = new List<(string, string)>();
            try
            {
                var psi = new ProcessStartInfo("pnputil.exe", "/enum-drivers")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    // 按驱动块收集：每块含 发布名称 + 原始名称（顺序对齐）
                    var oemRe = new Regex(@"(?:发布名称|Published Name)\s*[:：]\s*(\S+)", RegexOptions.IgnoreCase);
                    var origRe = new Regex(@"(?:原始名称|Original Name)\s*[:：]\s*(\S+)", RegexOptions.IgnoreCase);
                    var oems = new List<string>();
                    var origs = new List<string>();
                    foreach (Match m in oemRe.Matches(outp))
                    {
                        var v = m.Groups[1].Value.Trim();
                        if (v.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)) oems.Add(v);
                    }
                    foreach (Match m in origRe.Matches(outp))
                    {
                        var v = m.Groups[1].Value.Trim();
                        if (v.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)) origs.Add(v);
                    }
                    int n = Math.Min(oems.Count, origs.Count);
                    for (int i = 0; i < n; i++) list.Add((oems[i], origs[i]));
                    Console.WriteLine("  pnputil 解析出驱动包：" + list.Count + " 个 (oem=" + oems.Count + ", orig=" + origs.Count + ")");
                    if (list.Count > 0)
                        Console.WriteLine("  pnputil 样例：[" + string.Join(", ", list.Take(3).Select(x => x.Item1 + "/" + x.Item2)) + "]");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [!] pnputil 解析失败：" + ex.Message);
            }
            return list;
        }

        private static void RunOnce(string label)
        {
            Console.WriteLine("===== " + label + " =====");
            var map = BuildMap();
            Console.WriteLine("  SetupAPI 设备名 map 条目数：" + map.Count);
            if (map.Count > 0)
            {
                var ks = string.Join(", ", System.Linq.Enumerable.Take(map.Keys, 5));
                Console.WriteLine("  SetupAPI map 键样例：[" + ks + "]");
            }
            var drivers = GetPnpUtilDrivers();
            int hitOem = 0, hitOrigOnly = 0, hitTotal = 0;
            var extraOrig = new List<string>();
            foreach (var (oem, orig) in drivers)
            {
                bool byOem = map.ContainsKey(oem);
                bool byOrig = !string.IsNullOrEmpty(orig) && map.ContainsKey(orig);
                if (byOem) hitOem++;
                if (byOrig && !byOem) { hitOrigOnly++; extraOrig.Add(oem + "/" + orig); }
                if (byOem || byOrig) hitTotal++;
            }
            Console.WriteLine("  >>> 仅 OemName 命中：" + hitOem);
            Console.WriteLine("  >>> 仅 OriginalName 命中(补充)：" + hitOrigOnly);
            Console.WriteLine("  >>> SetupAPI 双键总命中：" + hitTotal + " / " + drivers.Count);
            if (hitOrigOnly > 0)
                Console.WriteLine("  双键补充样例：[" + string.Join(", ", extraOrig.Take(5)) + "]");
            Console.WriteLine();
        }

        [STAThread]
        static void Main()
        {
            Console.WriteLine("==== SetupAPI ↔ pnputil 匹配验证（STA 优先）====");
            RunOnce("STA（模拟 WPF UI 线程）");
            // MTA 对比
            var t = new System.Threading.Thread(() =>
            {
                RunOnce("MTA（模拟后台线程/控制台）");
            });
            t.SetApartmentState(System.Threading.ApartmentState.MTA);
            t.Start();
            t.Join();
            Console.WriteLine("按任意键退出...");
            try { Console.ReadKey(true); } catch { }
        }
    }
}

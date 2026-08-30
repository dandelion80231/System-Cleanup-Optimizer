using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CpqSystemTool
{
    /// <summary>
    /// 计量连接（保留微软商店）：把 DefaultMediaCost 下 Ethernet/Wifi/Default 设为 2。
    /// 该项默认由 TrustedInstaller 拥有并禁止写入，故需先取得所有权、改写 DACL，
    /// 写完再把所有权/ACL 还原。
    /// </summary>
    internal static class MeteredConnection
    {
        private const string SUBKEY = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\DefaultMediaCost";
        private const string OBJ_NAME = "MACHINE\\" + SUBKEY;
        private static readonly string[] IFACES = { "Ethernet", "Wifi", "Default" };

        private const uint SE_REGISTRY_KEY = 4;
        private const uint OWNER_SECURITY_INFORMATION = 0x00000001;
        private const uint DACL_SECURITY_INFORMATION = 0x00000004;
        private static readonly IntPtr HKEY_LOCAL_MACHINE = new IntPtr(unchecked((int)0x80000002));
        private const uint KEY_QUERY_VALUE = 0x0001;
        private const uint KEY_SET_VALUE = 0x0002;
        private const uint KEY_WRITE_DAC = 0x00040000;
        private const uint KEY_READ_CONTROL = 0x00020000;
        private const uint KEY_ALL_ACCESS = 0xF003F;
        private const uint REG_DWORD = 4;
        private const uint ACL_REVISION = 2;
        private const int SECURITY_MAX_SID_SIZE = 68;
        private const uint SE_PRIVILEGE_ENABLED = 0x2;
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x20;
        private const uint TOKEN_QUERY = 0x08;
        private const int WinBuiltinAdministratorsSid = 26;
        private const int WinLocalSystemSid = 22;
        private const uint AclSizeInformation = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID_AND_ATTRIBUTES Privileges; }

        [StructLayout(LayoutKind.Sequential)]
        private struct ACL_SIZE_INFORMATION { public uint AceCount; public uint AclBytesInUse; public uint AclBytesFree; }

        [StructLayout(LayoutKind.Sequential)]
        private struct ACE_HEADER { public byte AceType; public byte AceFlags; public ushort AceSize; }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValueW(IntPtr lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CreateWellKnownSid(int WellKnownSidType, IntPtr DomainSid, IntPtr pSid, ref uint cbSid);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint SetNamedSecurityInfoW(string pObjectName, uint ObjectType, uint SecurityInfo, IntPtr psidOwner, IntPtr psidGroup, IntPtr pDacl, IntPtr pSacl);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetSecurityDescriptorDacl(IntPtr pSecurityDescriptor, out bool lpdaclPresent, out IntPtr pDacl, out bool lpdaclDefaulted);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool InitializeSecurityDescriptor(IntPtr pSecurityDescriptor, uint dwRevision);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetSecurityDescriptorDacl(IntPtr pSecurityDescriptor, bool bDaclPresent, IntPtr pDacl, bool bDaclDefaulted);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetAclInformation(IntPtr pAcl, IntPtr pAclInformation, uint nAclInformationLength, uint dwAclInformationClass);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetAce(IntPtr pAcl, uint dwAceIndex, out IntPtr pAce);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool InitializeAcl(IntPtr pAcl, uint nAclLength, uint dwAclRevision);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AddAce(IntPtr pAcl, uint dwAceRevision, uint dwStartingIndex, IntPtr pAceList, ushort nAceListLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AddAccessAllowedAce(IntPtr pAcl, uint dwAceRevision, uint AccessMask, IntPtr pSid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern uint GetLengthSid(IntPtr pSid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegOpenKeyExW(IntPtr hKey, string lpSubKey, uint ulOptions, uint samDesired, out IntPtr phkResult);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegCloseKey(IntPtr hKey);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegGetKeySecurity(IntPtr hKey, uint SecurityInformation, IntPtr pSecurityDescriptor, ref uint lpcbSecurityDescriptor);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegSetKeySecurity(IntPtr hKey, uint SecurityInformation, IntPtr pSecurityDescriptor);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegSetValueExW(IntPtr hKey, string lpValueName, uint Reserved, uint dwType, byte[] lpData, uint cbData);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegQueryValueExW(IntPtr hKey, string lpValueName, IntPtr lpReserved, out uint lpType, IntPtr lpData, ref uint lpcbData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static void EnablePrivs()
        {
            IntPtr tok;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out tok))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken");
            try
            {
                foreach (var name in new[] { "SeTakeOwnershipPrivilege", "SeRestorePrivilege" })
                {
                    LUID luid;
                    if (!LookupPrivilegeValueW(IntPtr.Zero, name, out luid))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "LookupPrivilegeValueW " + name);
                    var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1 };
                    tp.Privileges.Luid = luid;
                    tp.Privileges.Attributes = SE_PRIVILEGE_ENABLED;
                    if (!AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "AdjustTokenPrivileges " + name);
                }
            }
            finally { CloseHandle(tok); }
        }

        private static IntPtr AllocWellKnownSid(int wk)
        {
            IntPtr buf = Marshal.AllocHGlobal(SECURITY_MAX_SID_SIZE);
            uint sz = SECURITY_MAX_SID_SIZE;
            if (!CreateWellKnownSid(wk, IntPtr.Zero, buf, ref sz))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWellKnownSid");
            return buf;
        }

        private static void TakeOwnership(IntPtr sid)
        {
            uint r = SetNamedSecurityInfoW(OBJ_NAME, SE_REGISTRY_KEY, OWNER_SECURITY_INFORMATION, sid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (r != 0) throw new Win32Exception((int)r, "SetNamedSecurityInfoW(owner)");
        }

        private static IntPtr GetSD(IntPtr hkey)
        {
            uint size = 0;
            RegGetKeySecurity(hkey, OWNER_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION, IntPtr.Zero, ref size);
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            int r = RegGetKeySecurity(hkey, OWNER_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION, buf, ref size);
            if (r != 0) throw new Win32Exception(r, "RegGetKeySecurity");
            return buf;
        }

        private static void SetDacl(IntPtr hkey, IntPtr adminSid)
        {
            // 所有原生缓冲区在 finally 中统一释放，避免异常路径泄漏句柄/内存
            IntPtr sd = IntPtr.Zero;
            IntPtr pInfo = IntPtr.Zero;
            IntPtr aclNew = IntPtr.Zero;
            IntPtr newSd = IntPtr.Zero;
            try
            {
                sd = GetSD(hkey);
                bool present, def; IntPtr pacl;
                if (!GetSecurityDescriptorDacl(sd, out present, out pacl, out def))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetSecurityDescriptorDacl");
                if (present && pacl != IntPtr.Zero)
                {
                    var info = new ACL_SIZE_INFORMATION();
                    pInfo = Marshal.AllocHGlobal(Marshal.SizeOf<ACL_SIZE_INFORMATION>());
                    Marshal.StructureToPtr(info, pInfo, false);
                    if (!GetAclInformation(pacl, pInfo, (uint)Marshal.SizeOf<ACL_SIZE_INFORMATION>(), AclSizeInformation))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "GetAclInformation");
                    info = Marshal.PtrToStructure<ACL_SIZE_INFORMATION>(pInfo);
                    uint need = info.AclBytesInUse + 256 + GetLengthSid(adminSid);
                    aclNew = Marshal.AllocHGlobal((int)need);
                    if (!InitializeAcl(aclNew, need, ACL_REVISION))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeAcl");
                    for (uint i = 0; i < info.AceCount; i++)
                    {
                        IntPtr pace;
                        if (!GetAce(pacl, i, out pace))
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetAce");
                        var hdr = Marshal.PtrToStructure<ACE_HEADER>(pace);
                        if (!AddAce(aclNew, ACL_REVISION, 0xFFFFFFFF, pace, hdr.AceSize))
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "AddAce");
                    }
                }
                else
                {
                    aclNew = Marshal.AllocHGlobal(1024);
                    if (!InitializeAcl(aclNew, 1024, ACL_REVISION))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeAcl");
                }
                if (!AddAccessAllowedAce(aclNew, ACL_REVISION, KEY_ALL_ACCESS, adminSid))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "AddAccessAllowedAce");
                newSd = Marshal.AllocHGlobal(1024);
                if (!InitializeSecurityDescriptor(newSd, 1))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeSecurityDescriptor");
                if (!SetSecurityDescriptorDacl(newSd, true, aclNew, false))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetSecurityDescriptorDacl");
                int r = RegSetKeySecurity(hkey, DACL_SECURITY_INFORMATION, newSd);
                if (r != 0) throw new Win32Exception(r, "RegSetKeySecurity");
            }
            finally
            {
                if (pInfo != IntPtr.Zero) Marshal.FreeHGlobal(pInfo);
                if (aclNew != IntPtr.Zero) Marshal.FreeHGlobal(aclNew);
                if (newSd != IntPtr.Zero) Marshal.FreeHGlobal(newSd);
                if (sd != IntPtr.Zero) Marshal.FreeHGlobal(sd);
            }
        }

        private static IntPtr OpenKey(uint access)
        {
            IntPtr h;
            int r = RegOpenKeyExW(HKEY_LOCAL_MACHINE, SUBKEY, 0, access, out h);
            if (r != 0) throw new Win32Exception(r, "RegOpenKeyExW 0x" + access.ToString("x"));
            return h;
        }

        private static void WriteVal(IntPtr h, string name, int value)
        {
            byte[] b = BitConverter.GetBytes(value);
            int r = RegSetValueExW(h, name, 0, REG_DWORD, b, (uint)b.Length);
            if (r != 0) throw new Win32Exception(r, "RegSetValueExW " + name);
        }

        private static int? ReadVal(IntPtr h, string name)
        {
            uint type;
            IntPtr buf = Marshal.AllocHGlobal(4);
            uint sz = 4;
            try
            {
                int r = RegQueryValueExW(h, name, IntPtr.Zero, out type, buf, ref sz);
                if (r != 0) return null;
                return Marshal.ReadInt32(buf);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private static void Prepare(out IntPtr admin, out IntPtr system, out IntPtr backup)
        {
            admin = IntPtr.Zero;
            system = IntPtr.Zero;
            backup = IntPtr.Zero;
            EnablePrivs();
            admin = AllocWellKnownSid(WinBuiltinAdministratorsSid);
            system = AllocWellKnownSid(WinLocalSystemSid);
            TakeOwnership(admin);
            // 修复：句柄用 finally 关闭——SetDacl 抛异常时旧代码会泄漏注册表句柄
            IntPtr h = OpenKey(KEY_WRITE_DAC | KEY_READ_CONTROL);
            try
            {
                backup = GetSD(h);
                SetDacl(h, admin);
            }
            finally { RegCloseKey(h); }
        }

        private static bool Restore(IntPtr admin, IntPtr system, IntPtr backup, Action<string> log = null)
        {
            bool daclOk = true;
            if (backup != IntPtr.Zero)
            {
                IntPtr h = IntPtr.Zero;
                try
                {
                    h = OpenKey(KEY_WRITE_DAC);
                    // 修复：旧代码未校验 RegSetKeySecurity 返回值，还原失败也当成成功
                    int rs = RegSetKeySecurity(h, DACL_SECURITY_INFORMATION, backup);
                    if (rs != 0)
                    {
                        daclOk = false;
                        log?.Invoke("  [!] 还原 DACL 失败（错误码 0x" + rs.ToString("X") + "）");
                    }
                }
                catch (Exception caughtEx)
                {
                    daclOk = false;
                    DebugLog.Ignore(caughtEx);
                    log?.Invoke("  [!] 还原 DACL 失败: " + caughtEx.Message);
                }
                finally { if (h != IntPtr.Zero) RegCloseKey(h); }
            }

            if (system == IntPtr.Zero)
            {
                log?.Invoke("  [!] 未取得 SYSTEM SID，跳过所有权还原");
                return false;
            }
            uint r = SetNamedSecurityInfoW(OBJ_NAME, SE_REGISTRY_KEY, OWNER_SECURITY_INFORMATION, system, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (r != 0) log?.Invoke("  [!] 还原所有权失败（错误码 0x" + r.ToString("X") + "）");
            return daclOk && r == 0;
        }

        private static void FreeAll(IntPtr admin, IntPtr system, IntPtr backup)
        {
            // 修复：Prepare 已纳入 try，异常时部分缓冲区可能仍为零指针，逐个判空后释放
            if (admin != IntPtr.Zero) Marshal.FreeHGlobal(admin);
            if (system != IntPtr.Zero) Marshal.FreeHGlobal(system);
            if (backup != IntPtr.Zero) Marshal.FreeHGlobal(backup);
        }

        /// <summary>轻量检测：当前是否已开启计量连接（任一接口=2）。无需提权，读失败返回 false。</summary>
        public static bool IsMetered()
        {
            IntPtr h;
            int r = RegOpenKeyExW(HKEY_LOCAL_MACHINE, SUBKEY, 0, KEY_QUERY_VALUE, out h);
            if (r != 0) return false;
            try
            {
                foreach (var iface in IFACES)
                {
                    int? v = ReadVal(h, iface);
                    if (v == 2) return true;
                }
                return false;
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
            finally { RegCloseKey(h); }
        }

        public static void MeteredStatus(Action<string> log)
        {
            log("=== 计量连接状态 ===");
            // 状态查询只需读权限，不需要夺权（写入才需要）
            // 先尝试直接打开读取；若被拒绝再提权
            IntPtr h;
            int r = RegOpenKeyExW(HKEY_LOCAL_MACHINE, SUBKEY, 0, KEY_QUERY_VALUE, out h);
            if (r != 0)
            {
                if (r == 2) // ERROR_FILE_NOT_FOUND
                {
                    log("  注册表项不存在（DefaultMediaCost）");
                    log("  建议：点击「计量连接·切换」按钮，会自动创建并设置。");
                    return;
                }
                // 权限不足，尝试提权后读取
                // 权限不足，提权后读取：分配的句柄须确保释放，且用完须还原所有权/DACL
                IntPtr admin = IntPtr.Zero, system = IntPtr.Zero, backup = IntPtr.Zero;
                IntPtr h2 = IntPtr.Zero;
                try
                {
                    admin = AllocWellKnownSid(WinBuiltinAdministratorsSid);
                    system = AllocWellKnownSid(WinLocalSystemSid);
                    EnablePrivs();
                    TakeOwnership(admin);
                    h2 = OpenKey(KEY_WRITE_DAC | KEY_READ_CONTROL);
                    backup = GetSD(h2);
                    SetDacl(h2, admin);
                    RegCloseKey(h2);
                    h2 = IntPtr.Zero;
                    h = OpenKey(KEY_QUERY_VALUE);
                }
                catch (Exception ex)
                {
                    Marshal.FreeHGlobal(admin);
                    Marshal.FreeHGlobal(system);
                    Marshal.FreeHGlobal(backup);
                    log("  [!] 读取失败（错误码: 0x" + r.ToString("X") + "）");
                    log("  [!] 提权读取也失败: " + ex.Message);
                    log("  建议：点击「计量连接·切换」按钮尝试直接切换");
                    return;
                }
                finally
                {
                    // 若 SetDacl 抛异常，h2 尚未关闭会泄漏；此处兜底关闭（成功路径上 h2 已置零）
                    if (h2 != IntPtr.Zero)
                    {
                        RegCloseKey(h2);
                        h2 = IntPtr.Zero;
                    }
                }
                // 提权读取成功 → 还原所有权/DACL 并释放句柄，避免留下被改写的权限
                if (!Restore(admin, system, backup))
                    log("  [!] 未能完全还原该项所有权，可忽略（重启后系统会自动修复）。");
                FreeAll(admin, system, backup);
            }

            try
            {
                foreach (var iface in IFACES)
                {
                    int? val = ReadVal(h, iface);
                    string txt = val == 2 ? "已开启计量连接 ✅" : (val == 1 ? "未计量（正常）⚪" : "未设置(" + val + ")");
                    log("  " + iface + " : " + txt);
                }
                bool anyMetered = false;
                foreach (var iface in IFACES)
                {
                    int? v = ReadVal(h, iface);
                    if (v == 2) { anyMetered = true; break; }
                }
                log("");
                if (anyMetered)
                    log("  📋 当前状态：已开启计量连接 → Windows 自动更新将被阻止，微软商店仍可手动更新");
                else
                    log("  📋 当前状态：未计量 → Windows 会正常自动更新");
            }
            finally { RegCloseKey(h); }
        }

        public static void ToggleMetered(Action<string> log)
        {
            IntPtr admin = IntPtr.Zero, system = IntPtr.Zero, backup = IntPtr.Zero;
            bool ok = false;
            bool on = false;
            try
            {
                // 修复：Prepare（夺所有权 + 改写 DACL）必须纳入 try——旧代码放在 try 之外，
                // 一旦 SetDacl 抛异常，安全描述符已被改写且永不还原（不可逆）。
                Prepare(out admin, out system, out backup);
                // 修复：句柄用 finally 关闭——旧代码只在成功路径 RegCloseKey，中途抛异常即泄漏
                IntPtr h = OpenKey(KEY_SET_VALUE | KEY_QUERY_VALUE);
                try
                {
                    int? cur = ReadVal(h, "Ethernet");
                    on = (cur != 2);
                    log("=== " + (on ? "设为" : "取消") + "计量连接 ===");
                    foreach (var iface in IFACES)
                        WriteVal(h, iface, on ? 2 : 1);
                    foreach (var iface in IFACES)
                        log("       " + iface + " = " + ReadVal(h, iface));
                }
                finally { RegCloseKey(h); }
            }
            // 无论成功失败都尝试还原安全描述符
            finally { ok = Restore(admin, system, backup, log); FreeAll(admin, system, backup); }
            log("完成。" + (on ? "已阻止 Windows 自动更新" : "已恢复自动更新") + "；微软商店仍可手动更新。");
            if (!ok) log("  [!] 未能完全还原该项所有权，可忽略（重启后系统会自动修复）。");
        }
    }
}

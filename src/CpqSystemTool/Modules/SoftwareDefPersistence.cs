using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CpqSystemTool
{
    /// <summary>
    /// 用户自定义软件条目：持久化到 exe 同目录 custom_software.json。
    /// 字段为 SoftwareDef 的可序列化子集；加载后转换为 SoftwareDef 并入「常用软件」列表（与内置列表合并，同 ID 覆盖内置）。
    /// 序列化采用 System.Runtime.Serialization.Json.DataContractJsonSerializer（net48 自带，需引用 System.Runtime.Serialization，不依赖 System.Web），无需第三方 JSON 库。
    /// </summary>
    [DataContract]
    internal class CustomSoftwareEntry
    {
        [DataMember] public string id { get; set; }
        [DataMember] public string name { get; set; }
        [DataMember] public string desc { get; set; }
        [DataMember] public string url { get; set; }
        [DataMember] public string[] installArgs { get; set; } = new string[0];
        [DataMember] public string risk { get; set; } = "low";
        [DataMember] public string storeId { get; set; }
        [DataMember] public string chocolateyId { get; set; }
        [DataMember] public string uninstallKeyword { get; set; }
        [DataMember] public string[] altKeywords { get; set; } = new string[0];
        [DataMember] public string[] knownExePaths { get; set; } = new string[0];
        [DataMember] public string regKey { get; set; }
        [DataMember] public string regKey2 { get; set; }
        [DataMember] public string sha256 { get; set; }
        [DataMember] public string referer { get; set; }
        [DataMember] public string pageUrl { get; set; }
        [DataMember] public string installDirSwitch { get; set; }
        [DataMember] public bool isPortable { get; set; } = false;
        /// <summary>软件分类（结构化，对应 SoftwareInstall.SoftwareCategories）。旧 json 无此字段则读为 null，展示按「其他」处理；可序列化。</summary>
        [DataMember] public string category { get; set; }

        /// <summary>管理对话框专用：标记该条目是否来自 custom_software.json（增补），不序列化。</summary>
        [IgnoreDataMember] public bool isCustom { get; set; }
        /// <summary>管理对话框专用：标记该条目是否是对内置条目的同 ID 覆盖，不序列化。</summary>
        [IgnoreDataMember] public bool isOverride { get; set; }
        /// <summary>管理对话框专用：列表显示来源文本，不序列化。</summary>
        [IgnoreDataMember] public string sourceText => isOverride ? "内置(覆盖)" : (isCustom ? "增补" : "内置");

        /// <summary>转换为运行时 SoftwareDef（经 Builder 流式构造，自动推断安装器类型）。</summary>
        public SoftwareDef ToSoftwareDef()
        {
            var b = new SoftwareDef.Builder(
                id ?? "",
                name ?? (id ?? "自定义软件"),
                desc ?? "",
                url ?? "",
                (installArgs ?? new string[0]));
            if (!string.IsNullOrEmpty(risk)) b.Risk(risk);
            if (!string.IsNullOrEmpty(storeId)) b.StoreId(storeId);
            if (!string.IsNullOrEmpty(chocolateyId)) b.ChocolateyId(chocolateyId);
            if (!string.IsNullOrEmpty(uninstallKeyword)) b.UninstallKeywords(uninstallKeyword);
            if (altKeywords != null && altKeywords.Length > 0) b.AltKeywords(altKeywords);
            if (knownExePaths != null && knownExePaths.Length > 0) b.KnownExePaths(knownExePaths);
            if (!string.IsNullOrEmpty(regKey)) b.RegKey(regKey);
            if (!string.IsNullOrEmpty(regKey2)) b.RegKey2(regKey2);
            if (!string.IsNullOrEmpty(sha256)) b.Sha256(sha256);
            if (!string.IsNullOrEmpty(pageUrl)) b.PageResolver(pageUrl);
            if (!string.IsNullOrEmpty(referer)) b.Referer(referer);
            if (!string.IsNullOrEmpty(installDirSwitch)) b.InstallDirSwitch(installDirSwitch);
            if (!string.IsNullOrEmpty(category)) b.Category(category);
            if (isPortable) b.Portable();
            return b.Build();
        }

        /// <summary>由运行时 SoftwareDef 生成编辑用 DTO；用于把内置条目也纳入管理对话框展示与编辑覆盖。</summary>
        public static CustomSoftwareEntry FromSoftwareDef(SoftwareDef def, bool isCustom = false)
        {
            if (def == null) return null;
            return new CustomSoftwareEntry
            {
                id = def.Id,
                name = def.Name,
                desc = def.Desc,
                url = def.DownloadUrl,
                installArgs = def.InstallArgs ?? new string[0],
                risk = def.Risk ?? "low",
                storeId = def.StoreId,
                chocolateyId = def.ChocolateyId,
                uninstallKeyword = def.UninstallKeywords,
                altKeywords = def.AltKeywords ?? new string[0],
                knownExePaths = def.KnownExePaths ?? new string[0],
                regKey = def.RegKey,
                regKey2 = def.RegKey2,
                sha256 = def.Sha256,
                referer = def.Referer,
                pageUrl = def.PageUrl,
                installDirSwitch = def.InstallDirSwitch,
                isPortable = def.IsPortable,
                category = def.Category,
                isCustom = isCustom
            };
        }
    }

    /// <summary>
    /// 自定义软件列表持久化层：读写 exe 同目录 custom_software.json；内存缓存 + Version 计数供合并层失效。
    /// 运行时无法重新打包 exe，故采用「内置默认列表 + 外部用户自定义列表」合并方案，重启后自动加载，增补的条目得以保留。
    /// </summary>
    internal static class SoftwareDefPersistence
    {
        private static readonly string FilePath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory,
            "custom_software.json");

        private static List<CustomSoftwareEntry> _cache;
        private static readonly object _lock = new object();

        /// <summary>每次保存成功自增，供 SoftwareInstall 合并缓存失效（避免每次调用都重建列表/映射）。</summary>
        public static int Version { get; private set; } = 0;

        public static string CustomFilePath => FilePath;

        public static List<CustomSoftwareEntry> Load()
        {
            lock (_lock)
            {
                if (_cache != null) return _cache;
                // 优先读取已固化进 exe 的 overlay（自包含，删 json 也不丢）；否则回退外部 json
                var baked = ReadOverlay();
                if (baked != null) { _cache = baked; return baked; }
                var list = new List<CustomSoftwareEntry>();
                try
                {
                    if (File.Exists(FilePath))
                    {
                        var json = File.ReadAllText(FilePath, Encoding.UTF8);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            var ser = new DataContractJsonSerializer(typeof(List<CustomSoftwareEntry>));
                            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                            {
                                var arr = ser.ReadObject(ms) as List<CustomSoftwareEntry>;
                                if (arr != null) list = arr;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 读取自定义软件列表失败(已忽略): " + ex.Message);
                }
                _cache = list;
                return list;
            }
        }

        public static void Save(List<CustomSoftwareEntry> entries)
        {
            List<CustomSoftwareEntry> snapshot;
            lock (_lock)
            {
                snapshot = entries ?? new List<CustomSoftwareEntry>();
                _cache = snapshot;
                try
                {
                    var ser = new DataContractJsonSerializer(typeof(List<CustomSoftwareEntry>));
                    string json;
                    using (var ms = new MemoryStream())
                    {
                        ser.WriteObject(ms, snapshot);
                        json = Encoding.UTF8.GetString(ms.ToArray());
                    }
                    // 先写临时文件再原子替换，避免崩溃残留半截 JSON 导致 Load 静默吞异常、数据丢失
                    AtomicFile.WriteFileAtomic(FilePath, json);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 保存自定义软件列表失败: " + ex.Message);
                    throw;
                }
                Version++;
            }
            // 暂存固化：下次启动把增补列表写进 exe 自身（自包含），失败不影响本次保存
            StageBake(snapshot);
        }

        public static void AddOrUpdate(CustomSoftwareEntry entry)
        {
            var list = Load();
            int idx = list.FindIndex(e => e != null && string.Equals(e.id, entry.id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) list[idx] = entry; else list.Add(entry);
            Save(list);
        }

        public static void Remove(string id)
        {
            var list = Load();
            list.RemoveAll(e => e != null && string.Equals(e.id, id, StringComparison.OrdinalIgnoreCase));
            Save(list);
        }

        // ===== exe 自包含固化（方案 A：增补写进 exe，重启替换一次）=====
        // 格式：在 exe 文件末尾追加 [OVERLAY_MAGIC(8B)][payloadLen(4B LE)][payload(UTF-8 JSON)]。
        // 系统加载器忽略 PE 镜像之后的尾部数据，故追加安全。运行时若检测到 overlay 则优先作为增补来源，
        // 使 exe 自包含（可单独拷贝、删 json 不丢）；否则回退外部 json。
        private static readonly byte[] OVERLAY_MAGIC = Encoding.ASCII.GetBytes("CPQSWOVR");
        private const int OVERLAY_MAGIC_LEN = 8;

        private static List<CustomSoftwareEntry> DeserializeList(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<CustomSoftwareEntry>();
            try
            {
                var ser = new DataContractJsonSerializer(typeof(List<CustomSoftwareEntry>));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var arr = ser.ReadObject(ms) as List<CustomSoftwareEntry>;
                    return arr ?? new List<CustomSoftwareEntry>();
                }
            }
            catch { return new List<CustomSoftwareEntry>(); }
        }

        private static string SerializeList(List<CustomSoftwareEntry> list)
        {
            var ser = new DataContractJsonSerializer(typeof(List<CustomSoftwareEntry>));
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, list ?? new List<CustomSoftwareEntry>());
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        /// <summary>在 exe 字节尾部定位 overlay，返回 payload 的起止。找不到返回 false。</summary>
        private static bool TryLocateOverlay(byte[] exe, out int payloadStart, out int payloadLen)
        {
            payloadStart = 0; payloadLen = 0;
            int searchStart = Math.Max(0, exe.Length - (OVERLAY_MAGIC_LEN + 4 + 4_000_000));
            for (int i = exe.Length - OVERLAY_MAGIC_LEN; i >= searchStart; i--)
            {
                bool match = true;
                for (int j = 0; j < OVERLAY_MAGIC_LEN; j++) if (exe[i + j] != OVERLAY_MAGIC[j]) { match = false; break; }
                if (!match) continue;
                if (i + OVERLAY_MAGIC_LEN + 4 > exe.Length) continue;
                int len = BitConverter.ToInt32(exe, i + OVERLAY_MAGIC_LEN);
                int start = i - len;
                if (len <= 0 || start < 0 || start > i) continue;
                payloadStart = start; payloadLen = len;
                return true;
            }
            return false;
        }

        private static byte[] SubArray(byte[] src, int offset, int count)
        {
            var r = new byte[count];
            Array.Copy(src, offset, r, 0, count);
            return r;
        }

        private static byte[] StripOverlay(byte[] exe)
        {
            if (TryLocateOverlay(exe, out int start, out int len))
                return SubArray(exe, 0, start);
            return exe;
        }

        /// <summary>优先读取已固化进 exe 的 overlay 增补列表；无/损坏则返回 null（回退 json）。</summary>
        private static List<CustomSoftwareEntry> ReadOverlay()
        {
            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;
                byte[] exe = File.ReadAllBytes(exePath);
                if (TryLocateOverlay(exe, out int start, out int len))
                {
                    string json = Encoding.UTF8.GetString(exe, start, len);
                    var list = DeserializeList(json);
                    if (list != null && list.Count > 0) return list;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] ReadOverlay 失败(已忽略): " + ex.Message); }
            return null;
        }

        /// <summary>暂存一次固化：把当前增补列表写入 bake_pending.bin，下次启动由 ApplyPendingBakeIfAny 写进 exe。</summary>
        public static void StageBake(List<CustomSoftwareEntry> entries)
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
                string pending = Path.Combine(dir, "bake_pending.bin");
                AtomicFile.WriteFileAtomic(pending, SerializeList(entries));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[CpqSystemTool] StageBake 失败(已忽略): " + ex.Message); }
        }

        /// <summary>启动时调用：若存在待固化标记，则把增补列表写进 exe 自身（原子替换 exe），使 exe 自包含。
        /// 替换前确认新 exe 就位；失败一律回滚恢复原 exe（绝不留下「主程序停在 .old」的中间态），json 保持真相源。</summary>
        public static void ApplyPendingBakeIfAny()
        {
            string exePath = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(exePath)) return;
            string dir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string pending = Path.Combine(dir, "bake_pending.bin");
            if (!File.Exists(pending)) return;

            try
            {
                string json = File.ReadAllText(pending, Encoding.UTF8);
                byte[] core = StripOverlay(File.ReadAllBytes(exePath));
                byte[] payload = Encoding.UTF8.GetBytes(json);
                byte[] lenBytes = BitConverter.GetBytes(payload.Length);

                string newPath = exePath + ".new";
                using (var fs = new FileStream(newPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(core, 0, core.Length);
                    fs.Write(OVERLAY_MAGIC, 0, OVERLAY_MAGIC.Length);
                    fs.Write(lenBytes, 0, lenBytes.Length);
                    fs.Write(payload, 0, payload.Length);
                }

                // 替换前先确认新 exe 就位（存在且非空），避免拿半截文件替换主程序
                var newInfo = new FileInfo(newPath);
                if (!newInfo.Exists || newInfo.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[CpqSystemTool] ApplyPendingBake 失败(已忽略): 新 exe 未就位: " + newPath);
                    return;
                }

                if (!ReplaceExecutableAtomically(exePath, newPath))
                    System.Diagnostics.Debug.WriteLine("[CpqSystemTool] ApplyPendingBake 替换失败(已回滚): 主程序仍为原 exe, json 保持真相源");
                try { if (File.Exists(pending)) File.Delete(pending); } catch (Exception ex) { DebugLog.Ignore(ex); }
            }
            catch (Exception ex)
            {
                // 固化失败：保留 json 为真相源，删除 pending 避免反复失败
                System.Diagnostics.Debug.WriteLine("[CpqSystemTool] ApplyPendingBake 失败(已忽略): " + ex.Message);
                try { if (File.Exists(pending)) File.Delete(pending); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
            }
        }

        // ===== exe 原子自替换 =====
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, uint dwFlags);

        private const uint MOVEFILE_REPLACE_EXISTING = 0x1;
        private const uint MOVEFILE_WRITE_THROUGH = 0x8;

        /// <summary>原子替换正在运行的 exe：优先 MoveFileEx 原地覆盖（同一卷 rename，一步完成）；
        /// 因镜像被运行锁定而失败时，回退「改名旧 exe→.old + 移入新 exe」（Windows 允许改名运行中的镜像），
        /// 任何失败经 finally 把 .old 改回 exe 回滚，保证主程序绝不处于「找不到 exe」状态。</summary>
        private static bool ReplaceExecutableAtomically(string exePath, string newPath)
        {
            // 尝试一：直接原子覆盖。目标未被锁定时一步完成。
            if (MoveFileEx(newPath, exePath, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
                return true;

            // 回退：带时间戳备份（失败不阻断），再两步改名。
            string bak = exePath + ".bak_" + DateTime.Now.ToString("yyyyMMddHHmmss");
            try { File.Copy(exePath, bak, true); } catch (Exception ex) { DebugLog.Ignore(ex); }

            string old = exePath + ".old";
            bool movedToOld = false;
            try
            {
                if (File.Exists(old)) File.Delete(old);
                if (!MoveFileEx(exePath, old, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
                    return false; // 旧 exe 改不动，直接放弃（exe 仍在原位）
                movedToOld = true;

                if (!MoveFileEx(newPath, exePath, MOVEFILE_WRITE_THROUGH))
                    return false; // 新 exe 移不到位，finally 回滚 old→exe
                try { File.Delete(old); } catch (Exception ex) { DebugLog.Ignore(ex); }
                return true;
            }
            finally
            {
                // 回滚：新 exe 未就位且旧 exe 已被改名时，把 old 改回 exe
                if (movedToOld && !File.Exists(exePath) && File.Exists(old))
                {
                    if (!MoveFileEx(old, exePath, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
                        System.Diagnostics.Debug.WriteLine("[CpqSystemTool] ApplyPendingBake 回滚失败(危险! Win32=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")");
                }
            }
        }
    }
}

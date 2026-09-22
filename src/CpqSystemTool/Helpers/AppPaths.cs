using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// 应用常用路径集中（R5 收编 + 2026-09-15 数据根收拢）。
    /// 历史：Config 目录此前硬编码于 MainWindow.Theme.cs、MainWindow.Tweaks.cs、Modules/ConfigBackup.cs；
    /// ODT 数据在 exe 同目录 cpq_office/。现统一收拢为「一个主数据文件夹跟随 exe」：
    ///     &lt;exe 所在目录&gt;\cpq-tool\
    ///         ├─ 配置\         配置自动备份/用户存配置 JSON、background.json、uninstall_prefs.json
    ///         ├─ Office 部署\  Office 部署页全部数据：odt\（引擎）、configs\（部署 XML）、run\（运行临时）、log\（执行日志）
    ///         ├─ 安全防护\     注册表快照 regbackup\（Defender 开关改动的恢复数据，勿删）
    ///         ├─ 系统信息\     系统信息页 TXT 导出默认落点
    ///         ├─ 壁纸\         背景 SVG 导出默认落点
    ///         ├─ 驱动备份\     驱动包导出默认落点
    ///         └─ 源码\         「导出源码」默认落点
    /// 各对话框只改默认位置（InitialDirectory/SelectedPath），用户仍可自选其他路径。
    /// 首次启动由 MigrateLegacyDirs() 把旧 exe 目录\Config 与 cpq_office 的内容迁入（幂等，不覆盖、不删除旧文件）；
    /// exe 被挪位时由 RecoverMovedDataRoot() 按注册表锚点（HKCU\Software\CpqSystemTool\DataRoot）自动把数据拉回新位置。
    /// </summary>
    public static class AppPaths
    {
        /// <summary>exe 所在目录。单文件场景优先 Environment.ProcessPath（.NET 6+，.NET 官方推荐的单文件 exe 定位方式；
        /// 不用 BaseDirectory——单文件运行时它可能指向临时解压目录）；非单文件回退 AppDomain.BaseDirectory（即 exe 目录）。</summary>
        public static string ExeDir
        {
            get
            {
                var pp = Environment.ProcessPath;
                string d = string.IsNullOrEmpty(pp) ? null : Path.GetDirectoryName(pp);
                if (string.IsNullOrEmpty(d) || !Directory.Exists(d))
                    d = AppDomain.CurrentDomain.BaseDirectory;
                return d;
            }
        }

        /// <summary>统一数据根目录：默认 exe 同目录 cpq-tool（唯一主数据文件夹跟随 exe）。
        /// 【v1.23 智能外壳】启动器注入环境变量 CPQ_DATA_ROOT（外壳目录\cpq-tool，即“数据跟外壳走”）时优先采用；
        /// 未注入（双击主 exe 的传统形态）时维持 exe 目录\cpq-tool 不变。</summary>
        public static string DataRoot
        {
            get
            {
                try
                {
                    string ov = Environment.GetEnvironmentVariable("CPQ_DATA_ROOT");
                    if (!string.IsNullOrWhiteSpace(ov))
                    {
                        ov = Path.GetFullPath(ov);
                        string parent = Path.GetDirectoryName(ov);
                        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) return ov;
                    }
                }
                catch { /* 环境变量异常 → 回退默认 */ }
                return Path.Combine(ExeDir, "cpq-tool");
            }
        }

        /// <summary>配置目录（数据根下 配置\；配置页「选择配置默认保存文件夹」可被用户改到别处）。</summary>
        public static string ConfigDir => Path.Combine(DataRoot, "配置");

        /// <summary>崩溃日志（数据根下 crash.log）。</summary>
        public static string CrashLogPath => Path.Combine(DataRoot, "crash.log");

        /// <summary>系统信息页 TXT 导出默认目录。</summary>
        public static string SysInfoDir => Path.Combine(DataRoot, "系统信息");

        /// <summary>自定义背景 SVG 导出默认目录。</summary>
        public static string WallpaperDir => Path.Combine(DataRoot, "壁纸");

        /// <summary>驱动备份默认目录。</summary>
        public static string DriverBackupDir => Path.Combine(DataRoot, "驱动备份");

        /// <summary>「导出源码」默认目录。</summary>
        public static string SourceExportDir => Path.Combine(DataRoot, "源码");



        /// <summary>Office 部署页数据根子目录：cpq-tool\Office 部署\（引擎/部署XML/运行临时全部收在此下，与其他页子目录平级统一）。</summary>
        public static string OfficeDeployDir => Path.Combine(DataRoot, "Office 部署");
        /// <summary>Office 部署页 UI 执行日志落盘目录：Office 部署\log\office_yyyyMMdd.log（按天分文件，保留最新 20 份；7 天以前的旧日志中 <8 行的无用日志自动删，近 7 天一律保留）。</summary>
        public static string OfficeDeployLogDir => Path.Combine(OfficeDeployDir, "log");

        /// <summary>ODT 引擎子目录：cpq-tool\Office 部署\odt\（setup.exe 落点）。</summary>
        public static string OdtEngineDir => Path.Combine(OfficeDeployDir, "odt");

        /// <summary>ODT 部署 XML 子目录：cpq-tool\Office 部署\configs\（时间戳命名的 config_*.xml，保留最新 20 份）。</summary>
        public static string OdtConfigsDir => Path.Combine(OfficeDeployDir, "configs");

        /// <summary>ODT 运行临时目录：cpq-tool\Office 部署\run\。</summary>
        public static string OdtRunDir => Path.Combine(OfficeDeployDir, "run");

        /// <summary>安全防护页数据目录：cpq-tool\安全防护\。</summary>
        public static string SecurityDir => Path.Combine(DataRoot, "安全防护");
        /// <summary>注册表快照目录（Defender 开关改动的 BEFORE/AFTER 快照，恢复功能数据，禁止自动清理）：cpq-tool\安全防护\regbackup\。</summary>
        public static string RegBackupDir => Path.Combine(SecurityDir, "regbackup");

        /// <summary>确保目录存在（不存在则创建；创建失败不抛异常，仍返回路径供上层自行兜底）。对话框打开前调用。</summary>
        public static string EnsureDir(string dir)
        {
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
            catch { /* 权限/只读介质等：返回原路径，对话框仍可用 */ }
            return dir;
        }

        /// <summary>确保配置目录存在（不存在则创建）。创建失败（权限不足/磁盘已满/路径被文件占用等）返回 false，不抛异常。
        /// 注意：目录已存在但不可写时此处无法探测，由实际写入失败路径（如背景设置保存）经首次告警补足。</summary>
        public static bool EnsureConfigDir()
        {
            try
            {
                if (Directory.Exists(ConfigDir)) return true;
                Directory.CreateDirectory(ConfigDir);
                return Directory.Exists(ConfigDir);
            }
            catch { return false; }
        }

        /// <summary>
        /// 启动时建全数据树：数据根 + 全部子目录（配置/odt/configs/run/系统信息/壁纸/驱动备份/源码/安全防护），
        /// 并在数据根缺失「文件夹说明.txt」时生成（不覆盖用户改动）。各导出子目录开机即齐，不用等对话框触发。
        /// </summary>
        public static void EnsureDataTree()
        {
            try
            {
                foreach (var d in new[] { DataRoot, ConfigDir, OfficeDeployDir, OdtEngineDir, OdtConfigsDir, OdtRunDir, OfficeDeployLogDir, SysInfoDir, WallpaperDir, DriverBackupDir, SourceExportDir, SecurityDir, RegBackupDir })
                {
                    if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                }
                WriteFolderIntroIfNeeded();

                // 启动时统一清理：Office 部署日志（7 天前的旧日志中 <8 行无用日志 + 保留最新 20 份）、configs 部署 XML（保留最新 20 份）
                PruneOfficeLogs();
                PruneDeployConfigs();
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }
        }

        /// <summary>按文件名倒序（时间戳命名即时间序）保留目录下匹配 pattern 的最近 keepCount 个文件，其余删除；单个删除失败跳过。</summary>
        private static void PruneKeepNewest(string dir, string pattern, int keepCount)
        {
            if (!Directory.Exists(dir)) return;
            try
            {
                var files = Directory.GetFiles(dir, pattern).OrderByDescending(f => f, StringComparer.Ordinal).ToArray();
                for (int i = keepCount; i < files.Length; i++)
                {
                    try { File.Delete(files[i]); } catch { /* 单个删失败不影响 */ }
                }
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }
        }

        /// <summary>
        /// Office 部署日志清理（Office 部署\log\office_*.log，文件名=日期，名称序即时间序）：
        /// ① 删除「无用日志」——行数不足 8 的文件（该会话未发生实质安装/卸载操作，正常操作日志必超 8 行）；
        /// ② 只保留最新 20 份，更旧的自动删除。
        /// 启动调用时所有日志文件均已写完；会话内首写时调用则当天文件受「名称跳过」保护（绝不参与①，只受②份数上限保护）。
        /// 【修 OCR-20260915】原实现会话内首写时当天文件只有几行，会被①直接误删、首条日志丢失。
        /// </summary>
        public static void PruneOfficeLogs()
        {
            try
            {
                string dir = OfficeDeployLogDir;
                if (!Directory.Exists(dir)) return;
                string today = DateTime.Now.ToString("yyyyMMdd");
                // 【修 2026-09-16 反馈】「<8 行自动删」原来每次启动都对全部旧文件生效：
                // 刚迁盘/刚开机的短测试日志（如 <8 行的 ODT 失败日志）被静默清掉，用户以为旧日志被顶替。
                // 现改为：近 7 天内的文件一律保留（短不短都留，方便回看）；只对 7 天以上的旧文件执行 8 行判定。
                DateTime recentCutoff = DateTime.Now.AddDays(-7);
                foreach (var f in Directory.GetFiles(dir, "office_*.log"))
                {
                    try
                    {
                        // 当天文件正在写（可能才几行），跳过①；只受②份数上限约束
                        if (Path.GetFileName(f).Contains(today)) continue;
                        DateTime w = File.GetLastWriteTime(f);
                        if (w >= recentCutoff) continue;   // 近 7 天：保留
                        // 数 8 行即可判定；旧文件 <8 行 = 无用日志，删
                        int lines = File.ReadLines(f).Take(8).Count();
                        if (lines < 8) File.Delete(f);
                    }
                    catch { /* 单文件检查失败跳过 */ }
                }
                PruneKeepNewest(dir, "office_*.log", 20);
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }
        }

        /// <summary>部署 XML 清理：configs\config_*.xml 只保留最新 20 份（名称序=时间序），多余的自动删。只匹配 config_*.xml，其他文件/子目录一律不动。</summary>
        public static void PruneDeployConfigs()
        {
            try { PruneKeepNewest(OdtConfigsDir, "config_*.xml", 20); }
            catch (Exception ex) { DebugLog.Ignore(ex); }
        }

        /// <summary>数据根下「文件夹说明.txt」：介绍各子文件夹用途 + 主文件夹须与 exe 同路径的核心规则。
        /// 版本机制：首行写 version=N；文件缺失或版本落后于当前 IntroVersion 时重新生成，同版本保留用户改动。</summary>
        private const int IntroVersion = 10;

        private static void WriteFolderIntroIfNeeded()
        {
            try
            {
                string intro = Path.Combine(DataRoot, "文件夹说明.txt");
                if (File.Exists(intro))
                {
                    try
                    {
                        string first = File.ReadLines(intro).FirstOrDefault() ?? "";
                        if (first.StartsWith($"version={IntroVersion}")) return; // 同版本：保留用户改动
                    }
                    catch { /* 读失败 → 重写 */ }
                }
                var lines = new[]
                {
                    $"version={IntroVersion}",
                    "==============================================",
                    " CpqSystemTool 数据文件夹说明（cpq-tool）",
                    "==============================================",
                    "",
                    "【重要规则 1】本文件夹与「启动入口」同目录：智能外壳启动时在外壳旁边（数据跟外壳走）；直接运行主程序 exe 时在该 exe 旁边（数据跟 exe 走）。",
                    "程序按 exe 所在位置定位本文件夹。若把 exe 移到本机其他路径：",
                    "・exe 与本文件夹一起移动 → 一切照旧；",
                    "・只移动 exe → 程序会在启动时自动把数据从上一次使用的路径拉回（记录并锚定最后使用路径），",
                    "  全部数据随 exe 到新位置，无需手动搬。",
                    "迁移到别的电脑/盘符时，请把 exe 与本文件夹一起拷贝。",
                    "",
                    "【文件夹结构与用途】",
                    "配置\\       配置数据：自动备份/用户保存的配置 JSON、背景设置（background.json）、",
                    "              OneDrive/Teams 卸载偏好（uninstall_prefs.json）；可选 probe-cdn.json",
                    "              （软件探测兑底直链热更：{\"kimi\": \"https://…exe\", \"coze\": \"https://…exe\"}，",
                    "              内置直链随厂商发版 404 时写入新直链、重启生效；不创建=用内置默认）",
                    "Office 部署\\   Office 部署页的全部数据：",
                    "  odt\\          ODT 部署引擎（setup.exe，约 7MB）。删除后程序会自动重新下载",
                    "  configs\\      部署 XML（带时间戳文件名，便于事后排查；只保留最新 20 份，自动清理）",
                    "  run\\          ODT 运行临时目录（启动时自动清扫空壳，可随时删）",
                    "  log\\          页面执行日志落盘（按天分文件，保留最新 20 份；7 天以前的旧日志中不足 8 行=无用日志自动删，可随时删）",
                    "安全防护\\     注册表快照（regbackup\\，Defender 开关改动的 BEFORE/AFTER 恢复数据，请勿删除）",
                    "系统信息\\     「系统信息导出为 TXT」的默认保存位置",
                    "壁纸\\        「自定义背景 - 导出 SVG」的默认保存位置",
                    "驱动备份\\     「驱动备份/导出」的默认保存位置",
                    "源码\\        「配置页 - 导出源码」的默认保存位置",
                    "根目录文件（可随时删，按需自动再生）：",
                    "  crash.log           未处理异常/崩溃堆栈记录（崩溃时自动生成）",
                    "  webview2_deps.log   WebView2 依赖补齐留痕（loader 自动下载/解压记录，排查用）",
                    "  MicrosoftEdgeWebview2Setup.exe  「Edge 管理 - 安装 WebView2 Runtime」的下载中转件（按需重新下载，v1.20 前机内已装好 Runtime 时无需它）",
                    "",
                    "【说明】",
                    "・各导出/保存对话框默认打开对应子文件夹，也可在对话框中随时改选其他位置。",
                    "・系统信息/壁纸/驱动备份/源码 及 Office 部署\\run、Office 部署\\log 子文件夹里的产物可随时删除，文件夹本身每次启动自动重建。",
                    "・请勿自行重命名本文件夹及其子文件夹：程序按 exe 旁的固定名称 cpq-tool 查找。",
                };
                File.WriteAllText(intro, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(true));
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }
        }

        /// <summary>一次性迁移旧版分散目录到新统一布局。幂等（目标已存在即跳过，绝不删旧文件）：
        /// 8 步：① 旧Config→数据根 ② Config→配置改名 ③ cpq_office\→Office 部署\ ④ crash.log ⑤ 拉回 ⑥ 平铺重排 ⑦ 注册表快照→安全防护\ ⑧ EnsureDataTree。
        /// 注：⑤拉回之后必须补做②/⑦（拉回回来的可能是改名前的旧树）。每次启动都会刷新注册表锚点为当前数据根。</summary>
        public static void MigrateLegacyDirs()
        {
            try
            {
                string exeDir = ExeDir;
                string data = DataRoot;

                // ① 旧 exe 目录\Config → cpq-tool\Config
                MoveTree(Path.Combine(exeDir, "Config"), Path.Combine(data, "Config"));

                // ② Config → 配置（中文名，与其他按页文件夹统一）
                MoveTree(Path.Combine(data, "Config"), Path.Combine(data, "配置"));

                // ③ 旧 exe 目录\cpq_office\{odt,configs,run} → cpq-tool\Office 部署\{odt,configs,run}
                string oldOffice = Path.Combine(exeDir, "cpq_office");
                foreach (var sub in new[] { "odt", "configs", "run" })
                    MoveTree(Path.Combine(oldOffice, sub), Path.Combine(OfficeDeployDir, sub));

                // ④ 旧 exe 目录\crash.log → cpq-tool\crash.log
                var oldCrash = Path.Combine(exeDir, "crash.log");
                if (File.Exists(oldCrash) && !File.Exists(CrashLogPath))
                    File.Move(oldCrash, CrashLogPath);

                // ⑤ exe 挪位拉回：本机数据根无数据且上次锚定路径有数据 → 整体搬回（必须在平铺重排与写 intro 之前，让进来的旧布局也能被归位）
                RecoverMovedDataRoot();

                // ⑥ 一代平铺重排（含⑤拉回进来的旧布局）：cpq-tool\{odt,configs,run} → cpq-tool\Office 部署\{...}
                foreach (var sub in new[] { "odt", "configs", "run" })
                    MoveTree(Path.Combine(data, sub), Path.Combine(OfficeDeployDir, sub));
                // 拉回进来的是旧布局也可能带 Config\，重排一次（幂等，无旧数据时空操作）
                MoveTree(Path.Combine(data, "Config"), Path.Combine(data, "配置"));

                // ⑦ 注册表快照归属重排：Office 部署\configs\regbackup → 安全防护\regbackup
                //（快照是「安全防护」页 Defender 功能的恢复数据，不属于 Office 部署；快照缺失时 Defender 会自动重建）
                MoveTree(Path.Combine(OdtConfigsDir, "regbackup"), RegBackupDir);

                // ⑧ 建全数据树（导出子目录 + 文件夹说明.txt），后续启动无旧数据可迁时也直接保证目录齐备
                EnsureDataTree();
            }
            catch (Exception ex) { DebugLog.Ignore(ex); /* 迁移失败不阻塞启动：OdtEnsure 缺引擎会自愈合下载 */ }
        }

        // ──────────── exe 挪位自动拉回（注册表锚定最后数据根）────────────

        /// <summary>注册表锚点：HKCU\Software\CpqSystemTool\DataRoot = 上一次使用的数据根绝对路径（HKCU，免管理员）。</summary>
        private const string AnchorKeyPath = @"Software\CpqSystemTool";
        private const string AnchorValueName = "DataRoot";

        private static string ReadAnchoredRoot()
        {
            try { return Registry.CurrentUser.OpenSubKey(AnchorKeyPath)?.GetValue(AnchorValueName) as string; }
            catch (Exception ex) { DebugLog.Ignore(ex); return null; }
        }

        private static void WriteAnchoredRoot(string path)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(AnchorKeyPath, writable: true);
                key.SetValue(AnchorValueName, path);
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }
        }

        /// <summary>目录下是否有任何「数据文件」（递归；文件夹说明.txt 不算数据——它由 EnsureDataTree 自动生成）。</summary>
        private static bool RootHasData(string root)
        {
            if (!Directory.Exists(root)) return false;
            try
            {
                foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    if (!string.Equals(Path.GetFileName(f), "文件夹说明.txt", StringComparison.Ordinal)) return true;
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }
            return false;
        }

        /// <summary>
        /// exe 被移到新位置后的数据拉回：本机数据根「无数据」且上次锚定路径有数据 → 把锚定路径的数据整体搬过来，
        /// 而不是重建空树。同卷 rename（瞬间）；跨卷 copy + 删旧（搬成功的才删，失败保留双份不丢数据）。
        /// 无论拉不拉，每次启动都把锚点刷新为当前数据根（保证「上次路径」永远最新）。
        /// </summary>
        public static void RecoverMovedDataRoot()
        {
            try
            {
                string local = Path.GetFullPath(DataRoot);
                string recorded = ReadAnchoredRoot();

                bool needPull = false;
                if (!string.IsNullOrEmpty(recorded))
                {
                    string rec = Path.GetFullPath(recorded);
                    if (!string.Equals(rec, local, StringComparison.OrdinalIgnoreCase)
                        && !RootHasData(local)
                        && RootHasData(rec))
                        needPull = true;
                }

                if (needPull)
                {
                    Directory.CreateDirectory(local); // 目标数据根必须存在才能 Move/Copy
                    string rec = Path.GetFullPath(ReadAnchoredRoot());
                    foreach (var child in Directory.GetDirectories(rec))
                        PullChild(child, Path.Combine(local, Path.GetFileName(child)));
                    foreach (var f in Directory.GetFiles(rec))
                        PullChild(f, Path.Combine(local, Path.GetFileName(f)));
                }

                WriteAnchoredRoot(local); // 刷新锚点为当前数据根（本机数据根已有数据时不动数据，只更锚点）
            }
            catch (Exception ex) { DebugLog.Ignore(ex); /* 拉回失败不阻塞启动：旧位置数据仍在原地 */ }
        }

        /// <summary>递归拷贝目录树（跨卷拉回的兜底；.NET 无 Directory.Copy，手写）。</summary>
        private static void CopyDirTree(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.EnumerateFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
            foreach (var d in Directory.EnumerateDirectories(src))
                CopyDirTree(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        /// <summary>搬移单个子项（目录或文件）：目标已存在则跳过（不覆盖）；同卷 rename，跨卷 copy + 删旧（删除失败保留旧壳）。</summary>
        private static void PullChild(string src, string dst)
        {
            try
            {
                if (Directory.Exists(src))
                {
                    if (Directory.Exists(dst) || File.Exists(dst)) return;
                    try { Directory.Move(src, dst); }
                    catch (IOException)
                    {
                        // 跨卷：rename 不可用 → 递归拷贝后删旧（搬成功才删，保证不丢数据）
                        CopyDirTree(src, dst);
                        try { Directory.Delete(src, true); } catch { /* 删旧失败保留旧壳，不阻塞 */ }
                    }
                }
                else if (File.Exists(src))
                {
                    if (File.Exists(dst) || Directory.Exists(dst)) return;
                    try { File.Move(src, dst); }
                    catch (IOException)
                    {
                        File.Copy(src, dst);
                        try { File.Delete(src); } catch { }
                    }
                }
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }
        }

        /// <summary>递归搬移 src → dst（文件或目录）：目标已存在的一律跳过，只搬「目标缺失」的部分；不删除、不覆盖源。</summary>
        private static void MoveTree(string src, string dst)
        {
            try
            {
                if (!Directory.Exists(src) && !File.Exists(src)) return;
                if (Directory.Exists(src))
                {
                    if (Directory.Exists(dst))
                    {
                        // 两边都是目录 → 逐条合并（缺什么补什么）
                        foreach (var f in Directory.GetFiles(src))
                            MoveTree(f, Path.Combine(dst, Path.GetFileName(f)));
                        foreach (var d in Directory.GetDirectories(src))
                            MoveTree(d, Path.Combine(dst, Path.GetFileName(d)));
                    }
                    else
                    {
                        // 目标整树缺失 → 整体搬走
                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        Directory.Move(src, dst);
                    }
                }
                else if (File.Exists(src))
                {
                    if (!File.Exists(dst))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        File.Move(src, dst);
                    }
                    // 目标已存在同名文件 → 跳过（不覆盖）
                }
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }
        }
    }
}

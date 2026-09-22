// 智能外壳：.NET Framework 4.8，Win10 1809+/Win11 内置即可启动
// 职责：检测 .NET 10 桌面运行时 → 缺则双选项（自动安装 / 官方下载页）→
//       把内嵌主程序解到 AppData 缓存目录并启动（不在外壳目录留多余文件）。
//       对外是单一 exe；另以命令行参数 --cpq-data-root「外壳目录\cpq-tool」向主程序注入数据根，
//       使数据文件夹“跟外壳走”（外壳挪位后自动拉回，主程序 App.OnStartup 解析该参数 → AppPaths.DataRootOverride）。
//       注：主程序 manifest=requireAdministrator，启动必须 UseShellExecute=true（系统弹 UAC），
//       而 .NET Framework 该模式下禁止注入环境变量（ArgumentException）→ 只能走参数。
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CpqShell
{
    internal static class Consts
    {
        // —— 构建时钉死的常量（版本升级时同步改） ——
        public const string MainExeName = "cpq_main_v122.exe";
        public const string MainExeSha256 = "93bdcd946c37c33c9aebf35718e84992e0215db47a81e1a95d0b113c38ef70c3"; // v1.23 主线（升版 + 双 BOM 修复 + #184 审查修复后重 build；数据根走 --cpq-data-root 参数）
        public const string LoaderDllName = "WebView2Loader.dll";
        public const string LoaderSha256 = "74f16550da608ec233a3e54871ec72657dff34cdef068193c1a7b554b670a1a3";

        public const string RuntimeVersion = "10.0.12";
        public const string RuntimeFileName = "windowsdesktop-runtime-10.0.12-win-x64.exe";
        public const string RuntimeSha256 = "55a67d8476cde95a9cc43a95803b4f54446e7c9251ed22aa6421d4908174ae84";
        public const string RuntimeR2Url = "https://dl.cab.dpdns.org/net-runtime/windowsdesktop-runtime-10.0.12-win-x64.exe";
        public const string RuntimeOfficialUrl = "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.12/windowsdesktop-runtime-10.0.12-win-x64.exe";
        public const string DownloadPageUrl = "https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0";

        // 主程序部署目录：per-user AppData 缓存（免 UAC 可写；不在外壳目录留多余文件）
        public static string CacheDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CpqSystemTool", "cache", "main"); }
        }

        // 数据根提示：外壳目录可写 → 「外壳目录\cpq-tool」（数据跟外壳走）；否则 null（主程序回退自身 exe 目录\cpq-tool）
        public static string DataRootHint
        {
            get
            {
                string shellDir = ShellDir();
                if (!string.IsNullOrEmpty(shellDir) && IsWritableDir(shellDir))
                    return Path.Combine(shellDir, "cpq-tool");
                return null;
            }
        }

        private static string ShellDir()
        {
            try
            {
                string loc = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(loc)) return null;
                string d = Path.GetDirectoryName(loc);
                return Directory.Exists(d) ? d : null;
            }
            catch { return null; }
        }

        private static bool IsWritableDir(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, "__writeprobe_" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var f = File.Create(probe)) { }
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }
        public static string TempDir
        {
            get { return Path.Combine(Path.GetTempPath(), "cpq-bootstrap"); }
        }
        public static string SharedWdfDir
        {
            get { return @"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App"; }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            // .NET Framework HttpWebRequest must explicitly enable TLS 1.2, otherwise HTTPS download
            // throws "The request was aborted: Could not create SSL/TLS secure channel".
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            try
            {
                if (RuntimePresent())
                {
                    // 有运行时：静默透传（首次解缓存，之后 <2s 直达）
                    LaunchMain();
                    return 0;
                }

                // 缺运行时：中文双选项
                using (var dlg = new BootstrapForm())
                {
                    int rc = dlg.RunBootstrap();
                    if (rc == 0) { LaunchMain(); return 0; }
                    return rc; // 取消=1 / 失败=2
                }
            }
            catch (Exception ex)
            {
                try
                {
                    string logPath = Path.Combine(Consts.CacheDir, "shell-error.log");
                    File.WriteAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + ex.ToString() + "\r\n");
                }
                catch { }
                MessageBox.Show(
                    "外壳遇到问题：\n" + ex.Message + "\n\n若主程序已打开请先关闭再重试；若仍失败，请用「官方下载页」手动安装 .NET 10 后直接运行主程序。",
                    "系统清理与优化工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 3;
            }
        }

        // 检测标准：WindowsDesktop.App 10.x 目录存在（WPF 需要桌面运行时）
        public static bool RuntimePresent()
        {
            try
            {
                if (!Directory.Exists(Consts.SharedWdfDir)) return false;
                return Directory.EnumerateDirectories(Consts.SharedWdfDir)
                    .Any(d => d.IndexOf(@"\10.0.", StringComparison.Ordinal) >= 0
                              && !d.EndsWith("_DISABLED", StringComparison.Ordinal));
            }
            catch { return false; }
        }

        public static void LaunchMain()
        {
            EnsureCache();
            var dir = Consts.CacheDir;
            var path = Path.Combine(dir, Consts.MainExeName);
            // 主程序 manifest=requireAdministrator：必须 ShellExecute（系统自动弹 UAC，与双击桌面版一致）；
            // UseShellExecute=false 会以 ELEVATION_REQUIRED 失败。
            var psi = new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = dir,
                UseShellExecute = true,
            };
            // “数据跟外壳走”：以命令行参数注入数据根（外壳目录\cpq-tool）；
            // 主程序 App.OnStartup 解析 --cpq-data-root。.NET Framework 的 UseShellExecute=true 模式
            // 禁止注入环境变量（ArgumentException: “Process 对象必须将 UseShellExecute 属性设置为 False 才能使用环境变量”），
            // 参数是唯一可行通道。外壳目录不可写时不注入（主程序回退自身 exe 目录）。
            string hint = Consts.DataRootHint;
            if (!string.IsNullOrEmpty(hint)) psi.Arguments = "--cpq-data-root \"" + hint + "\"";
            Process.Start(psi);
        }

        // 解内嵌资源到缓存目录（每次按 SHA 校验，不一致则重写）
        public static void EnsureCache()
        {
            Directory.CreateDirectory(Consts.CacheDir);
            CopyChecked("CpqShell.payload.main", Consts.MainExeName, Consts.MainExeSha256);
            CopyChecked("CpqShell.payload.loader", Consts.LoaderDllName, Consts.LoaderSha256);
        }

        private static void CopyChecked(string resName, string fileName, string wantSha)
        {
            var dst = Path.Combine(Consts.CacheDir, fileName);
            string haveSha = Sha256(dst);
            if (string.Equals(haveSha, wantSha, StringComparison.OrdinalIgnoreCase)) return; // 缓存命中
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName))
            {
                if (s == null) throw new FileNotFoundException("内嵌资源缺失：" + resName);
                string tmp = dst + ".tmp";
                using (var o = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    s.CopyTo(o);
                string got = Sha256(tmp);
                if (!string.Equals(got, wantSha, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("内嵌主程序校验失败（SHA 不一致），请重新下载本文件。");
                if (File.Exists(dst))
                {
                    // MoveFileEx REPLACE_EXISTING：可替换正在运行的 exe/dll（delete-share 机制）
                    if (!MoveFileEx(tmp, dst, MOVEFILE_REPLACE_EXISTING))
                    {
                        int err = Marshal.GetLastWin32Error();
                        throw new IOException("替换缓存文件失败（Win32 错误 " + err + "）。若主程序正在运行请先关闭它再重试。");
                    }
                }
                else
                {
                    File.Move(tmp, dst);
                }
            }
        }

        public static string Sha256(string path)
        {
            if (!File.Exists(path)) return null;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var h = SHA256.Create())
            {
                byte[] v = h.ComputeHash(fs);
                return BitConverter.ToString(v).Replace("-", "").ToLowerInvariant();
            }
        }

        const int MOVEFILE_REPLACE_EXISTING = 0x1;
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool MoveFileEx(string src, string dst, int flags);
    }

    // 缺运行时时显示的中文对话框 + 自动安装逻辑
    internal sealed class BootstrapForm : Form
    {
        // 进度条：不占满整页宽度，取 4/5 宽并水平居中（560 宽窗内 448，余 56 两边各留白）
        private readonly ProgressBar _bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 18, Left = 56, Top = 186, Width = 448, Anchor = AnchorStyles.None, Visible = false };
        private readonly Label _status;
        private readonly Button _btnAuto, _btnPage, _btnCancel;

        public BootstrapForm()
        {
            Text = "系统清理与优化工具 - 首次启动";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 250);
            Font = new Font("Microsoft YaHei UI", 9F);

            var head = new Label
            {
                Dock = DockStyle.Top,
                Height = 84,
                Padding = new Padding(16, 14, 16, 0),
                Text = "未检测到 .NET 10 桌面运行时（本程序运行所需）。\n\n"
                       + "「自动安装」会从国内镜像下载约 60MB 官方运行时并静默安装，全程一次；\n"
                       + "也可以到「官方下载页」自行下载安装。",
            };
            _status = new Label { Dock = DockStyle.Top, Height = 22, Padding = new Padding(16, 0, 16, 0) };

            var row = new Panel { Dock = DockStyle.Bottom, Height = 46 };
            _btnCancel = new Button { Text = "取消", Width = 84, Height = 34, Anchor = AnchorStyles.None };
            _btnPage = new Button { Text = "官方下载页", Width = 130, Height = 34 };
            _btnAuto = new Button { Text = "自动安装（推荐）", Width = 160, Height = 34 };
            _btnAuto.Click += (s, e) => DoAutoInstall();
            _btnPage.Click += (s, e) => OpenBrowser(Consts.DownloadPageUrl);
            _btnCancel.Click += (s, e) => { RunResult = 1; Close(); };

            row.Controls.Add(_btnCancel); row.Controls.Add(_btnPage); row.Controls.Add(_btnAuto);
            _btnCancel.Left = 476; _btnPage.Left = 326; _btnAuto.Left = 156;
            _btnCancel.Top = 6; _btnPage.Top = 6; _btnAuto.Top = 6;

            Controls.Add(_status);
            Controls.Add(head);
            Controls.Add(_bar);
            Controls.Add(row);
        }

        public int RunResult { get; private set; }

        public int RunBootstrap()
        {
            RunResult = 1;
            Application.Run(this);
            return RunResult;
        }

        private static void OpenBrowser(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }

        private void SetBusy(bool b)
        {
            _btnAuto.Enabled = _btnPage.Enabled = _btnCancel.Enabled = !b;
            _bar.Visible = b;
        }

        // 跨线程安全地更新 UI（worker 线程调用；安装期/复用分支的状态与进度条都走这里）
        private void Ui(Action a)
        {
            try { BeginInvoke(a); } catch { }
        }

        private void DoAutoInstall()
        {
            SetBusy(true);
            _status.Text = "准备下载…";
            var worker = new Thread(() =>
            {
                try
                {
                    Directory.CreateDirectory(Consts.TempDir);
                    string target = Path.Combine(Consts.TempDir, Consts.RuntimeFileName);

                    // ① 先 R2 镜像，失败回官方直链；均校验钉死 SHA
                    string r2Err = null, offErr = null;
                    if (File.Exists(target)
                        && string.Equals(Program.Sha256(target), Consts.RuntimeSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        Ui(delegate { _status.Text = "复用已下载的运行时安装器（跳过下载）…"; });
                    }
                    else if (DownloadWithProgress(Consts.RuntimeR2Url, target, out r2Err))
                    {
                        CheckSha(target, "国内镜像");
                    }
                    else if (DownloadWithProgress(Consts.RuntimeOfficialUrl, target, out offErr))
                    {
                        CheckSha(target, "官方直链");
                    }
                    else
                    {
                        Fail("运行时下载失败：\n"
                             + "· 国内镜像：" + (string.IsNullOrEmpty(r2Err) ? "不可达" : r2Err) + "\n"
                             + "· 官方直链：" + (string.IsNullOrEmpty(offErr) ? "不可达" : offErr) + "\n"
                             + "请改用「官方下载页」手动安装。");
                        return;
                    }

                    // ② 静默安装（需管理员；UAC 弹窗由系统给出）
                    // Burn 静默安装无法读取内部进度 → 进度条切 Marquee（滚动动画）表达“进行中”，装完切回 100%
                    Ui(delegate { _status.Text = "安装中（若弹出 UAC 授权框请点「是」）…"; _bar.Style = ProgressBarStyle.Marquee; });
                    int installCode = -1;
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = target,
                            Arguments = "/quiet /norestore",
                            UseShellExecute = true,
                            Verb = "runas",
                            CreateNoWindow = false,
                        };
                        using (var p = Process.Start(psi))
                        {
                            p.WaitForExit();
                            installCode = p.ExitCode;
                        }
                    }
                    catch (System.UnauthorizedAccessException)
                    {
                        Fail("UAC 授权被拒绝或未弹出。请重试并在 UAC 窗口点「是」；或改用「官方下载页」手动安装。");
                        return;
                    }
                    catch (System.ComponentModel.Win32Exception wex)
                    {
                        Fail("启动安装器失败（" + wex.Message + "，错误码 " + wex.NativeErrorCode + "）。可重试或改用「官方下载页」。");
                        return;
                    }
                    if (installCode != 0 && installCode != 3010)
                    {
                        Fail("运行时安装未完成（安装器退出码 " + installCode + "；0 或 3010 为正常）。可重试或改用官方下载页。");
                        return;
                    }

                    // ③ 装完复查运行时目录
                    Ui(delegate { _status.Text = "安装完成，正在复查…"; _bar.Style = ProgressBarStyle.Continuous; _bar.Value = 100; });
                    Thread.Sleep(3000);
                    if (!Program.RuntimePresent())
                    {
                        Fail(BuildInstallNoOpMessage(installCode));
                        return;
                    }

                    Ui(delegate { _status.Text = "运行时安装成功"; });
                    Thread.Sleep(600);
                    RunResult = 0; // Main() 会在表单关闭后统一拉起主程序（避免双启动）
                    try { BeginInvoke(new Action(Close)); } catch { }
                }
                catch (Exception ex)
                {
                    Fail("自动安装失败：" + ex.Message);
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void Fail(string msg)
        {
            try
            {
                BeginInvoke(new Action(() =>
                {
                    SetBusy(false);
                    _bar.Visible = false;
                    _status.Text = "失败";
                    MessageBox.Show(this, msg, "系统清理与优化工具", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    SetBusy(false); // 解锁按钮：可重试自动安装，或点官方下载页
                }));
            }
            catch { /* 表单已关闭（用户点取消）→ worker 继续无意义，静默退出 */ }
        }

        // 读最新 .NET 10 安装器日志尾部（附到报错，帮助诊断“no-op”类失败）
        private static string InstallerLogTail()
        {
            try
            {
                string[] logs = Directory.GetFiles(Path.GetTempPath(), "Microsoft_Windows_Desktop_Runtime_*.log");
                if (logs == null || logs.Length == 0) return "（Temp 目录下未找到 .NET 10 安装器日志）";
                Array.Sort(logs, delegate (string a, string b)
                {
                    return File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b));
                });
                string newest = logs[logs.Length - 1];
                string[] lines = File.ReadAllLines(newest);
                int start = lines.Length > 8 ? lines.Length - 8 : 0;
                StringBuilder sb = new StringBuilder();
                for (int i = start; i < lines.Length; i++) sb.AppendLine(lines[i]);
                return "—— 安装器日志 " + Path.GetFileName(newest) + "（末尾 " + (lines.Length - start) + " 行）——\n" + sb.ToString();
            }
            catch (Exception ex) { return "（读取安装器日志失败：" + ex.Message + "）"; }
        }

        // 「安装器退出 0 但目录未出现」的针对性诊断：区分“目录被改名/隐藏（模拟脚本）”与“孤儿注册/其它 SDK 占用”两种状态
        private static string BuildInstallNoOpMessage(int installCode)
        {
            string hidden = DetectDisabledRuntimeDirs();
            string msg;
            if (hidden != null)
            {
                msg = "安装器已结束（退出码 " + installCode + "）但未检测到 .NET 10 运行时目录。\n\n"
                    + "检测到被「隐藏」的 .NET 10 目录（模拟脚本改名所致，安装注册仍在，安装器因此跳过了安装）：\n  " + hidden + "\n\n"
                    + "→ 若刚用「模拟无Net10」测试：请先双击「恢复Net10」还原（还原后本对话框不会再出现）；\n"
                    + "→ 若要测真实安装链路：请先用「Net10最终彻底清理v2」真卸载（连安装注册一起删），再回来点「自动安装」。";
            }
            else
            {
                msg = "安装器已结束（退出码 " + installCode + "）但未检测到 .NET 10 运行时目录。\n"
                    + "本机若装有 .NET SDK 等依赖 .NET 10 的组件，安装器会判定已安装而跳过（干净机器不会发生）。\n"
                    + "可重试、改用「官方下载页」手动安装，或联系维护者。";
            }
            return msg + "\n\n" + InstallerLogTail();
        }

        // 扫描 WDF/NETCore shared 目录下被改名隐藏的 10.x（模拟脚本的 *_DISABLED 后缀）
        private static string DetectDisabledRuntimeDirs()
        {
            try
            {
                string[] roots = { Consts.SharedWdfDir, @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App" };
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < roots.Length; i++)
                {
                    string root = roots[i];
                    if (!Directory.Exists(root)) continue;
                    string[] hits = Directory.GetDirectories(root, "10.*_DISABLED");
                    for (int j = 0; j < hits.Length; j++)
                    {
                        if (sb.Length > 0) sb.Append("、");
                        sb.Append(Path.GetFileName(hits[j]));
                    }
                }
                return sb.Length > 0 ? sb.ToString() : null;
            }
            catch { return null; }
        }

        private static void CheckSha(string file, string srcName)
        {
            string got = Program.Sha256(file);
            if (!string.Equals(got, Consts.RuntimeSha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(file); } catch { }
                throw new IOException(srcName + " 下载文件校验失败（SHA 不一致），已删除该文件。");
            }
        }

        private bool DownloadWithProgress(string url, string target, out string err)
        {
            err = null;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    long total = resp.ContentLength;
                    using (var rs = resp.GetResponseStream())
                    using (var fs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buf = new byte[64 * 1024];
                        int n; long done = 0;
                        while ((n = rs.Read(buf, 0, buf.Length)) > 0)
                        {
                            fs.Write(buf, 0, n);
                            done += n;
                            int pct = total > 0 ? (int)(done * 100 / total) : 0;
                            try { BeginInvoke(new Action(() => { _bar.Value = pct; _status.Text = "下载中 " + pct + "%（" + done / 1048576 + "MB）"; })); } catch { }
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                if (ex.InnerException != null) err = err + " | " + ex.InnerException.Message;
                try { if (File.Exists(target)) File.Delete(target); } catch { }
                return false;
            }
        }
    }
}

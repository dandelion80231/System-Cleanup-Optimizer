using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CpqSystemTool
{
    // ===================== WebView2 实现的探针浏览器驱动 =====================
    // 复用系统已安装的 Microsoft Edge / WebView2 Runtime，无需下载 Chromium 或 Node。
    // 设计：独立 STA 线程承载一个 WinForms 隐藏 Form + WinForms WebView2 控件，经 CDP 捕获安装包网络请求与下载事件，
    //       并在页面上下文中执行注入 JS 完成 DOM 扫描 / 点击策略。
    // 采用 WinForms 而非 WPF 承载：WPF 的 WebView2 控件要求在存在 Application.Current 的线程上初始化，
    // 而一个 AppDomain 只能有一个 WPF Application 实例（主程序已占用），后台 STA 线程无法再创建，
    // 导致 EnsureCoreWebView2Async 握手机卡死。WinForms 的 Application 是每线程独立的，不受此限制，
    // 是业界做离屏 WebView2 抓取最稳的模式。底层 CoreWebView2（导航/执行 JS/抓包）接口不变。
    internal sealed class ProbeBrowserHost : IProbeBrowser
    {
        private Thread _thread;
        private Form _form;
        private WebView2 _wv;
        private CoreWebView2 _core;
        private TaskCompletionSource<bool> _initTcs;

        private ConcurrentBag<CandidateUrl> _captured = new ConcurrentBag<CandidateUrl>();
        private volatile bool _capturing;
        private string _initError;
        private string _lastStep = "未开始";
        private string _userDataDir;

        /// <summary>默认初始化 / 检测超时（秒）。所有创建 WebView2 环境的地方统一引用，避免魔法数字散落。</summary>
        private static readonly TimeSpan DefaultInitTimeout = TimeSpan.FromSeconds(20);

        /// <summary>初始化前等待窗口 HWND/DWM 稳定的延迟。</summary>
        private static readonly TimeSpan HwndSettleDelay = TimeSpan.FromMilliseconds(100);

        /// <summary>预检 Runtime 版本时的轻量超时。</summary>
        private static readonly TimeSpan RuntimeVersionCheckTimeout = TimeSpan.FromSeconds(5);

        /// <summary>WebView2 就绪检测结果的进程内缓存 TTL（秒）。命中时直接返回，不再真实创建离屏 WebView2 初始化。</summary>
        private static readonly TimeSpan WvReadyCacheTtl = TimeSpan.FromSeconds(30);

        /// <summary>就绪检测缓存：(Ready, Error, TickCount64 时间戳)。Ticks==0 表示从未检测过。读写都经 _wvReadyLock 互斥保护。</summary>
        private static (bool Ready, string Error, long Ticks) _wvReadyCache;

        /// <summary>保护 _wvReadyCache 的锁：初始化在后台线程、读取在 UI 线程，必须互斥（值元组跨 8 字节，不可无锁读取）。</summary>
        private static readonly object _wvReadyLock = new object();

        /// <summary>初始化失败时的真实异常信息（供调用方写入日志，便于排查）。</summary>
        public string InitError => _initError;

        // 显式实现 IProbeBrowser.InitAsync()（默认 20 秒超时，diagnostics 透传到具体实现）。
        Task<bool> IProbeBrowser.InitAsync(Action<string> diag) => InitAsync(DefaultInitTimeout, diag);

        // 带硬超时的初始化实现（默认 20 秒，可通过 timeout / diag 覆盖）。
        public async Task<bool> InitAsync(TimeSpan timeout = default, Action<string> diag = null)
        {
            _initTcs = new TaskCompletionSource<bool>();
            _lastStep = "启动 STA 线程";

            // 安全网：探针初始化前确保 exe 目录存在 WebView2 托管依赖（单文件分发场景）。
            // 失败仅记录、不抛异常，随后仍走既有 WebView2 初始化 / Node 回退逻辑。
            try { await WebView2ProbeDeps.EnsureWebView2ProbeDepsAsync(diag, p => (diag ?? (_ => { }))(WebView2ProbeDeps.ProgressLine(p))); }
            catch (Exception ex) { diag?.Invoke("[WebView2] 探针依赖预拉取异常（已忽略）：" + ex.Message); }

            // 若托管依赖在下载/解压后仍缺失（如离线、目录只读），不要启动 STA 线程——
            // 否则 STA 线程构造 WebView2 控件会触发 FileNotFoundException，且 assembly 缺失常在
            // STA 线程 JIT 阶段即炸，早于 lambda 内 try/catch 的执行，从而逃逸为
            // AppDomain.UnhandledException 导致进程崩溃。改为优雅返回 false，由上层回退 Node 方案。
            try
            {
                string exeDirNow = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(exeDirNow)
                    || !File.Exists(Path.Combine(exeDirNow, "Microsoft.Web.WebView2.WinForms.dll")))
                {
                    diag?.Invoke("[WebView2] 托管依赖缺失且无法获取，跳过 WebView2 探针（将回退 Node）");
                    _initTcs.TrySetResult(false);
                    return false;
                }
            }
            catch { /* 目录探测异常时继续尝试，交由 STA 线程兜底 */ }

            // 轻量预检：先尝试读取系统 WebView2 Runtime 版本（不创建窗口/控件）。
            // 这能区分「未安装」和「已安装但 EnsureCoreWebView2Async 卡住」两类场景。
            try
            {
                var verTask = Task.Run(() => CoreWebView2Environment.GetAvailableBrowserVersionString(null));
                var completedVer = await Task.WhenAny(verTask, Task.Delay(RuntimeVersionCheckTimeout));
                if (completedVer == verTask)
                    // verTask 已被 WhenAny 确认完成，此处读取不阻塞（非 sync-over-async）；
                    // GetAwaiter().GetResult() 保留原始异常语义、避免 AggregateException 包装。
                    diag?.Invoke("[WebView2] 检测到系统 Runtime 版本：" + verTask.GetAwaiter().GetResult());
                else
                    diag?.Invoke("[WebView2] 检测 Runtime 版本超时（5 秒），可能已损坏或无法访问");
            }
            catch (Exception ex)
            {
                diag?.Invoke("[WebView2] 未检测到可用 Runtime：" + ex.Message);
            }

            diag?.Invoke("[WebView2] " + _lastStep);
            _thread = new Thread(() =>
            {
                try
                {
                    // 在 STA 线程上建立 WinForms 消息循环。WinForms 的 Application 是每线程独立的，
                    // 不受主程序 WPF Application.Current 单例限制（一个 AppDomain 只能有一个 WPF Application，
                    // 主程序已占用），因此后台线程必须用 WinForms 承载 WebView2，否则 EnsureCoreWebView2Async 握手机卡死。
                    var form = new Form
                    {
                        // 8×8 像素置于工作区左上角：在屏幕内（HWND 有效、会被 DWM 合成），几乎不可见。
                        Width = 8,
                        Height = 8,
                        FormBorderStyle = FormBorderStyle.None,
                        ShowInTaskbar = false,
                        StartPosition = FormStartPosition.Manual,
                        Location = new System.Drawing.Point(SystemInformation.WorkingArea.Left, SystemInformation.WorkingArea.Top),
                        Opacity = 0,   // WinForms 的 Opacity 不会像 WPF 那样把子 HWND 变成分层黑框，可安全隐藏
                        BackColor = System.Drawing.Color.Black,
                        Text = "ProbeHost",
                    };
                    var wv = new WebView2 { Dock = DockStyle.Fill };
                    form.Controls.Add(wv);
                    _form = form;
                    _wv = wv;

                    // form.Load 在 STA 线程的 async 上下文中触发，异常可正确传播（而非被吞掉）。
                    form.Load += async (s, e) =>
                    {
                        try { await InitBrowserAsync(diag); }
                        catch (Exception ex)
                        {
                            _initError = "宿主初始化异常: " + ex.GetType().Name + " - " + ex.Message;
                            _initTcs.TrySetResult(false);
                        }
                    };
                    Application.Run(form);   // 阻塞直到 form.Close()；之后 STA 线程结束
                }
                catch (Exception ex)
                {
                    // 捕获 STA 线程启动初期的异常（如 WebView2 程序集缺失），避免未处理异常终止进程。
                    _initError = "STA 线程启动失败: " + ex.GetType().Name + " - " + ex.Message;
                    diag?.Invoke("[WebView2] " + _initError);
                    _initTcs.TrySetResult(false);
                }
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();

            if (timeout == default) timeout = DefaultInitTimeout;

            var completedTask = await Task.WhenAny(_initTcs.Task, Task.Delay(timeout));
            if (completedTask == _initTcs.Task)
            {
                try { return await _initTcs.Task; }
                catch { return false; }
            }

            // 硬超时：初始化在 timeout 内未完成，强制结束。记录最后停留步骤，便于定位卡点。
            _initError = "初始化超时（" + timeout.TotalSeconds + " 秒）—— 最后停留在：" + _lastStep
                + "；若持续，多为本机 WebView2/Edge 运行时文件残损（msedge.dll / WebView2Loader.dll 缺失）或注册损坏，建议重装 Microsoft Edge 或 WebView2 Runtime";
            diag?.Invoke("[WebView2] " + _initError);
            _initTcs.TrySetResult(false);
            // 通过 WinForms 消息循环关闭隐藏 Form 并退出 Application.Run，使 STA 线程结束。
            try
            {
                _form?.BeginInvoke(new Action(() =>
                {
                    try { _form?.Close(); } catch { }
                }));
            }
            catch { }
            return false;
        }

        /// <summary>判断运行时目录是否完整（含 msedgewebview2.exe / msedge.dll）。注意：现代 Edge 主二进制名为 msedge.dll（旧版曾叫 chrome.dll）；WebView2Loader.dll 是加载器，位于调用进程目录或 System32，不在运行时目录内，故不在此检查。</summary>
        private static bool RuntimeFolderComplete(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
            return File.Exists(Path.Combine(folder, "msedgewebview2.exe"))
                && File.Exists(Path.Combine(folder, "msedge.dll"));
        }

        /// <summary>
        /// 在 WinForms UI 线程上执行真实的 WebView2 初始化（CreateAsync + EnsureCoreWebView2Async）。
        /// 由 form.Load 事件在 STA 线程的 async 上下文中调用，异常可正确传播。
        /// </summary>
        private async Task InitBrowserAsync(Action<string> diag)
        {
            // CreateAsync 签名：(browserExecutableFolder, userDataFolder, options)
            //   第一个参数必须传 null —— 使用系统已装的 WebView2 Runtime / Edge；
            //   第二个参数才是用户数据目录。每次用全新 GUID 子目录，避免上次异常退出
            //   遗留的锁文件（WebView2 在用户数据目录写 .lock）导致本次 CreateAsync / EnsureCoreWebView2Async 挂起。
            CoreWebView2Environment env = null;
            try
            {
                _lastStep = "CoreWebView2Environment.CreateAsync（定位系统 Runtime）";
                // 主动定位运行时目录：本机 WebView2 注册表（EdgeWebView\Applications）损坏 /
                // WebView2Loader 未注册到 System32 时，CreateAsync(null) 走注册表解析会挂起；
                // 改为显式传入磁盘上实际存在的运行时目录，直接绕过注册表，复用已有二进制。
                var browserFolder = ResolveWebView2BrowserFolder();
                if (!string.IsNullOrEmpty(browserFolder))
                    diag?.Invoke("[WebView2] 显式运行时目录：" + browserFolder);
                else
                    diag?.Invoke("[WebView2] 未定位到显式目录，退回系统注册表解析");

                // 快速预检：显式目录若缺少核心二进制（msedgewebview2.exe / msedge.dll），说明本机
                // 运行时残损，直接失败并给出明确提示，避免白等 20 秒超时。
                if (!string.IsNullOrEmpty(browserFolder) && !RuntimeFolderComplete(browserFolder))
                {
                    _initError = "运行时目录文件残损（缺少 msedgewebview2.exe / msedge.dll）：" + browserFolder
                        + "；多为本机 WebView2/Edge 安装不完整，需重装 Microsoft Edge 或 WebView2 Runtime";
                    diag?.Invoke("[WebView2] " + _initError);
                    _initTcs.TrySetResult(false);
                    return;
                }

                diag?.Invoke("[WebView2] " + _lastStep);
                var tmp = CreateTempUserDataDir("CpqProbeWebView2_");
                _userDataDir = tmp;
                env = await CoreWebView2Environment.CreateAsync(browserFolder, tmp, CreateEnvironmentOptions());
            }
            catch (Exception ex)
            {
                _initError = "CreateAsync 失败: " + ex.GetType().Name + " - " + ex.Message;
                _initTcs.TrySetResult(false);
                return;
            }

            _lastStep = "EnsureCoreWebView2Async（初始化渲染进程）";
            diag?.Invoke("[WebView2] " + _lastStep);
            // 窗口 Show 后等待一帧，让 HWND/DWM 稳定，再初始化 CoreWebView2。
            await Task.Delay(HwndSettleDelay);
            try
            {
                await _wv.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                _initError = "EnsureCoreWebView2Async 失败: " + ex.GetType().Name + " - " + ex.Message;
                _initTcs.TrySetResult(false);
                return;
            }

            _core = _wv.CoreWebView2;
            if (_core == null)
            {
                _initError = "EnsureCoreWebView2Async 完成但 CoreWebView2 仍为 null";
                _initTcs.TrySetResult(false);
                return;
            }
            _lastStep = "配置 CDP（启用 Network 抓包）";
            await SetupCdpAsync();
            _lastStep = "完成";
            diag?.Invoke("[WebView2] 初始化成功（CoreWebView2 就绪）");
            _initTcs.TrySetResult(true);
        }

        /// <summary>
        /// 权威检测系统是否安装了可用的 WebView2 Runtime，且能真正完成初始化。
        /// 与早期仅调用 CreateAsync（只“定位”Runtime、不验证渲染）不同，本方法复用 InitAsync
        /// 真正创建离屏窗口并 await EnsureCoreWebView2Async——这正是探针实际使用的初始化路径。
        /// 因此它能准确区分「Runtime 已装但 EnsureCoreWebView2Async 挂起」这类故障
        /// （此前误报“就绪”的根因：CreateAsync 成功 ≠ 浏览器能初始化）。
        /// 在 timeout 内成功返回 (true, null)；失败或超时返回 (false, 错误信息)。
        /// </summary>
        public static async Task<(bool Ready, string Error)> CheckWebView2ReadyAsync(TimeSpan timeout, Action<string> diag = null, Action<int> progress = null)
        {
            // —— 状态缓存（进程内 30s TTL）——
            // 反复点击「管理依赖」每次都会真实创建离屏 WebView2 验证就绪（最长 15s）。这里把最近一次检测结果缓存 30s：
            // 命中直接返回，未命中才真正初始化；成功与失败结果都缓存（失败同样 30s，避免反复失败时每次慢速重试）。
            // 缓存只影响「是否真的跑初始化」；命中时无初始化过程，因此不触发 diag/progress 回调，
            // 外部签名与行为（timeout 默认化、diag/progress 回调语义）保持不变。
            lock (_wvReadyLock)
            {
                var cached = _wvReadyCache;
                if (cached.Ticks != 0 && Environment.TickCount - cached.Ticks < (long)WvReadyCacheTtl.TotalMilliseconds)
                    return (cached.Ready, cached.Error);
            }

            var result = await CheckWebView2ReadyCoreAsync(timeout, diag, progress);

            lock (_wvReadyLock)
            {
                _wvReadyCache = (result.Ready, result.Error, Environment.TickCount);
            }
            return result;
        }

        /// <summary>CheckWebView2ReadyAsync 的真实初始化实现（无缓存），由公开方法在缓存未命中时调用。内容与原实现一致。</summary>
        private static async Task<(bool Ready, string Error)> CheckWebView2ReadyCoreAsync(TimeSpan timeout, Action<string> diag = null, Action<int> progress = null)
        {
            if (timeout <= TimeSpan.Zero) timeout = DefaultInitTimeout;
            try
            {
                // 安全网前置：必须先于 new ProbeBrowserHost() 拉取托管依赖——
                // 否则构造时 CLR 需解析 WebView2 字段类型（_wv/_core），DLL 缺失会直接抛
                // TypeLoadException/FileNotFoundException，永远走不到 InitAsync 里的下载。
                // RunProbeInternal 已如此排序，此处对齐，确保刷新状态路径也能自愈。
                // 本方法由 deps 弹窗刷新（RefreshDepStatus，跑在 UI 线程）await 调用，
                // 首次 DLL 缺失时下载最长 60s；EnsureWebView2ProbeDepsAsync 自身即为异步且不阻塞调用线程，
                // 直接 await 即可避免界面冻结（无需再包一层 Task.Run）。
                // 刷新路径 diag 为 null（仅写诊断日志文件、不刷 UI），但 progress 回调由 RefreshDepStatus
                // 经 Dispatcher 回到 UI 线程写入日志框，显示下载百分比（\r 前缀原地刷新最后一行）。
                await WebView2ProbeDeps.EnsureWebView2ProbeDepsAsync(diag, progress);

                // 复用真实初始化路径：创建宿主窗口（屏幕内渲染）+ EnsureCoreWebView2Async。
                // 用 using 确保无论成败都 Dispose 掉 STA 线程与临时用户数据目录。
                using (var host = new ProbeBrowserHost())
                {
                    bool ok = await host.InitAsync(timeout, diag);
                    if (ok)
                    {
                        diag?.Invoke("[WebView2] 检测：Runtime 可用且 EnsureCoreWebView2Async 初始化成功");
                        return (true, null);
                    }
                    var detail = host.InitError;
                    var msg = "检测 WebView2 初始化失败" + (string.IsNullOrEmpty(detail) ? "" : "：" + detail);
                    diag?.Invoke("[WebView2] " + msg);
                    return (false, msg);
                }
            }
            catch (Exception ex)
            {
                var msg = "检测 WebView2 失败: " + ex.GetType().Name + " - " + ex.Message;
                diag?.Invoke("[WebView2] " + msg);
                return (false, msg);
            }
        }

        private async Task SetupCdpAsync()
        {
            _core.Settings.AreDefaultContextMenusEnabled = false;
            _core.Settings.AreDevToolsEnabled = false;
            _core.DownloadStarting += OnDownloadStarting;

            _core.GetDevToolsProtocolEventReceiver("Network.responseReceived").DevToolsProtocolEventReceived += OnCdpResponse;
            _core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent").DevToolsProtocolEventReceived += OnCdpRequest;

            // 启用 Network 域（捕获安装包请求 / 响应）。
            // 关键修复：必须在 UI 线程用 await 而非 .GetAwaiter().GetResult()/Result 同步阻塞——
            // CDP 响应经 UI 线程消息循环回派，阻塞等待会令 UI 线程卡死、响应永远无法派发，造成死锁（此前 20s 超时真因）。
            try { await _core.CallDevToolsProtocolMethodAsync("Network.enable", "{}"); }
            catch (Exception ex) { Debug.WriteLine("[ProbeBrowserHost] Network.enable 失败: " + ex.Message); }
        }

        private void OnDownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            try
            {
                var uri = e.DownloadOperation?.Uri;
                if (!string.IsNullOrEmpty(uri))
                    _captured.Add(new CandidateUrl { Url = uri, Strategy = "download" });
            }
            catch { }
            try { e.Cancel = true; } catch { }
        }

        private void OnCdpResponse(object sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            if (_capturing) CaptureInstallerFromCdp(e.ParameterObjectAsJson, "response");
        }

        private void OnCdpRequest(object sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            if (_capturing) CaptureInstallerFromCdp(e.ParameterObjectAsJson, "network");
        }

        private void CaptureInstallerFromCdp(string json, string strategy)
        {
            var url = ExtractUrl(json);
            if (string.IsNullOrEmpty(url)) return;
            // 仅接受 http(s) 安装包 URL，屏蔽 file:// 等本地 / 异常协议，避免把本地路径当作候选直链。
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            if (ProbeData.MobileMacPkg.IsMatch(url)) return;
            if (IsInstallerLike(url) || HasInstallerContentType(json))
                _captured.Add(new CandidateUrl { Url = url.Split('#')[0], Strategy = strategy });
        }

        private static bool IsInstallerLike(string u)
        {
            // 先排除明显不是安装包的脚本/配置/页面文件，避免如 qq 的 /assets/download.js 被误判。
            if (Regex.IsMatch(u, @"\.(js|css|html|htm|json|xml|txt|png|jpg|jpeg|gif|svg|webp|ico|woff|woff2|ttf|eot)(\?|$)", RegexOptions.IgnoreCase))
                return false;
            return Regex.IsMatch(u, @"\.exe(\?|$)|file_redirect\.fcg", RegexOptions.IgnoreCase)
                || Regex.IsMatch(u, @"/download|/setup|/client|/install(er)?", RegexOptions.IgnoreCase)
                || Regex.IsMatch(u, @"\b(win32|win64|x64|x86_64|amd64|windows)\b", RegexOptions.IgnoreCase);
        }

        private static bool HasInstallerContentType(string json)
        {
            return json.IndexOf("x-msdownload", StringComparison.OrdinalIgnoreCase) >= 0
                || json.IndexOf("octet-stream", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ExtractUrl(string json)
        {
            int idx = json.IndexOf("\"url\"", StringComparison.Ordinal);
            if (idx < 0) return null;
            int colon = json.IndexOf(':', idx);
            if (colon < 0) return null;
            int q1 = json.IndexOf('"', colon);
            if (q1 < 0) return null;
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return null;
            return json.Substring(q1 + 1, q2 - q1 - 1).Replace("\\/", "/");
        }

        public Task<BrowserProbeResult> ProbeSiteAsync(string entryUrl, bool skipDownloadCheck)
        {
            return RunOnBrowserThread(async () =>
            {
                var res = new BrowserProbeResult();
                try
                {
                    _captured = new ConcurrentBag<CandidateUrl>();
                    _capturing = true;
                    await NavigateAndWaitAsync(entryUrl);
                    // 部分站点（如 im.qq.com/pcqq）的下载链接由 JS 动态注入，需要给足渲染时间；
                    // 跳过点击模式不模拟点击，更依赖页面完全渲染。
                    await Task.Delay(skipDownloadCheck ? 3000 : 2500);

                    string script = BuildProbeScript(skipDownloadCheck);
                    string raw = await _core.ExecuteScriptAsync(script);
                    var fromJs = ParseDelimited(raw);
                    _capturing = false;

                    // 诊断：首轮为空时，多半是页面异步渲染未完成，再等 2.5s 重试一次 JS 提取。
                    if (fromJs.Count == 0 && _captured.Count == 0)
                    {
                        await Task.Delay(2500);
                        _capturing = true;
                        raw = await _core.ExecuteScriptAsync(script);
                        fromJs = ParseDelimited(raw);
                        _capturing = false;
                    }

                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var c in fromJs) if (seen.Add(c.Url)) res.Candidates.Add(c);
                    foreach (var c in _captured)
                        if (!ProbeData.MobileMacPkg.IsMatch(c.Url) && seen.Add(c.Url)) res.Candidates.Add(c);
                }
                catch (Exception ex)
                {
                    _capturing = false;
                    res.Error = ex.Message;
                }
                return res;
            });
        }

        public Task<BrowserSearchResult> SearchAsync(string name)
        {
            return RunOnBrowserThread(async () =>
            {
                var res = new BrowserSearchResult();
                try
                {
                    string q = Uri.EscapeDataString(name + " 官方下载 exe");
                    await NavigateAndWaitAsync("https://www.bing.com/search?q=" + q);
                    await Task.Delay(1500);
                    string script = BuildSearchScript();
                    string raw = await _core.ExecuteScriptAsync(script);
                    var links = ParseSearchDelimited(raw);

                    foreach (var l in links)
                    {
                        if (ProbeData.IsAppStoreUrl(l.Href) || ProbeData.IsWrongSiteUrl(l.Href)) continue;
                        if (Regex.IsMatch(l.Title + " " + l.Href, "下载|download|\\.exe", RegexOptions.IgnoreCase)
                            && Uri.IsWellFormedUriString(l.Href, UriKind.Absolute))
                        { res.Url = l.Href; return res; }
                    }
                    foreach (var l in links)
                    {
                        if (ProbeData.IsAppStoreUrl(l.Href) || ProbeData.IsWrongSiteUrl(l.Href)) continue;
                        if (Uri.IsWellFormedUriString(l.Href, UriKind.Absolute)) { res.Url = l.Href; return res; }
                    }
                    var storeHit = links.Find(l => ProbeData.IsAppStoreUrl(l.Href) && Uri.IsWellFormedUriString(l.Href, UriKind.Absolute));
                    if (!string.IsNullOrEmpty(storeHit.Href)) { res.StoreOnly = true; res.StoreUrl = storeHit.Href; }
                    else res.NotFound = true;
                }
                catch (Exception ex) { res.NotFound = true; Debug.WriteLine("[ProbeBrowserHost] Search 异常: " + ex.Message); }
                return res;
            });
        }

        private async Task<bool> NavigateAndWaitAsync(string url, int timeoutMs = 20000)
        {
            var tcs = new TaskCompletionSource<bool>();
            EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = null;
            handler = (s, e) =>
            {
                if (_core != null) _core.NavigationCompleted -= handler;
                tcs.TrySetResult(e != null && e.IsSuccess);
            };
            _core.NavigationCompleted += handler;
            _core.Navigate(url);
            // 防止导航永远不完成（网络挂起/WebView2 卡死）导致调用方永久 await
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            if (completed != tcs.Task)
            {
                if (_core != null) _core.NavigationCompleted -= handler;
                return false;
            }
            return await tcs.Task;
        }

        // ExecuteScriptAsync 等操作在渲染进程崩溃/挂起时可能永不完成；
        // 若不加超时，调用方的 await 会永久卡死，连 Dispose() 都无法唤醒。这里统一设上限。
        private const int BrowserThreadTimeoutMs = 30000;

        private Task<T> RunOnBrowserThread<T>(Func<Task<T>> func)
        {
            var tcs = new TaskCompletionSource<T>();
            if (_form == null) { tcs.TrySetException(new InvalidOperationException("WebView2 宿主未初始化")); return tcs.Task; }
            _form.BeginInvoke(new Action(async () =>
            {
                try { tcs.TrySetResult(await func()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }));
            return WithTimeout(tcs.Task, BrowserThreadTimeoutMs);
        }

        /// <summary>给可能永不完成的任务加超时上限，避免调用方永久 await（渲染进程挂起时连 Dispose 都唤不醒）。</summary>
        private static async Task<T> WithTimeout<T>(Task<T> task, int timeoutMs)
        {
            using (var cts = new System.Threading.CancellationTokenSource())
            {
                var delay = Task.Delay(timeoutMs, cts.Token);
                var finished = await Task.WhenAny(task, delay);
                if (finished != task)
                    throw new TimeoutException("WebView2 浏览器线程操作超时（" + timeoutMs + "ms），可能是渲染进程无响应。");
                cts.Cancel(); // 取消定时器，避免 30s 计时器滞留
                return await task;
            }
        }

        // ===================== 注入 JS =====================
        private static string BuildProbeScript(bool skip)
        {
            // skip=false 时启用「点击触发动态下载链接」策略；否则仅做静态 DOM 扫描。
            // 必须保持同步 IIFE：WebView2 的 ExecuteScriptAsync 对返回 Promise（async）的结果序列化不稳定，
            // 可能返回空/{} 导致整段被丢弃；且 clickMatching 已改为同步（去掉 await），避免 async 语法要求。
            // 整段包 try/catch 且 return 在 try 外，任何未预期异常都返回已收集候选，绝不抛 null。
            string skipBlock = skip ? "" : @"
    const triggerRe = /下载|桌面端|立即下载|download|download now|windows|windows版|win版/i;
    const winRe = /windows|windows版|win版|\bwin\b|macos|\bmac\b/i;
    const skipRe = /移动端|mobile|android|ios|\bapk\b|\bipa\b/i;
    const collectMatches = () => {
      const sel = 'a,button,[role=button],[role=link],div,span,label,li';
      const els = [...document.querySelectorAll(sel)];
      const m = [];
      for (const b of els) {
        const tk = (b.innerText||''); const aria = b.getAttribute('aria-label')||''; const title = b.getAttribute('title')||''; const hk = b.getAttribute('href')||'';
        const sig = (tk+' '+aria+' '+title+' '+hk).trim();
        if (triggerRe.test(sig) && !skipRe.test(sig)) m.push(b);
      }
      return m;
    };
    const clickMatching = () => {
      const triggers = collectMatches();
      for (const m of triggers) { try { m.dispatchEvent(new MouseEvent('mouseover',{bubbles:true})); } catch(e){} }
      let matches = collectMatches();
      matches.sort((x,y)=>(winRe.test((x.innerText||'')+(x.getAttribute('aria-label')||''))?-1:0)-(winRe.test((y.innerText||'')+(y.getAttribute('aria-label')||''))?-1:0));
      let n=0;
      for (const m of matches) { if (n>=12) break; n++; try { m.click(); } catch(e){} }
    };
    clickMatching();
    document.querySelectorAll('a[href]').forEach(a=>{ if (/\.exe/i.test(a.href)) add(a.href,'anchor'); });
    exes(document.documentElement.outerHTML).forEach(u=>add(u,'anchor'));
    clickMatching();
    document.querySelectorAll('a[href]').forEach(a=>{ if (/\.exe/i.test(a.href)) add(a.href,'anchor'); });
    exes(document.documentElement.outerHTML).forEach(u=>add(u,'anchor'));";

            return @"(function(){
  const found = new Map();
  const add = (url, strategy) => {
    if (!url) return;
    if (/\.(apk|ipa|dmg|app|pkg|deb|rpm)(\?|$)/i.test(url)) return;
    try {
      const u = new URL(url);
      // 过滤占位符/模板链接（如 https://null/xxx.exe）
      if (!u.hostname || u.hostname === 'null' || u.hostname === 'undefined' || u.hostname === 'localhost') return;
    } catch(e) { return; }
    const norm = url.split('#')[0];
    if (!found.has(norm)) found.set(norm, {url:norm, strategy:strategy});
    else if (!found.get(norm).strategy.includes(strategy)) found.get(norm).strategy += '+' + strategy;
  };
  const exes = (hay) => (hay.match(/https?:\/\/[^\s""'<>()\\]+?\.exe[^\s""'<>()\\]*/gi) || []);
  const set = new Set();
  const push = (u)=>{ if(u && /^https?:\/\//i.test(u)) set.add(u.split('#')[0]); };
  try {
    // 可靠的静态扫描先跑，确保即使后续动态段异常也不丢失已收集的候选。
    document.querySelectorAll('a[href]').forEach(a => { if (/\.exe/i.test(a.href)) add(a.href,'anchor'); });
    exes(document.documentElement.outerHTML).forEach(u => add(u,'anchor'));
" + skipBlock + @"
    // 资源链接扫描：每个元素单独 try，避免单个坏节点拖垮整段。
    document.querySelectorAll('[href],[src]').forEach(el=>{ try{ if(el.href) push(el.href); }catch(e){} try{ if(el.src) push(el.src); }catch(e){} });
    document.querySelectorAll('*').forEach(el=>{ try{ for (const a of el.attributes) { if (/^data-(href|url|src|download|file|link)$/i.test(a.name)) push(a.value); } }catch(e){} });
    // 扫描 script 文本：单独 try；注意此前此行多了一个右括号导致整段脚本语法错误、
    // WebView2 返回 null（浏览器探针整体失效），现已修正为 const m + if(m) 安全遍历。
    document.querySelectorAll('script').forEach(s=>{ try { const m = (s.textContent||'').match(/https?:\/\/[^\s""'<>()\\]+/gi); if (m) m.forEach(push); } catch(e){} });
    [...set].forEach(u=>{ if (/\.(exe|msi|zip)(\?|$)/i.test(u)) add(u,'anchor'); if (/\/(download|setup|client|install(er)?)(\b|\?|\.)/i.test(u)) add(u,'network'); });
  } catch(e) {
    // 整体兜底：任何未预期异常都返回已收集结果，绝不抛 null。WebView2 对异常返回 null 字符串，会令整段脚本被丢弃。
  }
  return [...found.values()].map(o => o.url + '|::|' + o.strategy).join('|;;|');
})();";
        }

        private static string BuildSearchScript()
        {
            return @"[...document.querySelectorAll('#b_results .b_algo')].map(blk => {
  if (blk.closest && blk.closest('.b_ad')) return null;
  const a = blk.querySelector('h2 a');
  if (!a || !a.href) return null;
  if (/\.(js|css|png|jpg|jpeg|gif|webp|svg|ico)(\?|$)/i.test(a.href)) return null;
  return a.href + '|::|' + (a.textContent||'').toLowerCase();
}).filter(Boolean).join('|;;|');";
        }

        // ===================== 解析注入 JS 的定界输出 =====================
        private static List<CandidateUrl> ParseDelimited(string raw)
        {
            var list = new List<CandidateUrl>();
            raw = Unquote(raw);
            if (string.IsNullOrEmpty(raw)) return list;
            foreach (var rec in Regex.Split(raw, Regex.Escape("|;;|")))
            {
                if (string.IsNullOrEmpty(rec)) continue;
                var parts = Regex.Split(rec, Regex.Escape("|::|"));
                var url = parts[0];
                if (string.IsNullOrWhiteSpace(url)) continue;
                // 二次过滤无效/占位符 URL
                try
                {
                    var uri = new Uri(url);
                    var host = uri.Host.ToLowerInvariant();
                    if (host == "null" || host == "undefined" || host == "localhost" || string.IsNullOrEmpty(host)) continue;
                }
                catch { continue; }
                list.Add(new CandidateUrl { Url = url, Strategy = parts.Length > 1 ? parts[1] : "anchor" });
            }
            return list;
        }

        private static List<(string Href, string Title)> ParseSearchDelimited(string raw)
        {
            var list = new List<(string, string)>();
            raw = Unquote(raw);
            if (string.IsNullOrEmpty(raw)) return list;
            foreach (var rec in Regex.Split(raw, Regex.Escape("|;;|")))
            {
                if (string.IsNullOrEmpty(rec)) continue;
                var parts = Regex.Split(rec, Regex.Escape("|::|"));
                list.Add((parts[0], parts.Length > 1 ? parts[1] : ""));
            }
            return list;
        }

        private static string Unquote(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char e = s[++i];
                    if (e == '"') sb.Append('"');
                    else if (e == '\\') sb.Append('\\');
                    else if (e == '/') sb.Append('/');
                    else if (e == 'n') sb.Append('\n');
                    else if (e == 't') sb.Append('\t');
                    else sb.Append(e);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 在 Temp 下创建带 GUID 后缀的 WebView2 用户数据目录。
        /// 每次都用全新目录，避免上次异常退出遗留的 .lock 锁文件导致本次 CreateAsync / EnsureCoreWebView2Async 挂起。
        /// </summary>
        private static string CreateTempUserDataDir(string prefix)
        {
            var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// 主动定位本机 WebView2 / Edge 的运行时目录（须含 msedgewebview2.exe）。
        /// 当系统注册表（EdgeWebView\Applications）损坏或 WebView2Loader 未注册到 System32 时，
        /// CreateAsync(null) 经由注册表解析会失败/挂起；此时改为显式传入浏览器目录，
        /// 直接绕过注册表，复用磁盘上实际存在的运行时二进制（Fixed Version 式用法）。
        /// 找不到则返回 null（调用方退回标准注册表解析）。
        /// </summary>
        private static string ResolveWebView2BrowserFolder()
        {
            var candidates = new List<string>();
            // 1) WebView2 Runtime 目录（优先，最轻量）
            var wvBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "EdgeWebView", "Application");
            if (Directory.Exists(wvBase)) candidates.Add(wvBase);
            // 2) 完整 Edge 浏览器目录（兜底，WebView2 可复用其二进制）
            var edgeBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application");
            if (Directory.Exists(edgeBase)) candidates.Add(edgeBase);
            var edgeBase64 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application");
            if (Directory.Exists(edgeBase64)) candidates.Add(edgeBase64);

            // 优先：基目录下直接含 msedgewebview2.exe（健康机器常见布局）
            foreach (var baseDir in candidates)
            {
                if (File.Exists(Path.Combine(baseDir, "msedgewebview2.exe"))
                    && IsPeBinary(Path.Combine(baseDir, "msedgewebview2.exe")))
                    return baseDir;
            }
            // 兜底：在基目录下找形如 151.0.4129.72 的版本子目录，
            // 按版本号降序遍历（优先用最新稳定版），跳过 msedgewebview2.exe 为文本占位符/损坏的版本。
            // 注：本机曾出现 151 版本该文件退化为路径字符串（"C:\Program Files (x86)\..."）而非 PE 二进制，
            //     CreateAsync 加载后报 BadImageFormatException(0x8007000B)，故必须做 MZ 头校验。
            foreach (var baseDir in candidates)
            {
                if (!Directory.Exists(baseDir)) continue;
                var subDirs = Directory.GetDirectories(baseDir)
                    .Where(d => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(d), @"^\d+\.\d+\.\d+\.\d+$"))
                    .OrderByDescending(d => d) // 按版本号字符串降序，最新在前
                    .ToArray();
                foreach (var sub in subDirs)
                {
                    var exe = Path.Combine(sub, "msedgewebview2.exe");
                    if (File.Exists(exe) && IsPeBinary(exe))
                        return sub;
                }
            }
            return null;
        }

        /// <summary>
        /// 验证文件是否为有效的 PE 可执行文件（读取前 2 字节检查 MZ 签名 0x4D5A）。
        /// 防止把损坏/退化为文本占位符的 msedgewebview2.exe 误当运行时。
        /// </summary>
        private static bool IsPeBinary(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                if (fs.Length < 2) return false;
                var header = new byte[2];
                fs.Read(header, 0, 2);
                return header[0] == 0x4D && header[1] == 0x5A; // "MZ"
            }
            catch { return false; }
        }

        /// <summary>
        /// 构造 WebView2 环境选项： suppress 首运行弹窗/默认浏览器提示/默认应用/后台网络等可能阻塞 EnsureCoreWebView2Async 的开关。
        /// </summary>
        private static CoreWebView2EnvironmentOptions CreateEnvironmentOptions()
        {
            // suppress 首运行弹窗/默认浏览器提示/默认应用/后台网络等可能阻塞 EnsureCoreWebView2Async 的开关。
            // 不关闭 SmartScreen（避免不必要地削弱安全策略）。
            var args = "--no-first-run --no-default-browser-check --disable-default-apps --disable-background-networking";
            // 诊断开关：仅在设置环境变量 CPQ_WV2_DIAG=1 时开启浏览器详细日志（落到用户数据目录 WebView2.log），
            // 便于在 EnsureCoreWebView2Async 仍卡住时拿到浏览器侧的确切堆栈，日常运行保持干净。
            if (Environment.GetEnvironmentVariable("CPQ_WV2_DIAG") == "1")
                args += " --enable-logging --v=1";
            return new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = args,
            };
        }

        public void Dispose()
        {
            // 关键：关闭离屏 Form 后 Application.Run() 才会返回，STA 线程才能结束，
            // 否则离屏窗口句柄与后台线程在每次抓取后泄漏（成功与失败路径均如此，超时路径也会关闭 Form）。
            try
            {
                _form?.BeginInvoke(new Action(() =>
                {
                    try { _form?.Close(); } catch { }
                }));
            }
            catch { }
            try { if (_thread != null && _thread.IsAlive) _thread.Join(2000); } catch { }
            // 清理本次使用的临时用户数据目录（避免残留缓存/锁文件累积导致后续初始化挂起）
            try { if (!string.IsNullOrEmpty(_userDataDir) && Directory.Exists(_userDataDir)) Directory.Delete(_userDataDir, true); } catch { }
        }
    }
}

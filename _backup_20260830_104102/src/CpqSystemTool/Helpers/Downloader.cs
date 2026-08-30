using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace CpqSystemTool
{
    /// <summary>
    /// 统一下载器：合并 4 套重复下载实现（About 代理回退 / Appx 断点续传 / WebView2 进度回调 / Office WebClient）。
    /// 基于共享单例 HttpClients.Default（系统代理路径），提供重试 + 请求级超时（CTS）+ 进度回调 + 可选代理回退 + 可选断点续传。
    /// 成功返回 true；失败返回 false，具体原因经 log 输出（形如 [下载] ...）。
    /// </summary>
    internal static class Downloader
    {
        /// <summary>默认 User-Agent（与 About 页原 WebClient 一致）。</summary>
        public const string DefaultUserAgent = "CpqSystemTool";

        /// <summary>
        /// 代理回退候选：系统代理 → 直连 → Watt Toolkit 本地 HTTP 代理（与 MainWindow.About 原 GetProxyCandidates 顺序一致）。
        /// </summary>
        private static readonly IWebProxy[] ProxyCandidates =
        {
            WebRequest.DefaultWebProxy,                      // 1) 系统代理（Watt Toolkit System 模式等）
            null,                                            // 2) 直连（无代理）
            new WebProxy("http://127.0.0.1:26561", false)    // 3) Watt Toolkit 本地端口（PAC/System 模式）
        };

        /// <summary>
        /// 统一下载入口。语义：
        /// <list type="bullet">
        /// <item>最多重试 maxAttempts 次（默认 3），每次重试前按 retryDelayMs 等待（默认 5 秒）；</item>
        /// <item>useProxyFallback=true 时，每次尝试按「系统代理 → 直连 → Watt Toolkit」顺序逐个代理发起请求，首个成功即完成；</item>
        /// <item>timeoutMs 为「连接 + 响应头」阶段超时；响应头收到后若 readTimeoutMs&gt;0，改由单次读空闲超时接管（0=不限制，避免大文件被总时长误杀）；</item>
        /// <item>resume=true 时启用断点续传（Range）：服务器支持则追加、不支持则覆盖；失败后下次从已下载字节续传；</item>
        /// <item>progress 回调在百分比变化时触发，并在确知 Content-Length 时补发 100% 收尾信号。</item>
        /// </list>
        /// </summary>
        public static async Task<bool> DownloadAsync(
            string url,
            string destPath,
            Action<string> log = null,
            Action<int> progress = null,
            int maxAttempts = 3,
            int timeoutMs = 120000,
            int readTimeoutMs = 0,
            bool resume = false,
            bool useProxyFallback = false,
            int retryDelayMs = 5000,
            string userAgent = DefaultUserAgent)
        {
            // 修复：内部 ConfigureAwait(false) 之后所有 await 续跑在线程池线程，progress / log 回调也随之脱离 UI 线程，
            // 调用方在回调里直接改 UI 控件会抛跨线程异常。故在入口捕获调用方同步上下文，回调统一封送回原线程。
            var ui = SynchronizationContext.Current;
            Action<string> logCb = log == null ? (Action<string>)null : (s => Post(ui, () => log(s)));
            Action<int> progressCb = progress == null ? (Action<int>)null : (v => Post(ui, () => progress(v)));

            if (string.IsNullOrEmpty(url)) { logCb?.Invoke("[下载] URL 为空，无法下载"); return false; }
            if (string.IsNullOrEmpty(destPath)) { logCb?.Invoke("[下载] 目标路径为空，无法下载"); return false; }

            IWebProxy[] candidates = useProxyFallback ? ProxyCandidates : new[] { WebRequest.DefaultWebProxy };

            string lastError = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                foreach (var proxy in candidates)
                {
                    string error = await TryDownloadOnce(url, destPath, proxy, logCb, progressCb, timeoutMs, readTimeoutMs, resume, userAgent).ConfigureAwait(false);
                    if (error == null) return true;
                    lastError = error;
                    logCb?.Invoke($"[下载] 第 {attempt}/{maxAttempts} 次尝试失败: {error}");
                }

                if (attempt < maxAttempts)
                {
                    logCb?.Invoke($"[下载] {retryDelayMs / 1000} 秒后{(resume ? "从断点续传" : "")}重试（第 {attempt + 1}/{maxAttempts} 次）...");
                    await Task.Delay(retryDelayMs).ConfigureAwait(false);
                }
            }

            logCb?.Invoke($"[下载] 全部 {maxAttempts} 次尝试均失败: {lastError ?? "未知错误"}");
            return false;
        }

        /// <summary>把回调封送回捕获到的同步上下文（UI 线程）执行；无上下文（控制台/后台线程调用）时直接同步执行。</summary>
        private static void Post(SynchronizationContext ctx, Action a)
        {
            if (ctx == null) { a(); return; }
            try { ctx.Post(_ => a(), null); }
            catch { a(); }   // 上下文已失效（如 UI 线程退出）时退化为直接调用，避免吞掉回调
        }

        /// <summary>单次单代理下载尝试。成功返回 null；失败返回错误描述（供外层统一记录）。</summary>
        private static async Task<string> TryDownloadOnce(
            string url, string destPath, IWebProxy proxy,
            Action<string> log, Action<int> progress,
            int timeoutMs, int readTimeoutMs, bool resume, string userAgent)
        {
            // 修复：HttpClient 现为共享/缓存实例（见 CreateClient），全程不得 Dispose
            HttpClient client = CreateClient(proxy);
            try
            {
                long existing = resume && File.Exists(destPath) ? new FileInfo(destPath).Length : 0;

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd(userAgent);
                if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null); // 断点续传

                using (var cts = new CancellationTokenSource(timeoutMs))
                {
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        // 响应头已收到：连接/响应阶段结束，解除总时长上限，改由 readTimeoutMs 按读空闲超时接管（0=不限）
                        cts.CancelAfter(Timeout.InfiniteTimeSpan);

                        bool append = resume && existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                        long total = response.Content.Headers.ContentLength ?? -1;

                        using (var src = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var dst = new FileStream(destPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write))
                        {
                            var buffer = new byte[65536];
                            // 续传时把已有字节计入，保证进度百分比接近真实；服务器忽略 Range 时从 0 计
                            long existingBytes = append ? existing : 0;
                            long downloaded = existingBytes;
                            long expected = total > 0 ? existingBytes + total : -1; // 预期总量（total 未知时不校验）
                            int lastPercent = -1;
                            int n;
                            while ((n = await ReadChunkAsync(src, buffer, readTimeoutMs, cts.Token).ConfigureAwait(false)) > 0)
                            {
                                await dst.WriteAsync(buffer, 0, n).ConfigureAwait(false);
                                downloaded += n;
                                if (total > 0)
                                {
                                    int pct = (int)(downloaded * 100 / total);
                                    if (pct != lastPercent) { lastPercent = pct; progress?.Invoke(pct); }
                                }
                            }
                            // 修复：循环结束只代表流读到 EOF，服务端中途断开时文件被静默截断、仍然返回成功。
                            // 已知总长度时必须校验实际字节数，不符则判定不完整并删除残留文件（避免截断的安装包被当成品使用）。
                            if (expected > 0 && downloaded != expected)
                            {
                                string incomplete = "下载不完整：预期 " + expected + " 字节，实际 " + downloaded + " 字节（连接被中断，已删除不完整文件）";
                                try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
                                return incomplete;
                            }
                            if (total > 0) progress?.Invoke(100); // 仅在确知总长度且校验通过后补发收尾信号
                        }
                    }
                }

                if (resume)
                {
                    long finalSize = new FileInfo(destPath).Length;
                    log?.Invoke("[下载] 完成 " + finalSize + " 字节" + (existing > 0 ? "（续传 " + existing + " + 新 " + (finalSize - existing) + "）" : ""));
                }
                return null;
            }
            catch (OperationCanceledException)
            {
                return "请求超时（连接或读取超过时限）";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // HttpClient 缓存锁（懒加载，见 CreateClient）
        private static readonly object ClientLock = new object();
        // 修复：此前每次尝试都 new HttpClient(new HttpClientHandler())，等于每次新建连接池、短连接关闭后
        // 大量 socket 停在 TIME_WAIT，正是本类注释声称要避免的问题。改为按代理实例缓存复用。
        private static HttpClient _directClient;                                    // 直连（proxy == null）
        private static readonly System.Collections.Generic.Dictionary<IWebProxy, HttpClient> ProxyClients =
            new System.Collections.Generic.Dictionary<IWebProxy, HttpClient>(ProxyRefComparer.Instance);

        /// <summary>按代理选择 HttpClient 并缓存复用：系统代理 → 共享单例 HttpClients.Default；
        /// 直连 / 自定义代理（如 Watt Toolkit）→ 按代理实例懒加载缓存（代理切换逻辑保持原样）。
        /// 返回实例均为共享或缓存实例，调用方不得 Dispose。</summary>
        private static HttpClient CreateClient(IWebProxy proxy)
        {
            if (proxy == null)   // 直连
            {
                lock (ClientLock)
                    return _directClient ?? (_directClient = new HttpClient(new HttpClientHandler { UseProxy = false }));
            }
            if (ReferenceEquals(proxy, WebRequest.DefaultWebProxy)) return HttpClients.Default;   // 系统代理 → 复用共享单例

            lock (ClientLock)    // 自定义代理
            {
                HttpClient c;
                if (!ProxyClients.TryGetValue(proxy, out c))
                {
                    c = new HttpClient(new HttpClientHandler { UseProxy = true, Proxy = proxy });
                    ProxyClients[proxy] = c;
                }
                return c;
            }
        }

        /// <summary>按引用比较代理实例：IWebProxy 实现可能重写 Equals/GetHashCode，缓存必须按实例区分。</summary>
        private sealed class ProxyRefComparer : System.Collections.Generic.IEqualityComparer<IWebProxy>
        {
            public static readonly ProxyRefComparer Instance = new ProxyRefComparer();
            public bool Equals(IWebProxy x, IWebProxy y) { return ReferenceEquals(x, y); }
            public int GetHashCode(IWebProxy obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }

        /// <summary>单次读：readTimeoutMs&gt;0 时用「链接 CTS + 每读 CancelAfter」实现空闲超时（等价原 HttpWebRequest.ReadWriteTimeout）。</summary>
        private static async Task<int> ReadChunkAsync(Stream src, byte[] buffer, int readTimeoutMs, CancellationToken ct)
        {
            if (readTimeoutMs <= 0)
                return await src.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                readCts.CancelAfter(readTimeoutMs);
                return await src.ReadAsync(buffer, 0, buffer.Length, readCts.Token).ConfigureAwait(false);
            }
        }
    }
}

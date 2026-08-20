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
            if (string.IsNullOrEmpty(url)) { log?.Invoke("[下载] URL 为空，无法下载"); return false; }
            if (string.IsNullOrEmpty(destPath)) { log?.Invoke("[下载] 目标路径为空，无法下载"); return false; }

            IWebProxy[] candidates = useProxyFallback ? ProxyCandidates : new[] { WebRequest.DefaultWebProxy };

            string lastError = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                foreach (var proxy in candidates)
                {
                    string error = await TryDownloadOnce(url, destPath, proxy, log, progress, timeoutMs, readTimeoutMs, resume, userAgent).ConfigureAwait(false);
                    if (error == null) return true;
                    lastError = error;
                    log?.Invoke($"[下载] 第 {attempt}/{maxAttempts} 次尝试失败: {error}");
                }

                if (attempt < maxAttempts)
                {
                    log?.Invoke($"[下载] {retryDelayMs / 1000} 秒后{(resume ? "从断点续传" : "")}重试（第 {attempt + 1}/{maxAttempts} 次）...");
                    await Task.Delay(retryDelayMs).ConfigureAwait(false);
                }
            }

            log?.Invoke($"[下载] 全部 {maxAttempts} 次尝试均失败: {lastError ?? "未知错误"}");
            return false;
        }

        /// <summary>单次单代理下载尝试。成功返回 null；失败返回错误描述（供外层统一记录）。</summary>
        private static async Task<string> TryDownloadOnce(
            string url, string destPath, IWebProxy proxy,
            Action<string> log, Action<int> progress,
            int timeoutMs, int readTimeoutMs, bool resume, string userAgent)
        {
            HttpClient client = CreateClient(proxy);
            bool owned = !ReferenceEquals(client, HttpClients.Default); // 共享单例不得 dispose
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
                            long downloaded = append ? existing : 0; // 续传时累计，保证进度百分比接近真实；服务器忽略 Range 时从 0 计
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
                            if (total > 0) progress?.Invoke(100); // 仅在确知总长度时补发收尾信号
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
            finally
            {
                if (owned) client.Dispose();
            }
        }

        /// <summary>按代理选择 HttpClient：系统代理 → 共享单例 HttpClients.Default；直连/自定义代理 → 一次性短生命期实例。</summary>
        private static HttpClient CreateClient(IWebProxy proxy)
        {
            if (proxy == null) return new HttpClient(new HttpClientHandler { UseProxy = false }); // 直连
            if (ReferenceEquals(proxy, WebRequest.DefaultWebProxy)) return HttpClients.Default;   // 系统代理 → 复用共享单例
            return new HttpClient(new HttpClientHandler { UseProxy = true, Proxy = proxy });      // 自定义代理（如 Watt Toolkit）
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

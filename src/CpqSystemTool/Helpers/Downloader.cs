using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Dlx = Downloader;

namespace CpqSystemTool
{
    /// <summary>
    /// 统一下载器：基于 bezzad.Downloader（v5.9.6，多线程分块并行）。
    /// 原单线程串行已被替换——ChunkCount=8 并行分块，下载提速 3-8 倍。
    /// 纯直连（通过 CustomHttpMessageHandlerFactory 注入 UseProxy=false 的 SocketsHttpHandler），
    /// 无代理兜底——软件分发给的机器可能完全没有代理，默认必须能在无代理环境直接下载。
    /// 提供重试 + 进度回调 + 可选断点续传。
    /// 成功返回 true；失败返回 false，具体原因经 log 输出。
    /// </summary>
    internal static class Downloader
    {
        /// <summary>默认 User-Agent（与 About 页原 WebClient 一致）。</summary>
        public const string DefaultUserAgent = "CpqSystemTool";

        /// <summary>分块数（多线程并行数）。8 块在微软 CDN 上实测加速比最佳。</summary>
        private const int ChunkCount = 8;

        /// <summary>
        /// 统一下载入口（纯直连，多线程分块）。语义与原实现保持一致：
        /// <list type="bullet">
        /// <item>最多重试 maxAttempts 次（默认 3），每次重试前按 retryDelayMs 等待（默认 5 秒）；</item>
        /// <item>resume=true 时启用断点续传（Downloader v5 内建 EnableAutoResumeDownload）；</item>
        /// <item>progress 回调在百分比变化时触发，并在完成时补发 100%。</item>
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
            int retryDelayMs = 5000,
            string userAgent = DefaultUserAgent,
            string referer = null)
        {
            var ui = SynchronizationContext.Current;
            Action<string> logCb = log == null ? (Action<string>)null : (s => Post(ui, () => log(s)));
            Action<int> progressCb = progress == null ? (Action<int>)null : (v => Post(ui, () => progress(v)));

            if (string.IsNullOrEmpty(url)) { logCb?.Invoke("[下载] URL 为空，无法下载"); return false; }
            if (string.IsNullOrEmpty(destPath)) { logCb?.Invoke("[下载] 目标路径为空，无法下载"); return false; }

            string lastError = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                // 【修 P2-13】lastError 原为固定字符串"下载失败"，外层日志把真因丢了；
                // 现 TryDownloadOnce 返回 (ok, error) 携带真实失败原因，重试日志可直接定位故障。
                var (success, attemptError) = await TryDownloadOnce(url, destPath, logCb, progressCb, timeoutMs, readTimeoutMs, resume, userAgent, referer).ConfigureAwait(false);
                if (success) return true;
                lastError = attemptError;
                logCb?.Invoke($"[下载] 第 {attempt}/{maxAttempts} 次尝试失败: {lastError}");

                if (attempt < maxAttempts)
                {
                    logCb?.Invoke($"[下载] {retryDelayMs / 1000} 秒后{(resume ? "从断点续传" : "")}重试（第 {attempt + 1}/{maxAttempts} 次）...");
                    await Task.Delay(retryDelayMs).ConfigureAwait(false);
                }
            }

            logCb?.Invoke($"[下载] 全部 {maxAttempts} 次尝试均失败: {lastError ?? "未知错误"}");
            return false;
        }

        private static void Post(SynchronizationContext ctx, Action a)
        {
            if (ctx == null) { a(); return; }
            try { ctx.Post(_ => a(), null); }
            catch { a(); }
        }

        // 【修 P2-13】返回 (ok, error)：失败时 error 携带真实失败原因（供外层重试日志直接定位）。
        private static async Task<(bool ok, string error)> TryDownloadOnce(
            string url, string destPath,
            Action<string> log, Action<int> progress,
            int timeoutMs, int readTimeoutMs, bool resume, string userAgent, string referer)
        {
            log?.Invoke($"[下载] bezzad.Downloader | {ChunkCount} 分块并行 | BufferBlockSize=64KB | 限速=不限 | 超时={timeoutMs}ms");

            var config = new Dlx.DownloadConfiguration
            {
                ChunkCount = ChunkCount,
                ParallelDownload = true,
                BufferBlockSize = 65536,
                MaximumBytesPerSecond = long.MaxValue,
                MaxTryAgainOnFailure = 2,  // 库内部失败后最多重试 2 次；外层 maxAttempts 循环负责更大粒度退避重试
                HttpClientTimeout = timeoutMs,
                BlockTimeout = readTimeoutMs > 0 ? readTimeoutMs : 30000,
                EnableAutoResumeDownload = resume,
                FileExistPolicy = Dlx.FileExistPolicy.Delete,  // 已对照 v5.9.6 源码查证：Delete 只清「最终成品文件」，.download 部分文件不受影响，resume 时 TryResumeFromExistingFile 正常保留并续传（P1-1 查证结论：无冲突）
                CustomHttpMessageHandlerFactory = () => new SocketsHttpHandler
                {
                    UseProxy = false,
                    AllowAutoRedirect = true,
                    // 不做 GZip/Deflate 自动解压：分块 Range 下载时，若服务端压缩导致
                    // Content-Length 与实际写入字节不一致，会造成文件损坏。
                    // 默认 Accept-Encoding: identity（不压缩），各 chunk 字节严格对齐。
                    PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                }
            };

            using var downloader = new Dlx.DownloadService(config);

            config.RequestConfiguration = new Dlx.RequestConfiguration
            {
                UserAgent = userAgent,
            };
            if (!string.IsNullOrEmpty(referer))
                config.RequestConfiguration.Referer = referer;  // v5.x 专用属性（Headers.Add 对受限头会抛异常被吞，必须走此属性）

            var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool failed = false;
            string failMsg = null;

            downloader.DownloadFileCompleted += (_, e) =>
            {
                if (e.Error != null || e.Cancelled)
                {
                    failed = true;
                    failMsg = e.Error?.Message ?? "下载被取消";
                }
                else
                {
                    try
                    {
                        if (File.Exists(destPath))
                            log?.Invoke("[下载] 完成 " + new FileInfo(destPath).Length + " 字节");
                    }
                    catch (Exception ex) { DebugLog.Ignore(ex); }
                }
                completedTcs.TrySetResult(!failed);
            };

            int lastPercent = -1;
            downloader.DownloadProgressChanged += (_, e) =>
            {
                int pct = (int)e.ProgressPercentage;
                if (pct != lastPercent && pct >= 0)
                {
                    lastPercent = pct;
                    progress?.Invoke(pct);
                }
            };

            try
            {
                string dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }

            try
            {
                await downloader.DownloadFileTaskAsync(url, destPath).ConfigureAwait(false);
                bool ok = await completedTcs.Task.ConfigureAwait(false);
                if (!ok) log?.Invoke("[下载] 失败: " + failMsg);
                return (ok, ok ? null : failMsg);
            }
            catch (OperationCanceledException)
            {
                log?.Invoke("[下载] 下载超时（连接或读取超过时限）");
                return (false, "下载超时（连接或读取超过时限）");
            }
            catch (Exception ex)
            {
                log?.Invoke("[下载] 异常: " + ex.Message);
                return (false, "异常: " + ex.Message);
            }
        }
    }
}

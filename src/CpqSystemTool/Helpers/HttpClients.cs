using System;
using System.Net.Http;

namespace CpqSystemTool
{
    /// <summary>共享 HttpClient 单例：避免频繁 new/dispose 导致 socket TIME_WAIT 堆积。HttpClient 线程安全可长期复用。</summary>
    internal static class HttpClients
    {
        // 默认无代理（UseProxy=false）：软件分发给他人时，目标机可能根本没有代理；
        // 默认套系统代理（new HttpClient() 的行为）会因死代理把网络请求全拖挂。
        // 需要系统代理的「显式代理兜底」场景由 Downloader.CreateClient 单独走缓存 client，不复用本单例。
        // SocketsHttpHandler + 10 分钟连接寿命轮转（与 Downloader.cs 同款）：框架版 HttpClientHandler 的 socket 默认不轮转，
        // 长寿命进程里 TCP 连接不释放易堆积；轮转后后台连接最多存活 10 分钟即断开重建。
        public static readonly HttpClient Default =
            new HttpClient(new SocketsHttpHandler
            {
                UseProxy = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            });
    }
}

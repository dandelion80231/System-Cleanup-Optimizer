using System;
using System.Net.Http;

namespace CpqSystemTool
{
    /// <summary>共享 HttpClient 单例：避免频繁 new/dispose 导致 socket TIME_WAIT 堆积。HttpClient 线程安全可长期复用。</summary>
    internal static class HttpClients
    {
        public static readonly HttpClient Default = new HttpClient();
    }
}

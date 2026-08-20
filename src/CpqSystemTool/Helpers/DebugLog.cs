using System;
namespace CpqSystemTool
{
    /// <summary>统一的「已忽略异常」诊断输出，替代全项目 109 处逐字内联 Debug.WriteLine。</summary>
    internal static class DebugLog
    {
        public static void Ignore(Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + (ex?.Message ?? "null"));
        }
    }
}

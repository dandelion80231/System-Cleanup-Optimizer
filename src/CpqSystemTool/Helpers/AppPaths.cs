using System;
using System.IO;

namespace CpqSystemTool
{
    /// <summary>
    /// 应用常用路径集中（R5 收编）。
    /// Config 目录此前硬编码于 MainWindow.Theme.cs、MainWindow.Tweaks.cs、Modules/ConfigBackup.cs，
    /// 现统一由此处提供，行为零变化。
    /// </summary>
    public static class AppPaths
    {
        /// <summary>配置目录（默认位于 exe 同目录下的 Config）。</summary>
        public static string ConfigDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
    }
}

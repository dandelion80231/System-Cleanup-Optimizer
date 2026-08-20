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
    }
}

using System;
using System.Diagnostics;
using System.IO;

namespace CpqSystemTool
{
    /// <summary>
    /// 上帝模式（God Mode）：在桌面创建 GodMode 文件夹并打开，作为系统设置总入口。
    /// </summary>
    internal static class GodMode
    {
        private const string GODMODE_NAME = "GodMode.{ED7BA470-8E54-465E-825C-99712043E01C}";

        public static void Create(Action<string> log)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string dest = Path.Combine(desktop, GODMODE_NAME);
            log("=== 上帝模式（God Mode）===");
            log("目标位置：" + desktop);
            if (Directory.Exists(dest))
            {
                log("   [提示] 上帝模式文件夹已存在，直接打开。");
            }
            else
            {
                try
                {
                    Directory.CreateDirectory(dest);
                    log("   [OK] 已创建：" + dest);
                }
                catch (Exception e)
                {
                    log("   [失败] 创建失败：" + e.Message);
                    return;
                }
            }
            try
            {
                Process.Start(new ProcessStartInfo(dest) { UseShellExecute = true });
                log("   [OK] 已打开上帝模式（系统设置总入口）。");
            }
            catch (Exception e)
            {
                log("   [提示] 无法自动打开，请手动双击桌面的「GodMode」文件夹：" + e.Message);
            }
            log("完成。桌面上会出现一个名为 GodMode 的文件夹，里面是所有 Windows 设置的集中入口。");
        }
    }
}

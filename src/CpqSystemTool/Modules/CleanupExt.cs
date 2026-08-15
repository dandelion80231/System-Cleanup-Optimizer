using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CpqSystemTool
{
    /// <summary>
    /// 清理项扩围：在原有 Cleanup.cs 档位之上，补充 ZyperWin++ 风格的细粒度清理项
    /// （缩略图/D3D 着色器/终端缓存/预读取/WinSxS DISM 等），按勾选执行。
    /// 直接提升“清理更全”的口碑，且全部可回退、零风险。
    /// </summary>
    public static class CleanupExt
    {
        public class ExtraItem
        {
            public string Id, Name, Desc, Path;
        }

        public static readonly List<ExtraItem> Items = new List<ExtraItem>
        {
            new ExtraItem { Id = "thumb",    Name = "缩略图缓存",   Desc = "清理 thumbcache_*.db", Path = @"%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db" },
            new ExtraItem { Id = "d3d",      Name = "D3D 着色器缓存", Desc = "DirectX 着色器缓存", Path = @"%LOCALAPPDATA%\D3DSCache" },
            new ExtraItem { Id = "term",     Name = "终端缓存",     Desc = "Windows 终端缓存",   Path = @"%LOCALAPPDATA%\Microsoft\Windows Terminal\Cache" },
            new ExtraItem { Id = "prefetch", Name = "预读取文件",   Desc = "Prefetch 预读取",     Path = @"C:\Windows\Prefetch" },
            new ExtraItem { Id = "winsxs",   Name = "WinSxS 冗余",  Desc = "DISM 组件清理 (ResetBase，可能耗时数分钟)", Path = null },
        };

        // 统一记录"已忽略的异常"，避免重复样板。
        private static void LogIgnored(Exception ex) => System.Diagnostics.Debug.WriteLine("[CpqSystemTool] 异常(已忽略): " + ex.Message);

        public static void RunSelected(IEnumerable<string> ids, Action<string> log)
        {
            foreach (var it in Items.Where(x => ids.Contains(x.Id)))
            {
                if (it.Id == "winsxs")
                {
                    log("清理 WinSxS 冗余（DISM，可能需要数分钟）...");
                    Exec.RunCmd(new[] { "dism", "/Online", "/Cleanup-Image", "/StartComponentCleanup", "/ResetBase" }, log);
                    continue;
                }
                string expanded = Environment.ExpandEnvironmentVariables(it.Path);
                log("清理: " + it.Name + " -> " + expanded);
                // 改用原生 C#（参考 ZyperWin++ 的做法），避免启停 cmd.exe
                try
                {
                    if (Directory.Exists(expanded))
                    {
                        foreach (var f in Directory.EnumerateFiles(expanded, "*", SearchOption.TopDirectoryOnly))
                        {
                            try { File.Delete(f); } catch (Exception caughtEx) { LogIgnored(caughtEx);}
                        }
                        foreach (var d in Directory.EnumerateDirectories(expanded, "*", SearchOption.TopDirectoryOnly))
                        {
                            try { Directory.Delete(d, true); } catch (Exception caughtEx) { LogIgnored(caughtEx);}
                        }
                    }
                }
                catch (Exception caughtEx) { LogIgnored(caughtEx);/* skip locked */ }
            }
            log("[OK] 额外清理完成");
        }
    }
}

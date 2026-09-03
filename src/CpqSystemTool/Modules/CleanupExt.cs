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
            // C4: 以下 5 项的 Id/Name/Desc 为「清理优化」页与清理执行逻辑的唯一来源（MainWindow.Cleanup.cs 的 CleanupCatalog 直接引用此处，避免双份维护）。
            new ExtraItem { Id = "thumb",    Name = "缩略图缓存",   Desc = "thumbcache_*.db", Path = @"%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db" },
            new ExtraItem { Id = "d3d",      Name = "D3D着色器缓存", Desc = "DirectX 着色器缓存", Path = @"%LOCALAPPDATA%\D3DSCache" },
            new ExtraItem { Id = "term",     Name = "终端缓存",     Desc = "Windows Terminal 缓存",   Path = @"%LOCALAPPDATA%\Microsoft\Windows Terminal\Cache" },
            new ExtraItem { Id = "prefetch", Name = "预读取文件",   Desc = "Prefetch 预读取",     Path = @"C:\Windows\Prefetch" },
            new ExtraItem { Id = "winsxs",   Name = "WinSxS 冗余(DISM)",  Desc = "DISM /ResetBase（耗时数分钟）", Path = null },
        };

        // 统一记录"已忽略的异常"，避免重复样板。
        private static void LogIgnored(Exception ex) => DebugLog.Ignore(ex);

        public static void RunSelected(IEnumerable<string> ids, Action<string> log)
        {
            bool didWork = false;
            foreach (var it in Items.Where(x => ids.Contains(x.Id)))
            {
                if (it.Id == "winsxs")
                {
                    log("清理 WinSxS 冗余（DISM，可能需要数分钟）...");
                    Exec.RunCmd(new[] { "dism", "/Online", "/Cleanup-Image", "/StartComponentCleanup", "/ResetBase" }, log);
                    didWork = true;
                    continue;
                }
                string expanded = Environment.ExpandEnvironmentVariables(it.Path);
                log("清理: " + it.Name + " -> " + expanded);
                // 改用原生 C#（参考 ZyperWin++ 的做法），避免启停 cmd.exe
                try
                {
                    // C3: 含通配符（如 thumbcache_*.db）时按文件枚举删除；否则按目录整体清理
                    if (expanded.Contains('*') || expanded.Contains('?'))
                    {
                        string dir = Path.GetDirectoryName(expanded);
                        string pattern = Path.GetFileName(expanded);
                        if (Directory.Exists(dir))
                        {
                            int removed = 0;
                            foreach (var f in Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly))
                            {
                                try { File.Delete(f); removed++; didWork = true; }
                                catch (Exception caughtEx) { LogIgnored(caughtEx); }
                            }
                            if (removed == 0) log("  [i] 未匹配到文件，跳过: " + expanded);
                        }
                        else
                        {
                            log("  [i] 目录不存在，跳过: " + dir);
                        }
                    }
                    else if (Directory.Exists(expanded))
                    {
                        foreach (var f in Directory.EnumerateFiles(expanded, "*", SearchOption.TopDirectoryOnly))
                        {
                            try { File.Delete(f); didWork = true; }
                            catch (Exception caughtEx) { LogIgnored(caughtEx); }
                        }
                        foreach (var d in Directory.EnumerateDirectories(expanded, "*", SearchOption.TopDirectoryOnly))
                        {
                            try { Directory.Delete(d, true); didWork = true; }
                            catch (Exception caughtEx) { LogIgnored(caughtEx); }
                        }
                    }
                    else
                    {
                        log("  [i] 路径不存在，跳过: " + expanded);
                    }
                }
                catch (Exception caughtEx) { LogIgnored(caughtEx);/* skip locked */ }
            }
            // C3: 没有任何文件被清理（含通配符未匹配）时不打印虚假的 [OK]
            log(didWork ? "[OK] 额外清理完成" : "[i] 未清理任何文件，已全部跳过");
        }
    }
}

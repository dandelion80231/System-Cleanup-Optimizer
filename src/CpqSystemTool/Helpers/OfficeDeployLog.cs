using System;
using System.IO;
using System.Text;

namespace CpqSystemTool
{
    /// <summary>
    /// Office 部署页执行日志落盘：写入 cpq-tool\Office 部署\log\office_yyyyMMdd.log（按天分文件）。
    /// 页面全部日志行（ODT 引擎/下载/进度/部署、注册表快照、结果验证、各卸载分支）都汇入页面 sink 单一入口，
    /// 故该入口一行调用即覆盖整页日志。纯显示层的本地留存：UI 里 2000 行上限裁剪不影响这里，这里也不影响 UI。
    /// 文件数控制：启动时（AppPaths.EnsureDataTree）与会话内首写日志时都会执行 PruneOfficeLogs——
    /// 删除 <8 行的无用日志 + 只保留最新 20 份。任何写盘失败都不阻塞 UI（全程 try/catch，锁内完成防同日并发写冲突）。
    /// </summary>
    public static class OfficeDeployLog
    {
        private static readonly object Gate = new();
        private static StreamWriter _writer;
        private static string _writerFile;
        private static bool _prunedThisSession;
        // Q30：批量 flush —— 原先每行 Append 都 _writer.Flush()（ODT 成百上千行 → 逐次同步磁盘 IO）；改为每 100 行 flush 一次（切天/Dispose 时仍会 flush 剩余）。
        private static int _linesSinceFlush;

        /// <summary>追加一行日志（写盘在锁内完成，批量 flush 控制 IO 频率）。</summary>
        public static void Append(OfficeDeployControl.LogEntry entry)
        {
            if (entry == null) return;
            lock (Gate)
            {
                try
                {
                    string dir = AppPaths.OfficeDeployLogDir;
                    Directory.CreateDirectory(dir);
                    string file = Path.Combine(dir, "office_" + DateTime.Now.ToString("yyyyMMdd") + ".log");

                    if (!_prunedThisSession)
                    {
                        AppPaths.PruneOfficeLogs(); // 会话内首写：无用日志 + 20 份上限（当前文件受保护）
                        _prunedThisSession = true;
                    }

                    // 跨天自动切换文件（持有同一 writer 时目标文件名变了就重开）
                    if (_writer == null || !string.Equals(_writerFile, file, StringComparison.OrdinalIgnoreCase))
                    {
                        _writer?.Dispose();
                        _writer = new StreamWriter(
                            File.Open(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                            new UTF8Encoding(true));
                        _writerFile = file;
                    }
                    _writer.WriteLine((entry.Timestamp ?? "") + "  " + (entry.Text ?? ""));
                    _linesSinceFlush++;
                    // Q30：每 100 行才 flush 一次，避免 ODT 逐行同步写盘拖慢日志。
                    if (_linesSinceFlush >= 100) { _writer.Flush(); _linesSinceFlush = 0; }
                }
                catch { /* 写盘失败不阻塞 UI 显示 */ }
            }
        }
    }
}

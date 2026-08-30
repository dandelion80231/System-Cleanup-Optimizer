using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CpqSystemTool
{
    /// <summary>
    /// 原子文件写入：先写同目录 .tmp（同卷保证 rename 原子），再 MoveFileEx(REPLACE_EXISTING|WRITE_THROUGH) 覆盖替换；
    /// 目标被占用时回退「删除目标 + File.Move」；删除失败则放弃并保留 tmp（下次覆盖）不抛异常；finally 清理 tmp。
    /// 原在 MainWindow.Theme.cs / Modules\ConfigBackup.cs / Modules\SoftwareDefPersistence.cs 三处逐字重复，现收敛为单一实现。
    /// </summary>
    internal static class AtomicFile
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, uint dwFlags);

        private const uint MOVEFILE_REPLACE_EXISTING = 0x1;
        private const uint MOVEFILE_WRITE_THROUGH = 0x8;

        public static void WriteFileAtomic(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) dir = ".";
            string tmp = Path.Combine(dir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            bool keepTmp = false;
            try
            {
                File.WriteAllText(tmp, content, Encoding.UTF8);
                if (!MoveFileEx(tmp, path, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
                {
                    try
                    {
                        if (File.Exists(path)) File.Delete(path);
                    }
                    catch { keepTmp = true; return; } // 删除失败：放弃并保留 tmp（下次覆盖），不抛异常打断调用方
                    try { File.Move(tmp, path); }
                    catch { /* 改名失败：由 finally 清理 tmp */ }
                }
            }
            finally
            {
                if (!keepTmp)
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                }
            }
        }
    }
}

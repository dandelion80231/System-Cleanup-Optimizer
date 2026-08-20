﻿using System;
namespace CpqSystemTool
{
    /// <summary>全局操作互斥：同一时间只允许一个耗时操作（清理/优化/禁用等）运行，
    /// 防止按钮连点或跨模块并发导致并行删同目录/并行写同注册表键的竞态。</summary>
    internal static class OperationLock
    {
        private static readonly object Gate = new object();
        private static bool _busy;
        private static string _busyOperation;

        /// <summary>尝试进入操作；已有操作在运行时返回 false 并给出占用者名称。</summary>
        public static bool TryEnter(string operation, out string busyBy)
        {
            lock (Gate)
            {
                if (_busy) { busyBy = _busyOperation; return false; }
                _busy = true; _busyOperation = operation; busyBy = null; return true;
            }
        }
        public static void Exit()
        {
            lock (Gate) { _busy = false; _busyOperation = null; }
        }
    }
}

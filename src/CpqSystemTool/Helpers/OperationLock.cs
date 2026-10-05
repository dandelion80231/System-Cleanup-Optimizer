using System;
namespace CpqSystemTool
{
    /// <summary>全局操作互斥：同一时间只允许一个耗时操作（清理/优化/禁用等）运行，
    /// 防止按钮连点或跨模块并发导致并行删同目录/并行写同注册表键的竞态。</summary>
    internal static class OperationLock
    {
        private static readonly object Gate = new object();
        private static bool _busy;
        private static string _busyOperation;
        private static DateTime _busySinceUtc = DateTime.MinValue;
        /// <summary>锁持有超时自愈阈值。调用侧约定：TryEnter 成功后任何异常路径都要保证 Exit 执行；
        /// 若某调用点漏了 Exit（如 TryEnter 与后台任务启动之间抛异常），锁会永久卡死、后续所有耗时操作被「已有操作在运行」拦死，
        /// 只能重启程序。此超时作为底线自愈：持有超过 30 分钟视为泄漏，下一次 TryEnter 强放并告警。
        /// 阈值远大于任何正常操作（最慢的驱动清理也在 30 分钟内），不会误伤真在跑的操作。</summary>
        private static readonly TimeSpan MaxHold = TimeSpan.FromMinutes(30);

        /// <summary>尝试进入操作；已有操作在运行时返回 false 并给出占用者名称。
        /// 异常自愈：占用者持有超过 <see cref="MaxHold"/> 时判定为泄漏，强放后继续获取本次操作（同时 DebugLog.Warn 告警）。</summary>
        public static bool TryEnter(string operation, out string busyBy)
        {
            lock (Gate)
            {
                if (_busy)
                {
                    if (DateTime.UtcNow - _busySinceUtc >= MaxHold)
                    {
                        string leaked = _busyOperation;
                        _busy = false; _busyOperation = null; _busySinceUtc = DateTime.MinValue;
                        DebugLog.Warn("OperationLock 泄漏自愈：「" + leaked + "」持有超过 " + MaxHold.TotalMinutes + " 分钟未 Exit，已强制放行");
                        // 不 return：落入下方重新获取
                    }
                    else
                    {
                        busyBy = _busyOperation; return false;
                    }
                }
                _busy = true; _busyOperation = operation; _busySinceUtc = DateTime.UtcNow; busyBy = null; return true;
            }
        }
        /// <summary>
        /// 「转空闲」通知：Exit 释放忙碌状态（busy→idle 转变）时触发。
        /// 供主题挂起重建使用：耗时操作运行中发生主题变化时，待操作结束再重建当前页，
        /// 既不打断进行中的日志/勾选显示，也不留旧主题页面。
        /// 订阅者需自行路由到 UI 线程（触发线程不保证是 UI 线程）。
        /// </summary>
        public static event Action Idle;

        public static void Exit()
        {
            bool wasBusy;
            lock (Gate) { wasBusy = _busy; _busy = false; _busyOperation = null; _busySinceUtc = DateTime.MinValue; }
            // 在锁外触发：避免订阅者（挂起重建等）在事件处理中再进 TryEnter 造成死锁
            if (wasBusy) Idle?.Invoke();
        }

        /// <summary>当前是否有耗时操作在运行。供后台轮询（如 TP 状态轮询）判断：
        /// 操作期间跳过自动刷新，避免把用户正在操作时的 UI 状态冲掉/造成闪烁。</summary>
        public static bool IsBusy
        {
            get { lock (Gate) { return _busy; } }
        }

        /// <summary>当前持锁的操作名（空闲时 null）。供状态栏等调用方在跨页切走时显示「操作进行中」。</summary>
        public static string BusyOperation
        {
            get { lock (Gate) { return _busy ? _busyOperation : null; } }
        }
    }
}

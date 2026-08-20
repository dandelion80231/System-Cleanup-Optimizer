﻿using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace CpqSystemTool
{
    public partial class App : Application
    {
        // ---- 单实例保护：同一时间只允许一个实例（第二实例激活已有窗口后退出）----
        private static System.Threading.Mutex _singleInstanceMutex;
        private const string SingleInstanceMutexName = @"Local\CpqSystemTool_SingleInstance";

        // ---- 启动计时埋点（定位白屏卡顿用，发布后可移除）----
        public static readonly string TracePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cleaner_trace.log");
        public static void Trace(string stage)
        {
            try { System.IO.File.AppendAllText(TracePath, System.DateTime.Now.ToString("HH:mm:ss.fff") + "  " + stage + "\n"); }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
        }

        static App()
        {
            // 极早期兜底：连 App.xaml 解析/Application 初始化期的崩溃也能捕获（DispatcherUnhandledException 此时尚未挂上）
            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandled;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // 单实例检查必须在最前：第二实例不创建新 MainWindow，激活已有窗口后直接退出
            if (!TryAcquireSingleInstance()) return;
            try { System.IO.File.WriteAllText(TracePath, "=== trace " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===\n"); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            Trace("OnStartup.start");
            // 启动时若有待固化标记，把增补列表写进 exe 自身（自包含；失败自动回退 json）
            SoftwareDefPersistence.ApplyPendingBakeIfAny();
            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherUnhandled;
            Trace("OnStartup.end");
        }

        /// <summary>尝试获取单实例互斥量；已有实例在运行则激活其主窗口并返回 false（本实例将退出）。失败时放行，保证工具始终可用。</summary>
        private bool TryAcquireSingleInstance()
        {
            try
            {
                bool createdNew;
                _singleInstanceMutex = new System.Threading.Mutex(true, SingleInstanceMutexName, out createdNew);
                if (createdNew) return true;
                ActivateExistingInstance();
                Shutdown();
                return false;
            }
            catch (Exception caughtEx)
            {
                DebugLog.Ignore(caughtEx);
                return true; // 互斥量获取异常（极端权限/命名冲突）时放行启动
            }
        }

        /// <summary>把已运行实例的主窗口（若有）恢复到前台，用户双击第二下时能看到已有窗口。</summary>
        private static void ActivateExistingInstance()
        {
            try
            {
                var me = System.Diagnostics.Process.GetCurrentProcess();
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(me.ProcessName))
                {
                    if (p.Id == me.Id) continue;
                    var h = p.MainWindowHandle;
                    if (h != System.IntPtr.Zero)
                    {
                        ShowWindow(h, 9); // SW_RESTORE：最小化时恢复
                        SetForegroundWindow(h);
                        return;
                    }
                }
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(System.IntPtr hWnd);

        private static void OnCurrentDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            WriteCrashLog("AppDomain.CurrentDomain.UnhandledException (IsTerminating=" + e.IsTerminating + ")", ex);
            ShowCrash(ex);
        }

        private void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            WriteCrashLog("DispatcherUnhandledException", e.Exception);
            ShowCrash(e.Exception);
        }

        private static void WriteCrashLog(string where, Exception ex)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                var sb = new StringBuilder();
                sb.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====");
                sb.AppendLine("Where: " + where);
                var x = ex;
                int i = 0;
                while (x != null)
                {
                    sb.AppendLine("[" + i + "] " + x.GetType().FullName + ": " + x.Message);
                    sb.AppendLine(x.StackTrace);
                    x = x.InnerException;
                    i++;
                }
                sb.AppendLine();
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  /* 日志失败也不应再抛异常 */ }
        }

        private static void ShowCrash(Exception ex)
        {
            try
            {
                var msg = (ex?.GetType().FullName ?? "Unknown") + ": " + (ex?.Message ?? "");
                if (ex?.InnerException != null)
                    msg += "\n内层: " + ex.InnerException.GetType().FullName + ": " + ex.InnerException.Message;
                msg += "\n\n（详细已写入 crash.log）";
                MessageBox.Show(msg, "系统清理与优化工具 · 未处理异常", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  /* MessageBox 不可用（极早期崩溃）时忽略，crash.log 已记录 */ }
        }
    }
}

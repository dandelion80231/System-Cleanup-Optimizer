﻿using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace CpqSystemTool
{
    public partial class App : Application
    {
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
            try { System.IO.File.WriteAllText(TracePath, "=== trace " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===\n"); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            Trace("OnStartup.start");
            // 启动时若有待固化标记，把增补列表写进 exe 自身（自包含；失败自动回退 json）
            SoftwareDefPersistence.ApplyPendingBakeIfAny();
            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherUnhandled;
            Trace("OnStartup.end");
        }

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

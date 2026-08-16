using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.Threading;

namespace CpqSystemTool
{
    /// <summary>
    /// UI 辅助方法：按钮、卡片、标题、日志框、进度条、后台任务编排。
    /// </summary>
    public partial class MainWindow
    {
        private UIElement Header(string title, string sub)
        {
            // 不输出大标题（顶部 PageTitle 已经显示），只输出描述
            if (string.IsNullOrEmpty(sub)) return new StackPanel { Height = 0 };
            return new TextBlock { Text = sub, FontSize = 12, Foreground = _textDim, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
        }


        internal Border Card(params UIElement[] children)
        {
            var sp = new StackPanel();
            foreach (var c in children) sp.Children.Add(c);
            return new Border
            {
                Background = _bgCard,  // 已设为 Brushes.Transparent（两层通用）
                CornerRadius = new CornerRadius(12),
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16),
                // 轻微投影，让卡片从背景图中"浮起"，增强层次感（深/浅模式都适用）
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 16,
                    ShadowDepth = 2,
                    Opacity = 0.30,
                    Color = Color.FromRgb(0x00, 0x00, 0x00)
                },
                Child = sp
            };
        }

        internal Button Btn(string text, bool primary, Action onClick, double minW = 130)
        {
            var b = new Button
            {
                Content = text,
                MinWidth = minW,
                MinHeight = 34,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(12, 7, 12, 7),
                Cursor = Cursors.Hand,
                FontSize = 12,
                FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
                Background = primary ? _accent : _btnSecondaryBg,
                Foreground = primary ? _btnPrimaryFg : _btnSecondaryFg,
                BorderThickness = primary ? new Thickness(0) : new Thickness(1),
                BorderBrush = primary ? Brushes.Transparent : _panelBorder
            };
            if (onClick != null)
                b.Click += (s, e) => onClick();
            return b;
        }

        // ===================== Win32 剪贴板操作（绕过 WPF Clipboard 的 STA/占用限制） =====================
        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        /// <summary>
        /// 使用 Win32 API 直接设置剪贴板 Unicode 文本。会强制 EmptyClipboard，确保内容被替换。
        /// </summary>
        private bool SetClipboardTextWin32(string text)
        {
            if (!OpenClipboard(IntPtr.Zero)) return false;
            try
            {
                if (!EmptyClipboard()) return false;
                var bytes = Encoding.Unicode.GetBytes(text + "\0");
                var hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
                if (hMem == IntPtr.Zero) return false;
                try
                {
                    var ptr = GlobalLock(hMem);
                    if (ptr == IntPtr.Zero) { GlobalFree(hMem); return false; }
                    Marshal.Copy(bytes, 0, ptr, bytes.Length);
                    GlobalUnlock(hMem);
                    // SetClipboardData 成功后，hMem 所有权转移给系统，不能再 GlobalFree
                    return SetClipboardData(CF_UNICODETEXT, hMem) != IntPtr.Zero;
                }
                catch
                {
                    GlobalFree(hMem);
                    throw;
                }
            }
            finally
            {
                CloseClipboard();
            }
        }

        /// <summary>
        /// 异步重试写入剪贴板。先尝试 WPF Clipboard（兼容剪贴板历史/云同步），
        /// 若失败再降级到 Win32 API 强制写入。
        /// </summary>
        private async Task<bool> TrySetClipboardTextAsync(string text, int retryCount = 20, int delayMs = 200)
        {
            // 第一次：尝试 WPF Clipboard（对 Windows 剪贴板历史、云同步友好）
            try { Clipboard.SetDataObject(text, true); return true; }
            catch { }

            // 失败后异步重试：Win32 API 强制写入
            for (int i = 0; i < retryCount; i++)
            {
                if (SetClipboardTextWin32(text)) return true;
                if (i < retryCount - 1) await Task.Delay(delayMs);
            }
            return false;
        }

        /// <summary>
        /// 将多个按钮排成一行、等宽均分整行宽度；每个按钮在所属列内居中、保持原始大小。
        /// 用于卡片内多按钮操作行（系统优化 / Defender / 隐私 / 安全防护更新），视觉一致。
        /// </summary>
        private Grid MakeBtnRow(params Button[] btns)
        {
            var g = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            for (int i = 0; i < btns.Length; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                btns[i].HorizontalAlignment = HorizontalAlignment.Center;
                btns[i].Margin = new Thickness(0);
                Grid.SetColumn(btns[i], i);
                g.Children.Add(btns[i]);
            }
            return g;
        }

        /// <summary>
        /// 提示条：左侧 3px 强调竖条 + 图标 + 正文，用于声明 / 注意事项 / 免责等旁注信息。
        /// 语义色由调用方传入（_accent=信息、_warnOrange=注意、_dangerRed=风险），
        /// 不单靠颜色传达含义——图标与文案同时表意，满足无障碍要求。
        /// </summary>
        /// <summary>
        /// 外部链接文本：下划线 + 手型光标 + hover 变色。
        /// 不只用颜色区分链接（WCAG 1.4.1 非仅凭颜色传达信息），故始终保留下划线。
        /// </summary>
        private TextBlock LinkText(string text, string url, double fontSize = 11.5)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                Foreground = _accent,
                TextDecorations = TextDecorations.Underline,
                Cursor = Cursors.Hand,
                ToolTip = url
            };
            tb.MouseEnter += (s, e) => tb.Opacity = 0.75;
            tb.MouseLeave += (s, e) => tb.Opacity = 1.0;
            tb.MouseLeftButtonUp += (s, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); }
                catch (Exception ex) { SetStatus("打开链接失败: " + ex.Message); }
            };
            return tb;
        }

        private ProgressBar MakeProgress() => new ProgressBar
        {
            Height = 4,
            IsIndeterminate = true,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 6, 0, 6),
            Foreground = _accent,
            Background = _panelBorder
        };

        private TextBox MakeLogBox() => new TextBox
        {
            Background = _bgCard,  // 已设为 Transparent（两模式通用）
            Foreground = _textDim,
            BorderBrush = _panelBorder,
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Consolas, 'Courier New'"),
            FontSize = 11.5,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 80,
            Margin = new Thickness(0, 4, 0, 0)
        };

        /// <summary>
        /// 给日志框加统一圆角外框。调用后 log 的边框/背景会移到外层 Border，返回的 Border 可直接放入布局。
        /// </summary>
        private Border WrapLogBox(TextBox log, double cornerRadius = 8)
        {
            log.BorderThickness = new Thickness(0);
            log.Background = Brushes.Transparent;
            log.Margin = new Thickness(0);
            return new Border
            {
                Child = log,
                Background = _isDarkMode ? Brushes.Transparent : _bgCard,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(cornerRadius),
                Padding = new Thickness(2),
                Margin = new Thickness(0, 4, 0, 0),
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }

        /// <summary>
        /// 将页面根元素高度约束绑定到内容区(ScrollViewer)的实际渲染高度。
        /// ★ 用 ActualHeight（只读 DependencyProperty，尺寸变化时自动通知）替代手动读 ViewportHeight：
        ///   ViewportHeight 不是 DependencyProperty，绑定只求值一次、缩放后永不更新 → 首帧/缩放后状态陈旧。
        ///   本方法使"Star 行填充视口 + 窗口缩放稳定跟随"在任何尺寸下自动成立，根治两类时序 bug：
        ///     (1) 首次打开时 ViewportHeight=0 → MaxHeight 未设置 → 内容不填充；
        ///     (2) 最大化后恢复默认尺寸 → 手动 MaxHeight 未及时更新 → 页面内容漂移（如预览区忽大忽小）。
        /// </summary>
        private void BindRootHeightToViewport(FrameworkElement root)
        {
            var b = new Binding("ActualHeight") { Source = ContentArea, Mode = BindingMode.OneWay };
            root.SetBinding(FrameworkElement.MaxHeightProperty, b);
        }

        private string RiskLabel(string r) => r == "high" ? "高风险" : r == "mid" ? "中风险" : "低风险";

        /// <summary>
        /// 从给定节点出发，沿视觉树向上查找指定类型的祖先元素。
        /// 用于 HitTest 后从叶子节点回溯找到包含它的容器（如 ScrollViewer）。
        /// 兼容 Inline（Run 等）非 Visual 对象：先沿逻辑树走到宿主容器，再转视觉树。
        /// </summary>
        private static T FindVisualAncestor<T>(DependencyObject node) where T : DependencyObject
        {
            // Inline (Run/Hyperlink 等) 不是 Visual/Visual3D，VisualTreeHelper.GetParent 会抛异常
            while (node != null && !(node is System.Windows.Media.Visual) && !(node is System.Windows.Media.Media3D.Visual3D))
            {
                node = System.Windows.LogicalTreeHelper.GetParent(node);
            }

            for (var current = node; current != null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            {
                if (current is T t) return t;
            }
            return null;
        }


        private void AutoLoad(Action action)
        {
            // 用 Loaded 之后立即同步执行（不用 BeginInvoke 排队——窗口首次加载后，
            // 后续 Build* 重建页面时 Loaded 已触发，BeginInvoke 不再执行，导致 Action 永不运行）
            // 这里用低优先级 Background 同步，绕开构造函数调用栈（避免在控件未加载时改 UI）
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { action(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("AutoLoad 失败: " + ex.Message); }
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private void RunInBg(TextBox log, Action<Action<string>> work, string done = "完成", Action onDoneUi = null)
        {
            log?.Dispatcher.Invoke(() => { log.Visibility = Visibility.Visible; log.Clear(); });
            var disp = Dispatcher;
            Action<string> logf = s => disp.BeginInvoke(new Action(() => AppendOrReplaceLog(log, s)));
            new Thread(() =>
            {
                try
                {
                    work(logf);
                    disp.Invoke(() => { SetStatus(done); onDoneUi?.Invoke(); });
                }
                catch (Exception ex)
                {
                    disp.Invoke(() =>
                    {
                        logf("[!] 异常: " + ex.Message);
                        SetStatus("执行出错");
                        try { onDoneUi?.Invoke(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("onDoneUi 失败: " + ex.Message); }
                    });
                }
            }).Start();
        }

        /// <summary>
        /// 向日志框追加一行；若消息以 \r 开头，则原地替换最后一行（用于下载百分比等进度，避免刷屏）。
        /// 调用方需自行确保在 UI 线程（RunInBg 的 logf 已通过 Dispatcher 保证）。
        /// </summary>
        private static void AppendOrReplaceLog(TextBox tb, string s)
        {
            if (tb == null) return;
            tb.Visibility = Visibility.Visible;
            if (s != null && s.Length > 0 && s[0] == '\r')
            {
                // 进度行（\r 前缀）。用 tb.Tag 记录“最后一行是否为进度行”：
                //   - 上一行是进度行 → 原地替换最后一行（避免刷屏）；
                //   - 上一行是普通日志（如“检测到缺少…”）→ 进度行作为新行追加，不覆盖提示信息。
                // 不把 \r 存进文本（WPF 会把孤立 \r 当换行，破坏显示），仅用 Tag 标记状态。
                string content = s.Substring(1);
                bool prevIsProgress = tb.Tag is string;
                if (prevIsProgress)
                {
                    string text = tb.Text;
                    if (text.EndsWith("\r\n")) text = text.Substring(0, text.Length - 2);
                    int lastNl = text.LastIndexOf('\n');
                    string head = lastNl >= 0 ? text.Substring(0, lastNl + 1) : "";
                    tb.Text = head + content + "\r\n";
                }
                else
                {
                    tb.AppendText(content + "\r\n");
                }
                tb.Tag = content;
            }
            else
            {
                tb.AppendText(s + "\r\n");
                tb.Tag = null; // 普通行后，进度需重新起一行
            }
            tb.ScrollToEnd();
        }

        /// <summary>
        /// 搜索框：TextBox + 🔍 图标叠放（Grid + ZIndex），图标常驻左侧、不拦截命中测试。
        /// Appx 商店 / 常用软件两列表页共用，消除约 25 行重复样板。
        /// </summary>
        private (Grid wrap, TextBox box) MakeSearchBox(double fontSize, string toolTip = null, bool accentCaret = false)
        {
            var box = new TextBox
            {
                Text = "",
                FontSize = fontSize,
                Padding = new Thickness(28, 6, 10, 6),  // 左边 28px 给 🔍 图标留位置
                Background = _isDarkMode ? Brushes.Transparent : _bgDeep,
                Foreground = _textMain,
                BorderBrush = _accent,
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            if (accentCaret) box.CaretBrush = _accent;
            if (toolTip != null) box.ToolTip = toolTip;
            var icon = new TextBlock
            {
                Text = "🔍",
                FontSize = fontSize,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(10, 0, 0, 0),
                IsHitTestVisible = false,
                Foreground = _textDim
            };
            var wrap = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            wrap.Children.Add(box);
            wrap.Children.Add(icon);
            Panel.SetZIndex(icon, 1);  // 强制图标在 TextBox 之上
            return (wrap, box);
        }

        /// <summary>
        /// 选中计数状态栏：写入底部 StatusText（已选中 X/Y 单位 / 就绪），高亮随选中态切换。
        /// 三处列表页（Appx 商店 / Appx 管理 / 常用软件）共用同一形态，消除重复 lambda。
        /// </summary>
        private void UpdateSelStatus(int checkedCount, int total, string unit)
        {
            StatusText.Text = checkedCount > 0 ? $"已选中 {checkedCount}/{total} {unit}" : "就绪";
            StatusText.Foreground = checkedCount > 0 ? _accent : _textDim;
            StatusText.FontWeight = checkedCount > 0 ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }
}

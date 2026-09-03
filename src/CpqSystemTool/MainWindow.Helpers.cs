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
    /// 页面缓存：把「整页 UI + 构建时主题 + 可选内容键 + 动态状态刷新委托」这四件套收拢成一个对象。
    ///
    /// 背景：本项目有 9 个页面各自维护 _cachedXxxPage / _xxxCacheDark / _xxxCacheKey / _xxxRefresh
    /// 四个字段，并在 Build 开头重复同一段「命中判断 + 失效清理」样板（合计约 200 行）。
    /// 分散写法还埋过一次真实 bug：某些处置空缓存时只清了 _cachedXxxPage，漏清 _xxxRefresh /
    /// _xxxCacheKey，导致页面虽已重建、却仍持有旧页面的刷新委托与旧缓存键。
    /// 收拢成泛型类后，"失效"只有 Invalidate() 一个入口，不可能再漏清。
    ///
    /// 用法：
    ///   private readonly PageCache&lt;UIElement&gt; _xxxCache = new PageCache&lt;UIElement&gt;();
    ///   // Build 开头
    ///   var cached = _xxxCache.TryGet(buildDark);            // 可选第二参：内容键
    ///   if (cached != null) return cached;
    ///   // 构建完成后
    ///   _xxxCache.Set(root, buildDark);
    ///   _xxxCache.SetRefresh(() => { /* 复位动态状态 */ });
    ///   // 数据变化需要重建时
    ///   _xxxCache.Invalidate();
    /// </summary>
    /// <typeparam name="T">页面根元素类型（一般为 UIElement）</typeparam>
    internal sealed class PageCache<T> where T : UIElement
    {
        private T _page;
        private string _key;      // 非 null 即表示"已构建过"；部分页面还会用它比对内容键
        private bool _dark;       // 构建时的主题；主题变了必须重建，否则会复用旧主题画刷
        private Action _refresh;  // 再次进页时复位动态状态（清空日志/进度条/勾选等）

        /// <summary>
        /// 尝试命中缓存。命中时先执行刷新委托再返回已构建页面；未命中（或主题变化/内容键变化）
        /// 则自动失效并返回 null，调用方走完整重建。
        /// </summary>
        /// <param name="dark">本次构建时的主题（应与上次构建时一致才算命中）</param>
        /// <param name="contentKey">
        /// 可选的内容键：传入时要求与上次构建记录的内容键完全相同才命中。
        /// 用于列表内容会变的页面（如 Appx 目录、常用软件清单），避免内容已变却复用旧页。
        /// 不传则只校验主题。
        /// </param>
        public T TryGet(bool dark, string contentKey = null)
        {
            if (_page != null
                && _dark == dark
                && !string.IsNullOrEmpty(_key)
                && (contentKey == null || string.Equals(_key, contentKey, StringComparison.Ordinal)))
            {
                var refresh = _refresh;
                // 注意下面的 return 读的是**字段** _page，而不是先捕获到局部变量：
                // 刷新委托内部有可能调用 Invalidate()（例如刷新时发现数据已失效需要重建），
                // 那样 _page 会变成 null，此处便返回 null → 调用方走完整重建。
                // 若改成"先捕获旧引用再执行刷新"，就会把本该丢弃的旧页又返回回去。
                if (refresh != null)
                {
                    try { refresh(); }
                    catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
                }
                return _page;
            }
            // 主题变化 / 内容键变化 / 无缓存 → 丢弃旧页走完整重建
            Invalidate();
            return null;
        }

        /// <summary>记录本次构建的页面。refresh 也可随后用 SetRefresh 单独设置（构建代码顺序更自然）。</summary>
        public void Set(T page, bool dark, Action refresh = null)
        {
            _page = page;
            _dark = dark;
            _refresh = refresh;
            // 内容键为空时用固定串标记"已构建"，保持 _key != null 这个命中前提
            _key = "1";
        }

        /// <summary>单独设置内容键（用于 Appx / 常用软件这类"列表内容变了就要重建"的页面）。</summary>
        public void SetContentKey(string contentKey)
        {
            _key = contentKey;
        }

        /// <summary>单独设置动态状态刷新委托（构建代码里 refresh 的定义往往晚于页面根构建）。</summary>
        public void SetRefresh(Action refresh)
        {
            _refresh = refresh;
        }

        /// <summary>失效缓存：页面、内容键、刷新委托一并清空，下次进页完整重建。</summary>
        public void Invalidate()
        {
            _page = null;
            _key = null;
            _refresh = null;
        }
    }

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
            catch (Exception ex) { DebugLog.Ignore(ex); }

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
        /// 去重：原 DriverStorePanel 内有一份逐行相同的拷贝，已删除并改为调用本方法。
        /// 实现不依赖任何实例状态，故提为 internal static，外部（如 DriverStorePanel）可直接静态调用，
        /// 无需持有 MainWindow 实例，也不必把成员暴露为 public。
        /// </summary>
        internal static Grid MakeBtnRow(params Button[] btns)
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
            // 窗口关闭后 Dispatcher 关停，BeginInvoke/Invoke 均抛 InvalidOperationException；
            // 后台线程未处理异常在 net48 会直接终止进程。safeUi 统一兜底：UI 更新静默忽略。
            Action<Action> safeUi = a => { try { disp.BeginInvoke(a); } catch { /* 窗口已关闭，忽略 */ } };
            Action<string> logf = s => safeUi(() => AppendOrReplaceLog(log, s));
            // 修复：前台线程（IsBackground=false）会阻止进程退出——任务跑完后窗口关闭，
            // 只要有这种线程还活着，CLR 就不会结束进程（表现为"关窗后 exe 仍在后台驻留"）。
            // 统一设为后台线程：随进程退出自动终止，与本项目其它后台加载线程（Defender/Firewall/Update）一致。
            new Thread(() =>
            {
                try
                {
                    work(logf);
                    safeUi(() => { SetStatus(done); onDoneUi?.Invoke(); });
                }
                catch (Exception ex)
                {
                    safeUi(() =>
                    {
                        logf("[!] 异常: " + ex.Message);
                        SetStatus("执行出错");
                        try { onDoneUi?.Invoke(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("onDoneUi 失败: " + ex.Message); }
                    });
                }
            }) { IsBackground = true, Name = "RunInBgWorker" }.Start();
        }

        /// <summary>
        /// 与 RunInBg 相同，但状态栏文案由回调在任务结束后动态决定。
        /// 用于「结果可能部分失败」的任务——固定文案会在失败时也显示"完成"，构成假成功
        /// （参见 MainWindow.Maint.cs 的「卸载本地依赖」与「清理探针缓存」）。
        /// 回调在 UI 线程上执行，可安全读取任务中统计的计数变量。
        /// </summary>
        private void RunInBgWithStatus(TextBox log, Action<Action<string>> work, Func<string> done, Action onDoneUi = null)
        {
            log?.Dispatcher.Invoke(() => { log.Visibility = Visibility.Visible; log.Clear(); });
            var disp = Dispatcher;
            Action<Action> safeUi = a => { try { disp.BeginInvoke(a); } catch { /* 窗口已关闭，忽略 */ } };
            Action<string> logf = s => safeUi(() => AppendOrReplaceLog(log, s));
            new Thread(() =>
            {
                try
                {
                    work(logf);
                    safeUi(() => { SetStatus(done?.Invoke() ?? "完成"); onDoneUi?.Invoke(); });
                }
                catch (Exception ex)
                {
                    safeUi(() =>
                    {
                        logf("[!] 异常: " + ex.Message);
                        SetStatus("执行出错");
                        try { onDoneUi?.Invoke(); } catch (Exception ex2) { System.Diagnostics.Debug.WriteLine("onDoneUi 失败: " + ex2.Message); }
                    });
                }
            }) { IsBackground = true, Name = "RunInBgWorker" }.Start();
        }

        // 日志滚动降频参数：普通行在行数低于阈值时保持“每行 ScrollToEnd”（与旧行为一致）；
        // 超过阈值后改为每 N 行滚动一次，降低长任务（清理/探针/下载）大量追加日志时的 UI 线程布局压力。
        private const int LogScrollLineThreshold = 500;
        private const int LogScrollEveryN = 10;
        // 日志行数上限：超过 LOG_MAX_LINES 后从头部裁剪旧行（保留尾部 LOG_TRIM_KEEP_LINES 行，
        // 留余量避免频繁触发），约束长任务（清理/探针/下载百分比）期间文本无限增长的内存与布局渲染开销。
        private const int LOG_MAX_LINES = 3000;
        private const int LOG_TRIM_KEEP_LINES = 2500;
        // 批量裁剪阈值：行数超限后，每累计 LOG_TRIM_BATCH_LINES 行才真正裁剪一次。
        // 原实现每次追加都做「读全量 tb.Text + O(n) 数换行 + Substring 回写」，追加 N 行即 O(N²)
        // （3000 行后每追加一行都要扫 10 万+ 字符）。改成累计计数后，单次裁剪成本被 N 行摊薄。
        // 裁剪回到 LOG_TRIM_KEEP_LINES=2500 行，批量上限 200 行 → 稳态峰值 2700 行，
        // 始终低于 LOG_MAX_LINES=3000 上限；另保留 LineCount 硬上限兜底，绝不越界。
        private const int LOG_TRIM_BATCH_LINES = 200;
        // 各日志框「距上次裁剪已追加的行数」。用 ConditionalWeakTable 挂在 TextBox 实例上：
        // 不占用已被进度行标记占用的 Tag，且日志框被回收时计数自动释放，不会泄漏。
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBox, int[]> LogTrimCounters =
            new System.Runtime.CompilerServices.ConditionalWeakTable<TextBox, int[]>();

        private static int[] GetTrimCounter(TextBox tb)
        {
            int[] box;
            if (!LogTrimCounters.TryGetValue(tb, out box))
            {
                box = new int[1];
                LogTrimCounters.Add(tb, box);
            }
            return box;
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
                if (tb.Tag is string)
                {
                    // 原地替换最后一行：用行索引 API 直接定位替换区间（GetLineIndexFromCharacterIndex /
                    // GetCharacterIndexFromLineIndex 走文本容器行表，不构造整段字符串副本），
                    // 取代旧实现的「全量读 tb.Text + Substring + 整段回写」——日志再长也是 O(log n)，不随文本长度退化。
                    // 语义与旧实现等价：替换最后一个“\r\n”之前的最后一行内容。
                    // 本方法所有写入都以 "\r\n" 结尾（进度行/普通行均如此），故文本非空则必以 "\r\n" 收尾；
                    // 进度行内容本身为单行（百分比等短消息），行表定位与 TextLength 天然对齐。
                    int total = tb.Text.Length;
                    if (total >= 2)
                    {
                        int lastLine = tb.GetLineIndexFromCharacterIndex(total);
                        if (lastLine >= 1)
                        {
                            int lineStart = tb.GetCharacterIndexFromLineIndex(lastLine - 1);
                            int len = total - lineStart;
                            if (lineStart >= 0 && len > 0)
                            {
                                tb.Select(lineStart, len);
                                tb.SelectedText = "";
                            }
                        }
                    }
                }
                tb.AppendText(content + "\r\n");
                tb.Tag = content;
                // 进度行原地替换后行数不变，ScrollToEnd 不触发重排；保持进度数字持续可见（与旧行为一致）。
                // 行数上限：替换本身不增行数，但若此前普通行已把行数推超上限，仍需在此触发一次裁剪。
                TrimLogHeadIfHardLimit(tb);
                tb.ScrollToEnd();
                return;
            }

            tb.AppendText(s + "\r\n");
            tb.Tag = null; // 普通行后，进度需重新起一行
            // 行数上限：攒够 LOG_TRIM_BATCH_LINES 行（或触及硬性上限）才批量裁剪一次，
            // 避免每次追加都做一次全量文本拷贝 + O(n) 扫描（原实现为 O(n²)）。
            // 以 tb.LineCount 判定是否越界——各框自身实时渲染行数，非 static 共享计数，
            // 天然按框隔离；log.Clear() 后归零，自动与文本一致。
            TrimLogHeadIfOverLimit(tb);
            // 滚动降频：短日志（行数 < 阈值）每行都滚到底（与旧行为一致，避免小日志滚动迟滞）；
            // 超阈值的长日志每 N 行滚一次。LineCount 是各日志框自身的行数（每追加一行 +1），
            // 天然按框隔离，无 static 共享计数状态，多日志框并发追加时互不干扰、也不会丢失自动滚底。
            if (tb.LineCount < LogScrollLineThreshold || tb.LineCount % LogScrollEveryN == 0)
                tb.ScrollToEnd();
        }

        /// <summary>
        /// 日志行数上限裁剪（批量版）：从头部删除旧行，保留尾部 LOG_TRIM_KEEP_LINES 行。
        /// 原实现每次追加都做「读全量 tb.Text + O(n) 数换行 + Substring 回写」，追加 N 行即 O(N²)。
        /// 现改为：累计追加行数，攒够 LOG_TRIM_BATCH_LINES 行才真正裁剪一次（或 LineCount 触及
        /// LOG_MAX_LINES 硬上限时立即裁剪，保证行数绝不越界）。单次 O(n) 成本被 200 行摊薄。
        /// 实际裁剪按文本中的 '\n' 精确定位（本方法所有写入均以 "\r\n" 结尾，'\n' 计数即行数），
        /// 故日志含长行折行（LineCount 计折行行）时也不会删过头——逻辑行不足保留量则跳过。
        /// 头部裁剪必然保留最后一行：Tag 标记的进度行始终在尾部，天然存活，无需清 Tag；
        /// 裁剪后 ScrollToEnd() 保证用户仍看到底部最新日志。
        /// </summary>
        private static void TrimLogHeadIfOverLimit(TextBox tb)
        {
            int[] counter = GetTrimCounter(tb);
            counter[0]++;
            // 批量门槛：未攒够 LOG_TRIM_BATCH_LINES 行、且未触及硬上限时 O(1) 返回，不碰文本。
            if (counter[0] < LOG_TRIM_BATCH_LINES && tb.LineCount < LOG_MAX_LINES) return;
            counter[0] = 0;
            if (tb.LineCount <= LOG_TRIM_KEEP_LINES) return;   // 如刚 Clear 过，无需裁剪
            DoTrimLogHead(tb);
        }

        /// <summary>进度行（原地替换、不新增行）用的硬上限兜底：行数确实超限时才裁剪。</summary>
        private static void TrimLogHeadIfHardLimit(TextBox tb)
        {
            if (tb.LineCount < LOG_MAX_LINES) return;
            GetTrimCounter(tb)[0] = 0;
            DoTrimLogHead(tb);
        }

        private static void DoTrimLogHead(TextBox tb)
        {
            string text = tb.Text;
            // 统计当前总行数（按 '\n' 计）
            int totalLines = 0;
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '\n') totalLines++;
            int removeLines = totalLines - LOG_TRIM_KEEP_LINES;
            if (removeLines <= 0) return; // 折行导致渲染行数超限但逻辑行未超 → 不裁剪
            int cut = 0;
            for (int i = 0; i < removeLines; i++)
            {
                int nl = text.IndexOf('\n', cut);
                if (nl < 0) break; // 防御：理论上不会发生（removeLines ≤ 总行数）
                cut = nl + 1;
            }
            if (cut <= 0) return;
            tb.Text = text.Substring(cut);
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

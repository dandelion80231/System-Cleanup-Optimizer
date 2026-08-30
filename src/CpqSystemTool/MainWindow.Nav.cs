using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CpqSystemTool
{
    /// <summary>
    /// 导航：侧边栏构建、页面切换、侧边栏宽度拖拽调整。
    /// </summary>
    public partial class MainWindow
    {
        // 软件版本号：左下角显示（同 BuildAbout 关于页更新日志）。
        // ⚠ 升版时须同步修改 CpqSystemTool.csproj 的 AssemblyVersion / FileVersion / InformationalVersion（当前 1.0.17.0 ↔ v1.17），两处保持一致。
        private const string APP_VERSION = "v1.17";

        // 导航按钮最小高度：16 个按钮平分视口高度（Star 行），窗口变矮或高 DPI 下高度会小于文字行高，
        // 导致文字被压扁/截断。给行与按钮同时设该下限，空间不足时由外层 ScrollViewer 滚动兜底。
        private const double NAV_BUTTON_MIN_HEIGHT = 32;

        private void BuildSidebar()
        {
            _nav = new List<NavItem>
            {
                new NavItem { Key = "tweaks",    Title = "系统优化",   Icon = "⚙", Build = BuildTweaks },
                new NavItem { Key = "cleanup",   Title = "清理优化",   Icon = "🧹", Build = BuildCleanup },
                new NavItem { Key = "services",  Title = "服务优化",   Icon = "🛠", Build = BuildServices },
                new NavItem { Key = "appx",      Title = "Appx 商店", Icon = "🛒", Build = BuildAppx },
                new NavItem { Key = "appxraw",   Title = "Appx 管理", Icon = "📦", Build = BuildAppxRaw },
                new NavItem { Key = "commonsw",  Title = "常用软件",   Icon = "📦", Build = BuildCommonSoftware },
                new NavItem { Key = "security",  Title = "安全防护",   Icon = "🛡", Build = BuildSecurity },
                new NavItem { Key = "edge",      Title = "Edge 管理",  Icon = "🌐", Build = BuildEdge },
                new NavItem { Key = "privacy",   Title = "隐私设置",   Icon = "🔒", Build = BuildPrivacy },
                new NavItem { Key = "systools",  Title = "系统工具",   Icon = "🧰", Build = BuildSystemTools },
                new NavItem { Key = "memory",    Title = "内存工具",   Icon = "🧠", Build = BuildMemory },
                new NavItem { Key = "activation",Title = "激活工具", Icon = "🔑", Build = BuildActivation },
                new NavItem { Key = "sysinfo",   Title = "系统信息",   Icon = "ℹ", Build = BuildSystemInfo },
                new NavItem { Key = "maint",     Title = "维护工具",   Icon = "🔧", Build = BuildMaintenanceTools },
                new NavItem { Key = "driverstore", Title = "驱动清理", Icon = "🗑", Build = BuildDriverStore },
                new NavItem { Key = "config",    Title = "配置管理",   Icon = "⚙", Build = BuildConfig },
                // 隐藏页：不占用侧边栏列表，由底部品牌区（图标 + 版本号）点击进入
                new NavItem { Key = "about",     Title = "关于",       Icon = "©", Build = BuildAbout, Hidden = true },
            };

            // 用 DockPanel 让底部 footer 始终贴底，中间内容自然撑满
            var dock = new DockPanel();

            // ---- 底部 footer：图标 + 版本号（先 Dock=Bottom 再 add，最后元素填满剩余）----
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(2, 6, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            try
            {
                var icon = new Image
                {
                    Source = new BitmapImage(new Uri("pack://application:,,,/系统清理与优化工具;component/brush.png", UriKind.Absolute)),
                    Width = 32,
                    Height = 32,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    SnapsToDevicePixels = true
                };
                // 源图 200×200，32×32 显示为缩小（清晰，无放大模糊）
                RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
                // hover 反馈统一提升到外层 Border（整块可点击），此处不再单独处理，
                // 避免"图标变淡但点击无响应"的暗示落空。
                footer.Children.Add(icon);
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); 
                // 图标加载失败时用 emoji 占位
                footer.Children.Add(new TextBlock { Text = "🎨", FontSize = 24, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            }
            // 左下角版本+关于入口：文字颜色随主题切换，统一由 UpdateSidebarTitleColors 刷新
            var verTb = new TextBlock
            {
                // 修正：原注释写 APP_VERSION = "v1.07" → "关于 V1.06"（版本号既与实际常量不符，
                // 箭头两侧也自相矛盾）。实际常量为 "v1.16"，Substring(1) 去掉前缀 'v' 后得 "关于 V1.16"；
                // 常量升级时此处自动跟随，无需改注释里的具体数字。
                Text = "关于 V" + APP_VERSION.Substring(1), // APP_VERSION = "v1.16" → "关于 V1.16"
                FontSize = 12,
                Foreground = _textMain,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                Name = "FooterVersionLabel"
            };
            footer.Children.Add(verTb);

            // 整块品牌区作为「关于」页入口：图标原本 hover 变淡却无点击响应，此处补齐交互闭环
            _aboutEntry = new Border
            {
                Child = footer,
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(6, 0, 8, 6),
                Cursor = Cursors.Hand
            };
            _aboutEntry.MouseEnter += (s, e) =>
            {
                if (_activeNavKey != "about") _aboutEntry.Background = _rowHover;
            };
            _aboutEntry.MouseLeave += (s, e) =>
            {
                if (_activeNavKey != "about") _aboutEntry.Background = Brushes.Transparent;
            };
            _aboutEntry.MouseLeftButtonUp += (s, e) => Navigate("about");

            DockPanel.SetDock(_aboutEntry, Dock.Bottom);
            dock.Children.Add(_aboutEntry);

            // ---- 主体：标题 + 副标题（Dock=Top 固定，不随滚动） + 滚动按钮区（ScrollViewer 兜底矮窗口）----
            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            // 按钮区用 Grid + Star 行：所有按钮按可用空间平均分配高度，标准窗口下无空白无滚动
            var sp = new Grid { Margin = new Thickness(14, 6, 12, 8) };
            int btnRow = 0;

            // 侧边栏头部标题（放 DockPanel 顶部，按钮区滚动时标题固定不动）
            var titleTb = new TextBlock { Text = "系统清理与优化", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = _textMain, Margin = new Thickness(14, 14, 0, 2), Name = "SidebarTitle" };
            var subtitleTb = new TextBlock { Text = "WPF 版 · 既全又可回退", FontSize = 11, Foreground = _accent, Opacity = 0.9, Margin = new Thickness(14, 0, 0, 8), Name = "SidebarSubtitle" };
            dock.Children.Add(titleTb);
            DockPanel.SetDock(titleTb, Dock.Top);
            dock.Children.Add(subtitleTb);
            DockPanel.SetDock(subtitleTb, Dock.Top);

            foreach (var n in _nav)
            {
                if (n.Hidden) continue;   // 隐藏页（关于）不占列表，由底部品牌区进入
                // 行也要设 MinHeight：Star 行只按可用空间均分，子控件的 MinHeight 不会反向撑开行，
                // 只有 RowDefinition.MinHeight 才会被 Grid 的 Star 解析算法尊重（不足时整块溢出→滚动）。
                sp.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = NAV_BUTTON_MIN_HEIGHT });
                var b = new Button
                {
                    // 文字用 TextBlock 承载并开启省略号：窄侧边栏下图标+标题放不下时省略而非硬裁剪。
                    // （Button 的 Foreground 由 Navigate 设置，TextBlock 走属性值继承，不受影响）
                    Content = new TextBlock
                    {
                        Text = n.Icon + "  " + n.Title,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    MinHeight = NAV_BUTTON_MIN_HEIGHT,   // 兜底：按钮自身不允许被压到文字行高以下
                    Background = Brushes.Transparent,
                    Foreground = _textDim,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(2, 0, 2, 0),
                    Padding = new Thickness(8, 0, 8, 0),
                    FontSize = 13,
                    BorderThickness = new Thickness(0),
                    Tag = n.Key
                };
                b.Click += (s, e) => Navigate(n.Key);
                // 悬浮高亮（仅未选中项），与列表行悬浮同色，保持交互一致
                b.MouseEnter += (s, e) =>
                {
                    if ((b.Tag as string) != _activeNavKey) b.Background = _rowHover;
                };
                b.MouseLeave += (s, e) =>
                {
                    if ((b.Tag as string) != _activeNavKey) b.Background = Brushes.Transparent;
                };
                Grid.SetRow(b, btnRow++);
                sp.Children.Add(b);
            }

            scroller.Content = sp;
            // scroller 后加 → 自动填充 DockPanel 剩余空间（标题固定顶部，按钮区矮窗可滚动，footer 贴底）
            dock.Children.Add(scroller);

            // 已迁移至 XAML 全局 Button Style，代码中不再需要 FrameworkElementFactory。
            Sidebar.Child = dock;
        }

        private void Navigate(string key)
        {
            var n = _nav.FirstOrDefault(x => x.Key == key);
            if (n == null) return;
            _activeNavKey = key;

            // 复位最外侧滚动控制：驱动清理页会临时关闭 ContentArea 纵向滚动（改为 DataGrid/日志各自滚动），
            // 每次导航先恢复为 Auto，确保其它页面不被误关。
            ContentArea.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            PageTitle.Text = n.Title;
            // 先清空旧内容再设新的，避免 WPF 视觉树残留导致页面重叠
            ContentArea.Content = null;
            ContentArea.UpdateLayout();

            // 统一包装：响应式拉伸（最大化时撑满 + 超宽屏 MaxWidth=1400 居中）
            // 改为走 BuildPageSafe —— 页面构建（Build + SetPageContent）全程包异常兜底，
            // 任一页出错都只在内容区显示错误卡片，不让异常逸出到 Dispatcher。
            BuildPageSafe(n);

            // 驱动清理页采用"各自独立滚动"（DataGrid + 日志框自带滚动），关闭最外层纵向滚动。
            // 放在 Build() 之后设置，避免预加载 BuildDriverStore() 时影响当前显示的其它页面。
            if (key == "driverstore")
            {
                ContentArea.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                // 每次进入都后台刷新驱动列表（进入即用已加载数据，同时后台拉取最新）
                _driverStorePanel?.Refresh();
            }

            ContentArea.ScrollToTop();

            // 递归遍历侧边栏视觉树，找到所有导航按钮（Sidebar.Child 是 DockPanel，按钮嵌套在内部 StackPanel，必须递归）
            if (Sidebar.Child is Panel p)
            {
                foreach (var bb in FindNavButtons(p))
                {
                    bool sel = (bb.Tag as string) == key;
                    // 激活填充：复用主题的 _accent（实色 primary 青绿）——与"恢复更新"等 primary 按钮完全一致
                    bb.Background = sel ? _accent : Brushes.Transparent;
                    // 激活文字用黑/白（取决于主题）——保证在实色青绿背景上有对比度
                    bb.Foreground = sel
                        ? (_isDarkMode ? Brushes.Black : Brushes.White)
                        : _textMain;
                }
            }

            // 底部品牌区（关于入口）同步选中态——与主导航按钮点击态保持一致
            if (_aboutEntry != null)
                _aboutEntry.Background = key == "about" ? _accent : Brushes.Transparent;
        }

        /// <summary>
        /// 统一页面内容设置：确保最大化窗口时内容正确填充宽度。
        /// 策略：直接将页面内容放入 ScrollViewer，设 HorizontalAlignment=Stretch + MaxWidth 上限。
        /// 不再用 Grid 双层包装（会导致内部 Grid 的 Star 行在 Auto 父行中塌缩）。
        /// </summary>
        /// <summary>
        /// 统一页面内容设置。
        /// 水平拉伸由 MainWindow.SizeChanged 动态控制 ContentArea.HorizontalScrollBarVisibility：
        ///   - 大窗/最大化时 H=Disabled → ScrollViewer 传有限宽度 → 内容自然填满
        ///   - 默认窗口时 H=Auto   → 保持原始行为（StackPanel 按内容自然宽度）
        /// 纵向滚动由 ScrollViewer 默认测量保证（传 ∞ 高度）。
        /// 所有赋值 ContentArea.Content 的地方都应走此方法（含异步回调刷新）。
        /// </summary>
        private void SetPageContent(UIElement pageContent)
        {
            // 背景透明（让窗口背景图透出来）
            if (pageContent is Panel pp && pp.Background == null)
                pp.Background = Brushes.Transparent;

            // 直接赋值——零包装、不改属性、不设 MaxWidth/Center
            // 让 WPF 原生布局链工作。页面根高度约束由各 Build* 通过 BindRootHeightToViewport
            // 自行绑定到 ContentArea.ActualHeight（只读 DP 自动跟随首帧+缩放，无 vp=0 时序 bug）。
            // 注意：此处不再统一设置 MaxHeight——否则会给滚动型页面（系统优化/清理等）误加高度锁，
            // 导致窗口缩放后内容无法滚动被裁剪。
            ContentArea.Content = pageContent;
        }

        /// <summary>
        /// 页面构建的统一异常兜底：只包「Build() + SetPageContent」这一环。
        /// 为什么必须兜：Navigate 由侧边栏按钮 Click、底部品牌区 MouseUp、主题切换等事件处理器调用，
        /// 而 net48 下事件处理器里未捕获的异常会一路逸出到 Dispatcher，直接终止进程
        /// —— 用户看到的就是「点一下导航就闪退」或「页面一片空白」，既没有提示也没有日志，无从排查。
        /// 兜住之后最坏情况只是这一页显示成一张错误卡片，导航高亮等其余逻辑照旧执行（不 return）。
        /// 主题切换、导航切走再切回都会重建页面，所以本兜底对每一页都生效，不局限于维护工具页。
        /// </summary>
        private void BuildPageSafe(NavItem n)
        {
            try
            {
                SetPageContent(n.Build());
            }
            catch (Exception ex)
            {
                DebugLog.Ignore(ex);
                // 兜底的兜底：构造错误卡片本身也依赖主题画刷（_bgCard/_accent/…），
                // 极端时序下（如首次导航早于 ApplyTheme）这些字段可能还未初始化而再次抛异常。
                // 这里必须再套一层，最差情况只记录后返回——绝不能让异常二次逃逸到 Dispatcher。
                try
                {
                    SetPageContent(BuildPageErrorCard(n.Title, ex, n.Key));
                }
                catch (Exception ex2)
                {
                    DebugLog.Ignore(ex2);
                }
            }
        }

        /// <summary>
        /// 构造「页面加载失败」的错误展示卡片（配色跟随当前主题，与本程序其它卡片视觉一致）。
        /// 内容：一句人话提示 + 异常类型与 Message + 可折叠/可横向滚动的完整调用栈 + 「重试」按钮。
        /// 调用栈做成只读 TextBox 而不是 TextBlock，是为了让用户能整段选中复制给我们排查。
        /// </summary>
        private UIElement BuildPageErrorCard(string pageTitle, Exception ex, string navKey)
        {
            var root = new StackPanel { Margin = new Thickness(0) };

            var card = new Border
            {
                Background = _isDarkMode ? Brushes.Transparent : _bgCard,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var inner = new StackPanel();
            card.Child = inner;

            inner.Children.Add(new TextBlock
            {
                Text = "加载「" + (pageTitle ?? "当前") + "」页面时出错",
                FontWeight = FontWeights.Bold,
                Foreground = _accent,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 8)
            });

            inner.Children.Add(new TextBlock
            {
                Text = "程序仍在正常运行，其它功能不受影响。可点「重试」重新加载本页；若反复出现，请把下面的错误信息复制给我们排查。",
                Foreground = _textDim,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            // 异常类型 + Message：可能很长，允许换行以保证完整可见
            var msgText = (ex?.GetType().FullName ?? "未知异常类型") + "：" + (ex?.Message ?? "(无异常消息)");
            inner.Children.Add(new TextBlock
            {
                Text = msgText,
                Foreground = _warnOrange,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            // 调用栈：默认收起（Expander），展开后是只读 TextBox。
            // TextWrapping=NoWrap + 横向滚动条，保证堆栈的每行缩进不被折行打乱、便于整段复制。
            var stackBox = new TextBox
            {
                IsReadOnly = true,
                Text = ex?.ToString() ?? "",
                FontFamily = new FontFamily("Consolas, 'Courier New'"),
                FontSize = 11,
                MaxHeight = 260,
                Padding = new Thickness(6, 4, 6, 4),
                Background = _isDarkMode ? Brushes.Transparent : _bgDeep,
                Foreground = _textMain,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            inner.Children.Add(new Expander
            {
                Header = "查看详细调用栈（可复制）",
                Content = stackBox,
                Foreground = _textMain,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };

            // 「重试」：重新走一次完整的 Navigate（会重建本页），而不是重试单个 Build。
            var retryBtn = Btn("重试", true, null, 100);
            retryBtn.Click += (s, e) =>
            {
                try { Navigate(navKey); }
                catch (Exception ex3) { DebugLog.Ignore(ex3); }
            };
            btnRow.Children.Add(retryBtn);

            // 「复制错误信息」：省去用户手动选中长堆栈的麻烦，一眼可粘贴给开发者。
            // async void 处理器内的异常不会抛给调用方而是直达 Dispatcher（net48 会终止进程），故整体包 try/catch。
            var copyBtn = Btn("复制错误信息", false, null, 120);
            copyBtn.Click += async (s, e) =>
            {
                try
                {
                    copyBtn.IsEnabled = false;
                    try
                    {
                        SetStatus(await TrySetClipboardTextAsync(msgText + Environment.NewLine + (ex?.ToString() ?? ""))
                            ? "错误信息已复制到剪贴板"
                            : "复制失败: 剪贴板被占用，请稍后重试");
                    }
                    finally { copyBtn.IsEnabled = true; }
                }
                catch (Exception ex4)
                {
                    DebugLog.Ignore(ex4);
                    try { copyBtn.IsEnabled = true; SetStatus("复制失败: " + ex4.Message); }
                    catch { /* 窗口已关闭，忽略 */ }
                }
            };
            btnRow.Children.Add(copyBtn);

            inner.Children.Add(btnRow);
            root.Children.Add(card);
            return root;
        }

        /// <summary>递归收集侧边栏视觉树中的所有 Button（DockPanel 嵌套 StackPanel 结构）</summary>
        private static IEnumerable<Button> FindNavButtons(DependencyObject node)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
                if (child is Button b) yield return b;
                foreach (var sub in FindNavButtons(child)) yield return sub;
            }
        }

        internal void SetStatus(string s) => StatusText.Text = s;

        // ---- 右边缘拖拽事件（侧边栏宽度调整，替代 GridSplitter）----
        private void Dragger_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSidebar = true;
            _dragStartX = e.GetPosition(this).X;
            _dragStartWidth = SidebarCol.Width.Value > 0 ? SidebarCol.Width.Value : SidebarCol.ActualWidth;
            SidebarDragger.CaptureMouse();
            e.Handled = true;
        }

        private void Dragger_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingSidebar) return;
            var newWidth = Math.Max(180, Math.Min(420, _dragStartWidth + (e.GetPosition(this).X - _dragStartX)));
            SidebarCol.Width = new GridLength(newWidth);
            SidebarDragger.Background = _accent;
        }

        private void Dragger_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSidebar = false;
            SidebarDragger.Background = Brushes.Transparent;
            SidebarDragger.ReleaseMouseCapture();
            e.Handled = true;
        }
    }
}

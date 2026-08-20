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
        // ⚠ 升版时须同步修改 CpqSystemTool.csproj 的 AssemblyVersion / FileVersion / InformationalVersion（当前 1.0.13.0 ↔ v1.13），两处保持一致。
        private const string APP_VERSION = "v1.13";

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
                Text = "关于 V" + APP_VERSION.Substring(1), // APP_VERSION = "v1.07" → "关于 V1.06"
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

            // ---- 主体：标题 + 副标题 + 13 按钮（StackPanel 从上往下排）----
            var sp = new StackPanel { Margin = new Thickness(14, 16, 12, 8) };

            // 侧边栏头部标题
            var titleTb = new TextBlock { Text = "系统清理与优化", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = _textMain, Margin = new Thickness(2, 0, 0, 2), Name = "SidebarTitle" };
            var subtitleTb = new TextBlock { Text = "WPF 版 · 既全又可回退", FontSize = 11, Foreground = _accent, Opacity = 0.9, Margin = new Thickness(2, 0, 0, 14), Name = "SidebarSubtitle" };
            sp.Children.Add(titleTb);
            sp.Children.Add(subtitleTb);

            foreach (var n in _nav)
            {
                if (n.Hidden) continue;   // 隐藏页（关于）不占列表，由底部品牌区进入
                var b = new Button
                {
                    Content = n.Icon + "  " + n.Title,
                    Background = Brushes.Transparent,
                    Foreground = _textDim,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Cursor = Cursors.Hand,
                    Height = 36,
                    Margin = new Thickness(0, 1, 0, 1),
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
                sp.Children.Add(b);
            }

            // sp 后加 → 自动填充 DockPanel 剩余空间（按钮区紧贴上方，footer 紧贴底部）
            dock.Children.Add(sp);

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
            SetPageContent(n.Build());

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

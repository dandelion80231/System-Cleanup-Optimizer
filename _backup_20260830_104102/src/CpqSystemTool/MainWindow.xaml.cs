using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace CpqSystemTool
{
    public partial class MainWindow : Window
    {
        // ---- Theme-aware brushes (mutable for light/dark switching) ----
        // 设为 internal，供同程序集内的子对话框（OtherTweaksDialog / RemotePortDialog）复用，保证整体配色统一。
        internal SolidColorBrush _accent;
        internal SolidColorBrush _textMain;
        internal SolidColorBrush _textDim;
        internal SolidColorBrush _panelBorder;
        internal SolidColorBrush _bgCard;
        internal SolidColorBrush _successGreen;
        internal SolidColorBrush _dangerRed;
        // 危急操作描边（强制删除等）：深于 _dangerRed，用于在亮色危险按钮上形成可见描边对比，避免页面内硬编码魔法色
        internal SolidColorBrush _dangerDark;
        internal SolidColorBrush _warnOrange;
        // 主题感知辅助字段（避免页面中硬编码深色）
        internal SolidColorBrush _bgDeep;          // 深一层（日志框/状态区/侧栏底栏）
        internal SolidColorBrush _bgTable;         // 表内次级区（Appx 商店功能选项 Card 等）
        internal SolidColorBrush _bgTableHead;     // 表头
        internal SolidColorBrush _rowSelected;     // 选中行背景（浅色=浅天蓝 / 深色=深青蓝）
        internal SolidColorBrush _rowHover;        // 悬停行背景
        // 主题感知辅助字段 2（统一色号，避免各页面自行硬编码导致前后不一）
        internal SolidColorBrush _installedBg;        // 已安装浅绿背景
        internal SolidColorBrush _installedBorder;    // 已安装浅绿边框
        internal SolidColorBrush _installedFg;        // 已安装文字色
        internal SolidColorBrush _notInstalledBg;     // 未安装浅红背景
        internal SolidColorBrush _notInstalledBorder; // 未安装浅红边框
        // ---- 统一 UI 派生笔刷（供按钮/输入框/子对话框沿用，确保配色完全一致）----
        internal SolidColorBrush _btnPrimaryFg;     // 主按钮文字（深/浅主题自适应对比）
        internal SolidColorBrush _btnSecondaryBg;   // 次按钮背景
        internal SolidColorBrush _btnSecondaryFg;   // 次按钮文字
        internal SolidColorBrush _windowBg;         // 窗口底色（子对话框沿用，保证一致）
        internal SolidColorBrush _inputBg;          // 输入框背景
        internal SolidColorBrush _inputFg;          // 输入框文字

        // ---- Theme state ----
        private bool _isDarkMode = true;
        /// <summary>当前是否深色主题（供背景设置弹窗选择 Dark/Light 图片路径预览）。</summary>
        internal bool IsDarkMode => _isDarkMode;
        private bool _userOverrodeTheme = false; // 用户是否手动切换过（手动后不再跟随系统）

        // ---- 自定义背景设置（运行时实例，用于弹窗编辑与实时预览）----
        internal BackgroundSettings _backgroundSettings = new BackgroundSettings();

        // ---- 右边缘拖拽状态（侧边栏宽度调整）----
        private bool _isDraggingSidebar = false;
        private double _dragStartX = 0, _dragStartWidth = 0;

        private class NavItem
        {
            public string Key, Title, Icon;
            public Func<UIElement> Build;
            /// <summary>true = 不在侧边栏列表渲染按钮，但仍可通过 Navigate(Key) 进入（如「关于」由底部品牌区进入）。</summary>
            public bool Hidden;
        }

        private List<NavItem> _nav;

        /// <summary>侧边栏底部品牌区（图标+版本号），兼作「关于」页入口，需在 Navigate 中同步选中态。</summary>
        private Border _aboutEntry;

        public MainWindow()
        {
            try
            {
                App.Trace("ctor.start");
                InitializeComponent();
                App.Trace("ctor.afterInitComp");
                SetDarkColors(); // 初始化深色主题笔刷（所有 Build* 方法依赖这些字段）
                App.Trace("ctor.afterSetDarkColors");

                // 加载自定义背景图设置（必须在 ApplyTheme/ApplyShellColors 之前）
                LoadBackgroundSettings();
                App.Trace("ctor.afterLoadBgSettings");

                // Mesh 光斑层跟随窗口 resize 重画（ActualWidth/Height 变化 → 按新尺寸重算像素位置）。
                // 改为 100ms 合并触发，避免拖窗口时每个 SizeChanged 都全量重建光斑导致卡顿。
                HookBgBlobsResizeMerge();

                // 启动时检测 Windows 系统主题（注册表 AppsUseLightTheme）
                _isDarkMode = !DetectSystemLightTheme();
                ApplyTheme(_isDarkMode);
                App.Trace("ctor.systemThemeDetected dark=" + _isDarkMode);
                // 外壳色统一按当前主题（_isDarkMode）强制一遍，避免 WPF 默认主题/DWM 覆盖
                ApplyShellColors();
                App.Trace("ctor.afterApplyShellColors");
                BuildSidebar(); // 轻量级：仅构建导航按钮，不读注册表
                App.Trace("ctor.afterBuildSidebar");
                // 窗口 Loaded 即同步导航（Loaded 已在首次渲染之后触发，无需再等 ContextIdle）。
                Loaded += (s, e) =>
                {
                    App.Trace("Loaded.start");
                    // 首次导航：构建内容页
                    Navigate("tweaks");
                    App.Trace("Loaded.afterNavigate");
                    // 后台预加载驱动清理页（构造即触发 Refresh() 枚举），用户切换时数据已就绪
                    PreloadDriverStore();
                    App.Trace("Loaded.afterPreloadDriverStore");
                    // ★★★ 导航完成后立即重刷全部外壳色（同步，确保首帧即正确）。
                    //   构造函数中的 ApplyShellColors 在 Navigate 之前执行，
                    //   WPF 渲染管道可能在 Navigate 重建内容时回退了部分外壳色。★★★
                    ApplyShellColors();
                    UpdateSidebarTitleColors();
                    App.Trace("Loaded.afterShellColors");
                    // 监听 Windows 系统主题变化（自动跟随，除非用户手动切换过）
                    HookSystemThemeChange();
                    App.Trace("Loaded.end");
                };
                // 修复鼠标滚轮穿透：让 ContentArea ScrollViewer 捕获所有子控件的滚轮事件
                // 优先跟随鼠标正下方的 ScrollViewer（包含 ComboBox Popup 等独立视觉树），找不到再回退到 ContentArea
                ContentArea.PreviewMouseWheel += (sender, e) =>
                {
                    var targetSv = ContentArea;
                    ScrollCandidate(out DependencyObject direct);
                    var directSv = FindVisualAncestor<ScrollViewer>(direct);
                    if (directSv != null && directSv != ContentArea && directSv.ScrollableHeight > 0)
                    {
                        targetSv = directSv;
                    }
                    else
                    {
                        DependencyObject hit = null;
                        try { var pos = e.GetPosition(ContentArea); hit = VisualTreeHelper.HitTest(ContentArea, pos)?.VisualHit; } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
                        var innerSv = FindVisualAncestor<ScrollViewer>(hit);
                        if (innerSv != null && innerSv != ContentArea && innerSv.ScrollableHeight > 0)
                            targetSv = innerSv;
                    }

                    if (targetSv != null && targetSv != ContentArea)
                    {
                        // 鼠标在可滚动子控件内（更新日志 / DataGrid / 日志框 / 规则列表等）：
                        // 只有它还能朝当前方向滚动时才在此处消费事件；已滚到尽头则【不设 Handled】，
                        // 让事件沿内置冒泡链继续上抛——内层 ScrollViewer.OnMouseWheel 因无法滚动而不会置 Handled，
                        // 外层 ContentArea 随即接手滚动。旧实现无条件 e.Handled = true 会吞掉这次冒泡，
                        // 造成"子区域滚到底后整页卡死、再也滚不动"。
                        const double EDGE_EPS = 0.5;   // 容差：避免亚像素抖动导致边界处来回传递
                        bool atEnd = e.Delta > 0
                            ? targetSv.VerticalOffset <= EDGE_EPS
                            : targetSv.VerticalOffset >= targetSv.ScrollableHeight - EDGE_EPS;
                        // Popup 等独立视觉树内的滚动区不在此冒泡链上（事件到不了 ContentArea），
                        // 只有确实是 ContentArea 后代时才敢放行，否则仍由本处理器手动滚动外层。
                        if (atEnd && IsDescendantOfContentArea(targetSv)) return;
                    }
                    if (targetSv == null) targetSv = ContentArea;

                    // 按目标 ScrollViewer 的滚动模式选择步进：
                    //  - 物理滚动（CanContentScroll=false，如 ContentArea/普通 ScrollViewer）：沿用 ~40px/格，平滑。
                    //  - 逻辑滚动（CanContentScroll=true，如 DataGrid 内部 ScrollViewer）：按"行"步进（默认 3 行/格），
                    //    避免把 e.Delta/3 当作"逻辑单位"导致一次跳几十行（用户反馈"切换太快 / 滚动行数过大"）。
                    double step = targetSv.CanContentScroll
                        ? Math.Sign(e.Delta) * (double)SystemParameters.WheelScrollLines
                        : e.Delta / 3.0;
                    var offset = targetSv.VerticalOffset - step;
                    if (offset < 0) offset = 0;
                    if (offset > targetSv.ScrollableHeight) offset = targetSv.ScrollableHeight;
                    targetSv.ScrollToVerticalOffset(offset);
                    e.Handled = true;
                };

                // 获取鼠标正下方的元素；Popup 等独立视觉树不在 ContentArea 的 HitTest 范围内，需用 Mouse.DirectlyOver
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                void ScrollCandidate(out DependencyObject element)
                {
                    element = Mouse.DirectlyOver as DependencyObject;
                }

                // 判断元素是否位于 ContentArea 视觉树内（决定"子滚动区到底后能否交给外层继续滚"）：
                // 只有在其内部，未处理的 MouseWheel 才会沿冒泡链到达 ContentArea；Popup 等独立 HWND 树到不了。
                bool IsDescendantOfContentArea(DependencyObject node)
                {
                    for (var cur = node; cur != null; cur = VisualTreeHelper.GetParent(cur))
                    {
                        if (ReferenceEquals(cur, ContentArea)) return true;
                    }
                    return false;
                }

                // ★ ContentArea 横向滚动必须保持 Disabled（曾尝试改 Auto，已验证会引入严重回归，故回退）。
                //
                //   为什么不能用 Auto：
                //   ScrollViewer 会把 CanHorizontallyScroll 设为 (H != Disabled)，于是
                //   ScrollContentPresenter.MeasureOverride 以「无限宽」度量子内容；而 Grid.MeasureOverride
                //   在无限宽下【不解析 Star 列】，Star 会退化成内容宽度，只有 Arrange 阶段才按最终宽度重分配。
                //   结果：只要页面内有任一 TextWrapping 长文本（如「关于」页约 230 中字的简介，不换行约
                //   2800px ≫ 视口 ~959px），整页就会按这个超宽尺寸排列 —— 长文本不换行、Star 列不再等分
                //   视口、底部常驻横条。即项目长期记忆里那条铁律："ScrollViewer 内 Star 列居中须
                //   HorizontalScrollBarVisibility=Disabled"，Disabled 正是"固定视口宽"的唯一保证。
                //
                //   已知的取舍：窄窗/高 DPI 下「系统信息」两列（各 MinWidth=180）与 Edge 两列卡片会被裁剪且
                //   无法横滚。要修应在**具体页面**外层单独包 ScrollViewer（页面级可控），而不是放开全局横滚。
                ContentArea.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

                // 页面根高度跟随视口：改由各 Build* 通过 BindRootHeightToViewport 绑定 ContentArea.ActualHeight
                // （只读 DP，尺寸变化时自动通知），不再需要此手动 SizeChanged 处理——
                // 手动方案会在首帧 vp=0 跳过、且缩放后状态陈旧导致"恢复默认尺寸后内容漂移"。
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动失败：\n\n" + ex.GetType().FullName + ": " + ex.Message + "\n\n" + ex.StackTrace,
                    "系统清理与优化工具", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }

        // ---- 当前选中的导航 key（用于主题切换后恢复）----
        private string _activeNavKey = "tweaks";

        // ---- 更新管理页：最后点击的操作按钮标识（用于操作后高亮反馈）----
        private string _lastUpdateAction = null;

        /// <summary>右上角「自定义背景」按钮：打开背景设置对话框。</summary>
        private void BgSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new BackgroundSettingsDialog(this, _backgroundSettings?.Clone() ?? new BackgroundSettings());
                dlg.Owner = this;
                if (dlg.ShowDialog() == true)
                {
                    ApplyBackgroundSettings(dlg.ResultSettings);
                    SaveBackgroundSettings();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开背景设置失败：\n" + ex.Message + "\n\n" + ex.StackTrace, "系统清理与优化工具", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ---- 所有页面 Build 方法迁移至 MainWindow.Pages.cs ----
    }
}

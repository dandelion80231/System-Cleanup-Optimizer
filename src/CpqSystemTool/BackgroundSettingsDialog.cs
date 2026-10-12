using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CpqSystemTool
{
    /// <summary>
    /// 自定义背景设置对话框：支持纯色、线性渐变、径向渐变、网格渐变，参考 gradients.app 色轮与多色叠加风格。
    /// </summary>
    public class BackgroundSettingsDialog : Window
    {
        private readonly MainWindow _owner;
        private BackgroundSettings _settings;
        private readonly BackgroundSettings _initial;

        // UI 元素
        private ComboBox _modeCombo;
        private Border _previewHost;
        private Rectangle _previewRect;
        private Canvas _blobCanvas;            // 网格渐变：光斑渲染层（真实像素 Ellipse + RadialGradientBrush 0→α 柔边堆叠，与主窗口同源）
        private Canvas _previewOverlay;        // 网格渐变：预览区上的光斑拖拽句柄层
        private Border _wheelHost;
        private Image _wheelImage;
        private TextBox _hexBox;
        private Slider _rSlider, _gSlider, _bSlider;
        private TextBox _rVal, _gVal, _bVal;   // 数值可点击直接键入修改（回车/失焦提交，解析+钳制）
        // 双列布局：HSV 列（H 色相 / S 饱和 / V 明度），与 RGB 列并排、互相实时联动
        private Slider _hSlider, _sSlider;
        private TextBox _hVal, _sVal;   // 同 R/G/B：数值可点击直接键入修改
        private StackPanel _stopPanel;
        private StackPanel _blobPanel;
        private TextBox _angleBox;
        private TextBox _centerXBox, _centerYBox, _radiusXBox, _radiusYBox;
        private StackPanel _angleGroup;   // 几何行「角度: [45]°」组（仅线性模式显示；径向不需要角度）
        private Button _addStopBtn, _addBlobBtn;
        private Button _applyBtn, _saveBtn, _cancelBtn;
        private TextBlock _modeHint;
        private Border _geometryCard;
        private Border _listCard;
        private Border _imageCard;        // 图片模式背景控制卡片（仅 Image 模式可见）
        private ColumnDefinition _previewColumn; // 内容区右侧预览列（图片模式下折叠，左列展开显示双预览）

        // 功能1：正在编辑标签 + 功能2/3 的卡片与控件
        private TextBlock _editLabel;
        private Border _harmonyCard;
        private ComboBox _harmonyCombo;
        private Button _applyHarmonyGradBtn, _applyHarmonyMeshBtn;
        private StackPanel _harmonySwatches;
        private Border _cssCard;
        private Button _randMeshBtn, _exportSvgBtn;   // P1c：网格专属按钮（非网格模式隐藏）
        // 图片模式控件（方案 A：左列深色/浅色选择 + 独立透明度；右列随主题的大预览）
        private Slider _darkOpacitySlider;
        private Slider _lightOpacitySlider;
        private TextBlock _darkOpacityVal;
        private TextBlock _lightOpacityVal;
        private Image _imgBigPreview;        // 右列大预览：按「深浅预览切换行」所选套显示深色/浅色背景图
        private TextBlock _imgBigPlaceholder;
        private TextBox _cssBox;
        private TextBlock _cssStatus;

        // 网格底色行（并入颜色卡 HEX 行下方；预设统一走「预设色板」卡片）
        private StackPanel _meshBaseRow;
        private Border _baseSwatch;
        private TextBox _baseHexBox;
        private bool _editingMeshBase;   // 网格模式编辑目标：true=网格底色（色轮/HEX/滑块直接写底色），false=选中的光斑（默认）
        private TextBlock _baseEditHint;   // 底色编辑提示行（仅编辑底色时可见）

        // 色阶生成器（Shades / Tints / Tones）
        private Border _sttCard;
        private WrapPanel _shadesPanel, _tintsPanel, _tonesPanel;

        // 预设色板库卡片（12 套精选配色，点击应用到当前模式；对齐参考站 online-color.com 的调色板库）
        private Border _paletteCard;
        // 色轮上和色标记层（和谐规则的小圆点，叠加在轮盘上；对齐参考站色轮上的和色点）
        private Canvas _harmonyOverlay;
        // 「深浅背景切换」：非图片模式下，预览/编辑的当前套（true=深色套，false=浅色套）
        private bool _previewDark = true;
        private Button _previewThemeBtn;   // 深浅切换单按钮（非图片模式；🌙/☀️ 同主窗口右上角，共用 _owner.ToggleTheme）
        private Button _imgThemeToggleBtn;  // 深浅切换单按钮（图片模式；同上）
        private StackPanel _dlToggleRow;   // 深浅切换行（非图片模式可见）
        private Button _dlSyncBtn;             // 同步深色套→浅色套 按钮

        // 当前编辑态
        private GradientStopSetting _selectedStop;
        private MeshBlobSetting _selectedBlob;
        private GradientStopSetting _lastSelectedStop;
        private MeshBlobSetting _lastSelectedBlob;
        private System.Windows.Threading.DispatcherTimer _harmonyFeedbackTimer;
        private Color _currentColor = Color.FromRgb(0x16, 0xE0, 0xBD);
        private double _currentHue;            // HSV 色相（色轮取色用）
        private double _currentSat;            // HSV 饱和度
        private double _currentValue = 0.95;   // HSV 明度（0..1），由 V 滑块控制当前颜色明暗
        // 色轮恒亮：轮盘像素恒以满明度渲染（对齐参考站 H/S 轮机制），明暗只由 V 滑块决定。
        // 旧实现用 _currentValue 渲染轮盘，当前色很暗时整个色轮变纯黑、无法取色。
        private const double WheelBrightness = 1.0;
        private bool _isUpdating;
        private bool _isInitializing;          // 防止初始化时 SelectionChanged 重入导致 UI 树抖动
        // 关闭时撤销用：_hasEditedAfterShow=用户曾编辑；_hasAppliedAtLeastOnce=曾点过"应用"；_appliedSnapshot=最近一次 Apply 时的 _settings 快照
        private bool _hasEditedAfterShow;
        private bool _hasAppliedAtLeastOnce;
        private BackgroundSettings _appliedSnapshot;
        private Slider _valSlider;             // 明度滑块（HSV 列，0–100% 量程）
        private TextBox _valVal;              // 明度数值（可点击直接键入修改）
        private Border _colorCard;             // 颜色选择区外层卡片（用于整体显隐）
        private Canvas _wheelOverlay;          // 色轮上的当前颜色指示器容器
        private Ellipse _wheelIndicator;       // 当前颜色在色轮上的位置指示器

        // 色轮渲染缓存（性能优化）：复用同一个 WriteableBitmap 与其像素数组，避免每次重绘都
        // new 一个 180×180 的位图 + 129,600 字节数组（原实现每次重绘都全量重新分配）。
        private WriteableBitmap _wheelBitmap;
        private byte[] _wheelPixels;
        private int _wheelBmpW, _wheelBmpH;
        // 同一帧内的重绘请求合并标志：拖明度/取色一帧内可能触发多次 RenderColorWheel，
        // 用 BeginInvoke + 标志位把多次请求压缩成一次真实渲染。
        private bool _wheelRenderScheduled;
        // 已经渲染到 _wheelBitmap 的明度值。色轮恒亮（像素 = f(角度→色相, 半径→饱和度), 明度恒 WheelBrightness=1.0），
        // 与当前选取完全无关 → 首绘一次后永久复用，取色/改 H/S/V 都只移动指示器（UpdateWheelIndicator），零像素重算。
        private double _wheelRenderedValue = double.NaN;

        // 增量维护停靠点/光斑列表的 UI 映射，避免每次改颜色都重建整个列表导致滑块焦点丢失
        private readonly Dictionary<GradientStopSetting, StackPanel> _stopRows = new Dictionary<GradientStopSetting, StackPanel>();
        private readonly Dictionary<MeshBlobSetting, StackPanel> _blobRows = new Dictionary<MeshBlobSetting, StackPanel>();
        private readonly Dictionary<MeshBlobSetting, Ellipse> _blobHandles = new Dictionary<MeshBlobSetting, Ellipse>(); // 预览区光斑句柄
        // 每模式最近一次的几何参数：模式切换时把当前值存回旧 mode、切回时从字典读回，避免 Linear 90° 切 Radial 后 90° 丢失
        private readonly Dictionary<BackgroundMode, double> _angleByMode = new Dictionary<BackgroundMode, double>();
        private readonly Dictionary<BackgroundMode, double> _centerXByMode = new Dictionary<BackgroundMode, double>();
        private readonly Dictionary<BackgroundMode, double> _centerYByMode = new Dictionary<BackgroundMode, double>();
        private MeshBlobSetting _draggingBlob;
        private Point _dragStartMouse;          // 拖拽开始时的鼠标位置（相对 previewOverlay）
        private Point _dragStartBlobCenter;     // 拖拽开始时光斑中心（0..1）
        // 预览区交互（线性/径向几何与停靠点拖拽）
        private GradientStopSetting _draggingStop;
        private bool _draggingLineEnd;        // 拖线性两端圆点 = 绕中心旋转角度
        private bool _draggingLineCenter;     // 拖线性轴线本体 = 平移渐变中心
        private Point _dragStartLineCenter;   // 拖中心开始时的中心坐标（0..1）
        private bool _draggingRadialCenter;
        private bool _draggingRadiusX;
        private bool _draggingRadiusY;
        private double _linearAngleRad;
        private readonly List<System.Windows.UIElement> _gradientOverlayItems = new List<System.Windows.UIElement>();
        private const double K45 = 0.70710678;  // √½，径向 45° 半径方向投影系数
        private const int MeshDefaultBlobCount = 9;   // 网格模式默认补齐到的光斑数量，保证首屏是多光斑效果
        // HEX 颜色输入校验：#RGB / #RRGGBB / 8 位，'#' 可省略（底色输入框用，防止中间态输入被误解析）
        private static readonly Regex HexColorPattern = new Regex(
            @"^#?(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);
        private Grid _rowRgb, _rowHsl, _rowHsv, _rowCmyk;   // 颜色格式只读显示块（标签+文本框+复制按钮，单行）
        private TextBlock _contrastText;        // 对比度检查提示

        public BackgroundSettings ResultSettings => _settings;

        // 随机光斑用的类级单例 Random：避免连续点击"随机光斑"按钮时秒级同种子产生相同序列。
        private static readonly Random _rng = new Random();

        public BackgroundSettingsDialog(MainWindow owner, BackgroundSettings settings)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _initial = settings?.Clone() ?? new BackgroundSettings();
            _settings = _initial.Clone();

            Title = "自定义背景";
            Width = 900;
            Height = 560;
            MinWidth = 830;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // 使用标准单边框窗口，确保标题栏×关闭按钮在所有主题下都稳定响应
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            // 【P3-15】本弹窗故意不用 DialogChrome.ApplyStandard：可缩放（CanResize）+ 保留任务栏常驻（大窗口便于与主窗并排调参），
            // 与其余固定尺寸弹窗不同属预期差异，非漏配。
            Background = owner._windowBg;
            Foreground = owner._textMain;
            FontFamily = new FontFamily("Microsoft YaHei");

            // 关闭窗口时（包括点标题栏×）：根据是否编辑过/应用过决定回滚目标，避免吞掉用户已"应用"的预览
            Closing += (s, e) =>
            {
                if (DialogResult == true) return;                              // 点确定保存退出，不动 owner
                if (_hasAppliedAtLeastOnce && _hasEditedAfterShow)            // 应用后又编辑：回到最近一次 Apply 的快照
                    _owner.ApplyBackgroundSettings(_appliedSnapshot?.Clone() ?? _initial.Clone());
                else                                                            // 未应用 / 应用后未再编辑：回到打开前
                    _owner.ApplyBackgroundSettings(_initial.Clone());
            };

            BuildUi();
            SyncUiToSettings();
            PromoteActiveSet();   // 「分别编辑浅色套」：打开时活动套=浅色且未同步过 → 克隆深色套内容，编辑落在浅色套
            RefreshStopList();
            RefreshBlobList();
            UpdateEditLabel();
        }

        private void BuildUi()
        {
            var root = new Grid { Margin = new Thickness(16) };
            _previewDark = _owner.IsDarkMode;   // 预览套初值 = 主窗口当前主题（弹窗内深浅与主窗背景保持一致）
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 标题+模式
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 内容
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 按钮

            // 第 0 行：标题 + 模式选择
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var title = new Emoji.Wpf.TextBlock
            {
                Text = "🎨 自定义背景",
                FontSize = 18.0,
                FontWeight = FontWeights.Bold,
                Foreground = _owner._accent,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(title);

            var modePanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            modePanel.Children.Add(new TextBlock
            {
                Text = "背景类型：",
                Foreground = _owner._textMain,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            // ★ 始终白底黑字，避开深色模式 _inputBg/_inputFg 在 ComboBox 上不可靠的问题。
            _modeCombo = new ComboBox
            {
                Width = 96,   // 最长项「线性/径向/网格渐变」4 字 ≈ 48px + 下拉箭头/边距；140 过宽（用户指出）
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.White,
                Foreground = Brushes.Black,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0x98, 0xA5))
            };
            _modeCombo.Items.Add(new ComboBoxItem { Content = "图片", Tag = BackgroundMode.Image, Foreground = Brushes.Black });
            _modeCombo.Items.Add(new ComboBoxItem { Content = "纯色", Tag = BackgroundMode.Solid, Foreground = Brushes.Black });
            _modeCombo.Items.Add(new ComboBoxItem { Content = "线性渐变", Tag = BackgroundMode.LinearGradient, Foreground = Brushes.Black });
            _modeCombo.Items.Add(new ComboBoxItem { Content = "径向渐变", Tag = BackgroundMode.RadialGradient, Foreground = Brushes.Black });
            _modeCombo.Items.Add(new ComboBoxItem { Content = "网格渐变", Tag = BackgroundMode.MeshGradient, Foreground = Brushes.Black });
            _modeCombo.SelectionChanged += ModeCombo_SelectionChanged;
            modePanel.Children.Add(_modeCombo);
            header.Children.Add(modePanel);
            root.Children.Add(header);
            Grid.SetRow(header, 0);

            // 第 1 行：左编辑区 + 右预览区（图片模式下折叠右列，左列展开容纳双预览）
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            _previewColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            content.ColumnDefinitions.Add(_previewColumn);

            // 左：编辑面板（ScrollViewer 包裹）
            var editScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 0, 12, 0)
            };
            var editPanel = new StackPanel { Orientation = Orientation.Vertical };

            // 提示
            _modeHint = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.0,
                Foreground = _owner._textDim
            };
            // 「深浅背景切换」行上移到模式提示同一行（用户：往上挪更整齐；原在颜色卡内 editLabel 下方）
            // 「不需要『深浅背景:』描述 + 太阳图标放最右」：无标签，顺序 = [⧉ 同步深色→浅色] [☀️/🌙 切换]，
            // 切换图标与主窗口标题栏右上角深浅图标同位置（全页图标都在最右侧，统一美观）
            _dlToggleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            _dlSyncBtn = new Button
            {
                Content = new Emoji.Wpf.TextBlock { Text = "⧉ 同步深色→浅色", VerticalAlignment = VerticalAlignment.Center },
                Height = 26, Width = 130,
                Padding = new Thickness(4, 0, 4, 0),
                Background = _owner._btnSecondaryBg, Foreground = _owner._btnSecondaryFg, BorderThickness = new Thickness(0)
            };
            _dlSyncBtn.Click += (s, e) => SyncDarkToLight();
            _dlToggleRow.Children.Add(_dlSyncBtn);
            // 单按钮（同主窗口右上角 🌙）：点一下深浅套同翻，共用 _owner.ToggleTheme 切换逻辑；放最右
            _previewThemeBtn = new Button
            {
                Content = new Emoji.Wpf.TextBlock { Text = "🌙", VerticalAlignment = VerticalAlignment.Center, FontSize = 14 },
                Width = 36, Height = 26, Margin = new Thickness(8, 0, 0, 0),
                Background = _owner._btnSecondaryBg, Foreground = _owner._textDim, BorderThickness = new Thickness(0),
                ToolTip = "切换深浅背景（与主窗口右上角按钮共用同一切换逻辑）"
            };
            _previewThemeBtn.Click += (s, e) => { SetPreviewSet(!_previewDark); };
            _dlToggleRow.Children.Add(_previewThemeBtn);   // 最右侧
            _dlToggleRow.Visibility = Visibility.Collapsed;   // 初值收起，SyncModeUi 按模式恢复
            // 「往上挪」：深浅行挪到标题行（与「背景类型」下拉同行、右侧紧挨，随 modePanel 右对齐），
            // 不再占用编辑区顶部一行；提示回到编辑区顶部
            modePanel.Children.Add(_dlToggleRow);
            // 图片模式的深浅切换按钮：同样放标题行最右（用户：全页图标都在最右，位置保持一致）；
            // 仅图片模式显示（SyncModeUi），大预览上方不再单独占一行
            _imgThemeToggleBtn = new Button
            {
                Content = new Emoji.Wpf.TextBlock { Text = "🌙", VerticalAlignment = VerticalAlignment.Center, FontSize = 14 },
                Width = 36, Height = 26,
                Background = _owner._btnSecondaryBg, Foreground = _owner._textDim, BorderThickness = new Thickness(0),
                ToolTip = "切换深浅背景（与主窗口右上角按钮共用同一切换逻辑）",
                Visibility = Visibility.Collapsed
            };
            _imgThemeToggleBtn.Click += (s, e) => { _owner.ToggleTheme(); RefreshImageModePreviews(); UpdateDlToggleUi(); };
            modePanel.Children.Add(_imgThemeToggleBtn);
            _modeHint.Margin = new Thickness(0, 0, 0, 12);
            editPanel.Children.Add(_modeHint);

            // 颜色选择区（纯色/渐变/网格共用）
            _colorCard = new Border
            {
                Background = _owner._btnSecondaryBg,
                BorderBrush = _owner._panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var colorSp = new StackPanel();

            // 功能1：正在编辑提示，位于颜色编辑区上方
            _editLabel = new TextBlock
            {
                Text = "正在编辑：",
                FontSize = 12.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = _owner._accent,
                Margin = new Thickness(0, 0, 0, 8)
            };
            colorSp.Children.Add(_editLabel);

            // 颜色编辑区：两行结构
            //   行0：左=色轮，右=颜色格式/对比度面板（两列底部对齐）
            //   行1：HEX / RGB / 明度控件（跨两列，位于色轮下方）
            var colorGrid = new Grid();
            // MaxWidth=300 防御：左列 Auto 一旦超过卡片宽会把 Star 右列挤成 0（本次塌缩根因的兜底保险）
            // 左列固定 205（综合布局预算）：Auto+MaxWidth 会随模式内容（网格底色行 212px）在 205/210 间波动，
            // 右列宽度跟着 199/204 跳动 → 网格模式 HSV/HSL（18 字符 ≈122px）被截 1 字符。固定 205 后
            // 右列恒 204（409-205），所有背景类型的宽度预算一致，不会再「改了这个其他页面又出错」。
            colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) });
            colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            colorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 色轮/颜色格式（含 HEX 行、对比度）
            colorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // RGB/HSV 滑块区

            // 左列改 Grid：标题 + 色轮 + 底部「HEX/底色」区（底部对齐，填满右列撑高的空白带）
            // 第 3 行必须用 Auto（不能用 Star）：Star 行会跟随右列（颜色格式/对比度）高度，
            // 右列内容在任何刷新瞬间变矮（切深浅套/点底色触发重排）时，Star 行塌到 0，
            // HEX/底色行随之「消失」、卡片塌成大灰框——用户截图「点击底色后空白一大块」即此级联。
            // Auto 后左列高度=自然高度，卡片高度稳定取两列较高者，无反馈环。
            var leftTop = new Grid();
            leftTop.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 色轮（标题已删，直接 row0）
            leftTop.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });   // Star 填空（右列更高时防色轮下空带）
            var rightCol = new Grid { VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(16, 0, 0, 0) };
            // 色轮下方控件的顶部间距：色轮 wheelGrid 底部边距已置 0，这里用 22 维持
            // 与之前（wheelGrid 10 + 本行 12 = 22）一致的整体视觉间距，同时确保右列对比度文本
            // 能与色轮圆底边精确对齐（rightCol 拉伸对齐的是 leftTop 底边 = 色轮圆底边）。
            // 「这个还是没有挪」：HEX 行与上方（色轮/对比度）之间的空白带归零——
            // 10px 顶边距仍被看成一条空白带（实测截图约 30px 视觉间隙，含行高留白），直接 0 贴顶
            var lowerControls = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };

            // 左列：色轮（删「色轮」标题——用户：省下的行高给色轮圆，180→200 适当变大）
            // 色轮容器：用 Grid 把 Image 和指示器 Canvas 叠在一起，并用 EllipseGeometry 真正裁成圆形
            var wheelGrid = new Grid
            {
                Width = 200,   // 色轮 180→200（删标题后空间变大，用户：适当放大）
                Height = 200,
                HorizontalAlignment = HorizontalAlignment.Left,
                // 注意：这里底部不留边距。若留 10px，leftTop 会比色轮圆底边高出 10px，
                // 而右列 rightCol 按 VerticalAlignment=Stretch 对齐到 leftTop 底边，
                // 导致"颜色格式/对比度"面板的底部比色轮圆底部低 10px，违背"底部与色轮底部对齐"需求。
                // 因此底部边距置 0，下方 HEX/RGB/明度行的间距由 lowerControls 的 Margin 承担。
                Margin = new Thickness(0, 0, 0, 0)
            };
            _wheelHost = new Border
            {
                Width = 200,
                Height = 200,
                CornerRadius = new CornerRadius(100),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                // 关键：CornerRadius 只影响 Border 自身背景/边框，不会裁剪子元素；
                // 用 EllipseGeometry 裁剪才能确保色轮内容严格圆形（色轮 200：裁剪框必须同步 200×200，
                // 否则 180 的旧裁剪框把 200 色轮切歪成「椭圆」（用户截图形状异常））
                Clip = new EllipseGeometry(new Rect(0, 0, 200, 200))
            };
            _wheelImage = new Image { Width = 200, Height = 200, IsHitTestVisible = false, Stretch = Stretch.Fill };
            _wheelHost.Child = _wheelImage;
            _wheelHost.MouseLeftButtonDown += WheelHost_MouseLeftButtonDown;
            _wheelHost.MouseMove += WheelHost_MouseMove;
            _wheelHost.MouseLeftButtonUp += WheelHost_MouseLeftButtonUp;
            wheelGrid.Children.Add(_wheelHost);

            _wheelOverlay = new Canvas { Width = 200, Height = 200, IsHitTestVisible = false };
            _wheelIndicator = new Ellipse
            {
                Width = 10,
                Height = 10,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };
            _wheelOverlay.Children.Add(_wheelIndicator);
            wheelGrid.Children.Add(_wheelOverlay);

            // 和色标记层：和谐规则的小圆点（UpdateHarmonyWheelDots 填充），叠在指示器之上
            _harmonyOverlay = new Canvas { Width = 200, Height = 200, IsHitTestVisible = false };
            wheelGrid.Children.Add(_harmonyOverlay);
            Grid.SetRow(wheelGrid, 0);   // 标题行已删，色轮升 row0
            leftTop.Children.Add(wheelGrid);

            // HEX 行（并入右列「颜色格式 / 对比度」第 5 行，与 4 个格式行同列同宽）：
            // 单行 Grid = [HEX: 标签 46][输入框 弹性][复制 28]——5 行「复制」按钮全部同一竖线（用户三连对齐）。
            var hexRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hexLbl = new TextBlock { Text = "HEX:", Foreground = _owner._textMain, VerticalAlignment = VerticalAlignment.Center, Width = 40 };
            Grid.SetColumn(hexLbl, 0);
            hexRow.Children.Add(hexLbl);
            _hexBox = new TextBox
            {
                Background = _owner._inputBg,
                Foreground = _owner._inputFg,
                BorderBrush = _owner._panelBorder,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas")
            };
            _hexBox.TextChanged += HexBox_TextChanged;
            _hexBox.LostFocus += HexBox_LostFocus;
            Grid.SetColumn(_hexBox, 1);
            hexRow.Children.Add(_hexBox);
            var hexCopyBtn = new Button
            {
                Content = "复制",
                Height = 22,
                Width = 26,
                FontSize = 11,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = _owner._btnSecondaryBg,
                Foreground = _owner._btnSecondaryFg,
                BorderThickness = new Thickness(0)
            };
            hexCopyBtn.Click += (s, e) => CopyText(BackgroundSettings.ColorToHex(_currentColor), hexCopyBtn);
            Grid.SetColumn(hexCopyBtn, 2);
            hexRow.Children.Add(hexCopyBtn);
            // 「这个还是没有挪」真正根因：右列（标题+4 格式行+对比度）比左列（色轮）高，
            // Grid 行 0 按右列高度，色轮下方永远留一条空白带——margin 归零也消不掉。
            // 现在把 HEX 行（网格模式 + 底色行）放到左列底部（Bottom 对齐填满空白带），
            // 与右列「对比度」行同底，HEX 上移到位，空白带被内容填满。
            var leftBottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            Grid.SetRow(leftBottom, 1);
            leftTop.Children.Add(leftBottom);
            // hexRow 已上移到「HEX + 对比度」跨列单行（见 _contrastText 组装处）；左列底部只留网格底色行
            // 网格底色行：紧跟 HEX 行下方（仅网格模式可见）——选色与底色确认同屏
            BuildMeshBaseRow(leftBottom);

            // 双列布局：左 RGB 列 / 右 HSV 列（H 色相 / S 饱和 / V 明度），两列是同一颜色的两套坐标、
            // 实时互相联动（改任意一列，另一列 + HEX + 色轮指示器同步）。色轮保留在上排作 2D 快选。
            // 修复（孤立控件/布局错位）：明度滑块原孤悬在 RGB 下方，像「RGB 第 4 个分量」；
            // 现并入 HSV 列，量程改 0–100% 显示，新增 H/S 滑块补齐 HSV 三个维度。
            var rgbPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            rgbPanel.Children.Add(new TextBlock { Text = "RGB", FontWeight = FontWeights.SemiBold, Foreground = _owner._textDim, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
            var rPanel = CreateSliderRow("R", 0, 255, "", out _rSlider, out _rVal, v => { _currentColor = Color.FromRgb((byte)v, _currentColor.G, _currentColor.B); UpdateColor(_currentColor, true); });
            var gPanel = CreateSliderRow("G", 0, 255, "", out _gSlider, out _gVal, v => { _currentColor = Color.FromRgb(_currentColor.R, (byte)v, _currentColor.B); UpdateColor(_currentColor, true); });
            var bPanel = CreateSliderRow("B", 0, 255, "", out _bSlider, out _bVal, v => { _currentColor = Color.FromRgb(_currentColor.R, _currentColor.G, (byte)v); UpdateColor(_currentColor, true); });
            _rSlider.ValueChanged += (s, e) => { if (!_isUpdating) { _currentColor = Color.FromRgb((byte)_rSlider.Value, _currentColor.G, _currentColor.B); UpdateColor(_currentColor, true); } };
            _gSlider.ValueChanged += (s, e) => { if (!_isUpdating) { _currentColor = Color.FromRgb(_currentColor.R, (byte)_gSlider.Value, _currentColor.B); UpdateColor(_currentColor, true); } };
            _bSlider.ValueChanged += (s, e) => { if (!_isUpdating) { _currentColor = Color.FromRgb(_currentColor.R, _currentColor.G, (byte)_bSlider.Value); UpdateColor(_currentColor, true); } };
            rgbPanel.Children.Add(rPanel);
            rgbPanel.Children.Add(gPanel);
            rgbPanel.Children.Add(bPanel);

            var hsvPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            hsvPanel.Children.Add(new TextBlock { Text = "HSV", FontWeight = FontWeights.SemiBold, Foreground = _owner._textDim, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
            hsvPanel.Children.Add(CreateSliderRow("H", 0, 360, "°", out _hSlider, out _hVal, v => { _currentHue = v; UpdateColor(HsvToRgb(_currentHue, _currentSat, _currentValue), true, false); }));
            hsvPanel.Children.Add(CreateSliderRow("S", 0, 100, "%", out _sSlider, out _sVal, v => { _currentSat = v / 100.0; UpdateColor(HsvToRgb(_currentHue, _currentSat, _currentValue), true, false); }));
            hsvPanel.Children.Add(CreateSliderRow("V", 0, 100, "%", out _valSlider, out _valVal, v => { _currentValue = v / 100.0; UpdateColor(HsvToRgb(_currentHue, _currentSat, _currentValue), true, false); }));
            // HSV 列：滑块是色相/饱和/明度的来源——传 syncHsv:false，避免 UpdateColor 内从 RGB 反算
            // HSV 再写回滑块造成拖拽抖动；色轮像素重绘仍由 RenderColorWheel 缓存机制按需触发。
            _hSlider.ValueChanged += (s, e) => { if (!_isUpdating) { _currentHue = _hSlider.Value; UpdateColor(HsvToRgb(_currentHue, _currentSat, _currentValue), true, false); } };
            _sSlider.ValueChanged += (s, e) => { if (!_isUpdating) { _currentSat = _sSlider.Value / 100.0; UpdateColor(HsvToRgb(_currentHue, _currentSat, _currentValue), true, false); } };
            _valSlider.ValueChanged += (s, e) => { if (!_isUpdating) { _currentValue = _valSlider.Value / 100.0; UpdateColor(HsvToRgb(_currentHue, _currentSat, _currentValue), true, false); } };

            var dualGrid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            dualGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dualGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            dualGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(rgbPanel, 0);
            Grid.SetColumn(hsvPanel, 2);
            dualGrid.Children.Add(rgbPanel);
            dualGrid.Children.Add(hsvPanel);
            lowerControls.Children.Add(dualGrid);

            // 右列：颜色格式 / 对比度。
            // 「点底色后右列塌缩」验证：中间行 Star→Auto——Star 行在父容器（Auto 行）里反复重测时
            // 高度不稳定（点底色/切套触发卡片重排后，Star 行可能塌到 0，右列 3 个子元素随之「消失」，
            // 表现为颜色卡右半大灰框）。改 Auto 后右列高度=自然高度，重排稳定；
            // 代价：「对比度」文本不再贴卡片底（右列比左列矮时，其下方留白在卡片底部）。
            rightCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });            // 标题
            rightCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 4 个格式行铺满
            rightCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });            // 第 5 行：HEX（与 4 格式行同列同宽 → 复制按钮同一竖线）
            rightCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });            // 第 6 行：对比度（左缘对齐标签列）

            var infoTitle = new TextBlock { Text = "颜色格式 / 对比度", FontWeight = FontWeights.SemiBold, Foreground = _owner._textMain, Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(infoTitle, 0);
            rightCol.Children.Add(infoTitle);

            // 4 个格式行放入 4 行 Star Grid，让各行在标题与底部对比度文本之间均匀铺开、占满剩余空间
            var formatGrid = new Grid { VerticalAlignment = VerticalAlignment.Stretch };
            formatGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            formatGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            formatGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            formatGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _rowRgb = MakeReadOnlyFormatBox("RGB");
            _rowHsl = MakeReadOnlyFormatBox("HSL");
            _rowHsv = MakeReadOnlyFormatBox("HSV");
            _rowCmyk = MakeReadOnlyFormatBox("CMYK");
            // 每行内容垂直居中，配合 Star 行高实现整体均匀铺开（行距平均加宽、空位置占满）
            _rowRgb.VerticalAlignment = VerticalAlignment.Center;
            _rowHsl.VerticalAlignment = VerticalAlignment.Center;
            _rowHsv.VerticalAlignment = VerticalAlignment.Center;
            _rowCmyk.VerticalAlignment = VerticalAlignment.Center;
            // 可单独调整的两组（RGB/HSV，下方有滑块）排前面相邻；HSL/CMYK 只读展示靠后
            Grid.SetRow(_rowRgb, 0);
            Grid.SetRow(_rowHsv, 1);
            Grid.SetRow(_rowHsl, 2);
            Grid.SetRow(_rowCmyk, 3);
            formatGrid.Children.Add(_rowRgb);
            formatGrid.Children.Add(_rowHsl);
            formatGrid.Children.Add(_rowHsv);
            formatGrid.Children.Add(_rowCmyk);
            Grid.SetRow(formatGrid, 1);
            rightCol.Children.Add(formatGrid);

            _contrastText = new Emoji.Wpf.TextBlock
            {
                // 与上面 5 行格式行视觉统一（用户：行间距/字号感觉奇怪）：字号 12（同标签列）、
                // 单行 NoWrap（右列 204px 放得下 100px 文案，无需换行；旧 MinHeight=34 预留两行是行距不齐的根源）。
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,   // 兜底（正常文案 150px 内，极端值也不被硬裁）
                ToolTip = "与窗口主题主文字色的对比度（WCAG 评级：≥7 极佳 / ≥4.5 合格 / ≥3 可接受）",
                FontSize = 12.0,
                Foreground = _owner._textDim,
                Margin = new Thickness(0, 8, 0, 0)   // 与格式行行距一致的顶部间距
            };
            // HEX 行 = 右列第 5 行、对比度 = 右列第 6 行（与 4 个格式行同列同宽）：
            // 5 个「复制」按钮同一竖线（卡片右缘），「对比度」左缘与标签列对齐
            Grid.SetRow(hexRow, 2);
            rightCol.Children.Add(hexRow);
            Grid.SetRow(_contrastText, 3);
            rightCol.Children.Add(_contrastText);

            Grid.SetRow(leftTop, 0); Grid.SetColumn(leftTop, 0);
            Grid.SetRow(rightCol, 0); Grid.SetColumn(rightCol, 1);
            colorGrid.Children.Add(leftTop);
            colorGrid.Children.Add(rightCol);

            // 行1：HEX / RGB / 明度控件（跨两列，位于色轮下方）
            Grid.SetRow(lowerControls, 1);
            Grid.SetColumn(lowerControls, 0);
            Grid.SetColumnSpan(lowerControls, 2);
            colorGrid.Children.Add(lowerControls);
            colorSp.Children.Add(colorGrid);
            _colorCard.Child = colorSp;
            editPanel.Children.Add(_colorCard);

            // 图片模式专属（方案 A）：左列控件 + 右列随主题的大预览，仅 Image 模式可见
            _imageCard = new Border
            {
                Background = _owner._btnSecondaryBg,
                BorderBrush = _owner._panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var imgRoot = new Grid();
            imgRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            imgRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            imgRoot.MinHeight = 320;

            // ===== 左列：仅控件（标题 + 恢复默认 + 深色/浅色选择 + 独立透明度）=====
            var leftColImg = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };

            // 标题行：自定义背景图 + 恢复默认按钮
            var imgHeaderRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            imgHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            imgHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var imgTitlePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            imgTitlePanel.Children.Add(new Emoji.Wpf.TextBlock
            {
                Text = "自定义背景图",
                FontWeight = FontWeights.SemiBold,
                Foreground = _owner._textMain,
                VerticalAlignment = VerticalAlignment.Center
            });
            imgTitlePanel.Children.Add(new Emoji.Wpf.TextBlock
            {
                Text = "提示：支持 PNG/JPG/BMP/GIF/WebP；图片会被引用（不嵌入 exe），请勿删除原文件。切换主题后新背景自动生效。",
                FontSize = 11.0,
                Foreground = _owner._textDim,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.Wrap
            });
            imgHeaderRow.Children.Add(imgTitlePanel);

            var imgResetBtn = new Button
            {
                Content = new Emoji.Wpf.TextBlock { Text = "🔄 恢复默认背景", VerticalAlignment = VerticalAlignment.Center },
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                Background = _owner._btnSecondaryBg,
                Foreground = _owner._btnSecondaryFg,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            imgResetBtn.Click += (s, e) =>
            {
                // 修复（行为与文案不符，与 Config 页同名按钮对齐）：原实现只清图路径+不透明度，
                // 不清 Mode。弹窗在 Image 模式下显示此按钮（仅 Image 卡片可见），但用户可能刚切过
                // Solid/Mesh 再切回 Image；为语义一致，这里也显式把 Mode 复位为 Image。
                _settings.Mode = BackgroundMode.Image;
                _settings.DarkPath = "";
                _settings.LightPath = "";
                _settings.DarkOpacity = 0.85;   // 与 BackgroundSettings 默认值对齐（原 0.55 已随默认上移）
                _settings.LightOpacity = 1.0;
                if (_darkOpacitySlider != null) { _darkOpacitySlider.Value = 0.85; _darkOpacityVal.Text = "85%"; }
                if (_lightOpacitySlider != null) { _lightOpacitySlider.Value = 1.0; _lightOpacityVal.Text = "100%"; }
                // 修复：恢复默认背景也是一次真实编辑，原先未置位导致「应用→恢复默认→×关闭」时改动被回滚吞掉
                _hasEditedAfterShow = true;
                RefreshImageModePreviews();
            };
            Grid.SetColumn(imgResetBtn, 1);
            imgHeaderRow.Children.Add(imgResetBtn);
            leftColImg.Children.Add(imgHeaderRow);

            // 🌙 选择深色背景
            var darkSelectBtn = new Button
            {
                Content = new Emoji.Wpf.TextBlock { Text = "🌙 选择深色背景", VerticalAlignment = VerticalAlignment.Center },
                Height = 28,
                Margin = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(10, 0, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = _owner._btnSecondaryBg,
                Foreground = _owner._btnSecondaryFg,
                BorderThickness = new Thickness(0)
            };
            darkSelectBtn.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择深色背景",
                    Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                };
                if (dlg.ShowDialog() == true)
                {
                    _settings.DarkPath = dlg.FileName;
                    // 修复：选图是真实编辑，原先未置位导致「应用→换图→×关闭」时新图被回滚吞掉
                    _hasEditedAfterShow = true;
                    RefreshImageModePreviews();
                }
            };
            leftColImg.Children.Add(darkSelectBtn);
            BuildImageOpacityRow(leftColImg, true, out _darkOpacitySlider, out _darkOpacityVal);

            // ☀️ 选择浅色背景
            var lightSelectBtn = new Button
            {
                Content = new Emoji.Wpf.TextBlock { Text = "☀️ 选择浅色背景", VerticalAlignment = VerticalAlignment.Center },
                Height = 28,
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(10, 0, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = _owner._btnSecondaryBg,
                Foreground = _owner._btnSecondaryFg,
                BorderThickness = new Thickness(0)
            };
            lightSelectBtn.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择浅色背景",
                    Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                };
                if (dlg.ShowDialog() == true)
                {
                    _settings.LightPath = dlg.FileName;
                    // 修复：选图是真实编辑，原先未置位导致「应用→换图→×关闭」时新图被回滚吞掉
                    _hasEditedAfterShow = true;
                    RefreshImageModePreviews();
                }
            };
            leftColImg.Children.Add(lightSelectBtn);
            BuildImageOpacityRow(leftColImg, false, out _lightOpacitySlider, out _lightOpacityVal);

            Grid.SetColumn(leftColImg, 0);
            imgRoot.Children.Add(leftColImg);

            // ===== 右列：随主题的大预览（深色模式显示 DarkPath，浅色模式显示 LightPath）=====
            var bigPreviewHost = new Border
            {
                Background = _owner._windowBg,
                BorderBrush = _owner._panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                MinHeight = 320
            };
            var bigGrid = new Grid();
            _imgBigPreview = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            bigGrid.Children.Add(_imgBigPreview);
            _imgBigPlaceholder = new TextBlock
            {
                Text = "未选择背景图片",
                FontSize = 13.0,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(0xAA, 0x00, 0x00, 0x00)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3)),
                Padding = new Thickness(12, 6, 12, 6),
                Visibility = Visibility.Collapsed
            };
            bigGrid.Children.Add(_imgBigPlaceholder);
            bigPreviewHost.Child = bigGrid;
            // 图片模式深浅切换按钮已上移到标题行最右（_imgThemeToggleBtn，与非图片模式图标同位置），
            // 大预览上方不再单独占一行
            Grid.SetColumn(bigPreviewHost, 1);
            imgRoot.Children.Add(bigPreviewHost);
            UpdateDlToggleUi();

            _imageCard.Child = imgRoot;
            editPanel.Children.Add(_imageCard);

            // 和谐色（颜色组合 + 调色板）：显示在颜色编辑区下方（原位置）
            BuildHarmonyCard(editPanel);

            // 功能2b：色阶生成器（Shades / Tints / Tones），任意非图片模式可见
            BuildShadesTintsTonesCard(editPanel);

            // 功能4：预设色板库（12 套精选配色，点击应用到当前模式；对齐参考站 online-color.com 调色板库）
            BuildPresetPaletteCard(editPanel);

            // 渐变/径向专属：角度/中心/半径
            _geometryCard = new Border
            {
                Background = _owner._btnSecondaryBg,
                BorderBrush = _owner._panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var geoSp = new StackPanel();
            var geoTitle = new TextBlock { Text = "几何参数", FontWeight = FontWeights.SemiBold, Foreground = _owner._textMain, Margin = new Thickness(0, 0, 0, 8) };
            geoSp.Children.Add(geoTitle);

            // 几何参数合并一行（用户：角度/中心 X/Y 显示在同一行）：角度组并入中心行前端，不独立占行
            _angleBox = new TextBox { Width = 48, Foreground = _owner._inputFg, Background = _owner._inputBg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center };   // 收窄（用户：足够显示即可）
            // 修复：几何输入统一钳制。角度归一化到 [-360,360] 后再用（超出部分无意义），
            // 中心/半径也必须钳制，否则 0 或负半径会让 RadialGradientBrush 构造抛异常。
            _angleBox.TextChanged += (s, e) => TryParseBox(_angleBox, v => _settings.GradientAngle = ClampAngle(v), () => RefreshPreview());

            var centerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            // 角度组（仅线性模式显示；径向渐变无角度概念——用户：径向不需要角度）：整组可隐藏
            _angleGroup = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
            _angleGroup.Children.Add(new TextBlock { Text = "角度:", Foreground = _owner._textMain, Width = 40, VerticalAlignment = VerticalAlignment.Center });
            _angleGroup.Children.Add(_angleBox);
            _angleGroup.Children.Add(new TextBlock { Text = "°", Foreground = _owner._textDim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            centerRow.Children.Add(_angleGroup);
            centerRow.Children.Add(new TextBlock { Text = "中心 X:", Foreground = _owner._textMain, Width = 50, VerticalAlignment = VerticalAlignment.Center });
            _centerXBox = new TextBox { Width = 50, Background = _owner._inputBg, Foreground = _owner._inputFg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center };
            _centerXBox.TextChanged += (s, e) => TryParseBox(_centerXBox, v => SetCenterX(Clamp(v, 0, 1)), () => RefreshPreview());
            centerRow.Children.Add(_centerXBox);
            centerRow.Children.Add(new TextBlock { Text = "Y:", Foreground = _owner._textMain, Width = 26, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextAlignment = TextAlignment.Center });
            _centerYBox = new TextBox { Width = 50, Background = _owner._inputBg, Foreground = _owner._inputFg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center };
            _centerYBox.TextChanged += (s, e) => TryParseBox(_centerYBox, v => SetCenterY(Clamp(v, 0, 1)), () => RefreshPreview());
            centerRow.Children.Add(_centerYBox);
            geoSp.Children.Add(centerRow);

            var radiusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            radiusRow.Children.Add(new TextBlock { Text = "半径 X:", Foreground = _owner._textMain, Width = 50, VerticalAlignment = VerticalAlignment.Center });
            _radiusXBox = new TextBox { Width = 50, Background = _owner._inputBg, Foreground = _owner._inputFg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center };
            // 修复：半径原样写入，输入 0/负数/超大值会让 RadialGradientBrush 构造抛异常，
            // 而异常被 RefreshPreview 的 catch 静默吞掉，预览停在旧图且无任何提示。这里统一钳制到 [0.01, 10]。
            _radiusXBox.TextChanged += (s, e) => TryParseBox(_radiusXBox, v => _settings.RadialRadiusX = ClampRadius(v), () => RefreshPreview());
            radiusRow.Children.Add(_radiusXBox);
            radiusRow.Children.Add(new TextBlock { Text = "Y:", Foreground = _owner._textMain, Width = 26, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextAlignment = TextAlignment.Center });
            _radiusYBox = new TextBox { Width = 50, Background = _owner._inputBg, Foreground = _owner._inputFg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center };
            _radiusYBox.TextChanged += (s, e) => TryParseBox(_radiusYBox, v => _settings.RadialRadiusY = ClampRadius(v), () => RefreshPreview());
            radiusRow.Children.Add(_radiusYBox);
            geoSp.Children.Add(radiusRow);

            _geometryCard.Child = geoSp;
            editPanel.Children.Add(_geometryCard);

            // 停靠点/光斑列表
            _listCard = new Border
            {
                Background = _owner._btnSecondaryBg,
                BorderBrush = _owner._panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var listSp = new StackPanel();
            var listTitleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var listTitle = new TextBlock { Text = "颜色停靠点", FontWeight = FontWeights.SemiBold, Foreground = _owner._textMain, VerticalAlignment = VerticalAlignment.Center };
            listTitleRow.Children.Add(listTitle);
            _addStopBtn = new Button
            {
                Content = "+ 添加",
                Width = 60,
                Height = 24,
                Margin = new Thickness(12, 0, 0, 0),
                Background = _owner._accent,
                Foreground = _owner._btnPrimaryFg,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _addStopBtn.Click += AddStop_Click;
            listTitleRow.Children.Add(_addStopBtn);
            listSp.Children.Add(listTitleRow);

            _stopPanel = new StackPanel();
            _stopPanel.Focusable = true;     // 功能1：允许上下方向键在停靠点间移动选中
            _stopPanel.PreviewKeyDown += StopPanel_PreviewKeyDown;
            listSp.Children.Add(_stopPanel);

            _addBlobBtn = new Button
            {
                Content = "+ 添加光斑",
                Width = 90,
                Height = 24,
                Margin = new Thickness(0, 8, 0, 0),
                Background = _owner._accent,
                Foreground = _owner._btnPrimaryFg,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _addBlobBtn.Click += AddBlob_Click;
            listSp.Children.Add(_addBlobBtn);

            _blobPanel = new StackPanel();
            listSp.Children.Add(_blobPanel);

            _listCard.Child = listSp;
            editPanel.Children.Add(_listCard);

            // 功能3：CSS 导入/导出卡片 = 独立卡片（与「颜色停靠点」卡同级）。
            // 旧实现 BuildCssCard(listSp) 把它嵌进停靠点卡内部——多套一层卡片框（用户指认），
            // 整卡缩进深一级，卡片内标题/输入框/按钮行永远比停靠色块多一层 padding，怎么调都对不齐。
            BuildCssCard(editPanel);



            editScroll.Content = editPanel;
            content.Children.Add(editScroll);
            Grid.SetColumn(editScroll, 0);

            // 右：预览区
            _previewHost = new Border
            {
                Background = _owner._windowBg,
                BorderBrush = _owner._panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                MinHeight = 240
            };
            var previewGrid = new Grid();
            previewGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _previewRect = new Rectangle
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Stretch = Stretch.Fill
            };
            Grid.SetRow(_previewRect, 0);
            Grid.SetColumn(_previewRect, 0);
            previewGrid.Children.Add(_previewRect);
            // Mesh 光斑渲染层：与主窗口 BgBlobs 同源（PopulateBlobCanvas），叠在底色上、句柄层之下。
            // 不再依赖 VisualBrush viewport，杜绝“底色块只占中央”的 brush math 问题。
            _blobCanvas = new Canvas
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetRow(_blobCanvas, 0);
            Grid.SetColumn(_blobCanvas, 0);
            previewGrid.Children.Add(_blobCanvas);
            _previewOverlay = new Canvas
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = true,
                Cursor = Cursors.Cross,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetRow(_previewOverlay, 0);
            Grid.SetColumn(_previewOverlay, 0);
            previewGrid.Children.Add(_previewOverlay);
            _previewHost.Child = previewGrid;
            content.Children.Add(_previewHost);
            Grid.SetColumn(_previewHost, 1);

            _previewOverlay.MouseMove += PreviewOverlay_MouseMove;
            _previewOverlay.MouseLeftButtonUp += PreviewOverlay_MouseLeftButtonUp;
            _previewOverlay.SizeChanged += (s, e) => RefreshOverlayHandles();

            root.Children.Add(content);
            Grid.SetRow(content, 1);

            // 第 2 行：按钮
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            _applyBtn = new Button
            {
                Content = "应用",
                Width = 80,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Background = _owner._btnSecondaryBg,
                Foreground = _owner._btnSecondaryFg,
                BorderThickness = new Thickness(0)
            };
            _applyBtn.Click += (s, e) =>
            {
                // 应用按钮不设 DialogResult，仅把当前 _settings 推到 owner 并记快照，
                // 关闭时若用户又编辑了，可基于此快照精确回滚而不是吞回 _initial
                _owner.ApplyBackgroundSettings(_settings.Clone());
                _appliedSnapshot = _settings.Clone();
                _hasAppliedAtLeastOnce = true;
            };
            _saveBtn = new Button
            {
                Content = "保存并关闭",
                Width = 100,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Background = _owner._accent,
                Foreground = _owner._btnPrimaryFg,
                BorderThickness = new Thickness(0)
            };
            _saveBtn.Click += (s, e) => { DialogResult = true; Close(); };
            _cancelBtn = new Button
            {
                Content = "取消",
                Width = 80,
                Height = 32,
                Background = _owner._btnSecondaryBg,
                Foreground = _owner._btnSecondaryFg,
                BorderThickness = new Thickness(0)
            };
            _cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
            btnRow.Children.Add(_applyBtn);
            btnRow.Children.Add(_saveBtn);
            btnRow.Children.Add(_cancelBtn);
            root.Children.Add(btnRow);
            Grid.SetRow(btnRow, 2);

            Content = root;
            Loaded += (s, e) => { RenderColorWheel(); UpdateWheelIndicator(); };
        }

        /// <summary>双列布局通用滑块行：标签(自动) + 滑块(占满列宽) + 数值显示(40, 可带单位后缀)。
        /// RGB 列（0–255，无单位）与 HSV 列（H 0–360°、S/V 0–100%）共用同一构造，保证两列视觉对称。
        /// 行内必须用 Grid（Star 列）承载滑块：横向 StackPanel 不给 stretch 子项分配宽度，
        /// Slider 会塌缩到最小宽、滑轨几乎不可见（曾因此滑轨消失，现 Grid 保证占满）。</summary>
        private Grid CreateSliderRow(string label, double min, double max, string unit, out Slider slider, out TextBox valBlock, Action<double> onCommit)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock { Text = label + ":", Foreground = _owner._textMain, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            Grid.SetColumn(lbl, 0);
            slider = new Slider { Minimum = min, Maximum = max, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
            Grid.SetColumn(slider, 1);
            valBlock = new TextBox
            {
                Text = "0" + unit,
                Foreground = _owner._accent,
                Width = 44,
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                Background = _owner._inputBg,
                BorderBrush = _owner._panelBorder,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valBlock, 2);
            // 单击数字精确输入：回车 / 失焦提交（解析 + 钳制到 [min,max]）；解析失败则不提交、保留当前值。
            // out 参数 valBlock 不能直接进 lambda（CS1628），取本地副本 valBoxRef。
            var valBoxRef = valBlock;
            valBoxRef.LostFocus += (s, e) => CommitSliderValue(valBoxRef, min, max, onCommit);
            valBoxRef.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; CommitSliderValue(valBoxRef, min, max, onCommit); } };

            row.Children.Add(lbl);
            row.Children.Add(slider);
            row.Children.Add(valBlock);
            return row;
        }

        /// <summary>数值框提交：去掉单位后缀（°/%）解析为 double，钳制到 [min,max] 后回调 onCommit；解析失败不提交。</summary>
        private static void CommitSliderValue(TextBox box, double min, double max, Action<double> onCommit)
        {
            if (box == null || onCommit == null) return;
            var s = (box.Text ?? "").Trim().TrimEnd('°', '%');
            if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
                onCommit(Math.Max(min, Math.Min(max, v)));
        }

        // ---- 事件处理 ----

        private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FitModeComboWidth();
        {
            if (_isInitializing) return;
            if (_modeCombo.SelectedItem is ComboBoxItem item && item.Tag is BackgroundMode newMode)
            {
                var oldMode = _settings.Mode;
                if (oldMode != newMode)
                {
                    // 切走：把当前几何参数按旧 mode 存到最近值字典；切回时再写回 _settings
                    _angleByMode[oldMode] = _settings.GradientAngle;
                    _centerXByMode[oldMode] = GetCenterX();
                    _centerYByMode[oldMode] = GetCenterY();
                    // 切回：字典里有就恢复，否则保持 _settings 当前值（即首次进入此 mode 的初值）
                    _settings.GradientAngle = _angleByMode.TryGetValue(newMode, out var a) ? a : _settings.GradientAngle;
                    if (_centerXByMode.TryGetValue(newMode, out var cx)) SetCenterX(cx);
                    if (_centerYByMode.TryGetValue(newMode, out var cy)) SetCenterY(cy);
                }
                _settings.Mode = newMode;
                SyncModeUi();
                // 把切换后的几何值同步到 UI 文本框（仅在模式真正变化时更新，避免初始化阶段抖动）
                if (oldMode != newMode)
                {
                    if (_angleBox != null) _angleBox.Text = _settings.GradientAngle.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    if (_centerXBox != null) _centerXBox.Text = GetCenterX().ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                    if (_centerYBox != null) _centerYBox.Text = GetCenterY().ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                }
                // 修复（色轮/预览失步）：模式切换后必须把色轮同步到【新模式自身的代表色】，
                // 旧实现只在「有选中停靠点/光斑」时同步——但纯色/图片模式会把 _selectedStop/_selectedBlob
                // 置空，于是色轮残留上个模式的旧色（如 #16E0BD），预览却用 _settings.SolidColor 渲染，
                // 出现「色轮显示青色、右边预览却是纯黑」的矛盾。现按新模式分别取代表色：
                //   纯色 → _settings.SolidColor；渐变 → 选中停靠点（SyncModeUi 已 Ensure+选首）；
                //   网格 → 选中光斑（同上）；图片 → 不动色轮（图片模式色卡本就隐藏）。
                if (newMode == BackgroundMode.Solid)
                {
                    _currentColor = BackgroundSettings.ParseColor(_settings.SolidColor);
                    UpdateColor(_currentColor, true);   // 内部写回 SolidColor（同值）并重绘色轮、刷新预览
                }
                else if (_selectedStop != null)
                {
                    _currentColor = BackgroundSettings.ParseColor(_selectedStop.Color);
                    UpdateColor(_currentColor, true);   // 内部已请求色轮重绘
                }
                else if (_selectedBlob != null)
                {
                    _currentColor = BackgroundSettings.ParseColor(_selectedBlob.Color);
                    UpdateColor(_currentColor, true);
                }
                else if (newMode == BackgroundMode.Image)
                {
                    // 图片模式色卡隐藏，无需同步色轮；仅刷新预览（内置背景图 + 双透明度）
                }
                RefreshPreview();
            }
        }

        private void WheelHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _wheelHost.CaptureMouse();
            PickColorFromWheel(e.GetPosition(_wheelHost));
        }

        private void WheelHost_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _wheelHost.IsMouseCaptured)
            {
                PickColorFromWheel(e.GetPosition(_wheelHost));
            }
        }

        private void WheelHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_wheelHost.IsMouseCaptured)
                _wheelHost.ReleaseMouseCapture();
        }

        private void PickColorFromWheel(Point pos)
        {
            var (cx, cy, maxR) = GetWheelGeometry(_wheelHost.Width, _wheelHost.Height);
            double dx = pos.X - cx;
            double dy = pos.Y - cy;
            double r = Math.Sqrt(dx * dx + dy * dy);
            // 限制在圆内取色，超出圆心距时按边界饱和度 1.0 处理
            _currentSat = Math.Min(r / maxR, 1.0);
            _currentHue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
            _currentColor = HsvToRgb(_currentHue, _currentSat, _currentValue);
            UpdateColor(_currentColor, true); // 同步 RGB/HEX/明度滑块
            UpdateWheelIndicator();
        }

        private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;
            // [Q41] 裸 ParseColor 直接吃原始输入（#RGB 3 位 / 带 alpha / 中间态畸形串）会误解析；
            // 先经 NormalizeHexColor 归一化+校验（非法/不完整返回 null），合法再解析
            var norm = NormalizeHexColor(_hexBox.Text);
            if (norm == null) return;
            var c = BackgroundSettings.ParseColor(norm);
            if (c.A > 0)
            {
                _currentColor = c;
                UpdateColor(_currentColor, true);
            }
        }

        private void HexBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _hexBox.Text = BackgroundSettings.ColorToHex(_currentColor);
        }

        private void AddStop_Click(object sender, RoutedEventArgs e)
        {
            var stops = ActiveStops();   // 「分别编辑浅色套」：新增停靠点落在活动套
            // 修复：末点已在 100% 时旧实现把 off 钳成 1.0，新点与末点同位置（位置显示重复、预览点重叠）
            var sortedStops = stops.OrderBy(x => x.Offset).ToList();
            var last = sortedStops.LastOrDefault();
            double off;
            if (last == null) off = 0.0;
            else if (last.Offset < 1.0 - 1e-6) off = Math.Min(last.Offset + 0.2, 1.0);
            else
            {
                var prev = sortedStops.Count >= 2 ? sortedStops[sortedStops.Count - 2] : null;
                off = prev != null ? (prev.Offset + last.Offset) / 2.0 : 0.5;
            }
            var ns = new GradientStopSetting { Color = BackgroundSettings.ColorToHex(_currentColor), Offset = off };
            stops.Add(ns);
            stops.Sort((x, y) => x.Offset.CompareTo(y.Offset));
            _selectedStop = ns;
            _selectedBlob = null;
            RefreshStopList();
            RefreshPreview();
        }

        private void AddBlob_Click(object sender, RoutedEventArgs e)
        {
            var blobs = ActiveBlobs();   // 「分别编辑浅色套」：新增光斑落在活动套
            double radius = 0.35;
            double cx = 0.3 + (blobs.Count % 3) * 0.2;
            double cy = 0.3 + (blobs.Count / 3) * 0.2;
            // 保证光斑主体在画布 [0,1] 范围内，避免越界后被完全裁切
            cx = Math.Max(radius, Math.Min(1.0 - radius, cx));
            cy = Math.Max(radius, Math.Min(1.0 - radius, cy));
            var nb = new MeshBlobSetting
            {
                Color = BackgroundSettings.ColorToHex(_currentColor),
                CenterX = cx,
                CenterY = cy,
                Radius = radius,
                Opacity = 0.8
            };
            blobs.Add(nb);
            _selectedBlob = nb;
            _selectedStop = null;
            RefreshBlobList();
            RefreshPreview();
        }

        private void TryParseBox(TextBox box, Action<double> setter, Action onChanged)
        {
            if (_isUpdating) return;
            // 修复：NumberStyles.Float 会把 "NaN"/"Infinity" 也解析成功，这类值传入几何参数同样会让
            // 画刷构造抛异常；这里先排除非有限值，再交给各 setter 做范围钳制。
            if (double.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)
                && !double.IsNaN(v) && !double.IsInfinity(v))
            {
                _hasEditedAfterShow = true;   // 任何几何输入框被解析成功即视为用户编辑
                setter(v);
                onChanged();
            }
        }

        /// <summary>
        /// 渐变/径向半径钳制：负数取绝对值后取下限 0.01（半径必须为正，否则 RadialGradientBrush 构造失败），
        /// 上限 10 防止输入超大值把半径推到画布外完全看不见。
        /// </summary>
        private static double ClampRadius(double v) => Clamp(Math.Abs(v), 0.01, 10.0);

        /// <summary>渐变角度钳制到 [-360, 360]，超出一圈的角度没有视觉意义。</summary>
        private static double ClampAngle(double v) => Clamp(v, -360.0, 360.0);

        // 线性/径向共享「中心 X/Y」输入控件：按当前模式读写到对应字段
        private double GetCenterX() => _settings.Mode == BackgroundMode.LinearGradient ? _settings.LinearCenterX : _settings.RadialCenterX;
        private double GetCenterY() => _settings.Mode == BackgroundMode.LinearGradient ? _settings.LinearCenterY : _settings.RadialCenterY;
        private void SetCenterX(double v) { if (_settings.Mode == BackgroundMode.LinearGradient) _settings.LinearCenterX = v; else _settings.RadialCenterX = v; }
        private void SetCenterY(double v) { if (_settings.Mode == BackgroundMode.LinearGradient) _settings.LinearCenterY = v; else _settings.RadialCenterY = v; }

        /// <summary>
        /// 图片模式左列：构建单行独立透明度滑块（深色/浅色各一），写入对应 DarkOpacity/LightOpacity
        /// 并实时刷新右列大预览。
        /// </summary>
        private void BuildImageOpacityRow(StackPanel parent, bool isDark, out Slider slider, out TextBlock valText)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var opLabel = new TextBlock
            {
                Text = isDark ? "深色透明度:" : "浅色透明度:",
                Foreground = _owner._textMain,
                Width = 74,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            Grid.SetColumn(opLabel, 0);
            row.Children.Add(opLabel);

            slider = new Slider
            {
                Minimum = 0.1,
                Maximum = 1.0,
                Value = isDark ? _settings.DarkOpacity : _settings.LightOpacity,
                TickFrequency = 0.05,
                IsSnapToTickEnabled = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            valText = new TextBlock
            {
                Text = (isDark ? _settings.DarkOpacity : _settings.LightOpacity).ToString("P0"),
                FontSize = 11.5,
                Foreground = _owner._accent,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 34,
                Margin = new Thickness(0, 0, 6, 0)
            };
            var localSlider = slider;
            var localValText = valText;
            slider.ValueChanged += (s, e) =>
            {
                // 初始化阶段（_isUpdating）由 SyncUiToSettings 触发，不算用户编辑
                if (!_isUpdating) _hasEditedAfterShow = true;
                if (isDark) _settings.DarkOpacity = localSlider.Value;
                else _settings.LightOpacity = localSlider.Value;
                localValText.Text = localSlider.Value.ToString("P0");
                // 性能优化：透明度只影响渲染不透明度，不改变解码结果。原来每个 ValueChanged
                // 都走 RefreshImageModePreviews() → TryLoadImagePublic() 重新解码一次 PNG，
                // 拖一次滑块上百次解码。这里优先走「只改 Opacity」的快路径。
                if (ApplyImageOpacityOnly(isDark, localSlider.Value)) return;
                RefreshImageModePreviews();
            };
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);
            Grid.SetColumn(valText, 2);
            row.Children.Add(valText);
            parent.Children.Add(row);
        }

        // ---- 同步 UI ----

        private void SyncUiToSettings()
        {
            _isInitializing = true;
            _isUpdating = true;
            try
            {
                // 选中模式（设置 SelectedItem 会触发 ModeCombo_SelectionChanged；
                // 用 _isInitializing 阻止重入，避免初始化阶段反复重建停靠点/光斑列表）。
                foreach (ComboBoxItem item in _modeCombo.Items)
                {
                    if (item.Tag is BackgroundMode m && m == _settings.Mode)
                    {
                        _modeCombo.SelectedItem = item;
                        break;
                    }
                }

                _angleBox.Text = _settings.GradientAngle.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                _centerXBox.Text = GetCenterX().ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                _centerYBox.Text = GetCenterY().ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                _radiusXBox.Text = _settings.RadialRadiusX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                _radiusYBox.Text = _settings.RadialRadiusY.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

                // 初始颜色：根据模式取一个有意义的颜色
                if (_settings.Mode == BackgroundMode.Solid)
                    _currentColor = BackgroundSettings.ParseColor(_settings.SolidColor);
                else if (_settings.Stops != null && _settings.Stops.Count > 0)
                    _currentColor = BackgroundSettings.ParseColor(_settings.Stops[0].Color);
                else if (_settings.Blobs != null && _settings.Blobs.Count > 0)
                    _currentColor = BackgroundSettings.ParseColor(_settings.Blobs[0].Color);

                UpdateColor(_currentColor, true);   // 内部已请求色轮重绘，去掉原重复的 RenderColorWheel()
                SyncModeUi();
                UpdateWheelIndicator();
                RefreshPreview();
            }
            finally { _isUpdating = false; _isInitializing = false; }
        }

        private void SyncModeUi()
        {
            var mode = _settings.Mode;
            // 功能1：切换模式时保留当前选中（若在新模式列表里仍存在），否则才回退到首个
            if (mode == BackgroundMode.LinearGradient || mode == BackgroundMode.RadialGradient)
            {
                _settings.EnsureGradientStops();
                // 跨家族选中记忆：进入渐变家族时，先保存另一家族的当前选中，再恢复同家族上次选中
                _lastSelectedBlob = _selectedBlob;
                _selectedBlob = null;
                _selectedStop = (_lastSelectedStop != null && _settings.Stops.Contains(_lastSelectedStop))
                    ? _lastSelectedStop
                    : _settings.Stops.FirstOrDefault();
            }
            else if (mode == BackgroundMode.MeshGradient)
            {
                _settings.EnsureMeshBlobs();
                // 修复：EnsureMeshBlobs() 已保证 Blobs.Count >= 1，原写法 if (Count == 0) 恒为假，
                // CreateDefaultMeshBlobs 成了死代码，网格模式首屏只剩单色径向（正是上面注释要避免的效果）。
                // 现改为：光斑不足 MeshDefaultBlobCount 个时，用默认光斑补齐，让用户第一眼看到多光斑 mesh。
                if (_settings.Blobs.Count < MeshDefaultBlobCount)
                {
                    var defaults = BackgroundSettings.CreateDefaultMeshBlobs(
                        BackgroundSettings.ColorToHex(_currentColor));
                    if (defaults != null && defaults.Count > 0)
                    {
                        // 保留用户已有光斑，只补齐差额；多轮补齐时按序号轮换取值，避免全部同色同位
                        int existing = _settings.Blobs.Count;
                        while (_settings.Blobs.Count < MeshDefaultBlobCount)
                        {
                            var src = defaults[(_settings.Blobs.Count - existing) % defaults.Count];
                            _settings.Blobs.Add(new MeshBlobSetting
                            {
                                Color = src.Color,
                                CenterX = src.CenterX,
                                CenterY = src.CenterY,
                                Radius = src.Radius,
                                Opacity = src.Opacity
                            });
                        }
                    }
                }
                // 跨家族选中记忆：进入网格家族时，先保存另一家族的当前选中，再恢复同家族上次选中
                _lastSelectedStop = _selectedStop;
                _selectedStop = null;
                _selectedBlob = (_lastSelectedBlob != null && _settings.Blobs.Contains(_lastSelectedBlob))
                    ? _lastSelectedBlob
                    : _settings.Blobs.FirstOrDefault();
            }
            else
            {
                // 纯色/图片模式：保存两个家族的当前选中，便于切回时恢复
                _lastSelectedStop = _selectedStop;
                _lastSelectedBlob = _selectedBlob;
                _selectedStop = null;
                _selectedBlob = null;
            }
            _modeHint.Text = mode switch
            {
                BackgroundMode.Image => "当前使用背景图（可到「系统设置」页选择深色/浅色图片）。",
                BackgroundMode.Solid => "选择单一颜色作为窗口背景。",
                BackgroundMode.LinearGradient => "线性渐变：设置角度和两个以上的颜色停靠点。",
                BackgroundMode.RadialGradient => "径向渐变：设置中心、半径和颜色停靠点。",
                BackgroundMode.MeshGradient => "网格渐变：添加多个彩色光斑，模拟 gradients.app 的多层叠加效果。",
                _ => ""
            };

            // 颜色区在除 Image 外都可见
            bool showColor = mode != BackgroundMode.Image;
            // 几何参数：线性=角度；径向=中心+半径；网格=隐藏
            bool showAngle = mode == BackgroundMode.LinearGradient;
            bool showRadial = mode == BackgroundMode.RadialGradient;
            // 停靠点：线性/径向；光斑：网格
            bool showStops = mode == BackgroundMode.LinearGradient || mode == BackgroundMode.RadialGradient;
            bool showBlobs = mode == BackgroundMode.MeshGradient;
            // 列表+CSS 卡片：仅渐变/网格模式显示（纯色/图片模式折叠，避免无用 UI）
            bool showListCard = mode == BackgroundMode.LinearGradient || mode == BackgroundMode.RadialGradient || mode == BackgroundMode.MeshGradient;

            // 通过父容器 Border / StackPanel 控制显隐
            SetVisibility(_colorCard, showColor);
            SetVisibility(_imageCard, mode == BackgroundMode.Image);

            // 图片模式：隐藏通用右侧预览与顶部提示，折叠右列让左列双预览展开
            if (mode == BackgroundMode.Image)
            {
                if (_previewHost != null) _previewHost.Visibility = Visibility.Collapsed;
                if (_previewColumn != null) _previewColumn.Width = new GridLength(0);
                if (_modeHint != null) _modeHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                if (_previewHost != null) _previewHost.Visibility = Visibility.Visible;
                if (_previewColumn != null) _previewColumn.Width = new GridLength(1, GridUnitType.Star);
                if (_modeHint != null) _modeHint.Visibility = Visibility.Visible;
            }

            // 同步图片模式双滑块（切回 Image 模式时恢复当前设置值）
            if (_darkOpacitySlider != null)
            {
                _darkOpacitySlider.Value = _settings.DarkOpacity;
                _darkOpacityVal.Text = _settings.DarkOpacity.ToString("P0");
            }
            if (_lightOpacitySlider != null)
            {
                _lightOpacitySlider.Value = _settings.LightOpacity;
                _lightOpacityVal.Text = _settings.LightOpacity.ToString("P0");
            }

            FitModeComboWidth();
            SetVisibility(_geometryCard, showAngle || showRadial);
            if (_angleGroup != null) SetVisibility(_angleGroup, showAngle);   // 角度组仅线性（径向无角度）
            SetVisibility(_centerXBox.Parent as StackPanel, showAngle || showRadial);   // 中心 X/Y 行：线性+径向都显示
            SetVisibility(_radiusXBox.Parent as StackPanel, showRadial);
            SetVisibility(_stopPanel, showStops);
            _addStopBtn.Visibility = showStops ? Visibility.Visible : Visibility.Collapsed;
            SetVisibility(_blobPanel, showBlobs);
            _addBlobBtn.Visibility = showBlobs ? Visibility.Visible : Visibility.Collapsed;
            SetVisibility(_listCard, showListCard);

            // 功能2/3：卡片显隐与按钮可用态
            SetVisibility(_harmonyCard, mode != BackgroundMode.Image && mode != BackgroundMode.Solid);
            if (_applyHarmonyGradBtn != null) _applyHarmonyGradBtn.IsEnabled = showStops;
            if (_applyHarmonyMeshBtn != null) _applyHarmonyMeshBtn.IsEnabled = showBlobs;
            // P1c：CSS 卡片在 线性/径向/网格 均可见（原先仅网格）；网格专属按钮（随机光斑/SVG）仅网格显示
            bool showCss = _settings.Mode == BackgroundMode.LinearGradient
                         || _settings.Mode == BackgroundMode.RadialGradient
                         || _settings.Mode == BackgroundMode.MeshGradient;
            SetVisibility(_cssCard, showCss);
            SetVisibility(_randMeshBtn, showBlobs);
            SetVisibility(_exportSvgBtn, showBlobs);
            // 切模式时按当前预览套（_previewDark）刷新 CSS 文本：所见即所导
            if (showCss && _cssBox != null)
            {
                var stops = _settings.GetStops(_previewDark);
                if (_settings.Mode == BackgroundMode.LinearGradient && stops != null && stops.Count > 0)
                    _cssBox.Text = BackgroundSettings.ToCssLinear(_settings.GradientAngle, stops);
                else if (_settings.Mode == BackgroundMode.RadialGradient && stops != null && stops.Count > 0)
                    _cssBox.Text = BackgroundSettings.ToCssRadial(_settings.RadialCenterX, _settings.RadialCenterY, _settings.RadialRadiusX, _settings.RadialRadiusY, stops);
                else if (showBlobs)
                    _cssBox.Text = BackgroundSettings.ToCssGradient(_settings.GetBlobs(_previewDark));
            }

            // 网格底色行（并入颜色卡）：仅网格模式可见
            SetVisibility(_meshBaseRow, showBlobs);
            if (showBlobs) UpdateMeshBaseUi();
            // 色阶生成器：非图片模式可见
            SetVisibility(_sttCard, showColor);
            if (showColor) UpdateShadesTintsTones();
            // 预设色板库：非图片模式可见（纯色/线性/径向/网格都可用）
            SetVisibility(_paletteCard, showColor);
            _editingMeshBase = false;   // 切模式时编辑目标复位为光斑（底色编辑仅网格模式有意义）
            if (_dlToggleRow != null) SetVisibility(_dlToggleRow, _settings.Mode != BackgroundMode.Image);
            if (_imgThemeToggleBtn != null) SetVisibility(_imgThemeToggleBtn, _settings.Mode == BackgroundMode.Image);
            UpdateDlToggleUi();

            // 刷新列表以反映自动选中的停靠点/光斑高亮
            RefreshStopList();
            RefreshBlobList();
            UpdateEditLabel();
            UpdateHarmonySwatches();
        }

        private static void SetVisibility(FrameworkElement el, bool visible)
        {
            if (el != null) el.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        // 同色 SolidColorBrush 缓存（性能优化）：拖拽光斑/停靠点时按「光斑数 × 帧数」持续 new 笔刷，
        // 产生大量短命对象与 Freezable 变更通知。缓存后同色直接复用。
        // 上限保护：缓存条目超过 BrushCacheMax 时整表清空，避免长期运行无界增长（不会无限膨胀）。
        private const int BrushCacheMax = 256;
        private static readonly Dictionary<Color, SolidColorBrush> BrushCache = new Dictionary<Color, SolidColorBrush>();
        private static readonly object BrushCacheLock = new object();

        // 常量笔刷：颜色固定、确定不再修改，进程内共享一份并冻结（位于拖拽热路径，每个 mousemove 都会用到）。
        private static readonly SolidColorBrush AxisStrokeBrush = FrozenBrush(Color.FromArgb(200, 255, 255, 255));
        private static readonly SolidColorBrush LowContrastBrush = FrozenBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));

        private static SolidColorBrush FrozenBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();   // 常量色，永不修改 → 冻结安全，省去每次赋值的 Freezable 变更通知
            return b;
        }

        /// <summary>取一个可复用的同色 SolidColorBrush（已 Freeze，只读使用）。
        /// 调用方只把结果赋给 Fill/Background/Stroke，不再修改笔刷属性，因此冻结是安全的：
        /// 冻结后 WPF 跳过 Freezable 的变更订阅与失效传播，重复赋值开销显著降低。</summary>
        private static SolidColorBrush MakeColorBrush(string hex)
        {
            var color = BackgroundSettings.ParseColor(hex);
            lock (BrushCacheLock)
            {
                SolidColorBrush cached;
                if (BrushCache.TryGetValue(color, out cached)) return cached;
                var brush = new SolidColorBrush(color);
                brush.Freeze();   // 确定不再修改 → 冻结，去掉变更通知开销且可跨线程共享
                if (BrushCache.Count >= BrushCacheMax) BrushCache.Clear();   // 有界缓存，防止无限增长
                BrushCache[color] = brush;
                return brush;
            }
        }

        private void RefreshStopList()
        {
            _settings.EnsureGradientStops();

            // 「分别编辑浅色套」：列表显示活动套（GetStops 读回退，不产生写副作用）
            var list = _settings.GetStops(_previewDark);
            // 删除已不存在的停靠点对应的 UI 行
            var removed = new List<GradientStopSetting>();
            foreach (var kv in _stopRows)
            {
                if (!list.Contains(kv.Key))
                {
                    _stopPanel.Children.Remove(kv.Value);
                    removed.Add(kv.Key);
                }
            }
            foreach (var r in removed) _stopRows.Remove(r);

            // 按当前停靠点顺序重建/调整 UI 行（活动套）
            for (int i = 0; i < list.Count; i++)
            {
                var stop = list[i];
                if (!_stopRows.TryGetValue(stop, out var row))
                {
                    row = CreateStopRow(stop);
                    _stopRows[stop] = row;
                }
                // 确保顺序正确
                int idx = _stopPanel.Children.IndexOf(row);
                if (idx != i)
                {
                    if (idx >= 0) _stopPanel.Children.RemoveAt(idx);
                    _stopPanel.Children.Insert(i, row);
                }
                UpdateStopRow(stop);
            }
        }

        private StackPanel CreateStopRow(GradientStopSetting stop)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

            // 功能1：整行任意位置点击即可选中（不止色块），并刷新高亮
            row.MouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource is Button) return; // 避免与删除按钮冲突
                _editingMeshBase = false;
                _selectedStop = stop;
                _currentColor = BackgroundSettings.ParseColor(stop.Color);
                UpdateColor(_currentColor, true);
                RefreshStopList();
                UpdateEditLabel();
                e.Handled = true;
            };

            var colorBox = new Border
            {
                Width = 28,
                Height = 18,
                CornerRadius = new CornerRadius(3),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };
            colorBox.MouseLeftButtonDown += (s, e) =>
            {
                _selectedStop = stop;
                _currentColor = BackgroundSettings.ParseColor(stop.Color);
                // 高亮会由 UpdateColor -> UpdateStopRow 刷新；同时刷新所有行以清除旧高亮
                UpdateColor(_currentColor, true); // 同步 RGB/HEX/明度滑块
                RefreshStopList();
            };
            row.Children.Add(colorBox);

            var hex = new TextBox
            {
                Width = 70,
                Background = _owner._inputBg,
                Foreground = _owner._inputFg,
                BorderBrush = _owner._panelBorder,
                FontFamily = new FontFamily("Consolas"),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            hex.TextChanged += (s, e) =>
            {
                if (_isUpdating) return;
                // [Q41] 用户输入的 hex 先归一化+校验（NormalizeHexColor 对畸形/不完整返回 null），避免裸 ParseColor 误解析
                var norm = NormalizeHexColor(hex.Text);
                if (norm == null) return;
                var c = BackgroundSettings.ParseColor(norm);
                if (c.A > 0)
                {
                    stop.Color = BackgroundSettings.ColorToHex(c);
                    _hasEditedAfterShow = true;
                    UpdateStopRow(stop);
                    RefreshPreview();
                }
            };
            row.Children.Add(hex);

            row.Children.Add(new TextBlock { Text = "位置:", Foreground = _owner._textMain, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            var slider = new Slider { Minimum = 0, Maximum = 1, Width = 100, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            // 修复（「位置 0% 不对」根因）：滑块初值必须同步停靠点实际 offset——旧实现初值恒 0，
            // 所有「位置」滑块都停在 0%；且一碰滑块就把停靠点位置覆写成 0（改一下就坏了位置）。
            // 在挂 ValueChanged 之前赋值 → 不触发事件、不污染 _hasEditedAfterShow。
            slider.Value = stop.Offset;
            slider.ValueChanged += (s, e) =>
            {
                if (_isUpdating) return;
                stop.Offset = slider.Value;
                _hasEditedAfterShow = true;
                RefreshPreview();
                UpdateEditLabel();
            };
            row.Children.Add(slider);

            var del = new Button
            {
                Content = "×",
                Width = 24,
                Height = 20,
                Background = Brushes.Transparent,
                Foreground = _owner._dangerRed,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold
            };
            del.Click += (s, e) =>
            {
                _settings.GetStops(_previewDark).Remove(stop);
                if (_selectedStop == stop) _selectedStop = _settings.Stops.FirstOrDefault();
                RefreshStopList();
                RefreshPreview();
            };
            row.Children.Add(del);

            return row;
        }

        private void UpdateStopRow(GradientStopSetting stop)
        {
            if (!_stopRows.TryGetValue(stop, out var row)) return;
            bool isSel = stop == _selectedStop;

            // 功能1：整行高亮（保留色块 1px→2px accent 边框并叠加行背景）
            row.Background = isSel ? _owner._rowSelected : Brushes.Transparent;

            var colorBox = (Border)row.Children[0];
            colorBox.Background = MakeColorBrush(stop.Color);
            colorBox.BorderBrush = isSel ? _owner._accent : _owner._panelBorder;
            colorBox.BorderThickness = isSel ? new Thickness(2) : new Thickness(1);

            var hex = (TextBox)row.Children[1];
            if (!hex.IsKeyboardFocused && hex.Text != stop.Color)
                hex.Text = stop.Color;

            var slider = (Slider)row.Children[3];
            if (!slider.IsMouseCaptureWithin && !slider.IsMouseOver)
                slider.Value = stop.Offset;

            UpdateEditLabel();
        }

        private void RefreshBlobList()
        {
            _settings.EnsureMeshBlobs();

            var blobList = _settings.GetBlobs(_previewDark);   // 「分别编辑浅色套」：显示活动套
            var removed = new List<MeshBlobSetting>();
            foreach (var kv in _blobRows)
            {
                if (!blobList.Contains(kv.Key))
                {
                    _blobPanel.Children.Remove(kv.Value);
                    removed.Add(kv.Key);
                }
            }
            foreach (var r in removed) _blobRows.Remove(r);

            for (int i = 0; i < blobList.Count; i++)
            {
                var blob = blobList[i];
                if (!_blobRows.TryGetValue(blob, out var row))
                {
                    row = CreateBlobRow(blob);
                    _blobRows[blob] = row;
                }
                int idx = _blobPanel.Children.IndexOf(row);
                if (idx != i)
                {
                    if (idx >= 0) _blobPanel.Children.RemoveAt(idx);
                    _blobPanel.Children.Insert(i, row);
                }
                UpdateBlobRow(blob);
            }
        }

        private StackPanel CreateBlobRow(MeshBlobSetting blob)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

            // 功能1：整行任意位置点击即可选中光斑
            row.MouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource is Button) return;
                _editingMeshBase = false;   // 点光斑行 = 编辑目标切回光斑
                _selectedBlob = blob;
                _currentColor = BackgroundSettings.ParseColor(blob.Color);
                UpdateColor(_currentColor, true);
                RefreshBlobList();
                UpdateEditLabel();
                UpdateMeshBaseUi();
                e.Handled = true;
            };

            var colorBox = new Border
            {
                Width = 28,
                Height = 18,
                CornerRadius = new CornerRadius(3),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };
            colorBox.MouseLeftButtonDown += (s, e) =>
            {
                _editingMeshBase = false;
                _selectedBlob = blob;
                _currentColor = BackgroundSettings.ParseColor(blob.Color);
                UpdateColor(_currentColor, true); // 同步 RGB/HEX/明度滑块
                RefreshBlobList();
                UpdateMeshBaseUi();
            };
            row.Children.Add(colorBox);

            var hex = new TextBox
            {
                Width = 70,
                Background = _owner._inputBg,
                Foreground = _owner._inputFg,
                BorderBrush = _owner._panelBorder,
                FontFamily = new FontFamily("Consolas"),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            hex.TextChanged += (s, e) =>
            {
                if (_isUpdating) return;
                // [Q41] 用户输入的 hex 先归一化+校验（NormalizeHexColor 对畸形/不完整返回 null），避免裸 ParseColor 误解析
                var norm = NormalizeHexColor(hex.Text);
                if (norm == null) return;
                var c = BackgroundSettings.ParseColor(norm);
                if (c.A > 0)
                {
                    blob.Color = BackgroundSettings.ColorToHex(c);
                    _hasEditedAfterShow = true;
                    UpdateBlobRow(blob);
                    RefreshPreview();
                }
            };
            row.Children.Add(hex);

            row.Children.Add(new TextBlock { Text = "X", Foreground = _owner._textDim, FontSize = 10.0, VerticalAlignment = VerticalAlignment.Center });
            var xBox = new TextBox { Width = 36, Background = _owner._inputBg, Foreground = _owner._inputFg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
            xBox.TextChanged += (s, e) => { if (double.TryParse(xBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) { blob.CenterX = Math.Max(0, Math.Min(1, v)); _hasEditedAfterShow = true; RefreshPreview(); } };
            row.Children.Add(xBox);

            row.Children.Add(new TextBlock { Text = "Y", Foreground = _owner._textDim, FontSize = 10.0, VerticalAlignment = VerticalAlignment.Center });
            var yBox = new TextBox { Width = 36, Background = _owner._inputBg, Foreground = _owner._inputFg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
            yBox.TextChanged += (s, e) => { if (double.TryParse(yBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) { blob.CenterY = Math.Max(0, Math.Min(1, v)); _hasEditedAfterShow = true; RefreshPreview(); } };
            row.Children.Add(yBox);

            row.Children.Add(new TextBlock { Text = "R", Foreground = _owner._textDim, FontSize = 10.0, VerticalAlignment = VerticalAlignment.Center });
            var rBox = new TextBox { Width = 36, Background = _owner._inputBg, Foreground = _owner._inputFg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
            rBox.TextChanged += (s, e) => { if (double.TryParse(rBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) { blob.Radius = Math.Max(0.05, Math.Min(1, v)); _hasEditedAfterShow = true; RefreshPreview(); } };
            row.Children.Add(rBox);

            row.Children.Add(new TextBlock { Text = "A", Foreground = _owner._textDim, FontSize = 10.0, VerticalAlignment = VerticalAlignment.Center });
            var aBox = new TextBox { Width = 36, Background = _owner._inputBg, Foreground = _owner._inputFg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
            aBox.TextChanged += (s, e) => { if (double.TryParse(aBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) { blob.Opacity = Math.Max(0, Math.Min(1, v)); _hasEditedAfterShow = true; RefreshPreview(); } };
            row.Children.Add(aBox);

            var del = new Button
            {
                Content = "×",
                Width = 24,
                Height = 20,
                Background = Brushes.Transparent,
                Foreground = _owner._dangerRed,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold
            };
            del.Click += (s, e) =>
            {
                var src = _settings.GetBlobs(_previewDark);   // 「分别编辑浅色套」：从活动套删除
                src.Remove(blob);
                if (_selectedBlob == blob) _selectedBlob = src.FirstOrDefault();
                RefreshBlobList();
                RefreshPreview();
            };
            row.Children.Add(del);

            return row;
        }

        private void UpdateBlobRow(MeshBlobSetting blob)
        {
            if (!_blobRows.TryGetValue(blob, out var row)) return;
            bool isSel = blob == _selectedBlob;

            // 功能1：整行高亮
            row.Background = isSel ? _owner._rowSelected : Brushes.Transparent;

            var colorBox = (Border)row.Children[0];
            colorBox.Background = MakeColorBrush(blob.Color);
            colorBox.BorderBrush = isSel ? _owner._accent : _owner._panelBorder;
            colorBox.BorderThickness = isSel ? new Thickness(2) : new Thickness(1);

            var hex = (TextBox)row.Children[1];
            if (!hex.IsKeyboardFocused && hex.Text != blob.Color)
                hex.Text = blob.Color;

            var xBox = (TextBox)row.Children[3];
            if (!xBox.IsKeyboardFocused) xBox.Text = blob.CenterX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            var yBox = (TextBox)row.Children[5];
            if (!yBox.IsKeyboardFocused) yBox.Text = blob.CenterY.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            var rBox = (TextBox)row.Children[7];
            if (!rBox.IsKeyboardFocused) rBox.Text = blob.Radius.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            var aBox = (TextBox)row.Children[9];
            if (!aBox.IsKeyboardFocused) aBox.Text = blob.Opacity.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

            UpdateEditLabel();
        }

        /// <summary>syncHsv=false 表示本次改色来自 H/S/V 滑块（滑块是权威值）：不从 RGB 反算 HSV，
        /// 避免「拖 H → 反算出 215.3° → 写回滑块 215」的往返抖动；色轮指示器仍按当前 H/S 移动。</summary>
        private void UpdateColor(Color c, bool updateSliders, bool syncHsv = true)
        {
            _currentColor = c;
            if (!_isUpdating) _hasEditedAfterShow = true;   // 初始化阶段不算编辑；色轮/HEX/RGB/HSV 滑块等真实用户操作均视为编辑
            if (updateSliders)
            {
                _isUpdating = true;
                try
                {
                    _rSlider.Value = c.R;
                    _gSlider.Value = c.G;
                    _bSlider.Value = c.B;
                    _hexBox.Text = BackgroundSettings.ColorToHex(c);
                    if (syncHsv) SyncHsvFromColor(c);       // RGB/HEX/色轮来源：反算 HSV 供 H/S/V 列显示
                    _hSlider.Value = Math.Round(_currentHue);
                    _sSlider.Value = Math.Round(_currentSat * 100);
                    _valSlider.Value = Math.Round(_currentValue * 100);
                    RenderColorWheel();                     // 当前颜色明度可能改变，重绘色轮使指示器下方像素一致
                }
                finally { _isUpdating = false; }
            }
            _rVal.Text = c.R.ToString();
            _gVal.Text = c.G.ToString();
            _bVal.Text = c.B.ToString();
            _hVal.Text = ((int)Math.Round(_currentHue)) + "°";
            _sVal.Text = ((int)Math.Round(_currentSat * 100)) + "%";
            _valVal.Text = ((int)Math.Round(_currentValue * 100)) + "%";

            // 同步颜色格式显示（RGB / HSL / HSV / CMYK）与对比度检查
            SetFmt(_rowRgb, $"{c.R} {c.G} {c.B}");
            BackgroundSettings.RgbToHsl(c.R, c.G, c.B, out double hL, out double sL, out double lL);
            SetFmt(_rowHsl, $"{hL:F1}° {sL * 100:F1}% {lL * 100:F1}%");
            SetFmt(_rowHsv, $"{_currentHue:F1}° {_currentSat * 100:F1}% {_currentValue * 100:F1}%");
            BackgroundSettings.RgbToCmyk(c.R, c.G, c.B, out double cK, out double mK, out double yK, out double kK);
            SetFmt(_rowCmyk, $"{cK * 100:F0}% {mK * 100:F0}% {yK * 100:F0}% {kK * 100:F0}%");   // 紧凑（去逗号省宽）   // 取整（右列变窄后 1 位小数放不下；CMYK 为参考值，1% 精度足够）

            // 修复（对比目标误导）：旧实现非纯色模式拿 SolidColor 当对比目标、纯色模式拿「自身」（恒 1.00:1「不及格」），
            // 且渐变/网格模式下 SolidColor 根本不参与渲染，作为对比目标无意义（默认态首停靠点=SolidColor=#16E0BD 时全撞 1.00:1）。
            // 现统一对比【主题主文字色】：语义 = 这个颜色作为窗口背景/该色区时，窗口主文字是否可读——
            // 5 种模式都成立，且永不出现「自己比自己」。
            var textOnBg = _owner._textMain != null ? _owner._textMain.Color : Colors.White;
            double ratio = ContrastRatio(c, textOnBg);
            string verdict = ratio >= 7.0 ? "极佳" : ratio >= 4.5 ? "合格" : ratio >= 3.0 ? "可接受" : "不及格";   // 短文案：括号注释移除（超长会裁右缘），完整评级说明在 Tooltip
            _contrastText.Text = $"对比度 {ratio:F2}:1 — {verdict}";   // 短文案（「对主文字」说明挪到 Tooltip）
            // 性能优化：低对比度提示色是常量，原来每次 UpdateColor（拖拽时每帧）都 new 一支笔刷。
            _contrastText.Foreground = ratio >= 4.5 ? _owner._successGreen ?? _owner._textDim : LowContrastBrush;

            // 如果当前有选中的 stop/blob，同步颜色并局部刷新该行的颜色块/HEX，避免重建整个列表导致滑块丢失焦点
            if (_editingMeshBase && _settings.Mode == BackgroundMode.MeshGradient)
            {
                // 编辑目标=网格底色（点底色色块选中）：色轮/HEX/滑块直接写底色（活动套）
                WriteMeshBaseHex(BackgroundSettings.ColorToHex(c));
                UpdateMeshBaseUi();
            }
            else
            {
                if (_selectedStop != null)
                {
                    _selectedStop.Color = BackgroundSettings.ColorToHex(c);
                    UpdateStopRow(_selectedStop);
                }
                if (_selectedBlob != null)
                {
                    _selectedBlob.Color = BackgroundSettings.ColorToHex(c);
                    UpdateBlobRow(_selectedBlob);
                }
            }

            // 纯色模式直接同步（「分别编辑浅色套」：写入落活动套，浅色套未设先克隆再写）
            if (_settings.Mode == BackgroundMode.Solid)
            {
                WriteSolidHex(BackgroundSettings.ColorToHex(c));
            }

            UpdateWheelIndicator();
            UpdateEditLabel();
            UpdateHarmonySwatches();
            UpdateShadesTintsTones();
            RefreshPreview();
        }

        /// <summary>
        /// 统一返回色轮的圆心 (cx, cy) 与最大半径 maxR。
        /// 三处（渲染 / 取色 / 指示器定位）必须共用同一圆心，否则会与
        /// Clip=EllipseGeometry(new Rect(0,0,200,200)) 的圆心 (100,100) 错位，导致色轮四周内缩、右侧不圆。
        /// 圆心必须取 width/2.0（=90），禁止取 (width-1)/2（=89.5，会让列 x=0/x=179 整列留空、圆边缘内缩 1px）。
        /// </summary>
        private (double cx, double cy, double maxR) GetWheelGeometry(double width, double height)
        {
            double cx = width / 2.0, cy = height / 2.0;
            double maxR = Math.Min(cx, cy);
            return (cx, cy, maxR);
        }

        /// <summary>
        /// 请求重绘色轮（合并版）。原实现同步重算 32,400 个像素，而明度变更一帧内会走
        /// 「明度滑块 → UpdateColor」两条路径各调一次，取色拖拽时每个 mousemove 也调一次。
        /// 这里改为：同一帧内只排队一次真实渲染（Dispatcher.BeginInvoke + 标志位），
        /// 渲染前再判断内容是否真的变化（见 DoRenderColorWheel），从而消除绝大部分重绘。
        /// </summary>
        private void RenderColorWheel()
        {
            if (_wheelRenderScheduled) return;   // 本帧已有待执行的重绘请求，合并
            _wheelRenderScheduled = true;
            Dispatcher.BeginInvoke(new Action(DoRenderColorWheel),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        private void DoRenderColorWheel()
        {
            _wheelRenderScheduled = false;
            if (_wheelImage == null) return;
            // 色轮恒亮（对齐参考站 online-color/gradients.app 的 H/S 轮机制）：像素只由角度→色相、
            // 半径→饱和度决定，明度恒取 1.0。旧实现像素绑定当前 V——用户当前色很暗（如 #10151C，
            // V=11%）时整个轮盘渲染成纯黑、无法取色。现亮度只由 V 滑块控制，轮盘永远鲜艳可用。
            // 像素与选取完全无关 → 首绘后永久复用（32,400 次 HsvToRgb 只跑一次）。
            if (_wheelBitmap != null && _wheelRenderedValue.Equals(WheelBrightness)) return;

            int w = 200, h = 200;   // 位图与控件同步 180→200（缓存尺寸变更自动重分配）
            // 复用位图与像素数组：尺寸不变时不重新分配，避免每次重绘产生 129,600 字节垃圾。
            if (_wheelBitmap == null || _wheelBmpW != w || _wheelBmpH != h)
            {
                _wheelBitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
                _wheelPixels = new byte[h * (w * 4)];
                _wheelBmpW = w;
                _wheelBmpH = h;
            }
            var wb = _wheelBitmap;
            byte[] pixels = _wheelPixels;
            int stride = w * 4;
            // 色轮圆心/半径统一由 GetWheelGeometry 计算，确保与 Clip=EllipseGeometry(Rect(0,0,200,200)) 完全重合。
            var (cx, cy, maxR) = GetWheelGeometry(w, h);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double dx = x - cx, dy = y - cy;
                    double r = Math.Sqrt(dx * dx + dy * dy);
                    int idx = (y * w + x) * 4;
                    if (r <= maxR)
                    {
                        double hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                        double sat = r / maxR;
                        var c = HsvToRgb(hue, sat, WheelBrightness);
                        pixels[idx] = c.B;
                        pixels[idx + 1] = c.G;
                        pixels[idx + 2] = c.R;
                        pixels[idx + 3] = 255;
                    }
                    else
                    {
                        pixels[idx] = 0;
                        pixels[idx + 1] = 0;
                        pixels[idx + 2] = 0;
                        pixels[idx + 3] = 0;
                    }
                }
            }
            wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
            if (!ReferenceEquals(_wheelImage.Source, wb))
                _wheelImage.Source = wb;   // 复用同一位图实例，避免反复换 Source 触发重新解码/重绘
            _wheelRenderedValue = WheelBrightness;
        }

        /// <summary>在色轮上绘制当前颜色位置（小圆环），让用户知道当前选的是色轮上哪一点。</summary>
        private void UpdateWheelIndicator()
        {
            if (_wheelIndicator == null || _wheelOverlay == null) return;
            var (cx, cy, maxR) = GetWheelGeometry(_wheelHost.Width, _wheelHost.Height);
            // 极坐标 -> 笛卡尔坐标：角度 0° 在右，顺时针增加
            double rad = _currentHue * Math.PI / 180;
            double r = _currentSat * maxR;
            double x = cx + r * Math.Cos(rad) - _wheelIndicator.Width / 2;
            double y = cy + r * Math.Sin(rad) - _wheelIndicator.Height / 2;
            Canvas.SetLeft(_wheelIndicator, x);
            Canvas.SetTop(_wheelIndicator, y);

            // 根据当前颜色亮度自动调整指示器描边，避免在极端明暗处看不清
            byte lum = (byte)((_currentColor.R * 299 + _currentColor.G * 587 + _currentColor.B * 114) / 1000);
            _wheelIndicator.Stroke = lum > 128 ? Brushes.Black : Brushes.White;
            _wheelIndicator.Visibility = _settings.Mode == BackgroundMode.Image ? Visibility.Collapsed : Visibility.Visible;
        }

        private void RefreshPreview()
        {
            try
            {
                // 修复：停靠点 Offset 可能来自外部配置/导入的 CSS，越界会让 GradientStop 构造抛异常；
                // 统一钳制到 [0,1]（与拖拽、滑块、预设等其它写入路径的钳制保持一致）。
                if (_settings.Stops != null)
                {
                    foreach (var st in _settings.Stops)
                        st.Offset = Clamp(st.Offset, 0, 1);
                }

                if (_settings.Mode == BackgroundMode.Image)
                {
                    // 图片模式：BuildBrushFrom 对 Image 返回 Transparent（主窗口用独立 BgImage 控件渲染），
                    // 预览区也用独立 Image 元素展示真实背景图，与主窗口行为一致。
                    // 关键修复：切回图片模式时必须先清空底色 _previewRect.Fill。否则之前 Linear/Radial/Mesh
                    // 模式留下的渐变/网格底色会透过 Stretch.Uniform 的留白区显示出来，导致底部预览的「背景图」
                    // 与实际主窗口（图片覆盖整窗、留白处为窗口底色 _windowBg）不一致。底色复位为窗口底色，
                    // 与 ApplyShellColors 中 Background = _windowBg 对齐。
                    _previewRect.Fill = _owner._windowBg;
                    RefreshImageModePreviews();
                }
                else
                {
                    // 「深浅背景切换」：预览按「当前预览套」渲染（_previewDark=false 取浅色套，空则回退深色套）。
                    // 网格光斑层同源刷新（与主窗口 BgBlobs 同一 PopulateBlobCanvas + 取套逻辑）。
                    _previewRect.Fill = _owner.BuildBackgroundBrushPreview(_settings, _previewDark);
                    if (_settings.Mode == BackgroundMode.MeshGradient && _blobCanvas != null && _blobCanvas.Width > 1)
                        MainWindow.PopulateBlobCanvas(_blobCanvas, _settings.GetBlobs(_previewDark), _blobCanvas.Width, _blobCanvas.Height);
                    else if (_settings.Mode != BackgroundMode.MeshGradient)
                        _blobCanvas?.Children.Clear();
                }
            }
            catch (Exception ex)
            {
                // 修复：原来只写 Debug 输出，参数非法时预览静默停在旧图、用户完全无感知。
                // 现在把错误同时写到窗口底部的状态条，让用户看得见。
                System.Diagnostics.Debug.WriteLine("[BgDlg] preview error: " + ex.Message);
                SetCssStatus("预览刷新失败（参数可能超出范围）：" + ex.Message);
            }
            RefreshOverlayHandles();
        }

        /// <summary>
        /// 图片模式预览刷新（修正：原注释称「双预览刷新：同时加载深色/浅色背景图」，
        /// 实现只刷新右列的一个大预览 _imgBigPreview，按 _owner.IsDarkMode 二选一显示
        /// 深色或浅色背景图，并不存在并排的两个预览）。加载含自定义路径 → 内置回退，
        /// 并应用对应主题各自的透明度。
        /// </summary>
        private void RefreshImageModePreviews()
        {
            // 深浅背景切换走主窗口右上角单按钮（🌙/☀️，共用）：本弹窗模态期间主窗口禁用、
            // 大预览按打开时的主题显示对应背景图；关闭后点主窗口按钮切换主题，背景图随 ApplyShellColors 换套。
            bool dark = _owner.IsDarkMode;
            string path = dark ? _settings.DarkPath : _settings.LightPath;
            double opacity = dark ? _settings.DarkOpacity : _settings.LightOpacity;
            LoadImagePreview(_imgBigPreview, _imgBigPlaceholder, path, dark, opacity);
        }


        /// <summary>
        /// 透明度快路径：大预览已经显示着本滑块对应的图时，只改 Opacity 即可。
        /// 大预览跟随 _owner.IsDarkMode 选图，因此 isDark == IsDarkMode 即代表当前显示的就是它。
        /// 返回 false 表示尚未加载（或显示的不是这张），调用方需走完整的 RefreshImageModePreviews。
        /// </summary>
        private bool ApplyImageOpacityOnly(bool isDark, double opacity)
        {
            if (_imgBigPreview == null || _imgBigPreview.Source == null) return false;
            if (isDark != _owner.IsDarkMode) return false;
            _imgBigPreview.Opacity = Math.Max(0.05, Math.Min(1.0, opacity));
            return true;
        }

        // 已解码图片缓存（性能优化）：key = 背景图路径，value = 解码结果 + 回退提示文本。
        // 原来每次预览刷新都重新解码一次 PNG（拖透明度滑块时每个 ValueChanged 就一次），
        // 缓存后同一张图只解码一次。条目上限保护，避免反复换图导致无界增长。
        private const int ImageCacheMax = 8;
        private sealed class PreviewImageEntry
        {
            public ImageSource Source;
            public string Note;
        }
        private readonly Dictionary<string, PreviewImageEntry> _imageSourceCache =
            new Dictionary<string, PreviewImageEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>按路径取已解码的 ImageSource（优先命中缓存），失败时回退到内置默认背景图。
        /// note 返回非空表示用的是内置回退图（原路径无效）。</summary>
        private ImageSource GetPreviewImageSource(string path, bool dark, out string note)
        {
            PreviewImageEntry entry;
            if (_imageSourceCache.TryGetValue(path ?? "", out entry))
            {
                note = entry.Note;      // 与首次加载时保持完全一致
                return entry.Source;
            }

            var loaded = MainWindow.TryLoadImagePublic(path);
            note = null;
            if (loaded == null)
            {
                if (!string.IsNullOrWhiteSpace(path))
                    note = "背景图片路径无效或文件不存在，当前显示内置默认背景";
                // 回退到内置默认背景（与主窗口 ApplyShellColors 相同的回退链）
                try
                {
                    var uri = new Uri(dark
                        ? "pack://application:,,,/系统清理与优化工具;component/background.png"
                        : "pack://application:,,,/系统清理与优化工具;component/background-light.png",
                        UriKind.Absolute);
                    loaded = new BitmapImage(uri);
                    loaded.Freeze();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[BgDlg] fallback bg load failed: " + ex.Message);
                }
            }
            if (loaded != null)
            {
                // 解码结果可能未冻结（TryLoadImagePublic 内部已 Freeze，此处兜底），
                // 冻结后可安全跨帧复用并省去变更通知开销。
                var freezable = loaded as Freezable;
                if (freezable != null && freezable.CanFreeze) freezable.Freeze();
                if (_imageSourceCache.Count >= ImageCacheMax) _imageSourceCache.Clear();   // 有界缓存
                _imageSourceCache[path ?? ""] = new PreviewImageEntry { Source = loaded, Note = note };
            }
            return loaded;
        }

        private void LoadImagePreview(Image img, TextBlock placeholder, string path, bool dark, double opacity)
        {
            string note;
            var loaded = GetPreviewImageSource(path, dark, out note);

            if (loaded != null)
            {
                if (!ReferenceEquals(img.Source, loaded))
                    img.Source = loaded;   // 命中缓存时是同一实例，避免重复赋值触发重新解码/重绘
                img.Opacity = Math.Max(0.05, Math.Min(1.0, opacity));
                img.Visibility = Visibility.Visible;
                placeholder.Text = note ?? "当前使用的背景图";
                placeholder.Visibility = note != null ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                img.Source = null;
                img.Visibility = Visibility.Collapsed;
                placeholder.Text = "未选择背景图片";
                placeholder.Visibility = Visibility.Visible;
            }
        }

        // 在右侧预览区绘制网格渐变光斑的拖拽句柄；仅 Mesh 模式可见。
        private void RefreshMeshHandles()
        {
            if (_previewOverlay == null) return;
            if (_settings.Mode != BackgroundMode.MeshGradient || _settings.Blobs == null)
            {
                _previewOverlay.Children.Clear();
                _blobHandles.Clear();
                if (_blobCanvas != null) _blobCanvas.Children.Clear();
                return;
            }

            double w = Math.Max(1, _previewOverlay.ActualWidth);
            double h = Math.Max(1, _previewOverlay.ActualHeight);

            // 光斑渲染层：与主窗口共用 PopulateBlobCanvas（底色由 _previewRect 的 SolidColorBrush 铺满）
            if (_blobCanvas != null)
            {
                MainWindow.PopulateBlobCanvas(_blobCanvas, _settings.Blobs, w, h);
            }

            // 删除已不存在的句柄（先 -= 再从 dict 移除，避免 Ellipse 仍引用 handler 导致订阅泄漏）
            var removed = new List<MeshBlobSetting>();
            foreach (var kv in _blobHandles)
            {
                if (!_settings.Blobs.Contains(kv.Key))
                {
                    kv.Value.MouseLeftButtonDown -= BlobHandle_MouseLeftButtonDown;
                    _previewOverlay.Children.Remove(kv.Value);
                    removed.Add(kv.Key);
                }
            }
            foreach (var r in removed) _blobHandles.Remove(r);

            // 创建或更新句柄
            foreach (var blob in _settings.Blobs)
            {
                if (!_blobHandles.TryGetValue(blob, out var ellipse))
                {
                    ellipse = new Ellipse
                    {
                        Width = 14,
                        Height = 14,
                        StrokeThickness = 2,
                        Cursor = Cursors.SizeAll,
                        IsHitTestVisible = true
                    };
                    ellipse.MouseLeftButtonDown += BlobHandle_MouseLeftButtonDown;
                    _blobHandles[blob] = ellipse;
                    _previewOverlay.Children.Add(ellipse);
                }

                bool isSel = blob == _selectedBlob;
                ellipse.Stroke = isSel ? _owner._accent : Brushes.White;
                ellipse.Fill = MakeColorBrush(blob.Color);
                ellipse.Opacity = 0.7 + blob.Opacity * 0.3;

                Canvas.SetLeft(ellipse, blob.CenterX * w - ellipse.Width / 2);
                Canvas.SetTop(ellipse, blob.CenterY * h - ellipse.Height / 2);
                ellipse.Visibility = Visibility.Visible;
            }
        }

        private void BlobHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is Ellipse ellipse)) return;
            var blob = _blobHandles.FirstOrDefault(kv => kv.Value == ellipse).Key;
            if (blob == null) return;

            _selectedBlob = blob;
            _selectedStop = null;
            _currentColor = BackgroundSettings.ParseColor(blob.Color);
            UpdateColor(_currentColor, true);
            RefreshBlobList();
            UpdateEditLabel();

            _draggingBlob = blob;
            _dragStartMouse = e.GetPosition(_previewOverlay);
            _dragStartBlobCenter = new Point(blob.CenterX, blob.CenterY);
            _previewOverlay.CaptureMouse();
            e.Handled = true;
        }

        private void PreviewOverlay_MouseMove(object sender, MouseEventArgs e)
        {
            // 网格光斑拖拽（保持原逻辑）
            if (_draggingBlob != null)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    var pos = e.GetPosition(_previewOverlay);
                    double dx = (pos.X - _dragStartMouse.X) / Math.Max(1, _previewOverlay.ActualWidth);
                    double dy = (pos.Y - _dragStartMouse.Y) / Math.Max(1, _previewOverlay.ActualHeight);
                    _draggingBlob.CenterX = Math.Max(0, Math.Min(1, _dragStartBlobCenter.X + dx));
                    _draggingBlob.CenterY = Math.Max(0, Math.Min(1, _dragStartBlobCenter.Y + dy));
                    _hasEditedAfterShow = true;   // 拖动光斑也算编辑
                    UpdateBlobRow(_draggingBlob);
                    // 性能优化：RefreshPreview() 末尾会调 RefreshOverlayHandles() →（Mesh 模式）
                    // RefreshMeshHandles()，即已经重建了一次光斑层与句柄。原来这里又显式调一次
                    // RefreshMeshHandles()，导致每个 mousemove 执行两次 PopulateBlobCanvas +
                    // 两次句柄重建。删掉外部这次调用，保证一个 mousemove 只重建一次。
                    RefreshPreview();
                }
                return;
            }
            if (e.LeftButton != MouseButtonState.Pressed) return;

            var pos2 = e.GetPosition(_previewOverlay);
            double w = Math.Max(1, _previewOverlay.ActualWidth);
            double h = Math.Max(1, _previewOverlay.ActualHeight);

            if (_draggingStop != null)
            {
                if (_settings.Mode == BackgroundMode.LinearGradient)
                {
                    double mbx = pos2.X / w - _settings.LinearCenterX;
                    double mby = pos2.Y / h - _settings.LinearCenterY;
                    double c = Math.Cos(_linearAngleRad), s = Math.Sin(_linearAngleRad);
                    // 投影到渐变轴方向 dir=(c,-s)（标准约定 90°=上，屏幕 y 向下），offset = 投影长度 + 0.5
                    double ux = mbx * c - mby * s;
                    _draggingStop.Offset = Clamp(ux + 0.5, 0, 1);
                }
                else if (_settings.Mode == BackgroundMode.RadialGradient)
                {
                    double cx = _settings.RadialCenterX * w, cy = _settings.RadialCenterY * h;
                    double rx = Math.Max(2, _settings.RadialRadiusX * w), ry = Math.Max(2, _settings.RadialRadiusY * h);
                    double dx = pos2.X - cx, dy = pos2.Y - cy;
                    // 投影到 45° 椭圆半径方向 (rx·K45, −ry·K45)：offset = (dx·rx − dy·ry) / (K45·(rx²+ry²))
                    double denom = K45 * (rx * rx + ry * ry);
                    _draggingStop.Offset = Clamp((dx * rx - dy * ry) / Math.Max(1, denom), 0, 1);
                }
                _hasEditedAfterShow = true;
                UpdateStopRow(_draggingStop);
                UpdateEditLabel();   // 拖动停靠点时「位置 X%」必须实时更新（旧实现只在改色/切模式刷新，拖动后标签停在旧值）
                RefreshOverlayHandles();
                RefreshPreview();
                return;
            }
            if (_draggingLineEnd)
            {
                double dxc = pos2.X / w - _settings.LinearCenterX;
                double dyc = pos2.Y / h - _settings.LinearCenterY;
                double angDeg = Math.Atan2(-dyc, dxc) * 180.0 / Math.PI;   // 标准约定：90°=上（屏幕 y 向下）
                // 45° 吸附（gradients.app 惯例）：距 45° 倍数 3° 以内时锁定，方便对正
                double snap = Math.Round(angDeg / 45.0) * 45.0;
                if (Math.Abs(angDeg - snap) < 3.0) angDeg = snap;
                _settings.GradientAngle = angDeg;
                _linearAngleRad = angDeg * Math.PI / 180.0;
                _hasEditedAfterShow = true;
                SyncGeometryBoxes();
                RefreshOverlayHandles();
                RefreshPreview();
                return;
            }
            if (_draggingLineCenter)
            {
                double nc = _dragStartLineCenter.X + (pos2.X - _dragStartMouse.X) / w;
                double ny = _dragStartLineCenter.Y + (pos2.Y - _dragStartMouse.Y) / h;
                _settings.LinearCenterX = Clamp(nc, 0, 1);
                _settings.LinearCenterY = Clamp(ny, 0, 1);
                _hasEditedAfterShow = true;
                SyncGeometryBoxes();
                RefreshOverlayHandles();
                RefreshPreview();
                return;
            }
            if (_draggingRadialCenter)
            {
                _settings.RadialCenterX = Clamp(pos2.X / w, 0, 1);
                _settings.RadialCenterY = Clamp(pos2.Y / h, 0, 1);
                _hasEditedAfterShow = true;
                SyncGeometryBoxes();
                RefreshOverlayHandles();
                RefreshPreview();
                return;
            }
            if (_draggingRadiusX)
            {
                double cx = _settings.RadialCenterX * w;
                _settings.RadialRadiusX = Clamp((pos2.X - cx) / w, 0.02, 2.0);
                _hasEditedAfterShow = true;
                SyncGeometryBoxes();
                RefreshOverlayHandles();
                RefreshPreview();
                return;
            }
            if (_draggingRadiusY)
            {
                double cy = _settings.RadialCenterY * h;
                _settings.RadialRadiusY = Clamp((pos2.Y - cy) / h, 0.02, 2.0);
                _hasEditedAfterShow = true;
                SyncGeometryBoxes();
                RefreshOverlayHandles();
                RefreshPreview();
                return;
            }
        }

        private void PreviewOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            bool handled = false;
            if (_draggingBlob != null) { _draggingBlob = null; handled = true; }
            if (_draggingStop != null) { _draggingStop = null; handled = true; }
            if (_draggingLineEnd) { _draggingLineEnd = false; handled = true; }
            if (_draggingLineCenter) { _draggingLineCenter = false; handled = true; }
            if (_draggingRadialCenter) { _draggingRadialCenter = false; handled = true; }
            if (_draggingRadiusX) { _draggingRadiusX = false; handled = true; }
            if (_draggingRadiusY) { _draggingRadiusY = false; handled = true; }
            if (handled)
            {
                if (_previewOverlay.IsMouseCaptured) _previewOverlay.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        // ---- 线性/径向预览交互：几何参数 + 停靠点拖拽 ----

        private void RefreshOverlayHandles()
        {
            if (_previewOverlay == null) return;
            foreach (var el in _gradientOverlayItems) _previewOverlay.Children.Remove(el);
            _gradientOverlayItems.Clear();

            if (_settings.Mode == BackgroundMode.MeshGradient)
            {
                RefreshMeshHandles();
                return;
            }

            // 非 mesh：清掉残留的 mesh 句柄与光斑层
            foreach (var kv in _blobHandles) _previewOverlay.Children.Remove(kv.Value);
            _blobHandles.Clear();
            if (_blobCanvas != null) _blobCanvas.Children.Clear();

            double w = Math.Max(1, _previewOverlay.ActualWidth);
            double h = Math.Max(1, _previewOverlay.ActualHeight);
            if (_settings.Mode == BackgroundMode.LinearGradient) DrawLinearHandles(w, h);
            else if (_settings.Mode == BackgroundMode.RadialGradient) DrawRadialHandles(w, h);
        }

        private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

        private void SyncGeometryBoxes()
        {
            try
            {
                _isUpdating = true;
                if (_angleBox != null) _angleBox.Text = Math.Round(_settings.GradientAngle).ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (_centerXBox != null) _centerXBox.Text = GetCenterX().ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                if (_centerYBox != null) _centerYBox.Text = GetCenterY().ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                if (_radiusXBox != null) _radiusXBox.Text = _settings.RadialRadiusX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                if (_radiusYBox != null) _radiusYBox.Text = _settings.RadialRadiusY.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            }
            finally { _isUpdating = false; }
        }

        private void DrawLinearHandles(double w, double h)
        {
            _settings.EnsureGradientStops();
            double ang = _settings.GradientAngle * Math.PI / 180.0;
            _linearAngleRad = ang;
            double cx = _settings.LinearCenterX, cy = _settings.LinearCenterY;
            // 标准角度约定（与 WPF Angle / CSS 一致：0°=右、90°=上）：屏幕 y 向下，方向向量 = (cos, -sin)。
            // 旧实现用 +sin（90°=下），导致 45° 时 0% 停点画在上侧、与导出的 CSS 45deg 方向相反（所见≠所导）。
            double dx = Math.Cos(ang), dy = -Math.Sin(ang);
            double sx = (cx - 0.5 * dx) * w, sy = (cy - 0.5 * dy) * h;   // 线起点 = 中心 - 0.5·方向
            double ex = (cx + 0.5 * dx) * w, ey = (cy + 0.5 * dy) * h;   // 线终点 = 中心 + 0.5·方向

            var axis = new Line
            {
                X1 = sx, Y1 = sy, X2 = ex, Y2 = ey,
                // 性能优化：轴线颜色是常量，原来每个 mousemove 都 new 一支笔刷；改为共享的静态冻结笔刷。
                Stroke = AxisStrokeBrush,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 4 },
                IsHitTestVisible = false   // 纯显示；命中交给下方 16px 隐形走廊
            };
            _previewOverlay.Children.Add(axis);
            _gradientOverlayItems.Add(axis);

            // 「直接拖虚线 = 旋转」（Figma/gradients.app 惯例）：虚线上叠加 16px 隐形命中走廊——
            // 线身任何位置一抓就能转（角度=中心到鼠标的向量），不必精确点端点小圆。
            var axisHit = new Line
            {
                X1 = sx, Y1 = sy, X2 = ex, Y2 = ey,
                Stroke = Brushes.Transparent,
                StrokeThickness = 16,
                Cursor = Cursors.SizeAll,
                IsHitTestVisible = true
            };
            axisHit.MouseLeftButtonDown += (s2, e2) =>
            {
                _draggingLineEnd = true;   // 旋转分支
                _previewOverlay.CaptureMouse();
                e2.Handled = true;
            };
            _previewOverlay.Children.Add(axisHit);
            _gradientOverlayItems.Add(axisHit);

            // 中心点 = 平移渐变中心（轴线改为旋转后，平移的独立句柄；同径向模式圆心点）
            var cDot = new Ellipse
            {
                Width = 14, Height = 14,
                StrokeThickness = 2,
                Cursor = Cursors.SizeAll,
                IsHitTestVisible = true,
                Fill = _owner._accent,
                Stroke = Brushes.White
            };
            Canvas.SetLeft(cDot, cx * w - 7);
            Canvas.SetTop(cDot, cy * h - 7);
            cDot.MouseLeftButtonDown += (s2, e2) =>
            {
                _draggingLineCenter = true;
                _dragStartMouse = e2.GetPosition(_previewOverlay);
                _dragStartLineCenter = new Point(_settings.LinearCenterX, _settings.LinearCenterY);
                _previewOverlay.CaptureMouse();
                e2.Handled = true;
            };
            _previewOverlay.Children.Add(cDot);
            _gradientOverlayItems.Add(cDot);

            foreach (var stop in _settings.GetStops(_previewDark).OrderBy(x => x.Offset))   // 停点圆点跟活动套
            {
                double t = stop.Offset;
                double px = (cx + (t - 0.5) * dx) * w;
                double py = (cy + (t - 0.5) * dy) * h;
                var dot = new Ellipse
                {
                    Width = 16, Height = 16,
                    StrokeThickness = 2,
                    Cursor = Cursors.SizeAll,
                    IsHitTestVisible = true,
                    Fill = MakeColorBrush(stop.Color),
                    Stroke = stop == _selectedStop ? _owner._accent : Brushes.White,
                    Opacity = 0.92
                };
                Canvas.SetLeft(dot, px - 8);
                Canvas.SetTop(dot, py - 8);
                dot.MouseLeftButtonDown += (s, e) =>
                {
                    _selectedStop = stop;
                    _selectedBlob = null;
                    _currentColor = BackgroundSettings.ParseColor(stop.Color);
                    UpdateColor(_currentColor, true);
                    RefreshStopList();
                    UpdateEditLabel();
                    _draggingStop = stop;
                    _previewOverlay.CaptureMouse();
                    e.Handled = true;
                };
                _previewOverlay.Children.Add(dot);
                _gradientOverlayItems.Add(dot);
            }

            // 旋转 = 拖上方虚线命中走廊（含 45° 吸附）；停点圆 = 改位置；中心点 = 平移。不再单独画旋转小圆。
        }

        private Ellipse AddDot(double x, double y, Brush stroke, MouseButtonEventHandler down)
        {
            // 16px + 透明填充整圆可命中 + SizeAll 光标：旋转句柄要比停点圆（16px）好抓、意图明确
            var dot = new Ellipse
            {
                Width = 16, Height = 16,
                StrokeThickness = 2,
                Cursor = Cursors.SizeAll,
                IsHitTestVisible = true,
                Fill = Brushes.Transparent,
                Stroke = stroke
            };
            Canvas.SetLeft(dot, x - 8);
            Canvas.SetTop(dot, y - 8);
            dot.MouseLeftButtonDown += down;
            _previewOverlay.Children.Add(dot);
            return dot;
        }

        private void DrawRadialHandles(double w, double h)
        {
            _settings.EnsureGradientStops();
            double cx = _settings.RadialCenterX * w;
            double cy = _settings.RadialCenterY * h;
            double rx = Math.Max(2, _settings.RadialRadiusX * w);
            double ry = Math.Max(2, _settings.RadialRadiusY * h);

            var ring = new Ellipse
            {
                Width = rx * 2, Height = ry * 2,
                // 性能优化：轴线颜色是常量，原来每个 mousemove 都 new 一支笔刷；改为共享的静态冻结笔刷。
                Stroke = AxisStrokeBrush,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 4 },
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(ring, cx - rx);
            Canvas.SetTop(ring, cy - ry);
            _previewOverlay.Children.Add(ring);
            _gradientOverlayItems.Add(ring);

            var center = new Ellipse
            {
                Width = 14, Height = 14,
                StrokeThickness = 2,
                Cursor = Cursors.SizeAll,
                IsHitTestVisible = true,
                Fill = _owner._accent,
                Stroke = Brushes.White
            };
            Canvas.SetLeft(center, cx - 7);
            Canvas.SetTop(center, cy - 7);
            center.MouseLeftButtonDown += (s, e) => { _draggingRadialCenter = true; _previewOverlay.CaptureMouse(); e.Handled = true; };
            _previewOverlay.Children.Add(center);
            _gradientOverlayItems.Add(center);

            var hx = new Ellipse
            {
                Width = 12, Height = 12,
                StrokeThickness = 2,
                Cursor = Cursors.SizeWE,
                IsHitTestVisible = true,
                Fill = Brushes.White,
                Stroke = _owner._accent
            };
            Canvas.SetLeft(hx, cx + rx - 6);
            Canvas.SetTop(hx, cy - 6);
            hx.MouseLeftButtonDown += (s, e) => { _draggingRadiusX = true; _previewOverlay.CaptureMouse(); e.Handled = true; };
            _previewOverlay.Children.Add(hx);
            _gradientOverlayItems.Add(hx);

            var hy = new Ellipse
            {
                Width = 12, Height = 12,
                StrokeThickness = 2,
                Cursor = Cursors.SizeNS,
                IsHitTestVisible = true,
                Fill = Brushes.White,
                Stroke = _owner._accent
            };
            Canvas.SetLeft(hy, cx - 6);
            Canvas.SetTop(hy, cy + ry - 6);
            hy.MouseLeftButtonDown += (s, e) => { _draggingRadiusY = true; _previewOverlay.CaptureMouse(); e.Handled = true; };
            _previewOverlay.Children.Add(hy);
            _gradientOverlayItems.Add(hy);

            // 停靠点句柄：沿 45° 半径方向分布（offset 0=中心，1=边缘），与中心/半径句柄错开避免重叠
            foreach (var stop in _settings.GetStops(_previewDark).OrderBy(x => x.Offset))   // 停点圆点跟活动套
            {
                double px = cx + stop.Offset * rx * K45;
                double py = cy - stop.Offset * ry * K45;
                var dot = new Ellipse
                {
                    Width = 16, Height = 16,
                    StrokeThickness = 2,
                    Cursor = Cursors.SizeAll,
                    IsHitTestVisible = true,
                    Fill = MakeColorBrush(stop.Color),
                    Stroke = stop == _selectedStop ? _owner._accent : Brushes.White,
                    Opacity = 0.92
                };
                Canvas.SetLeft(dot, px - 8);
                Canvas.SetTop(dot, py - 8);
                dot.MouseLeftButtonDown += (s, e) =>
                {
                    _selectedStop = stop;
                    _selectedBlob = null;
                    _currentColor = BackgroundSettings.ParseColor(stop.Color);
                    UpdateColor(_currentColor, true);
                    RefreshStopList();
                    UpdateEditLabel();
                    _draggingStop = stop;
                    _previewOverlay.CaptureMouse();
                    e.Handled = true;
                };
                _previewOverlay.Children.Add(dot);
                _gradientOverlayItems.Add(dot);
            }
        }

        private static Color HsvToRgb(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromRgb(
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }

        private static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
        {
            double rn = r / 255.0, gn = g / 255.0, bn = b / 255.0;
            double max = Math.Max(rn, Math.Max(gn, bn));
            double min = Math.Min(rn, Math.Min(gn, bn));
            double d = max - min;
            v = max;
            if (d < 1e-9) { h = 0; s = 0; return; }
            s = max <= 1e-9 ? 0 : d / max;
            if (max == rn) h = 60 * (((gn - bn) / d) % 6);
            else if (max == gn) h = 60 * ((bn - rn) / d + 2);
            else h = 60 * ((rn - gn) / d + 4);
            if (h < 0) h += 360;
        }

        private void SyncHsvFromColor(Color c)
        {
            RgbToHsv(c.R, c.G, c.B, out double h, out double s, out double v);
            _currentHue = h; _currentSat = s; _currentValue = v;
        }

        private void SyncColorFromHsv()
        {
            var c = HsvToRgb(_currentHue, _currentSat, _currentValue);
            UpdateColor(c, true);
        }

        // ===================== 功能1：正在编辑标签 =====================

        private void UpdateEditLabel()
        {
            if (_editLabel == null) return;
            if (_settings.Mode == BackgroundMode.LinearGradient || _settings.Mode == BackgroundMode.RadialGradient)
            {
                _editLabel.Visibility = Visibility.Visible;
                if (_selectedStop != null)
                {
                    // 编号按活动套列表算（浅色套下用 Stops.IndexOf 会得到 0）
                    var activeStops = _settings.GetStops(_previewDark);
                    int n = Math.Max(0, activeStops.IndexOf(_selectedStop)) + 1;
                    int pct = (int)Math.Round(_selectedStop.Offset * 100);
                    // 位置语义显式化：线性 0%=渐变起点 / 100%=终点；径向 0%=圆心 / 100%=圆环边缘
                    bool radial = _settings.Mode == BackgroundMode.RadialGradient;
                    string kindTxt = radial ? "半径位置" : "位置";
                    string anchorTxt = radial
                        ? (pct <= 0 ? "，圆心" : pct >= 100 ? "，圆环边缘" : "")
                        : (pct <= 0 ? "，起点" : pct >= 100 ? "，终点" : "");
                    _editLabel.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "正在编辑：停靠点 #{0}（{1} {2}%{3}）", n, kindTxt, pct, anchorTxt);
                }
                else
                    _editLabel.Text = "正在编辑：未选择停靠点";
            }
            else if (_settings.Mode == BackgroundMode.MeshGradient)
            {
                _editLabel.Visibility = Visibility.Visible;
                if (_editingMeshBase)
                {
                    _editLabel.Text = "正在编辑：网格底色" + (_previewDark ? "" : "（浅色套）");
                }
                else if (_selectedBlob != null)
                {
                    int n = _settings.GetBlobs(_previewDark).IndexOf(_selectedBlob) + 1;
                    _editLabel.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "正在编辑：光斑 #{0}", n);
                }
                else
                    _editLabel.Text = "正在编辑：未选择光斑";
            }
            else if (_settings.Mode == BackgroundMode.Solid || _settings.Mode == BackgroundMode.Image)
            {
                _editLabel.Visibility = Visibility.Collapsed;
            }
        }

        // 功能1：停靠点列表方向键在停靠点间移动选中
        private void StopPanel_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_settings.Stops == null || _settings.Stops.Count == 0) return;
            int idx = _selectedStop != null ? _settings.Stops.IndexOf(_selectedStop) : -1;
            if (e.Key == Key.Up)
                idx = idx <= 0 ? _settings.Stops.Count - 1 : idx - 1;
            else if (e.Key == Key.Down)
                idx = idx >= _settings.Stops.Count - 1 ? 0 : idx + 1;
            else
                return;
            _selectedStop = _settings.Stops[idx];
            _currentColor = BackgroundSettings.ParseColor(_selectedStop.Color);
            UpdateColor(_currentColor, true);
            RefreshStopList();
            UpdateEditLabel();
            e.Handled = true;
        }

        // ===================== 功能2：和谐色 =====================

        private enum HarmonyMode
        {
            Complementary,          // 互补色
            Analogous,              // 类比
            Monochromatic,          // 单色（同 hue 不同 L）
            Triadic,                // 三色组
            Tetradic                // 四色组
        }

        private void BuildHarmonyCard(StackPanel parent)
        {
            _harmonyCard = new Border
            {
                Background = _owner._btnSecondaryBg,
                BorderBrush = _owner._panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var hSp = new StackPanel();
            hSp.Children.Add(new Emoji.Wpf.TextBlock
            {
                Text = "颜色组合 / 调色板",
                FontWeight = FontWeights.SemiBold,
                Foreground = _owner._textMain,
                Margin = new Thickness(0, 0, 0, 6)
            });

            // 和谐规则下拉 + 随机按钮在同一行
            var comboRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

            _harmonyCombo = new ComboBox
            {
                Width = 140,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Brushes.White,
                Foreground = Brushes.Black,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0x98, 0xA5)),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _harmonyCombo.Items.Add(new ComboBoxItem { Content = "互补色", Tag = HarmonyMode.Complementary, Foreground = Brushes.Black });
            _harmonyCombo.Items.Add(new ComboBoxItem { Content = "类比", Tag = HarmonyMode.Analogous, Foreground = Brushes.Black });
            _harmonyCombo.Items.Add(new ComboBoxItem { Content = "单色", Tag = HarmonyMode.Monochromatic, Foreground = Brushes.Black });
            _harmonyCombo.Items.Add(new ComboBoxItem { Content = "三色组", Tag = HarmonyMode.Triadic, Foreground = Brushes.Black });
            _harmonyCombo.Items.Add(new ComboBoxItem { Content = "四色组", Tag = HarmonyMode.Tetradic, Foreground = Brushes.Black });
            _harmonyCombo.SelectedIndex = 0;
            _harmonyCombo.SelectionChanged += (s, e) => UpdateHarmonySwatches();
            comboRow.Children.Add(_harmonyCombo);

            // 随机生成基色（对齐 gradients.app 的 shuffle）
            var randHarmonyBtn = new Button
            {
                Content = new Emoji.Wpf.TextBlock { Text = "🎲 随机", VerticalAlignment = VerticalAlignment.Center },
                Height = 26,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(8, 0, 0, 0),
                Background = _owner._btnSecondaryBg,
                Foreground = _owner._btnSecondaryFg,
                BorderThickness = new Thickness(0)
            };
            randHarmonyBtn.Click += (s, e) =>
            {
                // Q44：原先 new Random() 时间种子，快速连点同毫秒内会得同值；改用已声明的类级静态 _rng（跨点击持久、不重造）。
                _currentHue = _rng.Next(360);
                SyncColorFromHsv();
                UpdateHarmonySwatches();
            };
            comboRow.Children.Add(randHarmonyBtn);
            hSp.Children.Add(comboRow);

            // 调色板：纯展示（展示当前和谐规则生成的颜色组合）
            _harmonySwatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            hSp.Children.Add(_harmonySwatches);

            var hBtnRow = new WrapPanel { Orientation = Orientation.Horizontal };
            _applyHarmonyGradBtn = new Button
            {
                Content = "应用和谐色到渐变",
                Height = 28,
                MinWidth = 110,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 8, 4),
                Background = _owner._accent,
                Foreground = _owner._btnPrimaryFg,
                BorderThickness = new Thickness(0)
            };
            _applyHarmonyGradBtn.Click += ApplyHarmonyToGradient_Click;
            hBtnRow.Children.Add(_applyHarmonyGradBtn);

            _applyHarmonyMeshBtn = new Button
            {
                Content = "应用和谐色到网格",
                Height = 28,
                MinWidth = 110,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 0, 4),
                Background = _owner._accent,
                Foreground = _owner._btnPrimaryFg,
                BorderThickness = new Thickness(0)
            };
            _applyHarmonyMeshBtn.Click += ApplyHarmonyToMesh_Click;
            hBtnRow.Children.Add(_applyHarmonyMeshBtn);
            hSp.Children.Add(hBtnRow);

            _harmonyCard.Child = hSp;
            parent.Children.Add(_harmonyCard);
        }

        private List<Color> BuildHarmonyPalette()
        {
            var mode = HarmonyMode.Complementary;
            if (_harmonyCombo != null && _harmonyCombo.SelectedItem is ComboBoxItem item && item.Tag is HarmonyMode m)
                mode = m;

            double h = _currentHue;
            double s = _currentSat;
            double v = _currentValue;
            if (s < 0.05) s = 0.7; // 灰阶时给个默认饱和度，保证和谐色可见

            var hues = new List<double>();
            switch (mode)
            {
                case HarmonyMode.Complementary: hues.Add(h); hues.Add(h + 180); break;
                case HarmonyMode.Analogous: hues.Add(h - 30); hues.Add(h); hues.Add(h + 30); break;
                case HarmonyMode.Monochromatic: for (int i = 0; i < 7; i++) hues.Add(h); break;
                case HarmonyMode.Triadic: hues.Add(h); hues.Add(h + 120); hues.Add(h + 240); break;
                case HarmonyMode.Tetradic: hues.Add(h); hues.Add(h + 90); hues.Add(h + 180); hues.Add(h + 270); break;
            }

            var palette = new List<Color>();
            // Monochromatic：固定 H/S，沿 L 在 0.10..0.95 间 7 等分（避免纯黑/纯白看不出阶梯）
            if (mode == HarmonyMode.Monochromatic && hues.Count > 0)
            {
                for (int i = 0; i < hues.Count; i++)
                {
                    double t = hues.Count == 1 ? 0.5 : (double)i / (hues.Count - 1);
                    double lv = 0.10 + t * 0.85;
                    palette.Add(HsvToRgb(((hues[i] % 360) + 360) % 360, s, lv));
                }
            }
            else
            {
                foreach (var hue in hues)
                    palette.Add(HsvToRgb(((hue % 360) + 360) % 360, s, v));
            }
            return palette;
        }

        private void UpdateHarmonySwatches()
        {
            if (_harmonySwatches == null) return;
            _harmonySwatches.Children.Clear();
            var palette = BuildHarmonyPalette();
            foreach (var c in palette)
            {
                var sw = new Border
                {
                    Width = 26,
                    Height = 22,
                    Margin = new Thickness(0, 0, 5, 5),
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(c),
                    BorderBrush = _owner._panelBorder,
                    BorderThickness = new Thickness(1)
                };
                _harmonySwatches.Children.Add(sw);
            }
            UpdateHarmonyWheelDots();
        }

        /// <summary>在色轮上绘制当前和谐规则的和色标记（对齐参考站色轮上的和色点）：
        /// 除当前色（_wheelIndicator）外，其余和谐色相按当前饱和度半径在同圈上打点。</summary>
        private void UpdateHarmonyWheelDots()
        {
            if (_harmonyOverlay == null || _wheelHost == null) return;
            _harmonyOverlay.Children.Clear();
            var palette = BuildHarmonyPalette();
            if (palette.Count <= 1)
            {
                _harmonyOverlay.Visibility = Visibility.Collapsed;
                return;
            }
            var (cx, cy, maxR) = GetWheelGeometry(_wheelHost.Width, _wheelHost.Height);
            double radius = _currentSat * maxR;
            for (int i = 0; i < palette.Count; i++)   // 从 0 起：基色本身也画辐条+圆点（对齐参考站，补上此前漏的 i=0 那条）
            {
                RgbToHsv(palette[i].R, palette[i].G, palette[i].B, out double h, out double s2, out double v2);
                double rad = h * Math.PI / 180.0;
                double px = cx + radius * Math.Cos(rad);
                double py = cy + radius * Math.Sin(rad);
                // 虚线辐条：圆心 → 和色点（对齐参考站 online-color 色轮上的虚线辐条；之前只打了点漏了线）。
                // 样式与预览区渐变箭头/径向环完全同款（AxisStrokeBrush 白 78%、2px、虚线 {5,4}，见 DrawLinearHandles/DrawRadialHandles），
                // 整弹窗视觉一致；之前用过「深色衬线+白主线」双层描边，被用户指出与右侧预览虚线不一致。
                var spoke = new Line
                {
                    X1 = cx, Y1 = cy, X2 = px, Y2 = py,
                    Stroke = AxisStrokeBrush,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 5, 4 },
                    IsHitTestVisible = false
                };
                _harmonyOverlay.Children.Add(spoke);
                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Stroke = Brushes.White,
                    StrokeThickness = 1.5,
                    Fill = new SolidColorBrush(Color.FromArgb(0x80, palette[i].R, palette[i].G, palette[i].B)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, px - 4);
                Canvas.SetTop(dot, py - 4);
                _harmonyOverlay.Children.Add(dot);
            }
            _harmonyOverlay.Visibility = _settings.Mode == BackgroundMode.Image ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ApplyHarmonyToGradient_Click(object sender, RoutedEventArgs e)
        {
            var palette = BuildHarmonyPalette();
            if (palette.Count < 2) return;
            _settings.EnsureGradientStops();
            var dst = ActiveStops();   // 「分别编辑浅色套」：和谐调色板写活动套
            dst.Clear();
            dst.AddRange(palette.Select((c, i) => new GradientStopSetting
            {
                Color = BackgroundSettings.ColorToHex(c),
                Offset = palette.Count == 1 ? 0.0 : (double)i / (palette.Count - 1)
            }).ToList());
            _selectedStop = dst[0];
            _selectedBlob = null;
            _currentColor = BackgroundSettings.ParseColor(_selectedStop.Color);
            UpdateColor(_currentColor, true);
            RefreshStopList();
            RefreshPreview();
            UpdateEditLabel();
            ShowHarmonyFeedback(_applyHarmonyGradBtn, "已应用到渐变");
        }

        private void ApplyHarmonyToMesh_Click(object sender, RoutedEventArgs e)
        {
            var palette = BuildHarmonyPalette();
            if (palette.Count == 0 || _settings.Blobs == null || _settings.Blobs.Count == 0) return;
            // 按索引把调色板颜色赋给各光斑，不改光斑数量与位置
            for (int i = 0; i < _settings.Blobs.Count; i++)
                _settings.Blobs[i].Color = BackgroundSettings.ColorToHex(palette[i % palette.Count]);
            _selectedBlob = _settings.Blobs[0];
            _selectedStop = null;
            _currentColor = BackgroundSettings.ParseColor(_selectedBlob.Color);
            UpdateColor(_currentColor, true);
            RefreshBlobList();
            RefreshPreview();
            UpdateEditLabel();
            ShowHarmonyFeedback(_applyHarmonyMeshBtn, "已应用到网格");
        }

        private void ShowHarmonyFeedback(Button btn, string message)
        {
            if (btn == null) return;
            var original = btn.Content as string;
            btn.Content = message;
            if (_harmonyFeedbackTimer != null) _harmonyFeedbackTimer.Stop();
            _harmonyFeedbackTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _harmonyFeedbackTimer.Tick += (s, ev) =>
            {
                _harmonyFeedbackTimer.Stop();
                if (btn != null) btn.Content = original;
            };
            _harmonyFeedbackTimer.Start();
        }

        // 颜色格式只读块：标签 + 只读框 + 复制按钮 单行（用户：复制要与输入框一行对齐；
        // 旧版固定 240px 定宽 + 复制按钮下一行右对齐，在右列 ~153px 宽里溢出、按钮掉到「对比度」行错位）。
        private Grid MakeReadOnlyFormatBox(string label)
        {
            const double labelWidth = 40;   // 标签列收紧（CMYK: 33px 实测够）：省 6px 给输入框，HSV/HSL 18 字符（119px）完整
            // 该行变高、视觉与其他三行不齐（用户：复制没有完全对齐）；加宽并 NoWrap 双保险
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock { Text = label + ":", Width = labelWidth, TextWrapping = TextWrapping.NoWrap, Foreground = _owner._textMain, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lbl, 0);
            row.Children.Add(lbl);

            var box = new TextBox
            {
                IsReadOnly = true,
                Background = _owner._inputBg,
                Foreground = _owner._inputFg,
                BorderBrush = _owner._panelBorder,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11   // 右列恒定 204 预算：输入框 122px；11pt 时 HSV/HSL 18 字符 ≈ 117px 全模式完整
            };
            Grid.SetColumn(box, 1);
            row.Children.Add(box);

            var copy = new Button
            {
                Content = "复制",
                Width = 26,
                Height = 22,
                FontSize = 11,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = _owner._btnSecondaryBg,
                Foreground = _owner._btnSecondaryFg,
                BorderThickness = new Thickness(0)
            };
            copy.Click += (sender, args) => CopyText(box.Text, copy);
            Grid.SetColumn(copy, 2);
            row.Children.Add(copy);

            // 把 TextBox 挂在 Tag 上，SetFmt 可直接取，避免硬编码视觉树索引。
            row.Tag = box;
            return row;
        }

        // CSS 状态行：非空显示、空隐藏（空 TextBlock 仍占一行高，留白即按钮下「多余空行」）
        private void SetCssStatus(string msg)
        {
            if (_cssStatus == null) return;
            _cssStatus.Text = msg;
            _cssStatus.Visibility = string.IsNullOrEmpty(msg) ? Visibility.Collapsed : Visibility.Visible;
        }

        // 下拉框宽度贴合当前选项文字（用户：背景类型下拉过宽——按字数 2/4 字自适应）
        private void FitModeComboWidth()
        {
            var txt = (_modeCombo.SelectedItem as ComboBoxItem)?.Content as string;
            if (txt != null) _modeCombo.Width = txt.Length * 12.0 + 40;
        }

        private static void SetFmt(Grid block, string text)
        {
            // block.Tag 由 MakeReadOnlyFormatBox 设为对应 TextBox
            if (block != null && block.Tag is TextBox box) box.Text = text;
        }

        /// <summary>
        /// 复制文本到剪贴板，并在按钮上短暂显示「已复制」反馈。
        /// 去重：本类原先此处另起 STA 线程 + Join 写剪贴板，与 CopyCss_Click 的直接调用构成两套实现。
        /// 两处调用方（色值复制按钮、格式文本复制按钮）都源自 UI 线程点击事件，本就是 STA，
        /// new Thread + Join 既无必要又会阻塞 UI；统一为直接调用 Clipboard.SetText。
        /// 保留原有 try/catch 吞异常的行为：写入失败不弹窗，仅按钮反馈照常。
        /// </summary>
        private static void CopyText(string text, Button btn)
        {
            try
            {
                Clipboard.SetText(text ?? string.Empty); // 调用方均在 UI 线程（STA），可直接写入剪贴板
                if (btn != null)
                {
                    var original = btn.Content as string;
                    btn.Content = "已复制";
                    var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                    t.Tick += (s, ev) => { t.Stop(); if (btn != null) btn.Content = original; };
                    t.Start();
                }
            }
            catch (Exception ex) { DebugLog.Ignore(ex); }
        }

        // WCAG 相对亮度 + 对比度
        private static double RelativeLuminance(Color c)
        {
            double f(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            double r = f(c.R / 255.0), g = f(c.G / 255.0), b = f(c.B / 255.0);
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        private static double ContrastRatio(Color a, Color b)
        {
            double la = RelativeLuminance(a), lb = RelativeLuminance(b);
            double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
            return (hi + 0.05) / (lo + 0.05);
        }

        // ===================== 功能3：网格 CSS 导入/导出 =====================

        private void BuildCssCard(StackPanel parent)
        {
            _cssCard = new Border
            {
                Background = _owner._btnSecondaryBg,
                BorderBrush = _owner._panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 8, 0, 0)
            };
            var cssSp = new StackPanel();
            cssSp.Children.Add(new Emoji.Wpf.TextBlock
            {
                Text = "CSS 导入/导出（线性/径向/网格）",
                FontWeight = FontWeights.SemiBold,
                Foreground = _owner._textMain,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var cssBtnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
            var copyCssBtn = new Button
            {
                Content = "复制为 CSS",
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),   // 宽度随文字自适应（定宽 90 右侧空 30px 像截断）
                Margin = new Thickness(0, 0, 8, 0),
                Background = _owner._accent,
                Foreground = _owner._btnPrimaryFg,
                BorderThickness = new Thickness(0)
            };
            copyCssBtn.Click += CopyCss_Click;
            cssBtnRow.Children.Add(copyCssBtn);

            var importCssBtn = new Button
            {
                Content = "粘贴 CSS 导入",
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),   // 同上
                Margin = new Thickness(0, 0, 8, 0),
                Background = _owner._accent,
                Foreground = _owner._btnPrimaryFg,
                BorderThickness = new Thickness(0)
            };
            importCssBtn.Click += ImportCss_Click;
            cssBtnRow.Children.Add(importCssBtn);

            var randMeshBtn = new Button
            {
                Content = new Emoji.Wpf.TextBlock { Text = "🎲 随机光斑", VerticalAlignment = VerticalAlignment.Center },
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                Background = _owner._btnSecondaryBg,
                Foreground = _owner._btnSecondaryFg,
                BorderThickness = new Thickness(0)
            };
            randMeshBtn.Click += (s, e) =>
            {
                if (_settings.Blobs == null || _settings.Blobs.Count == 0) _settings.EnsureMeshBlobs();
                // 8~11 个光斑,半径 0.45~0.85,透明度 0.30~0.55,基于当前主色做 HSL 小范围偏移。
                // 数量与参数对齐 gradients.app:多光斑大面积重叠 + 低透明度,消除圆形轮廓边界。
                int n = 8 + _rng.Next(4);
                // 锚色：优先用 _currentColor（用户当前选色),回退 _settings.SolidColor。
                Color anchor = _currentColor;
                BackgroundSettings.RgbToHsl(anchor.R, anchor.G, anchor.B, out double bh, out double bs, out double bl);
                _settings.Blobs.Clear();
                for (int i = 0; i < n; i++)
                {
                    // HSL 偏移：色相 ±25°,饱和度 0.55~0.90,亮度 0.40~0.75
                    double hue = (bh + (_rng.NextDouble() * 50.0 - 25.0) + 360.0) % 360.0;
                    double sat = Math.Min(0.90, Math.Max(0.55, bs + (_rng.NextDouble() - 0.5) * 0.30));
                    double lit = Math.Min(0.75, Math.Max(0.40, bl + (_rng.NextDouble() - 0.5) * 0.25));
                    var c = BackgroundSettings.HslToRgb(hue, sat, lit);
                    _settings.Blobs.Add(new MeshBlobSetting
                    {
                        Color = BackgroundSettings.ColorToHex(c),
                        CenterX = 0.05 + _rng.NextDouble() * 0.90,
                        CenterY = 0.05 + _rng.NextDouble() * 0.90,
                        Radius = 0.45 + _rng.NextDouble() * 0.40,
                        Opacity = 0.30 + _rng.NextDouble() * 0.25
                    });
                }
                _editingMeshBase = false;
                _selectedBlob = _settings.Blobs[0];
                _selectedStop = null;
                UpdateMeshBaseUi();
                _currentColor = BackgroundSettings.ParseColor(_selectedBlob.Color);
                UpdateColor(_currentColor, true);
                RefreshBlobList();
                RefreshPreview();
                UpdateEditLabel();
                SetCssStatus("已随机生成 " + _settings.Blobs.Count + " 个光斑。");
            };
            cssBtnRow.Children.Add(randMeshBtn);

            var exportSvgBtn = new Button
            {
                Content = "导出 SVG",
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 0, 0),
                Background = _owner._btnSecondaryBg,
                Foreground = _owner._btnSecondaryFg,
                BorderThickness = new Thickness(0)
            };
            exportSvgBtn.Click += ExportSvg_Click;
            cssBtnRow.Children.Add(exportSvgBtn);
            _randMeshBtn = randMeshBtn;
            _exportSvgBtn = exportSvgBtn;
            _cssBox = new TextBox
            {
                MinHeight = 60,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = _owner._inputBg,
                Foreground = _owner._inputFg,
                BorderBrush = _owner._panelBorder,
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(0, 0, 0, 4)
            };
            cssSp.Children.Add(_cssBox);
            cssSp.Children.Add(cssBtnRow);   // 复制为 CSS / 粘贴 CSS 导入（网格模式含随机光斑/导出SVG）= 输入框下方的操作行，左对齐
            _cssStatus = new TextBlock
            {
                FontSize = 11.0,
                Foreground = _owner._textDim,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                Visibility = Visibility.Collapsed   // 无状态不占行（空 TextBlock 仍占行高 = 按钮下多余空行）
            };
            cssSp.Children.Add(_cssStatus);

            _cssCard.Child = cssSp;
            parent.Children.Add(_cssCard);
        }

        private void CopyCss_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // P1c：按当前模式 + 当前预览套生成 CSS（所见即所拷）
                string css, note;
                var stops = _settings.GetStops(_previewDark);
                if (_settings.Mode == BackgroundMode.LinearGradient)
                {
                    css = BackgroundSettings.ToCssLinear(_settings.GradientAngle, stops);
                    note = "线性渐变 CSS";
                }
                else if (_settings.Mode == BackgroundMode.RadialGradient)
                {
                    css = BackgroundSettings.ToCssRadial(_settings.RadialCenterX, _settings.RadialCenterY, _settings.RadialRadiusX, _settings.RadialRadiusY, stops);
                    note = "径向渐变 CSS";
                }
                else
                {
                    css = BackgroundSettings.ToCssGradient(_settings.GetBlobs(_previewDark));
                    note = _settings.GetBlobs(_previewDark).Count + " 个光斑的网格 CSS";
                }
                if (_cssBox != null) _cssBox.Text = css;
                Clipboard.SetText(css); // 按钮点击在 UI 线程（STA），可直接写入剪贴板
                SetCssStatus("已复制" + (_previewDark ? "" : "（浅色套）") + " " + note + "到剪贴板。");
            }
            catch (Exception ex)
            {
                SetCssStatus("复制失败：" + ex.Message);
            }
        }

        private void ImportCss_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var css = _cssBox != null ? _cssBox.Text : string.Empty;
                // P1c：按内容分流——linear-gradient → 线性导入；单 radial 层（无本工具 r 注释）→ 径向导入；
                // 多层/r 注释 → 网格导入（原路径）。写入「当前预览套」（_previewDark），所见即所写。
                bool hasLinear = Regex.IsMatch(css, @"linear-gradient\s*\(", RegexOptions.IgnoreCase);
                int radialCount = Regex.Matches(css, @"radial-gradient\s*\(", RegexOptions.IgnoreCase).Count;
                bool hasRC = Regex.IsMatch(css, @"/\\*\\s*r:[\\d.]+\\s*\\*/", RegexOptions.IgnoreCase);
                if (hasLinear)
                {
                    if (BackgroundSettings.TryParseCssLinear(css, out double ang, out List<GradientStopSetting> stp))
                    {
                        _settings.GradientAngle = ang;
                        if (_previewDark) _settings.Stops = stp;
                        else _settings.LightStops = stp;
                        _selectedStop = stp[0];
                        _selectedBlob = null;
                        _currentColor = BackgroundSettings.ParseColor(_selectedStop.Color);
                        _isUpdating = true;
                        try { UpdateColor(_currentColor, true); }
                        finally { _isUpdating = false; }
                        _hasEditedAfterShow = true;
                        if (_settings.Mode != BackgroundMode.LinearGradient) _modeCombo.SelectedIndex = 2;
                        else SyncModeUi();
                        if (_angleBox != null) _angleBox.Text = ang.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                        RefreshStopList();
                        RefreshPreview();
                        UpdateEditLabel();
                        SetCssStatus("已导入线性渐变：" + stp.Count + " 个停靠点，角度 " + ang.ToString("F0") + "°（写入" + (_previewDark ? "深色" : "浅色") + "套）。");
                        return;
                    }
                    SetCssStatus("linear-gradient 解析失败：颜色须为 #hex/rgb/rgba/hsla，停靠点位置可选。");
                    return;
                }
                if (radialCount == 1 && !hasRC)
                {
                    if (BackgroundSettings.TryParseCssRadial(css, out double rcx, out double rcy, out double rrx, out double rry, out List<GradientStopSetting> stp))
                    {
                        _settings.RadialCenterX = rcx; _settings.RadialCenterY = rcy;
                        _settings.RadialRadiusX = rrx; _settings.RadialRadiusY = rry;
                        if (_previewDark) _settings.Stops = stp;
                        else _settings.LightStops = stp;
                        _selectedStop = stp[0];
                        _selectedBlob = null;
                        _currentColor = BackgroundSettings.ParseColor(_selectedStop.Color);
                        _isUpdating = true;
                        try { UpdateColor(_currentColor, true); }
                        finally { _isUpdating = false; }
                        _hasEditedAfterShow = true;
                        if (_settings.Mode != BackgroundMode.RadialGradient) _modeCombo.SelectedIndex = 3;
                        else SyncModeUi();
                        if (_centerXBox != null) _centerXBox.Text = rcx.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                        if (_centerYBox != null) _centerYBox.Text = rcy.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                        if (_radiusXBox != null) _radiusXBox.Text = rrx.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                        if (_radiusYBox != null) _radiusYBox.Text = rry.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                        RefreshStopList();
                        RefreshPreview();
                        UpdateEditLabel();
                        SetCssStatus("已导入径向渐变：" + stp.Count + " 个停靠点（写入" + (_previewDark ? "深色" : "浅色") + "套）。");
                        return;
                    }
                    SetCssStatus("radial-gradient 解析失败：支持「N% N% at A% B%」几何前缀 + #hex/rgb/hsla 停靠点。");
                    return;
                }
                var blobs = BackgroundSettings.ParseCssGradient(css);
                if (blobs == null)
                {
                    SetCssStatus("未识别到有效的 radial-gradient 层，请检查 CSS 格式（示例：radial-gradient(at 20% 30%, hsla(180,80%,50%,0.8) 0%, hsla(180,80%,50%,0) 100%)）。");
                    return;
                }
                _settings.Blobs = blobs;
                _settings.EnsureMeshBlobs();
                _selectedBlob = _settings.Blobs.FirstOrDefault();
                _selectedStop = null;
                _currentColor = BackgroundSettings.ParseColor(_selectedBlob.Color);
                UpdateColor(_currentColor, true);
                RefreshBlobList();
                RefreshPreview();
                UpdateEditLabel();
                // 修复（导入提示）：CSS 格式本身不携带半径。本工具导出的 CSS 带 /* r:<v> */ 注释
                // 可无损往返；外部 CSS（gradients.app 等）无注释，半径按 0.5 还原——给用户一条
                // 明确提示，避免「导入后光斑尺寸莫名变了」的困惑。
                bool hasRadiusHint = Regex.IsMatch(css, @"/\*\s*r:[\d.]+\s*\*/", RegexOptions.IgnoreCase);
                SetCssStatus(hasRadiusHint
                    ? "已导入 " + blobs.Count + " 个光斑（半径来自本工具注释，无损往返）。"
                    : "已导入 " + blobs.Count + " 个光斑（外部 CSS 不含半径信息，已按默认 0.5 还原尺寸）。");
            }
            catch (Exception ex)
            {
                SetCssStatus("导入失败：" + ex.Message);
            }
        }

        // ===================== 网格底色（预设模板已并入「预设色板」卡片） =====================

        /// <summary>网格底色行（底色: [色块] [hex] [用当前色] + 编辑提示）：并入颜色卡 HEX 行下方，
        /// 不再单开卡片（用户指出：只有一行，放在 HEX 旁边、选色与底色确认同屏才合理）。</summary>
        private void BuildMeshBaseRow(StackPanel parent)
        {
            _meshBaseRow = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

            // 网格底色（Base color）
            var baseRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            baseRow.Children.Add(new TextBlock { Text = "底色:", Foreground = _owner._textMain, Width = 40, VerticalAlignment = VerticalAlignment.Center });
            _baseSwatch = new Border
            {
                Width = 24,   // 底色行总宽预算 205（左列固定）：40+24+6+60+6+56 = 192
                Height = 20,
                CornerRadius = new CornerRadius(3),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 0),
                BorderBrush = _owner._panelBorder,
                BorderThickness = new Thickness(1)
            };
            _baseSwatch.MouseLeftButtonDown += (s, e) =>
            {
                // 点底色色块 = 选中「网格底色」为编辑目标：之后色轮/HEX/RGB/HSV/数值框 直接改底色
                // （旧行为只是"把当前色应用到底色"，必须先选色再点，反直觉——用户指出"点击之后应该能改色"）
                _editingMeshBase = true;
                _selectedBlob = null;
                _currentColor = BackgroundSettings.ParseColor(_settings.GetMeshBaseColor(_previewDark));
                _isUpdating = true;
                try { UpdateColor(_currentColor, true); }
                finally { _isUpdating = false; }
                _hasEditedAfterShow = true;
                RefreshPreview();
                UpdateEditLabel();
                UpdateMeshBaseUi();
            };
            baseRow.Children.Add(_baseSwatch);
            _baseHexBox = new TextBox
            {
                Width = 60,   // 底色行总宽 192 < 205（#RRGGBB 最长 9 字符 ≈ 54px 够放）

                Background = _owner._inputBg,
                Foreground = _owner._inputFg,
                BorderBrush = _owner._panelBorder,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas")
            };
            _baseHexBox.TextChanged += (s, e) =>
            {
                if (_isUpdating) return;
                var raw = (_baseHexBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(raw)) return;
                // 修复：原实现把任意串直接拼 '#' 后交给 ParseColor，ParseColor 的 3 位简写展开会把
                // "#123"/"FFF" 这类中间态输入也解析成功，导致逐字符输入时底色疯狂跳变；
                // 且存的是原始串（如 "FFF"），ToSvg 会写出 <rect fill="FFF"> 这类非法 SVG。
                // 现改为：先用正则严格校验，再把 3 位简写展开成 6 位、补齐 '#' 前缀，
                // 统一以 "#RRGGBB" / "#RRGGBBAA" 形式存储；非法输入直接忽略本次输入（保留旧值，不跳变）。
                var norm = NormalizeHexColor(raw);
                if (norm == null) return;
                var c = BackgroundSettings.ParseColor(norm);
                if (c.A > 0)
                {
                    WriteMeshBaseHex(norm);
                    UpdateMeshBaseUi();
                    RefreshPreview();
                }
            };
            baseRow.Children.Add(_baseHexBox);
            var basePickBtn = new Button
            {
                Content = "用当前色",
                Height = 24,
                Width = 56,
                Margin = new Thickness(6, 0, 0, 0),
                Background = _owner._btnSecondaryBg,
                Foreground = _owner._btnSecondaryFg,
                BorderThickness = new Thickness(0)
            };
            basePickBtn.Click += (s, e) =>
            {
                WriteMeshBaseHex(BackgroundSettings.ColorToHex(_currentColor));   // 「分别编辑浅色套」
                UpdateMeshBaseUi();
                RefreshPreview();
            };
            baseRow.Children.Add(basePickBtn);
            _meshBaseRow.Children.Add(baseRow);
            // 点底色色块进入「底色编辑」后的当场可见反馈（提示行 + 色块 accent 描边）：
            // 之前的反馈只在滚出屏幕的颜色卡（「正在编辑」标签/色轮）上，底色行本身无变化，用户以为"点了没反应"。
            _baseEditHint = new TextBlock
            {
                Text = "正在编辑网格底色：色轮 / HEX / 滑块 现在直接改底色（点光斑行或随机光斑可切回编辑光斑）",
                Foreground = _owner._accent,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 200,   // 贴左列新上限 210（限宽换行防撑爆列宽的根因修复保留）
                Visibility = Visibility.Collapsed
            };
            _meshBaseRow.Children.Add(_baseEditHint);

            // 「预设模板」按钮区已在早前合并中删除：预设统一走「预设色板」卡片（避免两处重复）。
            _meshBaseRow.Visibility = _settings.Mode == BackgroundMode.MeshGradient ? Visibility.Visible : Visibility.Collapsed;
            parent.Children.Add(_meshBaseRow);
        }

        // ===================== 深浅背景切换（非图片模式：深/浅两套色 + 预览套切换 + 一键同步） =====================

        /// <summary>「分别编辑浅色套」：活动套=浅色且其数据为空时，把深色套内容克隆进去（一次性升级，幂等）。
        /// 升级后 _selectedStop/_selectedBlob 重指到浅色套对应元素，列表行重建——之后所有编辑都落在浅色套。</summary>
        private void PromoteActiveSet()
        {
            if (_previewDark) return;
            bool changed = false;
            _settings.EnsureGradientStops();
            if (_settings.LightStops == null || _settings.LightStops.Count == 0)
            {
                _settings.LightStops = _settings.Stops.Select(x => new GradientStopSetting { Color = x.Color, Offset = x.Offset }).ToList();
                changed = true;
            }
            _settings.EnsureMeshBlobs();
            if (_settings.LightBlobs == null || _settings.LightBlobs.Count == 0)
            {
                _settings.LightBlobs = _settings.Blobs.Select(x => new MeshBlobSetting
                {
                    Color = x.Color, CenterX = x.CenterX, CenterY = x.CenterY, Radius = x.Radius, Opacity = x.Opacity
                }).ToList();
                changed = true;
            }
            if (string.IsNullOrEmpty(_settings.LightMeshBaseColor))
            {
                _settings.LightMeshBaseColor = _settings.MeshBaseColor;
                changed = true;
            }
            if (string.IsNullOrEmpty(_settings.LightSolidColor))
            {
                _settings.LightSolidColor = _settings.SolidColor;
                changed = true;
            }
            if (!changed) return;
            _hasEditedAfterShow = true;
            // 选中项重指到浅色套对应元素（按位置对齐），列表行重建
            _selectedStop = _settings.LightStops.FirstOrDefault();
            _selectedBlob = _settings.LightBlobs.FirstOrDefault();
            RefreshStopList();
            RefreshBlobList();
        }

        /// <summary>活动套的停靠点列表（写点用）：浅色套未升级时按 GetStops 回退深色套。</summary>
        private List<GradientStopSetting> ActiveStops() => _settings.GetStops(_previewDark);

        /// <summary>活动套的光斑列表（写点用）。</summary>
        private List<MeshBlobSetting> ActiveBlobs() => _settings.GetBlobs(_previewDark);

        /// <summary>纯色写入（落点跟活动套；浅色套未设时先克隆深色值再写）。</summary>
        private void WriteSolidHex(string hex)
        {
            if (_previewDark) { _settings.SolidColor = hex; return; }
            if (string.IsNullOrEmpty(_settings.LightSolidColor)) _settings.LightSolidColor = _settings.SolidColor;
            _settings.LightSolidColor = hex;
        }

        /// <summary>网格底色写入（同上）；显示侧用 GetMeshBaseColor(_previewDark)。</summary>
        private void WriteMeshBaseHex(string hex)
        {
            if (_previewDark) { _settings.MeshBaseColor = hex; return; }
            if (string.IsNullOrEmpty(_settings.LightMeshBaseColor)) _settings.LightMeshBaseColor = _settings.MeshBaseColor;
            _settings.LightMeshBaseColor = hex;
        }

        /// <summary>预览套从深色切到浅色（或反向）：共用 _owner.ToggleTheme 翻转主题，随后升级活动套并刷新。</summary>
        private void SetPreviewSet(bool dark)
        {
            // 共用主窗口切换逻辑：目标套与当前主题不一致时翻主题（主窗背景 + 弹窗预览一起换套）
            if (dark != _owner.IsDarkMode) _owner.ToggleTheme();
            _previewDark = _owner.IsDarkMode;
            // 当前编辑色切到对应套的首个停靠点/纯色，让色轮/HEX/滑块与预览套一致
            try
            {
                var first = _settings.Mode == BackgroundMode.Solid
                    ? BackgroundSettings.ParseColor(_settings.GetSolidColor(_previewDark))
                    : _settings.Mode == BackgroundMode.MeshGradient
                        ? BackgroundSettings.ParseColor(_settings.GetBlobs(_previewDark).FirstOrDefault()?.Color ?? _settings.GetMeshBaseColor(_previewDark))
                        : BackgroundSettings.ParseColor(_settings.GetStops(_previewDark).FirstOrDefault()?.Color ?? "#16E0BD");
                _isUpdating = true;
                try { _currentColor = first; UpdateColor(_currentColor, true); }
                finally { _isUpdating = false; }
            }
            catch { /* 取套失败保持当前色 */ }
            PromoteActiveSet();
            RefreshStopList();
            RefreshBlobList();
            RefreshPreview();
            UpdateDlToggleUi();
        }

        /// <summary>把当前深色套（SolidColor/Stops/Blobs/MeshBaseColor）整体复制到浅色套（Light*），
        /// 使浅色主题拥有独立的背景。若浅色套已有内容则直接覆盖（幂等）。之后可再单独编辑浅色套。</summary>
        private void SyncDarkToLight()
        {
            _settings.LightSolidColor = _settings.SolidColor;
            _settings.LightStops = _settings.Stops?.Select(s => new GradientStopSetting { Color = s.Color, Offset = s.Offset }).ToList();
            _settings.LightBlobs = _settings.Blobs?.Select(b => new MeshBlobSetting
            {
                Color = b.Color, CenterX = b.CenterX, CenterY = b.CenterY, Radius = b.Radius, Opacity = b.Opacity
            }).ToList();
            _settings.LightMeshBaseColor = _settings.MeshBaseColor;
            _hasEditedAfterShow = true;
            // 同步后切到浅色套预览（共用主窗口切换逻辑，主窗背景/弹窗预览一起换），让用户立刻看到浅色效果
            SetPreviewSet(false);
        }

        /// <summary>深浅切换单按钮的图标/提示（与主窗口 🌙 按钮同款语义：深色态=🌙"切换到浅色"，浅色态=☀️"切换到深色"）。</summary>
        private void UpdateDlToggleUi()
        {
            bool dark = _owner.IsDarkMode;
            string icon = dark ? "🌙" : "☀️";
            string tip = dark ? "当前深色背景，点击切换为浅色" : "当前浅色背景，点击切换为深色";
            if (_previewThemeBtn != null)
            {
                _previewThemeBtn.Content = new Emoji.Wpf.TextBlock { Text = icon, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 };
                _previewThemeBtn.ToolTip = tip;
            }
            if (_imgThemeToggleBtn != null)
            {
                _imgThemeToggleBtn.Content = new Emoji.Wpf.TextBlock { Text = icon, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 };
                _imgThemeToggleBtn.ToolTip = tip;
            }
        }

        // ===================== 功能4：预设色板库（点击应用到当前模式） =====================

        // 精选配色方案库（对齐参考站 online-color.com 主题调色板 / gradients.app 预设）：
        // 复用网格预设的 12 套配色（每套 = 底色 + 光斑色），按当前模式应用：
        //   网格→光斑+底色（复用 ApplyPreset）；纯色→底色；线性/径向→取前 5 色均布停靠点；
        //   图片→切到线性再应用停靠点。
        private void BuildPresetPaletteCard(StackPanel parent)
        {
            _paletteCard = new Border
            {
                Background = _owner._btnSecondaryBg,
                BorderBrush = _owner._panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var sp = new StackPanel();
            // 修复（标题过长被截断）：原 30 字描述在弹窗默认宽下显示不全且不换行。
            // 精简为主标题 + 短副标题两行，副标题设 TextWrapping=Wrap 双保险（窄窗也能折行不裁切）。
            sp.Children.Add(new TextBlock
            {
                Text = "预设色板",
                FontWeight = FontWeights.SemiBold,
                Foreground = _owner._textMain,
                Margin = new Thickness(0, 0, 0, 2)
            });
            sp.Children.Add(new TextBlock
            {
                Text = "点击应用到当前模式（纯色=底色 / 渐变=停靠点 / 网格=光斑）",
                FontSize = 11,
                Foreground = _owner._textDim,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
            // 修复（列数不匀）：原 WrapPanel 按可用宽度自动换行，12 套排成 4/3/3/2 每行列数不一致（用户指出）。
            // 改固定 4列×3行 Grid，每列等宽、每行等高；每格内部「名称在上 + 4 色块在下」堆叠，
            // 列宽由 Grid 均分保证整齐，弹窗缩放时仍保持 4 列。
            var grid = new Grid();
            for (int col = 0; col < 4; col++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int row = 0; row < 3; row++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int idx = 0;
            foreach (var p in BackgroundSettings.GetMeshPresets())
            {
                var preset = p;
                var btn = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 8, 8),
                    Padding = new Thickness(6, 4, 6, 4),
                    Background = _owner._btnSecondaryBg,
                    Foreground = _owner._btnSecondaryFg,
                    BorderThickness = new Thickness(0),
                    ToolTip = preset.Name + "（点击应用到当前背景模式）"
                };
                var content = new StackPanel { Orientation = Orientation.Vertical };
                content.Children.Add(new TextBlock { Text = preset.Name, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
                var swRow = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var blob in preset.Blobs.Take(4))
                {
                    swRow.Children.Add(new Border
                    {
                        Width = 12, Height = 12,
                        CornerRadius = new CornerRadius(2),
                        Margin = new Thickness(0, 0, 4, 0),
                        Background = new SolidColorBrush(BackgroundSettings.ParseColor(blob.Color)),
                        BorderBrush = _owner._panelBorder,
                        BorderThickness = new Thickness(1)
                    });
                }
                content.Children.Add(swRow);
                btn.Content = content;
                btn.Click += (s, e) => ApplyPaletteToCurrentMode(preset);
                Grid.SetColumn(btn, idx % 4);
                Grid.SetRow(btn, idx / 4);
                grid.Children.Add(btn);
                idx++;
            }
            sp.Children.Add(grid);
            _paletteCard.Child = sp;
            parent.Children.Add(_paletteCard);
        }

        /// <summary>把预设配色方案应用到当前模式：网格→光斑 / 纯色→底色 / 线性、径向→均布停靠点 / 图片→切线性再应用。</summary>
        private void ApplyPaletteToCurrentMode(BackgroundSettings.MeshPreset preset)
        {
            if (preset == null) return;
            if (_settings.Mode == BackgroundMode.MeshGradient)
            {
                ApplyPreset(preset);
                return;
            }
            if (_settings.Mode == BackgroundMode.Solid)
            {
                WriteSolidHex(preset.BaseColor);   // 「分别编辑浅色套」：写入活动套
                _selectedStop = null;
                _selectedBlob = null;
                _currentColor = BackgroundSettings.ParseColor(_settings.GetSolidColor(_previewDark));
                _isUpdating = true;
                try { UpdateColor(_currentColor, true); }
                finally { _isUpdating = false; }
                _hasEditedAfterShow = true;
                RefreshPreview();
                UpdateEditLabel();
                return;
            }
            var colors = preset.Blobs.Select(b => b.Color).Distinct().Take(5).ToList();
            _settings.EnsureGradientStops();
            var dst = ActiveStops();   // 「分别编辑浅色套」：停靠点写活动套
            dst.Clear();
            dst.AddRange(colors.Select((c, i) => new GradientStopSetting
            {
                Color = c,
                Offset = colors.Count == 1 ? 0.0 : (double)i / (colors.Count - 1)
            }).ToList());
            _selectedStop = dst[0];
            _selectedBlob = null;
            _currentColor = BackgroundSettings.ParseColor(_selectedStop.Color);
            _isUpdating = true;
            try { UpdateColor(_currentColor, true); }
            finally { _isUpdating = false; }
            _hasEditedAfterShow = true;
            RefreshStopList();
            RefreshPreview();
            UpdateEditLabel();
            if (_settings.Mode == BackgroundMode.Image)
            {
                foreach (ComboBoxItem it in _modeCombo.Items)
                {
                    if (it.Tag is BackgroundMode mm && mm == BackgroundMode.LinearGradient && _modeCombo.SelectedItem != it)
                    {
                        _modeCombo.SelectedItem = it;
                        break;
                    }
                }
            }
        }

        private void ApplyPreset(BackgroundSettings.MeshPreset preset)
        {
            if (preset == null) return;
            _settings.Mode = BackgroundMode.MeshGradient;
            WriteMeshBaseHex(preset.BaseColor);   // 「分别编辑浅色套」：底色写活动套
            var blobs = ActiveBlobs();
            blobs.Clear();
            blobs.AddRange(preset.Blobs.Select(b => new MeshBlobSetting
            {
                Color = b.Color,
                CenterX = b.CenterX,
                CenterY = b.CenterY,
                Radius = b.Radius,
                Opacity = b.Opacity
            }).ToList());
            _editingMeshBase = false;
            _selectedBlob = blobs.FirstOrDefault();
            _selectedStop = null;
            // [Q45] preset 无光斑（Blobs 为空）时 FirstOrDefault()=null，原直接 _selectedBlob.Color 会 NRE。
            // 回退用基础色初始化选中色；光斑列表为空时 RefreshBlobList 无行可刷，安全。
            _currentColor = BackgroundSettings.ParseColor(_selectedBlob != null ? _selectedBlob.Color : _settings.MeshBaseColor);
            _isUpdating = true;
            try { UpdateColor(_currentColor, true); }
            finally { _isUpdating = false; }
            // 修复：上面 UpdateColor 处于 _isUpdating 保护内，UpdateColor 里的 _hasEditedAfterShow 置位被跳过。
            // 若应用预设后直接点 × 关闭，Closing 会判定"应用后未再编辑"而回到打开前状态，把预设效果一起回滚吞掉。
            // 应用预设本身就是一次真实编辑，_isUpdating 恢复后必须显式置位。
            _hasEditedAfterShow = true;
            UpdateMeshBaseUi();
            RefreshBlobList();
            RefreshPreview();
            UpdateEditLabel();
            if (_cssBox != null) _cssBox.Text = BackgroundSettings.ToCssGradient(_settings.GetBlobs(_previewDark));
            // 同步模式下拉框（预设按钮只在网格模式可见，通常已是 MeshGradient，此处兜底）
            foreach (ComboBoxItem it in _modeCombo.Items)
            {
                if (it.Tag is BackgroundMode mm && mm == BackgroundMode.MeshGradient && _modeCombo.SelectedItem != it)
                {
                    _modeCombo.SelectedItem = it;
                    break;
                }
            }
        }

        /// <summary>
        /// HEX 颜色输入校验与规范化：只接受 #RGB / #RRGGBB / 8 位（'#' 可省略，大小写不敏感）。
        /// 3 位简写展开为 6 位，输出统一为大写 '#RRGGBB' / 8 位形式；非法输入返回 null。
        /// 目的：① 逐字符输入时的中间态（"#12"、"FF"）不再被误解析，底色不跳变；
        /// ② 存储值一定是带 '#' 的完整十六进制，避免 ToSvg 写出 fill="FFF" 这类非法 SVG。
        /// </summary>
        private static string NormalizeHexColor(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim();
            if (!HexColorPattern.IsMatch(s)) return null;   // 长度/字符不合法：忽略本次输入
            if (s.StartsWith("#", StringComparison.Ordinal)) s = s.Substring(1);
            if (s.Length == 3)
                s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });   // #RGB → #RRGGBB
            return "#" + s.ToUpperInvariant();
        }

        private void UpdateMeshBaseUi()
        {
            if (_baseSwatch == null || _baseHexBox == null) return;
            _isUpdating = true;
            try
            {
                var baseHex = _settings.GetMeshBaseColor(_previewDark);   // 「分别编辑浅色套」：显示活动套底色
                _baseSwatch.Background = MakeColorBrush(baseHex);
                // 编辑底色时的选中态：accent 2px 描边（同停靠点选中行风格），平时 1px 普通边框
                _baseSwatch.BorderBrush = _editingMeshBase ? _owner._accent : _owner._panelBorder;
                _baseSwatch.BorderThickness = new Thickness(_editingMeshBase ? 2 : 1);
                if (!_baseHexBox.IsKeyboardFocused && _baseHexBox.Text != baseHex)
                    _baseHexBox.Text = baseHex;
            }
            finally { _isUpdating = false; }
            if (_baseEditHint != null) _baseEditHint.Visibility = _editingMeshBase ? Visibility.Visible : Visibility.Collapsed;
        }

        // ===================== 色阶生成器（Shades / Tints / Tones） =====================

        private void BuildShadesTintsTonesCard(StackPanel parent)
        {
            _sttCard = new Border
            {
                Background = _owner._btnSecondaryBg,
                BorderBrush = _owner._panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = "色阶生成器 (Shades / Tints / Tones)",
                FontWeight = FontWeights.SemiBold,
                Foreground = _owner._textMain,
                Margin = new Thickness(0, 0, 0, 4)
            });
            sp.Children.Add(new TextBlock
            {
                Text = "基于当前颜色生成；点击色块即应用到选中的停靠点 / 光斑 / 纯色。",
                Foreground = _owner._textDim,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            });

            sp.Children.Add(MakeSttSection("Shades（加深 → 黑）", out _shadesPanel));
            sp.Children.Add(MakeSttSection("Tints（减淡 → 白）", out _tintsPanel));
            sp.Children.Add(MakeSttSection("Tones（灰调 → 灰）", out _tonesPanel));

            _sttCard.Child = sp;
            parent.Children.Add(_sttCard);
        }

        private StackPanel MakeSttSection(string label, out WrapPanel wrap)
        {
            var section = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            section.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = _owner._textDim,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 3)
            });
            wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            section.Children.Add(wrap);
            return section;
        }

        private void UpdateShadesTintsTones()
        {
            if (_shadesPanel == null || _tintsPanel == null || _tonesPanel == null) return;
            var baseC = _currentColor;
            var black = Colors.Black;
            var white = Colors.White;
            double gray = (baseC.R * 0.2126 + baseC.G * 0.7152 + baseC.B * 0.0722) / 255.0;
            var grayC = Color.FromRgb((byte)Math.Round(gray * 255), (byte)Math.Round(gray * 255), (byte)Math.Round(gray * 255));
            FillStt(_shadesPanel, i => BackgroundSettings.LerpColor(baseC, black, (double)i / 6));
            FillStt(_tintsPanel, i => BackgroundSettings.LerpColor(baseC, white, (double)i / 6));
            FillStt(_tonesPanel, i => BackgroundSettings.LerpColor(baseC, grayC, (double)i / 6));
        }

        private void FillStt(WrapPanel wrap, Func<int, Color> colorAt)
        {
            wrap.Children.Clear();
            for (int i = 0; i < 7; i++)
            {
                var c = colorAt(i);
                var sw = new Border
                {
                    Width = 26,
                    Height = 20,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 0, 3, 0),
                    Background = new SolidColorBrush(c),
                    BorderBrush = _owner._panelBorder,
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };
                sw.MouseLeftButtonDown += (s, e) =>
                {
                    _currentColor = c;
                    UpdateColor(c, true);
                };
                wrap.Children.Add(sw);
            }
        }

        // ===================== SVG 导出 =====================

        private void ExportSvg_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_settings.Mode != BackgroundMode.MeshGradient || _settings.Blobs == null || _settings.Blobs.Count == 0)
                {
                    SetCssStatus("SVG 导出仅支持网格渐变模式，且至少需有 1 个光斑。");
                    return;
                }
                var dlg = new SaveFileDialog
                {
                    Filter = "SVG 文件|*.svg|所有文件|*.*",
                    FileName = "mesh-gradient.svg",
                    DefaultExt = ".svg",
                    Title = "导出网格渐变为 SVG",
                    InitialDirectory = AppPaths.EnsureDir(AppPaths.WallpaperDir)
                };
                if (dlg.ShowDialog() == true)
                {
                    var svg = BackgroundSettings.ToSvg(_settings.Blobs, _settings.MeshBaseColor);
                    AtomicFile.WriteFileAtomic(dlg.FileName, svg);
                    SetCssStatus("已导出 SVG：" + dlg.FileName);
                }
            }
            catch (Exception ex)
            {
                SetCssStatus("导出失败：" + ex.Message);
            }
        }
    }
}

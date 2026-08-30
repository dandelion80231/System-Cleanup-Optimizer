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
        private TextBlock _rVal, _gVal, _bVal;
        private StackPanel _stopPanel;
        private StackPanel _blobPanel;
        private TextBox _angleBox;
        private TextBox _centerXBox, _centerYBox, _radiusXBox, _radiusYBox;
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
        // 图片模式控件（方案 A：左列深色/浅色选择 + 独立透明度；右列随主题的大预览）
        private Slider _darkOpacitySlider;
        private Slider _lightOpacitySlider;
        private TextBlock _darkOpacityVal;
        private TextBlock _lightOpacityVal;
        private Image _imgBigPreview;        // 右列大预览：随主题显示深色/浅色背景图
        private TextBlock _imgBigPlaceholder;
        private TextBox _cssBox;
        private TextBlock _cssStatus;

        // 网格专属卡片：底色 + 预设模板
        private Border _meshCard;
        private Border _baseSwatch;
        private TextBox _baseHexBox;

        // 色阶生成器（Shades / Tints / Tones）
        private Border _sttCard;
        private WrapPanel _shadesPanel, _tintsPanel, _tonesPanel;

        // 当前编辑态
        private GradientStopSetting _selectedStop;
        private MeshBlobSetting _selectedBlob;
        private GradientStopSetting _lastSelectedStop;
        private MeshBlobSetting _lastSelectedBlob;
        private System.Windows.Threading.DispatcherTimer _harmonyFeedbackTimer;
        private Color _currentColor = Color.FromRgb(0x16, 0xE0, 0xBD);
        private double _currentHue;            // HSV 色相（色轮取色用）
        private double _currentSat;            // HSV 饱和度
        private double _currentValue = 0.95;   // HSV 明度（0..1），配合色轮选暗色背景
        private bool _isUpdating;
        private bool _isInitializing;          // 防止初始化时 SelectionChanged 重入导致 UI 树抖动
        // 关闭时撤销用：_hasEditedAfterShow=用户曾编辑；_hasAppliedAtLeastOnce=曾点过"应用"；_appliedSnapshot=最近一次 Apply 时的 _settings 快照
        private bool _hasEditedAfterShow;
        private bool _hasAppliedAtLeastOnce;
        private BackgroundSettings _appliedSnapshot;
        private Slider _valSlider;             // 明度滑块
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
        // 已经渲染到 _wheelBitmap 的明度值。色轮像素 = f(角度→色相, 半径→饱和度, 明度)，
        // 与当前选中的 _currentHue/_currentSat 无关，故仅当明度变化时才需要重算像素，
        // 拖色轮改色相/饱和度时只需移动指示器（UpdateWheelIndicator），零像素运算。
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
        private StackPanel _rowRgb, _rowHsl, _rowHsv, _rowCmyk;   // 颜色格式只读显示块（标签+文本框+复制按钮）
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
        }

        private void BuildUi()
        {
            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 标题+模式
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 内容
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 按钮

            // 第 0 行：标题 + 模式选择
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var title = new TextBlock
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
                Width = 140,
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
                Foreground = _owner._textDim,
                Margin = new Thickness(0, 0, 0, 12)
            };
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
            colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            colorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            colorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var leftTop = new StackPanel();
            var rightCol = new Grid { VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(16, 0, 0, 0) };
            // 色轮下方控件的顶部间距：色轮 wheelGrid 底部边距已置 0，这里用 22 维持
            // 与之前（wheelGrid 10 + 本行 12 = 22）一致的整体视觉间距，同时确保右列对比度文本
            // 能与色轮圆底边精确对齐（rightCol 拉伸对齐的是 leftTop 底边 = 色轮圆底边）。
            var lowerControls = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };

            // 左列：色轮
            var wheelTitle = new TextBlock
            {
                Text = "色轮",
                FontWeight = FontWeights.SemiBold,
                Foreground = _owner._textMain,
                Margin = new Thickness(0, 0, 0, 8)
            };
            leftTop.Children.Add(wheelTitle);

            // 色轮容器：用 Grid 把 Image 和指示器 Canvas 叠在一起，并用 EllipseGeometry 真正裁成圆形
            var wheelGrid = new Grid
            {
                Width = 180,
                Height = 180,
                HorizontalAlignment = HorizontalAlignment.Left,
                // 注意：这里底部不留边距。若留 10px，leftTop 会比色轮圆底边高出 10px，
                // 而右列 rightCol 按 VerticalAlignment=Stretch 对齐到 leftTop 底边，
                // 导致"颜色格式/对比度"面板的底部比色轮圆底部低 10px，违背"底部与色轮底部对齐"需求。
                // 因此底部边距置 0，下方 HEX/RGB/明度行的间距由 lowerControls 的 Margin 承担。
                Margin = new Thickness(0, 0, 0, 0)
            };
            _wheelHost = new Border
            {
                Width = 180,
                Height = 180,
                CornerRadius = new CornerRadius(90),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                // 关键：CornerRadius 只影响 Border 自身背景/边框，不会裁剪子元素；
                // 用 EllipseGeometry 裁剪才能确保色轮内容严格圆形
                Clip = new EllipseGeometry(new Rect(0, 0, 180, 180))
            };
            _wheelImage = new Image { Width = 180, Height = 180, IsHitTestVisible = false, Stretch = Stretch.Fill };
            _wheelHost.Child = _wheelImage;
            _wheelHost.MouseLeftButtonDown += WheelHost_MouseLeftButtonDown;
            _wheelHost.MouseMove += WheelHost_MouseMove;
            _wheelHost.MouseLeftButtonUp += WheelHost_MouseLeftButtonUp;
            wheelGrid.Children.Add(_wheelHost);

            _wheelOverlay = new Canvas { Width = 180, Height = 180, IsHitTestVisible = false };
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
            leftTop.Children.Add(wheelGrid);

            // HEX + RGB
            var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            hexRow.Children.Add(new TextBlock { Text = "HEX:", Foreground = _owner._textMain, VerticalAlignment = VerticalAlignment.Center, Width = 36 });
            _hexBox = new TextBox
            {
                Width = 90,
                Background = _owner._inputBg,
                Foreground = _owner._inputFg,
                BorderBrush = _owner._panelBorder,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas")
            };
            _hexBox.TextChanged += HexBox_TextChanged;
            _hexBox.LostFocus += HexBox_LostFocus;
            hexRow.Children.Add(_hexBox);
            // 复制 HEX 按钮紧跟 HEX 输入框
            var hexCopyBtn = new Button
            {
                Content = "复制",
                Height = 26,
                Width = 48,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = _owner._btnSecondaryBg,
                Foreground = _owner._btnSecondaryFg,
                BorderThickness = new Thickness(0)
            };
            hexCopyBtn.Click += (s, e) => CopyText(BackgroundSettings.ColorToHex(_currentColor), hexCopyBtn);
            hexRow.Children.Add(hexCopyBtn);
            lowerControls.Children.Add(hexRow);

            var rgbPanel = new StackPanel();
            var rPanel = CreateRgbSlider(out _rSlider, out _rVal, "R");
            var gPanel = CreateRgbSlider(out _gSlider, out _gVal, "G");
            var bPanel = CreateRgbSlider(out _bSlider, out _bVal, "B");
            _rSlider.ValueChanged += (s, e) => { if (!_isUpdating) { _currentColor = Color.FromRgb((byte)_rSlider.Value, _currentColor.G, _currentColor.B); UpdateColor(_currentColor, true); } };
            _gSlider.ValueChanged += (s, e) => { if (!_isUpdating) { _currentColor = Color.FromRgb(_currentColor.R, (byte)_gSlider.Value, _currentColor.B); UpdateColor(_currentColor, true); } };
            _bSlider.ValueChanged += (s, e) => { if (!_isUpdating) { _currentColor = Color.FromRgb(_currentColor.R, _currentColor.G, (byte)_bSlider.Value); UpdateColor(_currentColor, true); } };
            rgbPanel.Children.Add(rPanel);
            rgbPanel.Children.Add(gPanel);
            rgbPanel.Children.Add(bPanel);
            lowerControls.Children.Add(rgbPanel);

            // 明度（HSV 的 V）：配合色轮取色，可选取暗色背景；拖动时色轮同步变暗/变亮
            var valRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            valRow.Children.Add(new TextBlock { Text = "明度:", Foreground = _owner._textMain, Width = 36, VerticalAlignment = VerticalAlignment.Center });
            _valSlider = new Slider { Minimum = 0, Maximum = 1, Width = 150, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            _valSlider.ValueChanged += (s, e) =>
            {
                if (_isUpdating) return;
                _currentValue = _valSlider.Value;
                _currentColor = HsvToRgb(_currentHue, _currentSat, _currentValue);
                // 性能优化：这里不再单独调 RenderColorWheel()。UpdateColor(...,true) 内部已会请求一次
                // 色轮重绘，原写法一帧内请求两次（且明度变化会真正重算像素），去掉重复的那一次。
                UpdateColor(_currentColor, true); // 同步 RGB/HEX/明度滑块 + 请求色轮重绘
            };
            valRow.Children.Add(_valSlider);
            lowerControls.Children.Add(valRow);

            // 右列：颜色格式 / 对比度（Grid 垂直撑满，底部与色轮底部对齐）
            rightCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });            // 标题
            rightCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 中部填空，把对比度推到底部
            rightCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });            // 对比度（与色轮底部对齐）

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
            Grid.SetRow(_rowRgb, 0);
            Grid.SetRow(_rowHsl, 1);
            Grid.SetRow(_rowHsv, 2);
            Grid.SetRow(_rowCmyk, 3);
            formatGrid.Children.Add(_rowRgb);
            formatGrid.Children.Add(_rowHsl);
            formatGrid.Children.Add(_rowHsv);
            formatGrid.Children.Add(_rowCmyk);
            Grid.SetRow(formatGrid, 1);
            rightCol.Children.Add(formatGrid);

            _contrastText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.0,
                Foreground = _owner._textDim,
                Margin = new Thickness(0, 0, 0, 0)
            };
            Grid.SetRow(_contrastText, 2);
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
            imgTitlePanel.Children.Add(new TextBlock
            {
                Text = "自定义背景图",
                FontWeight = FontWeights.SemiBold,
                Foreground = _owner._textMain,
                VerticalAlignment = VerticalAlignment.Center
            });
            imgTitlePanel.Children.Add(new TextBlock
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
                Content = "🔄 恢复默认背景",
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                Background = _owner._btnSecondaryBg,
                Foreground = _owner._btnSecondaryFg,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            imgResetBtn.Click += (s, e) =>
            {
                _settings.DarkPath = "";
                _settings.LightPath = "";
                _settings.DarkOpacity = 0.55;
                _settings.LightOpacity = 1.0;
                if (_darkOpacitySlider != null) { _darkOpacitySlider.Value = 0.55; _darkOpacityVal.Text = "55%"; }
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
                Content = "🌙 选择深色背景",
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
                    Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*"
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
                Content = "☀️ 选择浅色背景",
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
                    Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*"
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
            Grid.SetColumn(bigPreviewHost, 1);
            imgRoot.Children.Add(bigPreviewHost);

            _imageCard.Child = imgRoot;
            editPanel.Children.Add(_imageCard);

            // 和谐色（颜色组合 + 调色板）：显示在颜色编辑区下方（原位置）
            BuildHarmonyCard(editPanel);

            // 功能2b：色阶生成器（Shades / Tints / Tones），任意非图片模式可见
            BuildShadesTintsTonesCard(editPanel);

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

            var angleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            angleRow.Children.Add(new TextBlock { Text = "角度:", Foreground = _owner._textMain, Width = 50, VerticalAlignment = VerticalAlignment.Center });
            _angleBox = new TextBox { Width = 60, Background = _owner._inputBg, Foreground = _owner._inputFg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center };
            // 修复：几何输入统一钳制。角度归一化到 [-360,360] 后再用（超出部分无意义），
            // 中心/半径也必须钳制，否则 0 或负半径会让 RadialGradientBrush 构造抛异常。
            _angleBox.TextChanged += (s, e) => TryParseBox(_angleBox, v => _settings.GradientAngle = ClampAngle(v), () => RefreshPreview());
            angleRow.Children.Add(_angleBox);
            angleRow.Children.Add(new TextBlock { Text = "°", Foreground = _owner._textDim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            geoSp.Children.Add(angleRow);

            var centerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
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

            // 功能3：网格 CSS 导入/导出卡片（仅网格模式可见）
            BuildCssCard(listSp);

            _listCard.Child = listSp;
            editPanel.Children.Add(_listCard);

            // 网格专属卡片：底色（Base color）+ 预设模板，仅网格模式可见
            BuildMeshCard(editPanel);

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

        private StackPanel CreateRgbSlider(out Slider slider, out TextBlock valBlock, string label)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            // 标签宽度与明度滑块标签统一（36px），使 RGB 滑块与明度滑块左右边缘对齐，视觉上长度一致
            panel.Children.Add(new TextBlock { Text = label + ":", Foreground = _owner._textMain, Width = 36, VerticalAlignment = VerticalAlignment.Center });
            slider = new Slider { Minimum = 0, Maximum = 255, Width = 150, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            valBlock = new TextBlock { Text = "0", Foreground = _owner._accent, Width = 32, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(slider);
            panel.Children.Add(valBlock);
            return panel;
        }

        // ---- 事件处理 ----

        private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
                // 模式切换后，把当前编辑颜色同步到自动选中的首个停靠点/光斑，让色轮/HEX/RGB 保持一致
                if (_selectedStop != null)
                {
                    _currentColor = BackgroundSettings.ParseColor(_selectedStop.Color);
                    UpdateColor(_currentColor, true);   // 内部已请求色轮重绘，去掉原重复的 RenderColorWheel()
                }
                else if (_selectedBlob != null)
                {
                    _currentColor = BackgroundSettings.ParseColor(_selectedBlob.Color);
                    UpdateColor(_currentColor, true);   // 同上
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
            var c = BackgroundSettings.ParseColor(_hexBox.Text);
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
            _settings.EnsureGradientStops();
            var last = _settings.Stops.LastOrDefault();
            double off = last != null ? Math.Min(last.Offset + 0.2, 1.0) : 1.0;
            var ns = new GradientStopSetting { Color = BackgroundSettings.ColorToHex(_currentColor), Offset = off };
            _settings.Stops.Add(ns);
            _settings.Stops = _settings.Stops.OrderBy(x => x.Offset).ToList();
            _selectedStop = ns;
            _selectedBlob = null;
            RefreshStopList();
            RefreshPreview();
        }

        private void AddBlob_Click(object sender, RoutedEventArgs e)
        {
            _settings.EnsureMeshBlobs();
            double radius = 0.35;
            double cx = 0.3 + (_settings.Blobs.Count % 3) * 0.2;
            double cy = 0.3 + (_settings.Blobs.Count / 3) * 0.2;
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
            _settings.Blobs.Add(nb);
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

            SetVisibility(_geometryCard, showAngle || showRadial);
            SetVisibility(_angleBox.Parent as StackPanel, showAngle);
            SetVisibility(_centerXBox.Parent as StackPanel, showAngle || showRadial);
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
            SetVisibility(_cssCard, showBlobs);
            if (showBlobs && _cssBox != null) _cssBox.Text = BackgroundSettings.ToCssGradient(_settings.Blobs);

            // 网格专属卡片（底色 + 预设）：仅网格模式可见
            SetVisibility(_meshCard, showBlobs);
            if (showBlobs) UpdateMeshBaseUi();
            // 色阶生成器：非图片模式可见
            SetVisibility(_sttCard, showColor);
            if (showColor) UpdateShadesTintsTones();

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

            // 删除已不存在的停靠点对应的 UI 行
            var removed = new List<GradientStopSetting>();
            foreach (var kv in _stopRows)
            {
                if (!_settings.Stops.Contains(kv.Key))
                {
                    _stopPanel.Children.Remove(kv.Value);
                    removed.Add(kv.Key);
                }
            }
            foreach (var r in removed) _stopRows.Remove(r);

            // 按当前停靠点顺序重建/调整 UI 行
            for (int i = 0; i < _settings.Stops.Count; i++)
            {
                var stop = _settings.Stops[i];
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
                var c = BackgroundSettings.ParseColor(hex.Text);
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
                _settings.Stops.Remove(stop);
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
            if (!slider.IsMouseCaptureWithin)
                slider.Value = stop.Offset;

            UpdateEditLabel();
        }

        private void RefreshBlobList()
        {
            _settings.EnsureMeshBlobs();

            var removed = new List<MeshBlobSetting>();
            foreach (var kv in _blobRows)
            {
                if (!_settings.Blobs.Contains(kv.Key))
                {
                    _blobPanel.Children.Remove(kv.Value);
                    removed.Add(kv.Key);
                }
            }
            foreach (var r in removed) _blobRows.Remove(r);

            for (int i = 0; i < _settings.Blobs.Count; i++)
            {
                var blob = _settings.Blobs[i];
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
                _selectedBlob = blob;
                _currentColor = BackgroundSettings.ParseColor(blob.Color);
                UpdateColor(_currentColor, true);
                RefreshBlobList();
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
                _selectedBlob = blob;
                _currentColor = BackgroundSettings.ParseColor(blob.Color);
                UpdateColor(_currentColor, true); // 同步 RGB/HEX/明度滑块
                RefreshBlobList();
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
                var c = BackgroundSettings.ParseColor(hex.Text);
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
                _settings.Blobs.Remove(blob);
                if (_selectedBlob == blob) _selectedBlob = _settings.Blobs.FirstOrDefault();
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

        private void UpdateColor(Color c, bool updateSliders)
        {
            _currentColor = c;
            if (!_isUpdating) _hasEditedAfterShow = true;   // 初始化阶段不算编辑；色轮/HEX/RGB 滑块等真实用户操作均视为编辑
            if (updateSliders)
            {
                _isUpdating = true;
                try
                {
                    _rSlider.Value = c.R;
                    _gSlider.Value = c.G;
                    _bSlider.Value = c.B;
                    _hexBox.Text = BackgroundSettings.ColorToHex(c);
                    SyncHsvFromColor(c);              // 让色轮/明度滑块与当前颜色同步
                    _valSlider.Value = _currentValue;
                    RenderColorWheel();               // 当前颜色亮度可能改变，重绘色轮使指示器下方像素一致
                }
                finally { _isUpdating = false; }
            }
            _rVal.Text = c.R.ToString();
            _gVal.Text = c.G.ToString();
            _bVal.Text = c.B.ToString();

            // 同步颜色格式显示（RGB / HSL / HSV / CMYK）与对比度检查
            SetFmt(_rowRgb, $"{c.R}, {c.G}, {c.B}");
            BackgroundSettings.RgbToHsl(c.R, c.G, c.B, out double hL, out double sL, out double lL);
            SetFmt(_rowHsl, $"{hL:F1}°, {sL * 100:F1}%, {lL * 100:F1}%");
            SetFmt(_rowHsv, $"{_currentHue:F1}°, {_currentSat * 100:F1}%, {_currentValue * 100:F1}%");
            BackgroundSettings.RgbToCmyk(c.R, c.G, c.B, out double cK, out double mK, out double yK, out double kK);
            SetFmt(_rowCmyk, $"{cK * 100:F1}%, {mK * 100:F1}%, {yK * 100:F1}%, {kK * 100:F1}%");

            var bgColor = _settings.Mode == BackgroundMode.Solid
                ? c
                : BackgroundSettings.ParseColor(_settings.SolidColor);
            double ratio = ContrastRatio(c, bgColor);
            string verdict = ratio >= 7.0 ? "AAA 级（极佳）" : ratio >= 4.5 ? "AA 级（合格）" : ratio >= 3.0 ? "A 级（可接受）" : "不及格（文字可能难读）";
            _contrastText.Text = $"对比度 {ratio:F2}:1 — {verdict}";
            // 性能优化：低对比度提示色是常量，原来每次 UpdateColor（拖拽时每帧）都 new 一支笔刷。
            _contrastText.Foreground = ratio >= 4.5 ? _owner._successGreen ?? _owner._textDim : LowContrastBrush;

            // 如果当前有选中的 stop/blob，同步颜色并局部刷新该行的颜色块/HEX，避免重建整个列表导致滑块丢失焦点
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

            // 纯色模式直接同步
            if (_settings.Mode == BackgroundMode.Solid)
            {
                _settings.SolidColor = BackgroundSettings.ColorToHex(c);
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
        /// Clip=EllipseGeometry(new Rect(0,0,180,180)) 的圆心 (90,90) 错位，导致色轮四周内缩、右侧不圆。
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
            // 最小重绘：色轮像素只由明度 V 决定（色相由像素角度、饱和度由半径决定，与当前选取无关）。
            // 明度未变 → 位图内容已经正确，直接复用，省掉 32,400 次 HsvToRgb。
            if (_wheelBitmap != null && _wheelRenderedValue.Equals(_currentValue)) return;

            int w = 180, h = 180;
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
            // 色轮圆心/半径统一由 GetWheelGeometry 计算，确保与 Clip=EllipseGeometry(Rect(0,0,180,180)) 完全重合。
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
                        var c = HsvToRgb(hue, sat, _currentValue);
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
            _wheelRenderedValue = _currentValue;
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
                    _previewRect.Fill = _owner.BuildBackgroundBrushPreview(_settings);
                }
            }
            catch (Exception ex)
            {
                // 修复：原来只写 Debug 输出，参数非法时预览静默停在旧图、用户完全无感知。
                // 现在把错误同时写到窗口底部的状态条，让用户看得见。
                System.Diagnostics.Debug.WriteLine("[BgDlg] preview error: " + ex.Message);
                if (_cssStatus != null) _cssStatus.Text = "预览刷新失败（参数可能超出范围）：" + ex.Message;
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
            // 方案 A：右列大预览随当前主题显示对应的背景图（深色→DarkPath，浅色→LightPath）
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
                    // 投影到渐变轴方向 dir=(c,s)，offset = 投影长度 + 0.5（中线在中心处为 0.5）
                    double ux = mbx * c + mby * s;
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
                RefreshOverlayHandles();
                RefreshPreview();
                return;
            }
            if (_draggingLineEnd)
            {
                double dxc = pos2.X / w - _settings.LinearCenterX;
                double dyc = pos2.Y / h - _settings.LinearCenterY;
                _settings.GradientAngle = Math.Atan2(dyc, dxc) * 180.0 / Math.PI;
                _linearAngleRad = _settings.GradientAngle * Math.PI / 180.0;
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
            double dx = Math.Cos(ang), dy = Math.Sin(ang);
            double sx = (cx - 0.5 * dx) * w, sy = (cy - 0.5 * dy) * h;   // 线起点 = 中心 - 0.5·方向
            double ex = (cx + 0.5 * dx) * w, ey = (cy + 0.5 * dy) * h;   // 线终点 = 中心 + 0.5·方向

            var axis = new Line
            {
                X1 = sx, Y1 = sy, X2 = ex, Y2 = ey,
                // 性能优化：轴线颜色是常量，原来每个 mousemove 都 new 一支笔刷；改为共享的静态冻结笔刷。
                Stroke = AxisStrokeBrush,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 4 },
                Cursor = Cursors.SizeAll,
                IsHitTestVisible = true
            };
            // 拖轴线本体 = 平移渐变中心
            axis.MouseLeftButtonDown += (s2, e2) =>
            {
                _draggingLineCenter = true;
                _dragStartMouse = e2.GetPosition(_previewOverlay);
                _dragStartLineCenter = new Point(_settings.LinearCenterX, _settings.LinearCenterY);
                _previewOverlay.CaptureMouse();
                e2.Handled = true;
            };
            _previewOverlay.Children.Add(axis);
            _gradientOverlayItems.Add(axis);

            // 两端圆点 = 绕中心旋转角度
            _gradientOverlayItems.Add(AddDot(sx, sy, Brushes.White, (s, e) => { _draggingLineEnd = true; _previewOverlay.CaptureMouse(); e.Handled = true; }));
            _gradientOverlayItems.Add(AddDot(ex, ey, Brushes.White, (s, e) => { _draggingLineEnd = true; _previewOverlay.CaptureMouse(); e.Handled = true; }));

            foreach (var stop in _settings.Stops.OrderBy(x => x.Offset))
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
        }

        private Ellipse AddDot(double x, double y, Brush stroke, MouseButtonEventHandler down)
        {
            var dot = new Ellipse
            {
                Width = 12, Height = 12,
                StrokeThickness = 2,
                Cursor = Cursors.Hand,
                IsHitTestVisible = true,
                Fill = Brushes.Transparent,
                Stroke = stroke
            };
            Canvas.SetLeft(dot, x - 6);
            Canvas.SetTop(dot, y - 6);
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
            foreach (var stop in _settings.Stops.OrderBy(x => x.Offset))
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
                    int n = _settings.Stops.IndexOf(_selectedStop) + 1;
                    int pct = (int)Math.Round(_selectedStop.Offset * 100);
                    _editLabel.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "正在编辑：停靠点 #{0}（位置 {1}%）", n, pct);
                }
                else
                    _editLabel.Text = "正在编辑：未选择停靠点";
            }
            else if (_settings.Mode == BackgroundMode.MeshGradient)
            {
                _editLabel.Visibility = Visibility.Visible;
                if (_selectedBlob != null)
                {
                    int n = _settings.Blobs.IndexOf(_selectedBlob) + 1;
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
            hSp.Children.Add(new TextBlock
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
                Content = "🎲 随机",
                Height = 26,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(8, 0, 0, 0),
                Background = _owner._btnSecondaryBg,
                Foreground = _owner._btnSecondaryFg,
                BorderThickness = new Thickness(0)
            };
            randHarmonyBtn.Click += (s, e) =>
            {
                _currentHue = new Random().Next(360);
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
        }

        private void ApplyHarmonyToGradient_Click(object sender, RoutedEventArgs e)
        {
            var palette = BuildHarmonyPalette();
            if (palette.Count < 2) return;
            _settings.EnsureGradientStops();
            _settings.Stops = palette.Select((c, i) => new GradientStopSetting
            {
                Color = BackgroundSettings.ColorToHex(c),
                Offset = palette.Count == 1 ? 0.0 : (double)i / (palette.Count - 1)
            }).ToList();
            _selectedStop = _settings.Stops[0];
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

        // 颜色格式只读块：标签+只读框在同一行，复制按钮放在下一行并右对齐。
        // 所有格式框统一以 CMYK 最长内容（30 字符）定宽，避免过长或过短。
        private StackPanel MakeReadOnlyFormatBox(string label)
        {
            const double labelWidth = 42;   // CMYK: 4 字母+冒号
            const double boxWidth = 240;    // 容纳 "100.0%, 100.0%, 100.0%, 100.0%"（30 字符），留少量余量

            // 整行容器：垂直排列；HorizontalAlignment=Left 让容器按内容宽度收缩，
            // 复制按钮右对齐时才能贴到文本框右缘，而不是贴到父 Grid 单元格右缘。
            var root = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = label + ":", Width = labelWidth, Foreground = _owner._textMain, VerticalAlignment = VerticalAlignment.Center });
            var box = new TextBox
            {
                Width = boxWidth,
                IsReadOnly = true,
                Background = _owner._inputBg,
                Foreground = _owner._inputFg,
                BorderBrush = _owner._panelBorder,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas")
            };
            row.Children.Add(box);
            root.Children.Add(row);

            var copy = new Button
            {
                Content = "复制",
                Width = 32,
                Height = 22,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0),
                Background = _owner._btnSecondaryBg,
                Foreground = _owner._btnSecondaryFg,
                BorderThickness = new Thickness(0)
            };
            copy.Click += (sender, args) => CopyText(box.Text, copy);
            root.Children.Add(copy);

            // 把 TextBox 挂在 Tag 上，SetFmt 可直接取，避免硬编码视觉树索引。
            root.Tag = box;
            return root;
        }

        private static void SetFmt(StackPanel block, string text)
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
            catch { }
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
            cssSp.Children.Add(new TextBlock
            {
                Text = "网格 CSS 导入/导出",
                FontWeight = FontWeights.SemiBold,
                Foreground = _owner._textMain,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var cssBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var copyCssBtn = new Button
            {
                Content = "复制为 CSS",
                Height = 28,
                Width = 90,
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
                Width = 110,
                Margin = new Thickness(0, 0, 8, 0),
                Background = _owner._accent,
                Foreground = _owner._btnPrimaryFg,
                BorderThickness = new Thickness(0)
            };
            importCssBtn.Click += ImportCss_Click;
            cssBtnRow.Children.Add(importCssBtn);

            var randMeshBtn = new Button
            {
                Content = "🎲 随机光斑",
                Height = 28,
                Width = 100,
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
                _selectedBlob = _settings.Blobs[0];
                _selectedStop = null;
                _currentColor = BackgroundSettings.ParseColor(_selectedBlob.Color);
                UpdateColor(_currentColor, true);
                RefreshBlobList();
                RefreshPreview();
                UpdateEditLabel();
                if (_cssStatus != null) _cssStatus.Text = "已随机生成 " + _settings.Blobs.Count + " 个光斑。";
            };
            cssBtnRow.Children.Add(randMeshBtn);

            var exportSvgBtn = new Button
            {
                Content = "导出 SVG",
                Height = 28,
                Width = 90,
                Margin = new Thickness(0, 0, 0, 0),
                Background = _owner._btnSecondaryBg,
                Foreground = _owner._btnSecondaryFg,
                BorderThickness = new Thickness(0)
            };
            exportSvgBtn.Click += ExportSvg_Click;
            cssBtnRow.Children.Add(exportSvgBtn);
            cssSp.Children.Add(cssBtnRow);

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

            _cssStatus = new TextBlock
            {
                FontSize = 11.0,
                Foreground = _owner._textDim,
                TextWrapping = TextWrapping.Wrap
            };
            cssSp.Children.Add(_cssStatus);

            _cssCard.Child = cssSp;
            parent.Children.Add(_cssCard);
        }

        private void CopyCss_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var css = BackgroundSettings.ToCssGradient(_settings.Blobs);
                if (_cssBox != null) _cssBox.Text = css;
                Clipboard.SetText(css); // 按钮点击在 UI 线程（STA），可直接写入剪贴板
                if (_cssStatus != null) _cssStatus.Text = "已复制 " + _settings.Blobs.Count + " 个光斑的 CSS 到剪贴板。";
            }
            catch (Exception ex)
            {
                if (_cssStatus != null) _cssStatus.Text = "复制失败：" + ex.Message;
            }
        }

        private void ImportCss_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var css = _cssBox != null ? _cssBox.Text : string.Empty;
                var blobs = BackgroundSettings.ParseCssGradient(css);
                if (blobs == null)
                {
                    if (_cssStatus != null) _cssStatus.Text = "未识别到有效的 radial-gradient 层，请检查 CSS 格式（示例：radial-gradient(at 20% 30%, hsla(180,80%,50%,0.8) 0%, hsla(180,80%,50%,0) 100%)）。";
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
                if (_cssStatus != null) _cssStatus.Text = "已导入 " + blobs.Count + " 个光斑。";
            }
            catch (Exception ex)
            {
                if (_cssStatus != null) _cssStatus.Text = "导入失败：" + ex.Message;
            }
        }

        // ===================== 网格底色 + 预设模板 =====================

        private void BuildMeshCard(StackPanel parent)
        {
            _meshCard = new Border
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
                Text = "网格底色 & 预设模板",
                FontWeight = FontWeights.SemiBold,
                Foreground = _owner._textMain,
                Margin = new Thickness(0, 0, 0, 8)
            });

            // 网格底色（Base color）
            var baseRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            baseRow.Children.Add(new TextBlock { Text = "底色:", Foreground = _owner._textMain, Width = 40, VerticalAlignment = VerticalAlignment.Center });
            _baseSwatch = new Border
            {
                Width = 28,
                Height = 20,
                CornerRadius = new CornerRadius(3),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 0),
                BorderBrush = _owner._panelBorder,
                BorderThickness = new Thickness(1)
            };
            _baseSwatch.MouseLeftButtonDown += (s, e) =>
            {
                _settings.MeshBaseColor = BackgroundSettings.ColorToHex(_currentColor);
                UpdateMeshBaseUi();
                RefreshPreview();
            };
            baseRow.Children.Add(_baseSwatch);
            _baseHexBox = new TextBox
            {
                Width = 80,
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
                    _settings.MeshBaseColor = norm;
                    UpdateMeshBaseUi();
                    RefreshPreview();
                }
            };
            baseRow.Children.Add(_baseHexBox);
            var basePickBtn = new Button
            {
                Content = "用当前色",
                Height = 24,
                Margin = new Thickness(6, 0, 0, 0),
                Background = _owner._btnSecondaryBg,
                Foreground = _owner._btnSecondaryFg,
                BorderThickness = new Thickness(0)
            };
            basePickBtn.Click += (s, e) =>
            {
                _settings.MeshBaseColor = BackgroundSettings.ColorToHex(_currentColor);
                UpdateMeshBaseUi();
                RefreshPreview();
            };
            baseRow.Children.Add(basePickBtn);
            sp.Children.Add(baseRow);

            // 预设模板
            sp.Children.Add(new TextBlock
            {
                Text = "预设模板（点击应用）:",
                Foreground = _owner._textDim,
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 4)
            });
            var presetWrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var p in BackgroundSettings.GetMeshPresets())
            {
                var btn = new Button
                {
                    Content = p.Name,
                    Height = 26,
                    MinWidth = 56,
                    Margin = new Thickness(0, 0, 6, 6),
                    Padding = new Thickness(8, 2, 8, 2),
                    Background = _owner._btnSecondaryBg,
                    Foreground = _owner._btnSecondaryFg,
                    BorderThickness = new Thickness(0)
                };
                btn.Click += (s, e) => ApplyPreset(p);
                presetWrap.Children.Add(btn);
            }
            sp.Children.Add(presetWrap);

            _meshCard.Child = sp;
            parent.Children.Add(_meshCard);
        }

        private void ApplyPreset(BackgroundSettings.MeshPreset preset)
        {
            if (preset == null) return;
            _settings.Mode = BackgroundMode.MeshGradient;
            _settings.MeshBaseColor = preset.BaseColor;
            _settings.Blobs = preset.Blobs.Select(b => new MeshBlobSetting
            {
                Color = b.Color,
                CenterX = b.CenterX,
                CenterY = b.CenterY,
                Radius = b.Radius,
                Opacity = b.Opacity
            }).ToList();
            _selectedBlob = _settings.Blobs.FirstOrDefault();
            _selectedStop = null;
            _currentColor = BackgroundSettings.ParseColor(_selectedBlob.Color);
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
            if (_cssBox != null) _cssBox.Text = BackgroundSettings.ToCssGradient(_settings.Blobs);
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
                _baseSwatch.Background = MakeColorBrush(_settings.MeshBaseColor);
                if (!_baseHexBox.IsKeyboardFocused && _baseHexBox.Text != _settings.MeshBaseColor)
                    _baseHexBox.Text = _settings.MeshBaseColor;
            }
            finally { _isUpdating = false; }
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
                    if (_cssStatus != null) _cssStatus.Text = "SVG 导出仅支持网格渐变模式，且至少需有 1 个光斑。";
                    return;
                }
                var dlg = new SaveFileDialog
                {
                    Filter = "SVG 文件|*.svg|所有文件|*.*",
                    FileName = "mesh-gradient.svg",
                    DefaultExt = ".svg",
                    Title = "导出网格渐变为 SVG"
                };
                if (dlg.ShowDialog() == true)
                {
                    var svg = BackgroundSettings.ToSvg(_settings.Blobs, _settings.MeshBaseColor);
                    AtomicFile.WriteFileAtomic(dlg.FileName, svg);
                    if (_cssStatus != null) _cssStatus.Text = "已导出 SVG：" + dlg.FileName;
                }
            }
            catch (Exception ex)
            {
                if (_cssStatus != null) _cssStatus.Text = "导出失败：" + ex.Message;
            }
        }
    }
}

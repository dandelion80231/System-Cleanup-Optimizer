using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
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

        // 当前编辑态
        private GradientStopSetting _selectedStop;
        private MeshBlobSetting _selectedBlob;
        private Color _currentColor = Color.FromRgb(0x16, 0xE0, 0xBD);
        private double _currentHue;            // HSV 色相（色轮取色用）
        private double _currentSat;            // HSV 饱和度
        private double _currentValue = 0.95;   // HSV 明度（0..1），配合色轮选暗色背景
        private bool _isUpdating;
        private bool _isInitializing;          // 防止初始化时 SelectionChanged 重入导致 UI 树抖动
        private Slider _valSlider;             // 明度滑块
        private Border _colorCard;             // 颜色选择区外层卡片（用于整体显隐）
        private Canvas _wheelOverlay;          // 色轮上的当前颜色指示器容器
        private Ellipse _wheelIndicator;       // 当前颜色在色轮上的位置指示器

        // 增量维护停靠点/光斑列表的 UI 映射，避免每次改颜色都重建整个列表导致滑块焦点丢失
        private readonly Dictionary<GradientStopSetting, StackPanel> _stopRows = new Dictionary<GradientStopSetting, StackPanel>();
        private readonly Dictionary<MeshBlobSetting, StackPanel> _blobRows = new Dictionary<MeshBlobSetting, StackPanel>();

        public BackgroundSettings ResultSettings => _settings;

        public BackgroundSettingsDialog(MainWindow owner, BackgroundSettings settings)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _initial = settings?.Clone() ?? new BackgroundSettings();
            _settings = _initial.Clone();

            Title = "自定义背景";
            Width = 860;
            Height = 560;
            MinWidth = 700;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // 使用标准单边框窗口，确保标题栏×关闭按钮在所有主题下都稳定响应
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            Background = owner._windowBg;
            Foreground = owner._textMain;
            FontFamily = new FontFamily("Microsoft YaHei");

            // 关闭窗口时（包括点标题栏×）：若不是保存退出，则恢复打开前的初始背景
            Closing += (s, e) =>
            {
                if (DialogResult != true)
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
            _modeCombo = new ComboBox
            {
                Width = 140,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = _owner._inputBg,
                Foreground = _owner._inputFg,
                BorderBrush = _owner._panelBorder
            };
            _modeCombo.Items.Add(new ComboBoxItem { Content = "图片", Tag = BackgroundMode.Image });
            _modeCombo.Items.Add(new ComboBoxItem { Content = "纯色", Tag = BackgroundMode.Solid });
            _modeCombo.Items.Add(new ComboBoxItem { Content = "线性渐变", Tag = BackgroundMode.LinearGradient });
            _modeCombo.Items.Add(new ComboBoxItem { Content = "径向渐变", Tag = BackgroundMode.RadialGradient });
            _modeCombo.Items.Add(new ComboBoxItem { Content = "网格渐变", Tag = BackgroundMode.MeshGradient });
            _modeCombo.SelectionChanged += ModeCombo_SelectionChanged;
            modePanel.Children.Add(_modeCombo);
            header.Children.Add(modePanel);
            root.Children.Add(header);
            Grid.SetRow(header, 0);

            // 第 1 行：左编辑区 + 右预览区
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

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

            // 色轮
            var wheelTitle = new TextBlock
            {
                Text = "色轮",
                FontWeight = FontWeights.SemiBold,
                Foreground = _owner._textMain,
                Margin = new Thickness(0, 0, 0, 8)
            };
            colorSp.Children.Add(wheelTitle);

            // 色轮容器：用 Grid 把 Image 和指示器 Canvas 叠在一起，并用 EllipseGeometry 真正裁成圆形
            var wheelGrid = new Grid
            {
                Width = 180,
                Height = 180,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 10)
            };
            _wheelHost = new Border
            {
                Width = 180,
                Height = 180,
                CornerRadius = new CornerRadius(90),
                BorderThickness = new Thickness(1),
                BorderBrush = _owner._panelBorder,
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
            colorSp.Children.Add(wheelGrid);

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
            colorSp.Children.Add(hexRow);

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
            colorSp.Children.Add(rgbPanel);

            // 明度（HSV 的 V）：配合色轮取色，可选取暗色背景；拖动时色轮同步变暗/变亮
            var valRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            valRow.Children.Add(new TextBlock { Text = "明度:", Foreground = _owner._textMain, Width = 36, VerticalAlignment = VerticalAlignment.Center });
            _valSlider = new Slider { Minimum = 0, Maximum = 1, Width = 180, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            _valSlider.ValueChanged += (s, e) =>
            {
                if (_isUpdating) return;
                _currentValue = _valSlider.Value;
                _currentColor = HsvToRgb(_currentHue, _currentSat, _currentValue);
                RenderColorWheel();               // 明度改变，重绘色轮
                UpdateColor(_currentColor, true); // 同步 RGB/HEX/明度滑块
            };
            valRow.Children.Add(_valSlider);
            colorSp.Children.Add(valRow);

            _colorCard.Child = colorSp;
            editPanel.Children.Add(_colorCard);

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
            _angleBox.TextChanged += (s, e) => TryParseBox(_angleBox, v => _settings.GradientAngle = v, () => RefreshPreview());
            angleRow.Children.Add(_angleBox);
            angleRow.Children.Add(new TextBlock { Text = "°", Foreground = _owner._textDim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            geoSp.Children.Add(angleRow);

            var centerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            centerRow.Children.Add(new TextBlock { Text = "中心 X:", Foreground = _owner._textMain, Width = 50, VerticalAlignment = VerticalAlignment.Center });
            _centerXBox = new TextBox { Width = 50, Background = _owner._inputBg, Foreground = _owner._inputFg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center };
            _centerXBox.TextChanged += (s, e) => TryParseBox(_centerXBox, v => _settings.RadialCenterX = v, () => RefreshPreview());
            centerRow.Children.Add(_centerXBox);
            centerRow.Children.Add(new TextBlock { Text = "Y:", Foreground = _owner._textMain, Width = 26, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextAlignment = TextAlignment.Center });
            _centerYBox = new TextBox { Width = 50, Background = _owner._inputBg, Foreground = _owner._inputFg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center };
            _centerYBox.TextChanged += (s, e) => TryParseBox(_centerYBox, v => _settings.RadialCenterY = v, () => RefreshPreview());
            centerRow.Children.Add(_centerYBox);
            geoSp.Children.Add(centerRow);

            var radiusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            radiusRow.Children.Add(new TextBlock { Text = "半径 X:", Foreground = _owner._textMain, Width = 50, VerticalAlignment = VerticalAlignment.Center });
            _radiusXBox = new TextBox { Width = 50, Background = _owner._inputBg, Foreground = _owner._inputFg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center };
            _radiusXBox.TextChanged += (s, e) => TryParseBox(_radiusXBox, v => _settings.RadialRadiusX = v, () => RefreshPreview());
            radiusRow.Children.Add(_radiusXBox);
            radiusRow.Children.Add(new TextBlock { Text = "Y:", Foreground = _owner._textMain, Width = 26, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextAlignment = TextAlignment.Center });
            _radiusYBox = new TextBox { Width = 50, Background = _owner._inputBg, Foreground = _owner._inputFg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center };
            _radiusYBox.TextChanged += (s, e) => TryParseBox(_radiusYBox, v => _settings.RadialRadiusY = v, () => RefreshPreview());
            radiusRow.Children.Add(_radiusYBox);
            geoSp.Children.Add(radiusRow);

            _geometryCard.Child = geoSp;
            editPanel.Children.Add(_geometryCard);

            // 停靠点/光斑列表
            var listCard = new Border
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

            listCard.Child = listSp;
            editPanel.Children.Add(listCard);

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
                ClipToBounds = true
            };
            _previewRect = new Rectangle();
            _previewHost.Child = _previewRect;
            content.Children.Add(_previewHost);
            Grid.SetColumn(_previewHost, 1);

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
            _applyBtn.Click += (s, e) => { _owner.ApplyBackgroundSettings(_settings.Clone()); };
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
            panel.Children.Add(new TextBlock { Text = label + ":", Foreground = _owner._textMain, Width = 24, VerticalAlignment = VerticalAlignment.Center });
            slider = new Slider { Minimum = 0, Maximum = 255, Width = 180, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            valBlock = new TextBlock { Text = "0", Foreground = _owner._accent, Width = 32, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(slider);
            panel.Children.Add(valBlock);
            return panel;
        }

        // ---- 事件处理 ----

        private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (_modeCombo.SelectedItem is ComboBoxItem item && item.Tag is BackgroundMode mode)
            {
                _settings.Mode = mode;
                SyncModeUi();
                // 模式切换后，把当前编辑颜色同步到自动选中的首个停靠点/光斑，让色轮/HEX/RGB 保持一致
                if (_selectedStop != null)
                {
                    _currentColor = BackgroundSettings.ParseColor(_selectedStop.Color);
                    UpdateColor(_currentColor, true);
                    RenderColorWheel();
                }
                else if (_selectedBlob != null)
                {
                    _currentColor = BackgroundSettings.ParseColor(_selectedBlob.Color);
                    UpdateColor(_currentColor, true);
                    RenderColorWheel();
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
            double cx = _wheelHost.Width / 2;
            double cy = _wheelHost.Height / 2;
            double dx = pos.X - cx;
            double dy = pos.Y - cy;
            double r = Math.Sqrt(dx * dx + dy * dy);
            double maxR = Math.Min(cx, cy);
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
            if (double.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
            {
                setter(v);
                onChanged();
            }
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
                _centerXBox.Text = _settings.RadialCenterX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                _centerYBox.Text = _settings.RadialCenterY.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                _radiusXBox.Text = _settings.RadialRadiusX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                _radiusYBox.Text = _settings.RadialRadiusY.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

                // 初始颜色：根据模式取一个有意义的颜色
                if (_settings.Mode == BackgroundMode.Solid)
                    _currentColor = BackgroundSettings.ParseColor(_settings.SolidColor);
                else if (_settings.Stops != null && _settings.Stops.Count > 0)
                    _currentColor = BackgroundSettings.ParseColor(_settings.Stops[0].Color);
                else if (_settings.Blobs != null && _settings.Blobs.Count > 0)
                    _currentColor = BackgroundSettings.ParseColor(_settings.Blobs[0].Color);

                UpdateColor(_currentColor, true);
                SyncModeUi();
                RenderColorWheel();
                UpdateWheelIndicator();
                RefreshPreview();
            }
            finally { _isUpdating = false; _isInitializing = false; }
        }

        private void SyncModeUi()
        {
            var mode = _settings.Mode;
            // 渐变/网格模式自动选中首个停靠点/光斑，使色轮点击始终有作用目标（避免"点了没反应"）
            _selectedStop = null;
            _selectedBlob = null;
            if (mode == BackgroundMode.LinearGradient || mode == BackgroundMode.RadialGradient)
            {
                _settings.EnsureGradientStops();
                if (_settings.Stops.Count > 0) _selectedStop = _settings.Stops[0];
            }
            else if (mode == BackgroundMode.MeshGradient)
            {
                _settings.EnsureMeshBlobs();
                if (_settings.Blobs.Count > 0) _selectedBlob = _settings.Blobs[0];
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

            // 通过父容器 Border / StackPanel 控制显隐
            SetVisibility(_colorCard, showColor);
            SetVisibility(_geometryCard, showAngle || showRadial);
            SetVisibility(_angleBox.Parent as StackPanel, showAngle);
            SetVisibility(_centerXBox.Parent as StackPanel, showRadial);
            SetVisibility(_radiusXBox.Parent as StackPanel, showRadial);
            SetVisibility(_stopPanel, showStops);
            _addStopBtn.Visibility = showStops ? Visibility.Visible : Visibility.Collapsed;
            SetVisibility(_blobPanel, showBlobs);
            _addBlobBtn.Visibility = showBlobs ? Visibility.Visible : Visibility.Collapsed;
            // 刷新列表以反映自动选中的停靠点/光斑高亮
            RefreshStopList();
            RefreshBlobList();
        }

        private static void SetVisibility(FrameworkElement el, bool visible)
        {
            if (el != null) el.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
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
                RefreshPreview();
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

            var colorBox = (Border)row.Children[0];
            colorBox.Background = new SolidColorBrush(BackgroundSettings.ParseColor(stop.Color));
            colorBox.BorderBrush = isSel ? _owner._accent : _owner._panelBorder;
            colorBox.BorderThickness = isSel ? new Thickness(2) : new Thickness(1);

            var hex = (TextBox)row.Children[1];
            if (!hex.IsKeyboardFocused && hex.Text != stop.Color)
                hex.Text = stop.Color;

            var slider = (Slider)row.Children[3];
            if (!slider.IsMouseCaptureWithin)
                slider.Value = stop.Offset;
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
                    UpdateBlobRow(blob);
                    RefreshPreview();
                }
            };
            row.Children.Add(hex);

            row.Children.Add(new TextBlock { Text = "X", Foreground = _owner._textDim, FontSize = 10.0, VerticalAlignment = VerticalAlignment.Center });
            var xBox = new TextBox { Width = 36, Background = _owner._inputBg, Foreground = _owner._inputFg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
            xBox.TextChanged += (s, e) => { if (double.TryParse(xBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) { blob.CenterX = Math.Max(0, Math.Min(1, v)); RefreshPreview(); } };
            row.Children.Add(xBox);

            row.Children.Add(new TextBlock { Text = "Y", Foreground = _owner._textDim, FontSize = 10.0, VerticalAlignment = VerticalAlignment.Center });
            var yBox = new TextBox { Width = 36, Background = _owner._inputBg, Foreground = _owner._inputFg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
            yBox.TextChanged += (s, e) => { if (double.TryParse(yBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) { blob.CenterY = Math.Max(0, Math.Min(1, v)); RefreshPreview(); } };
            row.Children.Add(yBox);

            row.Children.Add(new TextBlock { Text = "R", Foreground = _owner._textDim, FontSize = 10.0, VerticalAlignment = VerticalAlignment.Center });
            var rBox = new TextBox { Width = 36, Background = _owner._inputBg, Foreground = _owner._inputFg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
            rBox.TextChanged += (s, e) => { if (double.TryParse(rBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) { blob.Radius = Math.Max(0.05, Math.Min(1, v)); RefreshPreview(); } };
            row.Children.Add(rBox);

            row.Children.Add(new TextBlock { Text = "A", Foreground = _owner._textDim, FontSize = 10.0, VerticalAlignment = VerticalAlignment.Center });
            var aBox = new TextBox { Width = 36, Background = _owner._inputBg, Foreground = _owner._inputFg, BorderBrush = _owner._panelBorder, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
            aBox.TextChanged += (s, e) => { if (double.TryParse(aBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) { blob.Opacity = Math.Max(0, Math.Min(1, v)); RefreshPreview(); } };
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

            var colorBox = (Border)row.Children[0];
            colorBox.Background = new SolidColorBrush(BackgroundSettings.ParseColor(blob.Color));
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
        }

        private void UpdateColor(Color c, bool updateSliders)
        {
            _currentColor = c;
            if (updateSliders)
            {
                _isUpdating = true;
                _rSlider.Value = c.R;
                _gSlider.Value = c.G;
                _bSlider.Value = c.B;
                _hexBox.Text = BackgroundSettings.ColorToHex(c);
                SyncHsvFromColor(c);              // 让色轮/明度滑块与当前颜色同步
                _valSlider.Value = _currentValue;
                _isUpdating = false;
            }
            _rVal.Text = c.R.ToString();
            _gVal.Text = c.G.ToString();
            _bVal.Text = c.B.ToString();

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
            RefreshPreview();
        }

        private void RenderColorWheel()
        {
            int w = 180, h = 180;
            var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
            int stride = w * 4;
            byte[] pixels = new byte[h * stride];
            double cx = w / 2.0, cy = h / 2.0;
            double maxR = Math.Min(cx, cy);
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
            _wheelImage.Source = wb;
        }

        /// <summary>在色轮上绘制当前颜色位置（小圆环），让用户知道当前选的是色轮上哪一点。</summary>
        private void UpdateWheelIndicator()
        {
            if (_wheelIndicator == null || _wheelOverlay == null) return;
            double cx = _wheelHost.Width / 2;
            double cy = _wheelHost.Height / 2;
            double maxR = Math.Min(cx, cy);
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
                _previewRect.Fill = _owner.BuildBackgroundBrushPreview(_settings);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[BgDlg] preview error: " + ex.Message); }
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
    }
}

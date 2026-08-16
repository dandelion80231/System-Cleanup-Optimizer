using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace CpqSystemTool
{
    /// <summary>
    /// 自定义软件编辑/管理对话框集合。复用主界面主题笔刷与圆角阴影卡样式（与 InstallPathDialog 同源），
    /// 保证新增/编辑/管理自定义软件时视觉与主界面完全一致。
    /// </summary>
    internal static class DialogChrome
    {
        /// <summary>为独立弹窗注入圆角阴影卡所需的 ControlTemplate 与同名主题笔刷（主界面 Window.Resources 不会自动继承到独立窗口）。</summary>
        internal static void Apply(Window w, MainWindow owner)
        {
            w.WindowStyle = WindowStyle.None;
            w.AllowsTransparency = true;
            w.Background = Brushes.Transparent;
            w.ResizeMode = ResizeMode.NoResize;
            w.ShowInTaskbar = false;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (owner == null) return;

            w.Resources["AccentBrush"] = owner.Resources["AccentBrush"] ?? (object)owner._accent;
            w.Resources["ButtonHoverBrush"] = owner.Resources["ButtonHoverBrush"];

            var btnTpl = new ControlTemplate(typeof(Button));
            var bBorder = new FrameworkElementFactory(typeof(Border), "PART_Border");
            bBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            bBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Border.BackgroundProperty));
            bBorder.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Border.BorderBrushProperty));
            bBorder.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Border.BorderThicknessProperty));
            bBorder.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Border.PaddingProperty));
            var bContent = new FrameworkElementFactory(typeof(ContentPresenter));
            bContent.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            bContent.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bBorder.AppendChild(bContent);
            btnTpl.VisualTree = bBorder;

            var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter { TargetName = "PART_Border", Property = Border.BackgroundProperty, Value = new DynamicResourceExtension("ButtonHoverBrush") });
            hover.Setters.Add(new Setter { TargetName = "PART_Border", Property = Border.BorderBrushProperty, Value = new DynamicResourceExtension("AccentBrush") });
            btnTpl.Triggers.Add(hover);
            var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.8));
            btnTpl.Triggers.Add(pressed);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
            btnTpl.Triggers.Add(disabled);

            var btnStyle = new Style(typeof(Button));
            btnStyle.Setters.Add(new Setter(Control.TemplateProperty, btnTpl));
            w.Resources[typeof(Button)] = btnStyle;
        }

        /// <summary>统一淡入错误提示（替代原生 MessageBox，风格与主界面一致）。</summary>
        internal static void ShowError(TextBlock tb, string msg)
        {
            tb.Text = msg;
            tb.Visibility = Visibility.Visible;
            tb.Opacity = 0;
            tb.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }

        /// <summary>
        /// 构建通用标题栏（可拖拽 + 关闭X）。返回 Border 由调用方加入布局。
        /// danger 为关闭按钮 hover 前景色，panelBorder 为标题栏底部分隔线颜色；差异用参数区分。
        /// </summary>
        internal static Border BuildTitleBar(Window w, string title, Brush fg, Brush dim, Brush danger, Brush panelBorder)
        {
            var titleBar = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Padding = new Thickness(16, 12, 12, 12),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = panelBorder,
                Cursor = Cursors.SizeAll
            };
            var titleGrid = new Grid();
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleGrid.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = fg,
                VerticalAlignment = VerticalAlignment.Center
            });
            var closeBtn = new Button
            {
                Content = "✕",
                Width = 28,
                Height = 28,
                FontSize = 13,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Foreground = dim,
                BorderThickness = new Thickness(0)
            };
            closeBtn.Click += (s, e) => w.DialogResult = false;
            closeBtn.MouseEnter += (s, e) => closeBtn.Foreground = danger;
            closeBtn.MouseLeave += (s, e) => closeBtn.Foreground = dim;
            Grid.SetColumn(closeBtn, 1);
            titleGrid.Children.Add(closeBtn);
            titleBar.Child = titleGrid;
            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource is DependencyObject src && closeBtn.IsAncestorOf(src)) return;
                w.DragMove();
            };
            return titleBar;
        }
    }

    /// <summary>
    /// 单条自定义软件编辑对话框：新增 / 修改。保存时经 SoftwareDefPersistence 写入 exe 同目录 custom_software.json。
    /// </summary>
    internal class CustomSoftwareEditDialog : Window
    {
        public CustomSoftwareEntry Entry { get; private set; }

        private readonly TextBox _idBox, _nameBox, _descBox, _urlBox, _argsBox, _uninstallBox,
            _storeBox, _chocolateyBox, _altBox, _pathsBox, _regKeyBox, _regKey2Box, _shaBox, _pageBox, _refererBox, _dirSwitchBox;
        private readonly ComboBox _riskCombo;
        private readonly ComboBox _categoryCombo;
        private readonly CheckBox _portableChk;
        private readonly TextBlock _errText;

        public CustomSoftwareEditDialog(MainWindow owner, CustomSoftwareEntry existing = null, string presetUrl = null, string presetName = null)
        {
            DialogChrome.Apply(this, owner);

            var fg = owner?._textMain ?? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
            var dim = owner?._textDim ?? new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            var panelBorder = owner?._panelBorder ?? new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x3C));
            var windowBg = owner?._windowBg ?? new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x1E));
            var danger = owner?._dangerRed ?? new SolidColorBrush(Color.FromRgb(0xE5, 0x4D, 0x4D));
            var rowHover = owner?._rowHover ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x26, 0x30));
            var rowSelected = owner?._rowSelected ?? new SolidColorBrush(Color.FromRgb(0x16, 0x36, 0x44));
            var isEdit = existing != null;

            Title = isEdit ? "编辑软件" : "新增增补软件";
            Width = 560;
            SizeToContent = SizeToContent.Height;
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) DialogResult = false; };

            var root = new Border
            {
                Background = windowBg,
                BorderBrush = panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 4,
                    Opacity = 0.35,
                    Color = Color.FromRgb(0x00, 0x00, 0x00)
                }
            };
            var stack = new StackPanel();

            // 标题栏（可拖拽 + 关闭）
            stack.Children.Add(DialogChrome.BuildTitleBar(this, Title, fg, dim, danger, panelBorder));

            // 内容区（可滚动，字段较多）
            var body = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            var scroller = new ScrollViewer
            {
                Content = body,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 540,
                Background = Brushes.Transparent
            };

            TextBox MakeField(string placeholder, string initial, bool multiline = false, double height = 0)
            {
                var tb = new TextBox
                {
                    FontSize = 12.5,
                    Padding = new Thickness(8, 6, 8, 6),
                    Background = owner?._inputBg ?? Brushes.Transparent,
                    Foreground = owner?._inputFg ?? fg,
                    BorderBrush = owner?._accent,
                    BorderThickness = new Thickness(1),
                    VerticalContentAlignment = VerticalAlignment.Top,
                    AcceptsReturn = multiline,
                    TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden
                };
                if (height > 0) tb.Height = height;
                if (!string.IsNullOrEmpty(placeholder)) tb.ToolTip = placeholder;
                tb.Text = initial ?? "";
                tb.GotFocus += (s, e) => tb.BorderBrush = owner?._accent;
                tb.LostFocus += (s, e) => tb.BorderBrush = owner?._accent;
                return tb;
            }

            UIElement Label(string text, string hint = null)
            {
                var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
                sp.Children.Add(new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = fg });
                if (!string.IsNullOrEmpty(hint))
                    sp.Children.Add(new TextBlock { Text = hint, FontSize = 10.5, Foreground = dim, Margin = new Thickness(0, 1, 0, 0) });
                return sp;
            }

            // ID
            body.Children.Add(Label("ID（可选）", "留空自动生成；与内置 ID 相同则覆盖内置项。"));
            _idBox = MakeField("自动生成", isEdit ? existing.id : "");
            // 内置条目 ID 固定：覆盖按此 ID 匹配，禁止改名，避免编辑内置改名后原内置与新条目并存产生重复
            if (isEdit && !existing.isCustom)
            {
                _idBox.IsEnabled = false;
                _idBox.ToolTip = "内置条目 ID 固定，覆盖按此 ID 匹配；如需另存为新条目请勿修改 ID";
            }
            body.Children.Add(_idBox);
            body.Children.Add(new TextBlock { Height = 8 });

            // 名称
            body.Children.Add(Label("软件名称 *", null));
            _nameBox = MakeField("如：MyApp", isEdit ? existing.name : presetName);
            body.Children.Add(_nameBox);
            body.Children.Add(new TextBlock { Height = 8 });

            // 描述
            body.Children.Add(Label("描述", null));
            _descBox = MakeField("如：下载工具", isEdit ? existing.desc : "");
            body.Children.Add(_descBox);
            body.Children.Add(new TextBlock { Height = 8 });

            // 分类（用于搜索页按分类筛选/展示；内置一组固定分类）
            body.Children.Add(Label("分类", "用于搜索页按分类筛选/展示，如「视频软件」「音乐」。"));
            _categoryCombo = new ComboBox
            {
                FontSize = 12.5,
                Padding = new Thickness(6, 5, 6, 5),
                Background = owner?._inputBg ?? Brushes.Transparent,
                Foreground = owner?._inputFg ?? fg,
                BorderBrush = owner?._accent,
                BorderThickness = new Thickness(1)
            };
            foreach (var c in SoftwareInstall.SoftwareCategories) _categoryCombo.Items.Add(c);
            _categoryCombo.SelectedItem = isEdit ? (existing.category ?? SoftwareInstall.DefaultCategory) : SoftwareInstall.DefaultCategory;
            body.Children.Add(_categoryCombo);
            // 统一深/浅色自适应（闭合框 + 下拉弹层背景与字体跟随主题）
            UiShapes.ApplyComboBoxTheme(_categoryCombo, owner?._inputBg ?? Brushes.Transparent, owner?._inputFg ?? fg, panelBorder, windowBg, panelBorder, fg, rowHover, rowSelected, dim);
            body.Children.Add(new TextBlock { Height = 8 });

            // 下载直链
            body.Children.Add(Label("下载直链 URL", "留空则走微软商店（需填下方商店 ID）或下方官方下载页解析。"));
            _urlBox = MakeField("https://.../*.exe", isEdit ? existing.url : presetUrl);
            // 离开 URL 框且名称/描述仍为空时，轻量自动补名（不覆盖已填内容）
            _urlBox.LostFocus += (s, e) => AutoFillFromUrlIfEmpty();
            body.Children.Add(_urlBox);
            // 自动识别按钮：从 URL 文件名推断名称 + 描述（均可手改）
            var autoBtnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
            var autoBtn = owner != null ? owner.Btn("🔍 自动识别", false, () => AutoFillFromUrl(), 96) : new Button { Content = "🔍 自动识别", Width = 96 };
            autoBtnRow.Children.Add(autoBtn);
            body.Children.Add(autoBtnRow);
            body.Children.Add(new TextBlock { Height = 8 });

            // 静默安装参数
            body.Children.Add(Label("静默安装参数", "空格分隔，如 /S 或 /VERYSILENT /NORESTART。"));
            _argsBox = MakeField("/S", isEdit ? string.Join(" ", existing.installArgs ?? new string[0]) : "");
            body.Children.Add(_argsBox);
            body.Children.Add(new TextBlock { Height = 8 });

            // 风险
            body.Children.Add(Label("风险等级", null));
            _riskCombo = new ComboBox
            {
                FontSize = 12.5,
                Padding = new Thickness(6, 5, 6, 5),
                Background = owner?._inputBg ?? Brushes.Transparent,
                Foreground = owner?._inputFg ?? fg,
                BorderBrush = owner?._accent,
                BorderThickness = new Thickness(1)
            };
            _riskCombo.Items.Add("low");
            _riskCombo.Items.Add("mid");
            _riskCombo.Items.Add("high");
            _riskCombo.SelectedItem = isEdit ? (existing.risk ?? "low") : "low";
            body.Children.Add(_riskCombo);
            // 统一深/浅色自适应（闭合框 + 下拉弹层背景与字体跟随主题）
            UiShapes.ApplyComboBoxTheme(_riskCombo, owner?._inputBg ?? Brushes.Transparent, owner?._inputFg ?? fg, panelBorder, windowBg, panelBorder, fg, rowHover, rowSelected, dim);
            body.Children.Add(new TextBlock { Height = 8 });

            // 商店 ID
            body.Children.Add(Label("微软商店 ID（StoreId）", "留空走普通安装；填写则走 winget 商店分支。"));
            _storeBox = MakeField("如：9PGJ3W9GK6L7", isEdit ? existing.storeId : "");
            body.Children.Add(_storeBox);
            body.Children.Add(new TextBlock { Height = 8 });

            // Chocolatey ID
            body.Children.Add(Label("Chocolatey ID（可选）", "留空则不使用；填写则安装时走 Chocolatey 实时解析分支。"));
            _chocolateyBox = MakeField("如：googlechrome", isEdit ? existing.chocolateyId : "");
            body.Children.Add(_chocolateyBox);
            body.Children.Add(new TextBlock { Height = 8 });

            // 卸载关键字
            body.Children.Add(Label("卸载匹配关键字", "注册表卸载项显示名；默认与名称相同。"));
            _uninstallBox = MakeField("如：MyApp", isEdit ? existing.uninstallKeyword : "");
            body.Children.Add(_uninstallBox);
            body.Children.Add(new TextBlock { Height = 8 });

            // 别名/英文关键字
            body.Children.Add(Label("别名 / 英文关键字（每行一个）", "用于注册表匹配别名。"));
            _altBox = MakeField("", isEdit ? string.Join("\n", existing.altKeywords ?? new string[0]) : "", true, 56);
            body.Children.Add(_altBox);
            body.Children.Add(new TextBlock { Height = 8 });

            // 已知 exe 路径
            body.Children.Add(Label("已知 exe 路径（每行一个）", "用于文件存在性降级检测是否已安装。"));
            _pathsBox = MakeField("", isEdit ? string.Join("\n", existing.knownExePaths ?? new string[0]) : "", true, 56);
            body.Children.Add(_pathsBox);
            body.Children.Add(new TextBlock { Height = 8 });

            // 注册表路径
            var regGrid = new Grid();
            regGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            regGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            regGrid.Margin = new Thickness(0, 0, 0, 8);
            _regKeyBox = MakeField("HKEY_LOCAL_MACHINE\\...\\Uninstall\\...", isEdit ? existing.regKey : "");
            _regKey2Box = MakeField("备用注册表路径（如 WOW6432Node）", isEdit ? existing.regKey2 : "");
            Grid.SetColumn(_regKeyBox, 0);
            Grid.SetColumn(_regKey2Box, 1);
            _regKeyBox.Margin = new Thickness(0, 0, 6, 0);
            regGrid.Children.Add(_regKeyBox);
            regGrid.Children.Add(_regKey2Box);
            body.Children.Add(Label("注册表路径 / 备用注册表路径", null));
            body.Children.Add(regGrid);
            body.Children.Add(new TextBlock { Height = 8 });

            // SHA256
            body.Children.Add(Label("期望 SHA256（可选）", "下载后校验，不匹配则拒绝安装（防篡改）。"));
            _shaBox = MakeField("十六进制，大小写不限", isEdit ? existing.sha256 : "");
            body.Children.Add(_shaBox);
            body.Children.Add(new TextBlock { Height = 8 });

            // PageUrl
            body.Children.Add(Label("官方下载页 URL（运行时解析，可选）", "填入后安装时实时抓取真实直链。"));
            _pageBox = MakeField("https://.../download", isEdit ? existing.pageUrl : "");
            body.Children.Add(_pageBox);
            body.Children.Add(new TextBlock { Height = 8 });

            // Referer
            body.Children.Add(Label("HTTP Referer（可选）", "部分厂商直链需特定 Referer 否则 403。"));
            _refererBox = MakeField("", isEdit ? existing.referer : "");
            body.Children.Add(_refererBox);
            body.Children.Add(new TextBlock { Height = 8 });

            // 安装目录开关
            body.Children.Add(Label("安装目录开关（可选）", "留空自动推断；可强制 /D= 或 /DIR=。"));
            _dirSwitchBox = MakeField("/D= 或 /DIR=", isEdit ? existing.installDirSwitch : "");
            body.Children.Add(_dirSwitchBox);
            body.Children.Add(new TextBlock { Height = 8 });

            // 便携版
            _portableChk = new CheckBox
            {
                Content = "便携版（解压即装，无需运行安装程序）",
                IsChecked = isEdit && existing.isPortable,
                FontSize = 12,
                Foreground = fg,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            body.Children.Add(_portableChk);

            // 错误提示
            _errText = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = danger,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 4, 0, 8),
                Visibility = Visibility.Collapsed
            };
            body.Children.Add(_errText);

            // 按钮行
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancelBtn = owner != null ? owner.Btn("取消", false, () => { DialogResult = false; }, 100)
                                           : new Button { Content = "取消", Width = 100 };
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            var okBtn = owner != null ? owner.Btn("保存", true, () => OnSave(), 100)
                                       : new Button { Content = "保存", Width = 100 };
            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(okBtn);
            body.Children.Add(btnRow);

            stack.Children.Add(scroller);
            root.Child = stack;
            Content = root;
        }

        private void OnSave()
        {
            var name = (_nameBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name)) { ShowError("请填写软件名称。"); return; }
            var url = (_urlBox.Text ?? "").Trim();
            var store = (_storeBox.Text ?? "").Trim();
            var page = (_pageBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(url) && string.IsNullOrEmpty(store) && string.IsNullOrEmpty(page)) { ShowError("请填写下载直链 URL、微软商店 ID 或官方下载页 URL（至少一项）。"); return; }
            // 下载直链仅允许 http/https，避免误填 file:// 或 UNC 路径被当成安装包下载执行
            if (!string.IsNullOrEmpty(url) && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            { ShowError("下载直链需以 http:// 或 https:// 开头。"); return; }
            // 官方下载页同理仅允许 http/https，防止 file:// 或 javascript: 被运行时解析器当作本地资源读取
            if (!string.IsNullOrEmpty(page) && !page.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !page.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            { ShowError("官方下载页 URL 需以 http:// 或 https:// 开头。"); return; }

            var id = (_idBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(id)) id = Slugify(name);

            var entry = new CustomSoftwareEntry
            {
                id = id,
                name = name,
                desc = (_descBox.Text ?? "").Trim(),
                url = url,
                installArgs = ParseLines(_argsBox.Text, ' '),
                risk = (_riskCombo.SelectedItem as string) ?? "low",
                storeId = store,
                chocolateyId = (_chocolateyBox.Text ?? "").Trim(),
                uninstallKeyword = (_uninstallBox.Text ?? "").Trim(),
                altKeywords = ParseLines(_altBox.Text, '\n'),
                knownExePaths = ParseLines(_pathsBox.Text, '\n'),
                regKey = (_regKeyBox.Text ?? "").Trim(),
                regKey2 = (_regKey2Box.Text ?? "").Trim(),
                sha256 = (_shaBox.Text ?? "").Trim(),
                pageUrl = page,
                // Referer 作为 HTTP 请求头发送，剥离 CR/LF 防止头注入
                referer = StripCrlf(_refererBox.Text),
                installDirSwitch = (_dirSwitchBox.Text ?? "").Trim(),
                category = (_categoryCombo.SelectedItem as string) ?? SoftwareInstall.DefaultCategory,
                isPortable = _portableChk.IsChecked == true
            };

            try
            {
                SoftwareDefPersistence.AddOrUpdate(entry);
                Entry = entry;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                ShowError("保存失败（可能无法写入 exe 同目录）：" + ex.Message);
            }
        }

        // 从下载直链 URL 文件名推断软件名称并填入（描述一并补「从下载直链自动识别」）；均可在框内手动修改。
        // 纯本地解析，不发任何网络请求，稳妥零延迟。
        private void AutoFillFromUrl()
        {
            var raw = (_urlBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(raw)) { ShowError("请先填写下载直链 URL。"); return; }
            var name = DeriveNameFromUrl(raw);
            if (string.IsNullOrEmpty(name)) { ShowError("无法从 URL 推断名称，请手动填写。"); return; }
            _nameBox.Text = name;
            if (string.IsNullOrEmpty((_descBox.Text ?? "").Trim()))
                _descBox.Text = "从下载直链自动识别";
        }

        // 离开 URL 框时：仅当名称、描述都为空才轻量自动补，避免覆盖用户已填内容。
        private void AutoFillFromUrlIfEmpty()
        {
            var raw = (_urlBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(raw)) return;
            if (!string.IsNullOrEmpty((_nameBox.Text ?? "").Trim())) return;
            var name = DeriveNameFromUrl(raw);
            if (string.IsNullOrEmpty(name)) return;
            _nameBox.Text = name;
            _descBox.Text = "从下载直链自动识别";
        }

        private static string DeriveNameFromUrl(string url)
        {
            try
            {
                var q = url.IndexOf('?');
                if (q >= 0) url = url.Substring(0, q);
                var frag = url.IndexOf('#');
                if (frag >= 0) url = url.Substring(0, frag);
                var lastSlash = url.LastIndexOf('/');
                var file = lastSlash >= 0 ? url.Substring(lastSlash + 1) : url;
                if (string.IsNullOrEmpty(file)) return "";
                var dot = file.LastIndexOf('.');
                if (dot > 0) file = file.Substring(0, dot);
                if (string.IsNullOrEmpty(file)) return "";
                var parts = file.Split(new[] { '-', '_', '.', ' ', '+', '%', '~' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "";
                var first = parts[0];
                if (string.IsNullOrEmpty(first)) return "";
                return char.ToUpperInvariant(first[0]) + first.Substring(1);
            }
            catch { return ""; }
        }

        private static string[] ParseLines(string text, char sep)
        {
            if (string.IsNullOrWhiteSpace(text)) return new string[0];
            return text.Split(new[] { sep }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .Where(s => s.Length > 0)
                       .ToArray();
        }

        private static string StripCrlf(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\r", "").Replace("\n", "").Trim();
        }

        private static string Slugify(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "custom";
            var sb = new StringBuilder();
            foreach (var c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            var r = sb.ToString();
            return string.IsNullOrEmpty(r) ? "custom" : r;
        }

        private void ShowError(string message)
        {
            DialogChrome.ShowError(_errText, message);
        }
    }

    /// <summary>
    /// 常用软件管理对话框：列出全部有效条目（内置 + 自定义），支持新增 / 编辑 / 删除。
    /// 编辑内置条目会写入 custom_software.json 形成覆盖；删除仅对自定义条目有效；删除经 Native MessageBox 二次确认。
    /// 所有变更即时写入 custom_software.json，重启后保留。
    /// </summary>
    internal class CustomSoftwareManagerDialog : Window
    {
        private readonly MainWindow _owner;
        private readonly DataGrid _dg;
        private List<CustomSoftwareEntry> _items;

        public CustomSoftwareManagerDialog(MainWindow owner)
        {
            _owner = owner;
            DialogChrome.Apply(this, owner);

            var fg = owner?._textMain ?? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
            var dim = owner?._textDim ?? new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            var panelBorder = owner?._panelBorder ?? new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x3C));
            var windowBg = owner?._windowBg ?? new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x1E));
            var secondaryBg = owner?._btnSecondaryBg ?? new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x3C));
            var secondaryFg = owner?._btnSecondaryFg ?? new SolidColorBrush(Color.FromRgb(0xD0, 0xD6, 0xDE));

            Title = "管理常用软件";
            Width = 760;
            Height = 540;
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) DialogResult = false; };

            var root = new Border
            {
                Background = windowBg,
                BorderBrush = panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 4,
                    Opacity = 0.35,
                    Color = Color.FromRgb(0x00, 0x00, 0x00)
                }
            };
            var stack = new StackPanel();

            // 标题栏
            stack.Children.Add(DialogChrome.BuildTitleBar(this, "管理常用软件", fg, dim, owner?._dangerRed ?? Brushes.Red, panelBorder));

            var body = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            body.Children.Add(new TextBlock
            {
                Text = "此处展示全部常用软件（内置 + 增补）。编辑内置条目会以同 ID 覆盖的形式保存，来源显示为「内置(覆盖)」；你增补的条目显示为「增补」；删除仅对增补/覆盖条目有效。",
                FontSize = 11.5,
                Foreground = dim,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            _dg = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                Background = Brushes.Transparent,
                Foreground = fg,
                BorderBrush = panelBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 4, 0, 0),
                MaxHeight = 360,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                RowBackground = Brushes.Transparent,
                AlternatingRowBackground = Brushes.Transparent,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = panelBorder
            };
            var headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, fg));
            headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, panelBorder));
            _dg.ColumnHeaderStyle = headerStyle;
            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(DataGridCell.BackgroundProperty, Brushes.Transparent));
            cellStyle.Setters.Add(new Setter(DataGridCell.ForegroundProperty, fg));
            cellStyle.Setters.Add(new Setter(DataGridCell.BorderBrushProperty, Brushes.Transparent));
            _dg.CellStyle = cellStyle;

            _dg.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding("id"), Width = new DataGridLength(110) });
            _dg.Columns.Add(new DataGridTextColumn { Header = "名称", Binding = new Binding("name"), Width = new DataGridLength(120) });
            _dg.Columns.Add(new DataGridTextColumn { Header = "URL", Binding = new Binding("url"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            _dg.Columns.Add(new DataGridTextColumn { Header = "描述", Binding = new Binding("desc"), Width = new DataGridLength(120) });
            _dg.Columns.Add(new DataGridTextColumn { Header = "分类", Binding = new Binding("category"), Width = new DataGridLength(64) });
            _dg.Columns.Add(new DataGridTextColumn { Header = "来源", Binding = new Binding("sourceText"), Width = new DataGridLength(60) });

            var opCol = new DataGridTemplateColumn { Header = "操作", Width = new DataGridLength(120) };
            var opFactory = new FrameworkElementFactory(typeof(StackPanel));
            opFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            var editBtn = new FrameworkElementFactory(typeof(Button));
            editBtn.SetValue(Button.ContentProperty, "编辑");
            editBtn.SetValue(Button.FontSizeProperty, 11.0);
            editBtn.SetValue(Button.MarginProperty, new Thickness(0, 0, 4, 0));
            editBtn.SetValue(Button.PaddingProperty, new Thickness(8, 3, 8, 3));
            editBtn.SetValue(Button.CursorProperty, Cursors.Hand);
            editBtn.SetValue(Button.BackgroundProperty, secondaryBg);
            editBtn.SetValue(Button.ForegroundProperty, secondaryFg);
            editBtn.SetValue(Button.BorderThicknessProperty, new Thickness(1));
            editBtn.SetValue(Button.BorderBrushProperty, panelBorder);
            editBtn.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnEditClick));
            var delBtn = new FrameworkElementFactory(typeof(Button));
            delBtn.SetValue(Button.ContentProperty, "删除");
            delBtn.SetValue(Button.FontSizeProperty, 11.0);
            delBtn.SetValue(Button.PaddingProperty, new Thickness(8, 3, 8, 3));
            delBtn.SetValue(Button.CursorProperty, Cursors.Hand);
            delBtn.SetValue(Button.BackgroundProperty, secondaryBg);
            delBtn.SetValue(Button.ForegroundProperty, secondaryFg);
            delBtn.SetValue(Button.BorderThicknessProperty, new Thickness(1));
            delBtn.SetValue(Button.BorderBrushProperty, panelBorder);
            delBtn.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnDeleteClick));
            opFactory.AppendChild(editBtn);
            opFactory.AppendChild(delBtn);
            opCol.CellTemplate = new DataTemplate { VisualTree = opFactory };
            _dg.Columns.Add(opCol);

            body.Children.Add(_dg);

            // 底部按钮行
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var addBtn = owner != null ? owner.Btn("新增", true, () => OnAdd(), 100) : new Button { Content = "新增", Width = 100 };
            addBtn.Margin = new Thickness(0, 0, 8, 0);
            var closeDlgBtn = owner != null ? owner.Btn("关闭", false, () => { DialogResult = true; }, 100) : new Button { Content = "关闭", Width = 100 };
            btnRow.Children.Add(addBtn);
            btnRow.Children.Add(closeDlgBtn);
            body.Children.Add(btnRow);

            stack.Children.Add(body);
            root.Child = stack;
            Content = root;

            BuildItems();
        }

        private void BuildItems()
        {
            var effective = SoftwareInstall.GetEffectiveList();
            var customList = SoftwareDefPersistence.Load();
            var customMap = new Dictionary<string, CustomSoftwareEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in customList)
                if (c != null && !string.IsNullOrEmpty(c.id))
                    customMap[c.id] = c;
            var rows = new List<CustomSoftwareEntry>();
            foreach (var def in effective)
            {
                if (def == null) continue;
                if (customMap.TryGetValue(def.Id, out var ce))
                {
                    ce.isCustom = true;
                    ce.isOverride = SoftwareInstall.IsBuiltInId(def.Id);
                    if (string.IsNullOrEmpty(ce.category)) ce.category = SoftwareInstall.DefaultCategory;
                    rows.Add(ce);
                }
                else
                {
                    rows.Add(CustomSoftwareEntry.FromSoftwareDef(def, false));
                }
            }
            _items = rows;
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            _dg.ItemsSource = null;
            _dg.ItemsSource = _items;
        }

        private void OnAdd()
        {
            var dlg = new CustomSoftwareEditDialog(_owner);
            dlg.Owner = _owner;
            if (dlg.ShowDialog() == true && dlg.Entry != null)
            {
                // 编辑对话框的 OnSave 已写入 custom_software.json，此处仅更新内存列表，避免重复写盘
                dlg.Entry.isCustom = true;
                dlg.Entry.isOverride = SoftwareInstall.IsBuiltInId(dlg.Entry.id);
                _items.Add(dlg.Entry);
                RefreshGrid();
                _owner?.SetStatus("已新增增补软件: " + (dlg.Entry.name ?? dlg.Entry.id));
            }
        }

        private void OnEditClick(object s, RoutedEventArgs e)
        {
            var entry = ((FrameworkElement)s).DataContext as CustomSoftwareEntry;
            if (entry == null) return;
            var dlg = new CustomSoftwareEditDialog(_owner, entry);
            dlg.Owner = _owner;
            if (dlg.ShowDialog() == true && dlg.Entry != null)
            {
                var newEntry = dlg.Entry;
                string oldId = entry.id;
                // 编辑对话框的 OnSave 已写入新条目；若 ID 变更，此处仅清除旧的残留条目（写盘可能失败需捕获）
                if (!string.Equals(oldId, newEntry.id, StringComparison.OrdinalIgnoreCase))
                {
                    try { SoftwareDefPersistence.Remove(oldId); }
                    catch (Exception ex) { _owner?.SetStatus("保存失败: " + ex.Message); return; }
                }
                newEntry.isCustom = true;
                newEntry.isOverride = SoftwareInstall.IsBuiltInId(newEntry.id);
                int idx = _items.FindIndex(x => x != null && string.Equals(x.id, oldId, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) _items[idx] = newEntry; else _items.Add(newEntry);
                RefreshGrid();
                _owner?.SetStatus("已更新常用软件: " + (newEntry.name ?? newEntry.id));
            }
        }

        private void OnDeleteClick(object s, RoutedEventArgs e)
        {
            var entry = ((FrameworkElement)s).DataContext as CustomSoftwareEntry;
            if (entry == null) return;
            if (!entry.isCustom)
            {
                System.Windows.MessageBox.Show(
                    "「" + (entry.name ?? entry.id) + "」是内置软件，不能直接删除。\n如需调整可点击「编辑」覆盖其配置。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var res = System.Windows.MessageBox.Show(
                "确定删除增补软件「" + (entry.name ?? entry.id) + "」？\n（仅删除增补条目，内置同名条目会恢复）",
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
            try { SoftwareDefPersistence.Remove(entry.id); }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("删除失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _items.RemoveAll(x => x != null && string.Equals(x.id, entry.id, StringComparison.OrdinalIgnoreCase));
            RefreshGrid();
            _owner?.SetStatus("已删除增补软件: " + (entry.name ?? entry.id));
        }
    }
}

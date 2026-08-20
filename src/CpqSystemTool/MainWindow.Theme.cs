using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.IO;

namespace CpqSystemTool
{
    /// <summary>
    /// 主题切换：深色/浅色模式、系统主题自动跟随、自定义背景图。
    /// </summary>
    public partial class MainWindow
    {
        // ---------- 自定义背景图（持久化到 Config\background.json） ----------
        private static string _bgSettingsPath => Path.Combine(AppPaths.ConfigDir, "background.json");
        private string _customBgDarkPath = "";
        private string _customBgLightPath = "";
        private double _customBgDarkOpacity = 0.55;   // 深色默认半透明
        private double _customBgLightOpacity = 1.0;   // 浅色默认不透明

        // 图片格式转换（pwsh 调用）超时：15 秒
        private const int IMAGE_CONVERT_TIMEOUT_MS = 15000;

        /// <summary>从 Config\background.json 加载自定义背景设置</summary>
        private void LoadBackgroundSettings()
        {
            try
            {
                if (!File.Exists(_bgSettingsPath)) return;
                var json = File.ReadAllText(_bgSettingsPath, System.Text.Encoding.UTF8);
                // 极简 JSON 解析（避免依赖外部库）：找 "DarkPath":"..." 和 "LightPath":"..."
                var d = ExtractJsonString(json, "DarkPath");
                var l = ExtractJsonString(json, "LightPath");
                var do_ = ExtractJsonDouble(json, "DarkOpacity");
                var lo = ExtractJsonDouble(json, "LightOpacity");
                if (d != null && File.Exists(d)) _customBgDarkPath = d;
                if (l != null && File.Exists(l)) _customBgLightPath = l;
                if (do_.HasValue) _customBgDarkOpacity = do_.Value;
                if (lo.HasValue) _customBgLightOpacity = lo.Value;
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
        }

        /// <summary>保存自定义背景设置到 Config\background.json</summary>
        private void SaveBackgroundSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_bgSettingsPath));
                var json = "{\n  \"DarkPath\": " + JsonStr(_customBgDarkPath) +
                    ",\n  \"LightPath\": " + JsonStr(_customBgLightPath) +
                    ",\n  \"DarkOpacity\": " + _customBgDarkOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ",\n  \"LightOpacity\": " + _customBgLightOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "\n}\n";
                File.WriteAllText(_bgSettingsPath, json, System.Text.Encoding.UTF8);
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
        }

        // ---------- 极简 JSON 工具（不引入额外依赖） ----------
        private static string ExtractJsonString(string json, string key)
        {
            // 找 "Key":"value" 或 "Key": "value"
            var idx = json.IndexOf("\"" + key + "\"");
            if (idx < 0) return null;
            var colon = json.IndexOf(':', idx);
            if (colon < 0) return null;
            var start = json.IndexOf('"', colon + 1);
            if (start < 0) return null;
            var end = start + 1;
            while (end < json.Length)
            {
                if (json[end] == '\\') { end += 2; continue; }
                if (json[end] == '"') break;
                end++;
            }
            if (end >= json.Length) return null;
            return json.Substring(start + 1, end - start - 1).Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static double? ExtractJsonDouble(string json, string key)
        {
            var idx = json.IndexOf("\"" + key + "\"");
            if (idx < 0) return null;
            var colon = json.IndexOf(':', idx);
            if (colon < 0) return null;
            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            int j = i;
            while (j < json.Length && (char.IsDigit(json[j]) || json[j] == '.' || json[j] == '-' || json[j] == '+' || json[j] == 'e' || json[j] == 'E')) j++;
            if (double.TryParse(json.Substring(i, j - i), out double v)) return v;
            return null;
        }

        private static string JsonStr(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        /// <summary>尝试从文件路径加载 BitmapImage（双通道：BitmapImage 原生 + System.Drawing 转码 webp）。失败返回 null。</summary>
        public static BitmapImage TryLoadImagePublic(string path) => TryLoadImageAny(path);

        /// <summary>双通道加载：
        /// ① BitmapImage 原生解码（png/jpg/bmp/gif）
        /// ② System.Drawing (GDI+/WIC) 转码——Win10 1709+ / Win11 内置 webp 解码器，转 PNG 后喂给 BitmapImage。
        /// </summary>
        private static BitmapImage TryLoadImageAny(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            // 通道 1：BitmapImage 原生（png/jpg/bmp/gif）
            var native = TryLoadImage(path);
            if (native != null) return native;
            // 通道 2：webp —— PowerShell 7 (pwsh.exe) 默认 .NET 5+ 主机，支持 webp。
            //          如果系统没装 PS7，返回 null 让 UI 弹窗提示用画图转 PNG。
            try
            {
                string pwshPath = FindPwsh7();
                if (pwshPath != null)
                {
                    string tmpPng = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(path) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png");
                    // 用 -EncodedCommand（Base64 UTF-16LE）替代 -Command 的手工引号转义：脚本内容含单引号路径与
                    // [System.Drawing.Image] 调用，直接 -Command "..." 包裹在含特殊字符时易破坏引号配对导致静默失败。
                    string psScript = "$img = [System.Drawing.Image]::FromFile('" + path.Replace("'", "''") + "'); $img.Save('" + tmpPng.Replace("'", "''") + "', [System.Drawing.Imaging.ImageFormat]::Png)";
                    string psEncoded = System.Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(psScript));
                    var psi = new System.Diagnostics.ProcessStartInfo(pwshPath,
                        "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + psEncoded)
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardError = true
                    };
                    using (var p = System.Diagnostics.Process.Start(psi))
                    {
                        if (!p.WaitForExit(IMAGE_CONVERT_TIMEOUT_MS)) { try { p.Kill(); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  } }
                        else if (p.ExitCode == 0 && File.Exists(tmpPng))
                        {
                            var bi = new BitmapImage();
                            bi.BeginInit();
                            bi.UriSource = new Uri(tmpPng, UriKind.Absolute);
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.EndInit();
                            bi.Freeze();
                            try { File.Delete(tmpPng); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
                            return bi;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // pwsh7 通道失败，静默忽略（已回退到其它加载通道）
            }
            return null;
        }

        /// <summary>查找 PowerShell 7+ 可执行文件（pwsh.exe）。Win11 默认装 PS5，PS7 是 Microsoft Store 可选装的。</summary>
        private static string FindPwsh7()
        {
            // 常见路径
            string[] candidates = {
                @"C:\Program Files\PowerShell\7\pwsh.exe",
                Environment.GetEnvironmentVariable("ProgramFiles") + @"\PowerShell\7\pwsh.exe",
                @"pwsh.exe"
            };
            foreach (var c in candidates)
            {
                try
                {
                    if (File.Exists(c)) return c;
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            }
            return null;
        }

        // ===================== WebP 解码器检测 + 自动安装 =====================

        /// <summary>检测系统是否已安装 WebP 解码器（Microsoft Store 的 WebP Image Extensions）。
        /// ★ Get-AppxPackage 没有 -PackageFamilyName 参数，必须用 -AllUsers | Where-Object Name 匹配。
        /// 注意：-AllUsers 需要管理员权限（我们的 exe 是 UAC admin，OK）。</summary>
        public static bool IsWebpCodecAvailable()
        {
            try
            {
                var s = Exec.RunPowerShellGet(
                    "@(Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue | Where-Object { `$_.Name -eq 'Microsoft.WebpImageExtension' }).Count", null);
                var t = s.Trim();
                return !string.IsNullOrEmpty(t) && t != "0";
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  return false; }
        }

        /// <summary>自动安装 WebP Image Extensions：调用 AppxManager.Install 三级通道（winget → rg-adguard → Store 页面），
        /// 装完后轮询解码器注册。返回是否安装成功。</summary>
        public static bool InstallWebpExtension(Action<string> log)
        {
            log("   自动安装 WebP Image Extensions（winget → rg-adguard → Store 三级通道）...");
            try
            {
                // AppxManager.Install 内部：① winget 静默装 ② rg-adguard 下载安装 ③ Store 页面兜底
                AppxManager.Install("9PG2DK419DRG", log);
                // 轮询 60 秒等解码器注册（winget/rg-adguard 成功则 2 秒内检测到；Store 手动则需用户点「获取」）
                for (int i = 0; i < 30; i++)
                {
                    System.Threading.Thread.Sleep(2000);
                    if (IsWebpCodecAvailable())
                    {
                        log("   [OK] WebP 解码器已安装");
                        return true;
                    }
                }
                log("   等待超时（60 秒）——未检测到 WebP 扩展安装。可稍后在「应用商店」页重试。");
            }
            catch (Exception ex) { log("   [!!] 自动安装异常: " + ex.Message); }
            return false;
        }

        private static BitmapImage TryLoadImage(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = new Uri(path, UriKind.Absolute);
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[TryLoadImage] " + path + " failed: " + ex.Message); return null; }
        }
        // ---------- Theme toggle & color management ----------
        private void SetDarkColors()
        {
            _accent = new SolidColorBrush(Color.FromRgb(0x16, 0xE0, 0xBD));
            _textMain = new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3));
            _textDim = new SolidColorBrush(Color.FromRgb(0x8B, 0x98, 0xA5));
            _panelBorder = new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x3C));
            _bgCard = Brushes.Transparent;
            _successGreen = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
            _dangerRed = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
            _dangerDark = new SolidColorBrush(Color.FromRgb(0x7A, 0x12, 0x12));   // 深色模式危急描边（与历史硬编码暗红一致）
            _warnOrange = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12));
            _bgDeep = Brushes.Transparent;
            _bgTable = Brushes.Transparent;
            _bgTableHead = Brushes.Transparent;
            _rowSelected = new SolidColorBrush(Color.FromRgb(0x16, 0x36, 0x44));
            // 深色模式 hover：与标准按钮悬浮色 ButtonHoverBrush 完全一致（#16E0BD @ 22%），
            // 让所有列表行、导航项、卡片的鼠标跟随背景填充色与按钮统一。
            _rowHover = new SolidColorBrush(Color.FromArgb(0x38, 0x16, 0xE0, 0xBD));
            _installedBg = new SolidColorBrush(Color.FromRgb(0x1C, 0x33, 0x28));
            _installedBorder = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
            _installedFg = new SolidColorBrush(Color.FromRgb(0xA8, 0xE6, 0xC1));
            _notInstalledBg = new SolidColorBrush(Color.FromRgb(0x33, 0x1C, 0x1C));
            _notInstalledBorder = new SolidColorBrush(Color.FromRgb(0x6B, 0x3B, 0x3B));
            // 统一派生笔刷（深色）
            _btnPrimaryFg = new SolidColorBrush(Color.FromRgb(0x04, 0x20, 0x1B));
            // secondary 按钮背景：从 #1C232C 提高到 #2D3748，与深色底图拉开对比，避免看起来像透明
            _btnSecondaryBg = new SolidColorBrush(Color.FromRgb(0x2D, 0x37, 0x48));
            _btnSecondaryFg = new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3));
            _windowBg = new SolidColorBrush(Color.FromRgb(0x0E, 0x11, 0x16));
            _inputBg = new SolidColorBrush(Color.FromRgb(0x0B, 0x0E, 0x12));
            _inputFg = new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3));
        }

        private void SetLightColors()
        {
            _accent = new SolidColorBrush(Color.FromRgb(0x08, 0x91, 0x82));
            _textMain = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
            // 浅色模式下的灰色字：原来 0x6B7280(RGB 107,114,128) 对比度约 5:1 偏浅看不清
            // 改为 0x4B5560(RGB 75,85,96) 对比度约 8.5:1 显著提升，仍不到纯黑（深色模式也能用）
            _textDim = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x60));
            _panelBorder = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB));
            _bgCard = Brushes.Transparent;
            _successGreen = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
            _dangerRed = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            _dangerDark = new SolidColorBrush(Color.FromRgb(0x9B, 0x1C, 0x1C));   // 浅色模式危急描边（深于浅色 _dangerRed）
            _warnOrange = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
            _bgDeep = Brushes.Transparent;
            _bgTable = Brushes.Transparent;
            _bgTableHead = Brushes.Transparent;
            _rowSelected = new SolidColorBrush(Color.FromRgb(0xB3, 0xD8, 0xF0));
            // 浅色 hover：与标准按钮悬浮色 ButtonHoverBrush 完全一致（#089182 @ 35%），
            // 让所有列表行、导航项、卡片的鼠标跟随背景填充色与按钮统一。
            _rowHover = new SolidColorBrush(Color.FromArgb(0x59, 0x08, 0x91, 0x82));
            _installedBg = new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE5));
            _installedBorder = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
            _installedFg = new SolidColorBrush(Color.FromRgb(0x0F, 0x4F, 0x2A));
            _notInstalledBg = new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
            _notInstalledBorder = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            // 统一派生笔刷（浅色）
            _btnPrimaryFg = new SolidColorBrush(Colors.White);
            // secondary 按钮背景：从 #E5E7EB 加深到 #D1D5DB，在浅色底图上更明显
            _btnSecondaryBg = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB));
            _btnSecondaryFg = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
            _windowBg = new SolidColorBrush(Color.FromRgb(0xF5, 0xF7, 0xFA));
            _inputBg = new SolidColorBrush(Colors.White);
            _inputFg = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
        }

        /// <summary>
        /// 把主题感知的外壳色（Window/Sidebar/TopBar/ContentArea/StatusBar/标题/状态文字）
        /// 统一应用到所有外壳元素。深色与浅色共用同一套逻辑，避免某侧硬编码导致明暗错位。
        /// 必须在 InitializeComponent() 之后调用（依赖 XAML 声明的命名元素）。
        /// </summary>
        private void ApplyShellColors()
        {
            if (_isDarkMode)
            {
                try
                {
                    // 优先使用自定义深色背景
                    var img = TryLoadImage(_customBgDarkPath);
                    if (img == null)
                    {
                        img = new BitmapImage(
                            new Uri("pack://application:,,,/系统清理与优化工具;component/background.png", UriKind.Absolute));
                        img.Freeze();
                    }
                    Background = _windowBg;
                    BgImage.Source = img;
                    BgImage.Opacity = _customBgDarkOpacity;
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  BgImage.Source = null; }
            }
            else
            {
                // 浅色：优先自定义，否则内置
                try
                {
                    var img = TryLoadImage(_customBgLightPath);
                    if (img == null)
                    {
                        img = new BitmapImage(
                            new Uri("pack://application:,,,/系统清理与优化工具;component/background-light.png", UriKind.Absolute));
                        img.Freeze();
                    }
                    Background = _windowBg;
                    BgImage.Source = img;
                    BgImage.Opacity = _customBgLightOpacity;
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); 
                    Background = _windowBg;
                    BgImage.Source = null;
                }
            }

            // 全部设为 Transparent
            MainGrid.Background = Brushes.Transparent;
            Sidebar.Background = Brushes.Transparent;
            Sidebar.BorderBrush = _panelBorder;
            TopBar.Background = Brushes.Transparent;
            TopBar.BorderBrush = _panelBorder;
            var contentParentGrid = ContentArea.Parent as Grid;
            if (contentParentGrid != null) contentParentGrid.Background = Brushes.Transparent;
            ContentArea.Background = Brushes.Transparent;
            StatusBar.Background = Brushes.Transparent;
            StatusBar.BorderBrush = _panelBorder;
            PageTitle.Foreground = _textMain;
            StatusText.Foreground = _textMain;

            // 统一滚动条滑块颜色（与当前主题一致，深/浅不同灰阶）
            // 注意：XAML 定义的 SolidColorBrush 是 frozen 的，不能改 .Color，必须替换整个对象
            Resources["ScrollThumbBrush"] = new SolidColorBrush(
                _isDarkMode ? Color.FromRgb(0x3C, 0x46, 0x54) : Color.FromRgb(0xB8, 0xC0, 0xCC));
            // 统一悬浮边框高亮色（XAML Button ControlTemplate IsMouseOver 触发器引用）
            Resources["AccentBrush"] = new SolidColorBrush(_accent.Color);
            // 按钮悬浮填充：深色模式用 accent 半透明（~35%），浅色模式用 accent（~22%）
            // 效果：non-primary 按钮显示明显悬浮层；primary 按钮 accent 底→微亮叠加
            // 按钮悬浮填充：深色模式用更亮的 #16E0BD 叠加（微亮），浅色模式用更暗的 #089182 叠加（微暗）
            // 修正：原写法深浅分支反了，导致深色按钮悬浮反而变暗、浅色按钮悬浮反而变亮
            Resources["ButtonHoverBrush"] = new SolidColorBrush(
                _isDarkMode ? Color.FromArgb(0x38, 0x16, 0xE0, 0xBD)   // #16E0BD @ 22% (深色：亮叠加)
                              : Color.FromArgb(0x59, 0x08, 0x91, 0x82));  // #089182 @ 35% (浅色：暗叠加)

        }

        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            _isDarkMode = !_isDarkMode;
            _userOverrodeTheme = true; // 标记用户手动切换，不再自动跟随系统
            ApplyTheme(_isDarkMode);
            // 同步侧边栏标题颜色（标题 TextBlock 在 BuildSidebar 中创建时捕获了旧笔刷）
            UpdateSidebarTitleColors();
            // 驱动清理页已缓存，主题变更后清空缓存以用新主题色重建
            InvalidateDriverStoreCache();
            // 用保存的 key 重建当前页（_activeNavKey 在 Navigate 中更新）
            Navigate(_activeNavKey);
        }

        private void ApplyTheme(bool dark)
        {
            if (dark)
            {
                SetDarkColors();
                ThemeToggleBtn.Content = "🌙";
                ThemeToggleBtn.Foreground = _textDim;
                ThemeToggleBtn.ToolTip = "切换到浅色模式";
            }
            else
            {
                SetLightColors();
                ThemeToggleBtn.Content = "☀️";
                ThemeToggleBtn.Foreground = _textDim;
                ThemeToggleBtn.ToolTip = "切换到深色模式";
            }
            // 外壳色（窗口/侧边栏/顶栏/底栏/标题/状态文字）统一按当前主题应用
            ApplyShellColors();
        }

        // ---- 系统主题自动跟随 ----

        /// <summary>
        /// 检测 Windows 当前是否使用浅色主题（读注册表 AppsUseLightTheme）。
        /// 0 = 深色, 1 = 浅色, 不存在/其他 = 默认深色
        /// </summary>
        private static bool DetectSystemLightTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("AppsUseLightTheme");
                        if (val is int i) return i == 1;
                    }
                }
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            return false; // 默认深色
        }

        /// <summary>
        /// 监听系统主题变化（用户在 Windows 设置里切换时触发）。
        /// 如果用户没有手动覆盖过（_userOverrodeTheme == false），自动跟随切换。
        /// </summary>
        private void HookSystemThemeChange()
        {
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (_userOverrodeTheme) return; // 用户手动选过，不跟随
                try { Dispatcher.BeginInvoke(new Action(() =>
                {
                    bool systemLight = DetectSystemLightTheme();
                    bool newDark = !systemLight;
                    if (newDark != _isDarkMode)
                    {
                        _isDarkMode = newDark;
                        ApplyTheme(_isDarkMode);
                        UpdateSidebarTitleColors();
                        InvalidateDriverStoreCache(); // 主题变更后重建驱动清理页
                        Navigate(_activeNavKey);
                    }
                })); } catch { /* 窗口已关闭，忽略 */ }
            };
        }

        private void UpdateSidebarTitleColors()
        {
            // 侧边栏标题实际嵌套在 Sidebar -> DockPanel -> StackPanel -> TextBlock 里，
            // 直接遍历 DockPanel.Children 找不到，需递归查找命名元素。
            if (Sidebar.Child == null) return;
            foreach (var tb in FindNamedTextBlocks(Sidebar.Child))
            {
                if (tb.Name == "SidebarTitle") tb.Foreground = _textMain;
                else if (tb.Name == "SidebarSubtitle") tb.Foreground = _accent;
                else if (tb.Name == "FooterVersionLabel") tb.Foreground = _textMain;
            }
        }

        private IEnumerable<TextBlock> FindNamedTextBlocks(DependencyObject root)
        {
            if (root == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TextBlock tb)
                {
                    yield return tb;
                }
                foreach (var nested in FindNamedTextBlocks(child))
                {
                    yield return nested;
                }
            }
        }
    }
}

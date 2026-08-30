using System;
using System.Collections.Generic;
using System.Linq;
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
        // ---------- 自定义背景（持久化到 Config\background.json） ----------
        private static string _bgSettingsPath => Path.Combine(AppPaths.ConfigDir, "background.json");

        // 图片格式转换（pwsh 调用）超时：15 秒
        private const int IMAGE_CONVERT_TIMEOUT_MS = 15000;

        /// <summary>从 Config\background.json 加载背景设置到 _backgroundSettings。</summary>
        private void LoadBackgroundSettings()
        {
            try
            {
                if (!File.Exists(_bgSettingsPath)) return;
                var json = File.ReadAllText(_bgSettingsPath, System.Text.Encoding.UTF8);
                _backgroundSettings = BackgroundSettings.FromJson(json);
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  }
            if (_backgroundSettings == null) _backgroundSettings = new BackgroundSettings();
        }

        /// <summary>保存 _backgroundSettings 到 Config\background.json。</summary>
        public void SaveBackgroundSettings()
        {
            try
            {
                if (_backgroundSettings == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(_bgSettingsPath));
                var json = _backgroundSettings.ToJson();
                WriteFileAtomic(_bgSettingsPath, json);   // tmp + 原子替换，避免崩溃留下半截 JSON
            }
            catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  ShowConfigDirWarningOnce(); }
        }

        /// <summary>应用弹窗返回的设置并刷新窗口背景。</summary>
        public void ApplyBackgroundSettings(BackgroundSettings settings)
        {
            if (settings == null) return;
            _backgroundSettings = settings.Clone();
            ApplyShellColors();
        }

        // ===== 原子写入：同目录 tmp + MoveFileEx 原子替换（不跨文件耦合，本类私有） =====
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, uint dwFlags);

        private const uint MOVEFILE_REPLACE_EXISTING = 0x1;
        private const uint MOVEFILE_WRITE_THROUGH = 0x8;

        /// <summary>原子写文件：先写同目录 .tmp（同卷保证 rename 原子），再 MoveFileEx(REPLACE_EXISTING|WRITE_THROUGH) 覆盖替换；
        /// 目标被占用时回退「删除目标 + File.Move」；删除失败则放弃并保留 tmp（下次覆盖）不抛异常；finally 清理 tmp。</summary>
        private static void WriteFileAtomic(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) dir = ".";
            string tmp = Path.Combine(dir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            bool keepTmp = false;
            try
            {
                File.WriteAllText(tmp, content, System.Text.Encoding.UTF8);
                if (!MoveFileEx(tmp, path, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
                {
                    try
                    {
                        if (File.Exists(path)) File.Delete(path);
                    }
                    catch { keepTmp = true; return; } // 删除失败：放弃并保留 tmp（下次覆盖），不抛异常打断调用方
                    try { File.Move(tmp, path); }
                    catch { /* 改名失败：由 finally 清理 tmp */ }
                }
            }
            finally
            {
                if (!keepTmp)
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                }
            }
        }

        // ===== Config 目录故障首告警（P2：避免配置静默丢失无感知） =====
        private static int _configDirWarningShown;

        /// <summary>首次发现 Config 目录不可用（不可写/创建失败）时弹一次警告。
        /// 线程安全：首次竞态只弹一次；后台线程经 Dispatcher 回 UI 线程（避免跨线程弹窗）。</summary>
        private static void ShowConfigDirWarningOnce()
        {
            if (System.Threading.Interlocked.Exchange(ref _configDirWarningShown, 1) != 0) return;
            string msg = "配置目录不可用，设置可能无法保存：\n" + AppPaths.ConfigDir + "\n\n请检查磁盘权限或磁盘是否已满。";
            Action show = () =>
            {
                try { MessageBox.Show(msg, "配置保存失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
                catch { /* 窗口/消息循环已关闭，忽略 */ }
            };
            try
            {
                var app = System.Windows.Application.Current;
                if (app != null && !app.Dispatcher.CheckAccess()) { app.Dispatcher.BeginInvoke(show); }
                else { show(); }
            }
            catch { show(); }
        }

        // 极简 JSON 解析已迁移至 BackgroundSettings.cs，此处不再重复实现。

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
            // 背景层：优先按用户自定义模式渲染
            var mode = _backgroundSettings?.Mode ?? BackgroundMode.Image;
            Background = _windowBg;
            if (mode == BackgroundMode.Image)
            {
                BgGradient.Fill = Brushes.Transparent;
                if (_isDarkMode)
                {
                    try
                    {
                        var img = TryLoadImage(_backgroundSettings.DarkPath);
                        if (img == null)
                        {
                            img = new BitmapImage(
                                new Uri("pack://application:,,,/系统清理与优化工具;component/background.png", UriKind.Absolute));
                            img.Freeze();
                        }
                        BgImage.Source = img;
                        BgImage.Opacity = _backgroundSettings.DarkOpacity;
                    }
                    catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  BgImage.Source = null; }
                }
                else
                {
                    try
                    {
                        var img = TryLoadImage(_backgroundSettings.LightPath);
                        if (img == null)
                        {
                            img = new BitmapImage(
                                new Uri("pack://application:,,,/系统清理与优化工具;component/background-light.png", UriKind.Absolute));
                            img.Freeze();
                        }
                        BgImage.Source = img;
                        BgImage.Opacity = _backgroundSettings.LightOpacity;
                    }
                    catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  BgImage.Source = null; }
                }
            }
            else
            {
                // 纯色/渐变/网格模式：隐藏背景图，把生成的 Brush 赋给 BgGradient
                BgImage.Source = null;
                try
                {
                    BgGradient.Fill = BuildBackgroundBrush(mode);
                    BgImage.Opacity = 1.0;
                }
                catch (Exception caughtEx) { DebugLog.Ignore(caughtEx);  BgGradient.Fill = Brushes.Transparent; }
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
            // 按钮悬浮填充：深色模式用更亮的 #16E0BD 叠加（微亮），浅色模式用更暗的 #089182 叠加（微暗）
            Resources["ButtonHoverBrush"] = new SolidColorBrush(
                _isDarkMode ? Color.FromArgb(0x38, 0x16, 0xE0, 0xBD)   // #16E0BD @ 22% (深色：亮叠加)
                              : Color.FromArgb(0x59, 0x08, 0x91, 0x82));  // #089182 @ 35% (浅色：暗叠加)

        }

        /// <summary>根据设置构建背景 Brush（纯色/线性/径向/网格）。
        /// 运行时与弹窗预览共用同一套逻辑，避免两套实现各自漂移。</summary>
        private Brush BuildBrushFrom(BackgroundSettings s)
        {
            var mode = s?.Mode ?? BackgroundMode.Image;
            if (mode == BackgroundMode.Solid)
            {
                return new SolidColorBrush(BackgroundSettings.ParseColor(s.SolidColor));
            }

            if (mode == BackgroundMode.LinearGradient)
            {
                var brush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0),
                    MappingMode = BrushMappingMode.RelativeToBoundingBox,
                    SpreadMethod = GradientSpreadMethod.Pad
                };
                // WPF 默认 LinearGradientBrush 方向是 0°（左→右）；用 RotateTransform 旋转角度
                brush.Transform = new RotateTransform(s.GradientAngle, 0.5, 0.5);
                foreach (var st in s.Stops.OrderBy(x => x.Offset))
                    brush.GradientStops.Add(new GradientStop(BackgroundSettings.ParseColor(st.Color), st.Offset));
                if (brush.GradientStops.Count == 0)
                {
                    brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
                    brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
                }
                return brush;
            }

            if (mode == BackgroundMode.RadialGradient)
            {
                var brush = new RadialGradientBrush
                {
                    GradientOrigin = new Point(s.RadialCenterX, s.RadialCenterY),
                    Center = new Point(s.RadialCenterX, s.RadialCenterY),
                    RadiusX = s.RadialRadiusX,
                    RadiusY = s.RadialRadiusY,
                    MappingMode = BrushMappingMode.RelativeToBoundingBox,
                    SpreadMethod = GradientSpreadMethod.Pad
                };
                foreach (var st in s.Stops.OrderBy(x => x.Offset))
                    brush.GradientStops.Add(new GradientStop(BackgroundSettings.ParseColor(st.Color), st.Offset));
                if (brush.GradientStops.Count == 0)
                {
                    brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
                    brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
                }
                return brush;
            }

            if (mode == BackgroundMode.MeshGradient)
            {
                // 用 DrawingBrush 叠加多层径向渐变来模拟 mesh gradient（类似 gradients.app）
                var drawing = new DrawingGroup();
                // 底层用窗口底色打底，保证文字可读
                drawing.Children.Add(new GeometryDrawing(
                    _windowBg, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
                foreach (var b in s.Blobs)
                {
                    var blobBrush = new RadialGradientBrush
                    {
                        GradientOrigin = new Point(0.5, 0.5),
                        Center = new Point(0.5, 0.5),
                        RadiusX = 0.5,
                        RadiusY = 0.5,
                        MappingMode = BrushMappingMode.RelativeToBoundingBox,
                        SpreadMethod = GradientSpreadMethod.Pad
                    };
                    var c = BackgroundSettings.ParseColor(b.Color);
                    // 透明度过大会让底层窗口色透不出，这里把 0..1 的 Opacity 收敛到 0..255 并钳制，避免 >1 时 (byte) 回绕
                    byte alpha = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(255 * b.Opacity)));
                    blobBrush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, c.R, c.G, c.B), 0));
                    blobBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
                    var geo = new EllipseGeometry(new Point(b.CenterX, b.CenterY), b.Radius, b.Radius);
                    drawing.Children.Add(new GeometryDrawing(blobBrush, null, geo));
                }
                return new DrawingBrush(drawing)
                {
                    Stretch = Stretch.Fill,
                    Viewbox = new Rect(0, 0, 1, 1),
                    ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                    TileMode = TileMode.None
                };
            }

            return Brushes.Transparent;
        }

        /// <summary>运行时应用：基于当前 _backgroundSettings 构建背景 Brush。</summary>
        private Brush BuildBackgroundBrush(BackgroundMode mode)
        {
            return BuildBrushFrom(_backgroundSettings);
        }

        /// <summary>为弹窗预览生成背景 Brush（不依赖 _backgroundSettings，支持任意设置实例）。</summary>
        internal Brush BuildBackgroundBrushPreview(BackgroundSettings settings)
        {
            return BuildBrushFrom(settings);
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace CpqSystemTool
{
    public partial class MainWindow
    {
        // =====================================================================
        //  Module: 配置管理（显示默认路径 + 可修改）
        // =====================================================================

        // 配置管理页缓存（与常用软件/Appx 同款模式）：首次构建后缓存整页外壳，
        // 二次进页复用并仅刷新动态状态（清空日志/复位进度条/刷新默认路径/透明度滑块/背景缩略图 + 重触发 AutoLoad 配置列表加载）；
        // 导出/导入/自动保存/背景变更等操作完成回调中置空缓存 → 下次进页重建，保证列表与状态最新。
        private readonly PageCache<UIElement> _configCache = new PageCache<UIElement>();

        /// <summary>
        /// 统一失效配置页缓存。本页是失效点最多的一页（导出/导入/自动保存/背景变更等 9 处回调），
        /// 保留具名入口让调用点读起来是"失效配置页缓存"而不是某个字段操作。
        /// 历史 bug（收拢为 PageCache<UIElement> 后已从结构上不可能复发）：
        /// 此前各处置空只清 _cachedConfigPage，漏清 _configRefresh / _configCacheKey，
        /// 页面虽会重建，但旧的刷新委托与缓存键仍残留，可能把已丢弃页面的刷新逻辑用到新页面上。
        /// 现在「失效」只有 Invalidate() 一个入口，页面、内容键、刷新委托必然一并清空。
        /// </summary>
        private void InvalidateConfigCache()
        {
            _configCache.Invalidate();
        }

        private UIElement BuildConfig()
        {
            // 记录本次构建时主题：缓存仅在主题一致时命中（主题切换会重建当前页，避免复用旧主题刷子的页面）
            bool buildDark = _isDarkMode;
            // 缓存命中且主题一致 → 复用已构建页面，仅刷新动态状态
            var cached = _configCache.TryGet(buildDark);
            if (cached != null) return cached;

            // Grid 布局：内容卡撑满视口，最大化时日志贴底、背景图放大、无死区
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // Header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // 内容卡（Star：撑满视口剩余空间）
            int rootRow = 0;

            var headerTb = Header("配置管理", "导出 / 导入当前勾选与开关状态（JSON），支持自动保存与默认路径设置。");
            Grid.SetRow(headerTb, rootRow++);
            root.Children.Add(headerTb);

            var card = Card();
            // ★ 核心修复：Star 给 bgCard（背景图卡片）而非日志
            // 原因：_bgCard=Transparent，若日志行=Star则膨胀区域透明→透出六边形背景→看起来像"空白"
            // 新方案：bgCard=Star（撑大预览区，内容靠顶）+ 日志固定高度紧凑贴底
            var inner = new Grid { ClipToBounds = true };  // ★ 裁剪溢出：防止 Star 行缩小时残留渲染缓存
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // [0] 路径卡片
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // [1] 操作按钮栏
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // [2] ★Star 背景图卡片（吸收多余空间）
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // [3] 进度条
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });          // [4] 日志固定60px（紧凑贴底）
            int r = 0;

            var log = MakeLogBox();
            // 日志容器：固定60px高 + 透明背景（与整体卡片风格一致，透出六边形窗口背景）
            // 固定高度保证不会因 Star 膨胀产生透明空白区
            var logClip = new Border
            {
                Child = log,
                ClipToBounds = true,
                Background = Brushes.Transparent,  // 透明：与 Card() 的 _bgCard 一致，保持设计统一
                CornerRadius = new CornerRadius(6),
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0),         // 与 Grid [4] 行高严格同步，避免顶部 Margin 导致底部被 ClipToBounds 裁掉圆角
                Height = 60  // 与 Grid [4] 行高同步，固定不膨胀
            };
            // log 本身不再设 Height/MaxHeight/MinHeight（由容器控制）
            log.ClearValue(HeightProperty);
            log.ClearValue(MaxHeightProperty);
            log.ClearValue(MinHeightProperty);
            log.BorderThickness = new Thickness(0);  // 边框改到外层容器
            log.Background = Brushes.Transparent;    // 背景改到外层容器
            var pb = MakeProgress();

            // 默认路径提示区
            var pathCard = new Border
            {
                Background = _isDarkMode ? Brushes.Transparent : _bgDeep,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var pathSp = new StackPanel();
            pathSp.Children.Add(new TextBlock { Text = "📁 配置默认保存路径", FontWeight = FontWeights.SemiBold, Foreground = _accent, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });
            // 路径输入 + 浏览按钮 同一行
            var pathRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // 可编辑路径输入框
            var pathInput = new TextBox
            {
                Text = ConfigBackup.ConfigDir,
                FontSize = 12.5,
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                Padding = new Thickness(8, 6, 8, 6),
                Background = _isDarkMode ? Brushes.Transparent : _bgDeep,
                Foreground = _textMain,
                BorderBrush = _accent,
                CaretBrush = _accent
            };
            Grid.SetColumn(pathInput, 0);
            pathRow.Children.Add(pathInput);
            // 浏览按钮
            var browseBtn = Btn("📂 浏览…", false, () =>
            {
                try
                {
                    using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
                    {
                        fbd.Description = "选择配置默认保存文件夹";
                        fbd.SelectedPath = ConfigBackup.ConfigDir;
                        if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            pathInput.Text = fbd.SelectedPath;
                            log.AppendText("[OK] 已选择新路径: " + fbd.SelectedPath + "\r\n");
                        }
                    }
                }
                catch (Exception ex) { log.AppendText("[!] 浏览失败: " + ex.Message + "\r\n"); }
            }, 90);
            browseBtn.Margin = new Thickness(6, 0, 0, 0);
            browseBtn.Padding = new Thickness(10, 5, 10, 5);
            browseBtn.FontSize = 11;
            Grid.SetColumn(browseBtn, 1);
            pathRow.Children.Add(browseBtn);
            // 应用路径按钮紧跟浏览后面
            var applyPathBtn = Btn("✅ 应用路径", true, () =>
            {
                string newPath = pathInput.Text.Trim();
                if (string.IsNullOrEmpty(newPath)) { log.AppendText("[!] 路径不能为空\r\n"); return; }
                try
                {
                    Directory.CreateDirectory(newPath);
                    ConfigBackup.ConfigDir = newPath;
                    log.AppendText("[OK] 配置路径已更改为: " + newPath + "\r\n");
                    SetPageContent(BuildConfig());
                }
                catch (Exception ex) { log.AppendText("[!] 无效路径: " + ex.Message + "\r\n"); }
            }, 90);
            applyPathBtn.Margin = new Thickness(6, 0, 0, 0);
            applyPathBtn.Padding = new Thickness(10, 5, 10, 5);
            applyPathBtn.FontSize = 11;
            Grid.SetColumn(applyPathBtn, 2);
            pathRow.Children.Add(applyPathBtn);
            pathSp.Children.Add(pathRow);
            pathSp.Children.Add(new TextBlock { Text = "提示：自动保存功能会将配置保存到上述路径下的 autosave.json 文件。可直接编辑路径，或点「📂 浏览…」选择。修改后点击「应用路径」生效。", Foreground = _textDim, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
            pathCard.Child = pathSp;
            Grid.SetRow(pathCard, r++);
            inner.Children.Add(pathCard);

            // ========== 导出/导入操作栏（上移到背景图前面，更易操作） ==========
            var wp = MakeBtnRow(
                Btn("📥 导出配置...", true, () =>
                {
                    var defaultName = $"系统清理与优化配置_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                    var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = defaultName, InitialDirectory = ConfigBackup.ConfigDir };
                    if (dlg.ShowDialog() == true)
                    {
                        var cfg = CollectConfig();
                        ConfigBackup.Save(dlg.FileName, cfg, s => log.AppendText(s + "\r\n"));
                        InvalidateConfigCache(); // 已存配置列表变化 → 失效缓存，下次进页重建
                    }
                }),
                Btn("📤 导入配置...", false, () =>
                {
                    var dlg = new OpenFileDialog { Filter = "JSON|*.json", InitialDirectory = ConfigBackup.ConfigDir };
                    if (dlg.ShowDialog() == true)
                    {
                        var cfg = ConfigBackup.Load(dlg.FileName, s => log.AppendText(s + "\r\n"));
                        ApplyConfig(cfg, log);
                        InvalidateConfigCache(); // 已应用配置，优化项状态变化 → 失效缓存
                    }
                }),
                Btn("💾 自动保存当前配置", false, () =>
                {
                    if (!AppPaths.EnsureConfigDir()) ShowConfigDirWarningOnce();
                    var cfg = CollectConfig();
                    ConfigBackup.AutoSave(cfg, s => log.AppendText(s + "\r\n"));
                    log.AppendText("[OK] 已保存到: " + Path.Combine(ConfigBackup.ConfigDir, "autosave.json") + "\r\n");
                    InvalidateConfigCache(); // 已写备份文件，配置目录内容变化 → 失效缓存
                }),
                Btn("📋 列出已存配置", false, () =>
                {
                    var configs = ConfigBackup.ListConfigs();
                    log.AppendText("默认配置目录: " + ConfigBackup.ConfigDir + "\r\n");
                    log.AppendText("已存配置:\r\n" + (configs.Count > 0 ? string.Join("\r\n", configs) : "(无)") + "\r\n");
                }),
                Btn("📦 导出源码", false, () =>
                {
                    try
                    {
                        var dlg = new System.Windows.Forms.FolderBrowserDialog();
                        dlg.Description = "选择保存源码的目录";
                        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            string target = dlg.SelectedPath;
                            string extractDir = Path.Combine(target, "系统清理与优化工具_源码");

                            // ★ 修复（数据丢失）：导出前先校验目标目录安全，拒绝盘符根目录 / 系统目录 /
                            // 用户配置文件根目录等危险位置。旧代码对用户选中的目录下同名文件夹直接
                            // Directory.Delete(extractDir, true) 递归删除，无任何提示，会静默清掉用户已有文件。
                            string denyReason;
                            if (!IsSafeExtractDir(extractDir, out denyReason))
                            {
                                log.AppendText("[!] 已取消导出（目标目录不安全）：" + denyReason + " → " + extractDir + "\r\n");
                                System.Windows.MessageBox.Show(this,
                                    "拒绝导出到该目录：\n" + extractDir + "\n\n原因：" + denyReason + "\n\n请另选一个普通文件夹后重试。",
                                    "导出已取消", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                                return;
                            }

                            var asm = System.Reflection.Assembly.GetExecutingAssembly();
                            string resName = "CpqSystemTool.src.zip";
                            using (var stream = asm.GetManifestResourceStream(resName))
                            {
                                if (stream == null) { log.AppendText("[!] 未找到嵌入的源码包 src.zip\r\n"); return; }
                                string zipPath = Path.Combine(target, "src.zip");
                                using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                                {
                                    stream.CopyTo(fs);
                                }
                                // 删除前必须经用户确认：这是用户主动触发的导出操作，确认一次不打断任何自动化流程，
                                // 但能避免同名目录（可能是用户自己的源码目录）被无提示递归删除。
                                if (Directory.Exists(extractDir))
                                {
                                    var confirm = System.Windows.MessageBox.Show(this,
                                        "该目录下已存在同名文件夹：\n" + extractDir + "\n\n继续导出会先删除它，原有内容不可恢复。是否继续？",
                                        "确认覆盖导出", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                                    if (confirm != System.Windows.MessageBoxResult.Yes)
                                    {
                                        log.AppendText("[*] 用户取消导出（未删除已存在目录）：" + extractDir + "\r\n");
                                        try { File.Delete(zipPath); } catch { }
                                        return;
                                    }
                                    Directory.Delete(extractDir, true);
                                }
                                Directory.CreateDirectory(extractDir);
                                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);
                                File.Delete(zipPath);
                            }
                            log.AppendText("[OK] 源码已导出到: " + target + "\\系统清理与优化工具_源码\r\n");
                            System.Windows.MessageBox.Show(this, "源码已导出到：\n" + target + "\\系统清理与优化工具_源码\n\n包含所有 .cs/.xaml/.csproj 文件。", "导出成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        }
                    }
                    catch (Exception ex) { log.AppendText("[!] 导出源码失败: " + ex.Message + "\r\n"); }
                })
            );
            wp.Margin = new Thickness(0, 0, 0, 12);
            Grid.SetRow(wp, r++);
            inner.Children.Add(wp);

            // ========== 背景图设置卡片（预览加大 + 透明度并排） ==========
            var bgCard = new Border
            {
                Background = _isDarkMode ? Brushes.Transparent : _bgDeep,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8),
                ClipToBounds = true // ★ 防止最大化时子内容溢出 + 缩小时残留大尺寸渲染缓存
            };
            // ★ bgSp 改为 Grid：标题 Auto + 预览区 Star（自动填充剩余空间，默认/最大化都利用完）
            var bgSp = new Grid();
            bgSp.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // [0] 标题行（固定）
            bgSp.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // [1] ★预览区（填充剩余空间）
            // 前向声明（标题行按钮闭包会引用）
            System.Windows.Controls.Slider darkOpSlider = null, lightOpSlider = null;
            Action refreshThumbs = null;

            // 标题行：标题 + 提示（左） | 恢复默认背景按钮（右）
            var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            var titleLeft = new StackPanel { Orientation = Orientation.Horizontal };
            titleLeft.Children.Add(new TextBlock { Text = "🎨 自定义背景图", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            titleLeft.Children.Add(new TextBlock { Text = "  提示：支持 PNG/JPG/BMP/GIF/WebP；图片会被引用（不嵌入 exe），请勿删除原文件。切换主题后新背景自动生效。", Foreground = _textDim, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            DockPanel.SetDock(titleLeft, Dock.Left);
            titleRow.Children.Add(titleLeft);
            // 恢复默认背景按钮（右上角）
            var resetBgBtn = Btn("🔄 恢复默认背景", false, () =>
            {
                _backgroundSettings.DarkPath = "";
                _backgroundSettings.LightPath = "";
                _backgroundSettings.DarkOpacity = 0.55;
                _backgroundSettings.LightOpacity = 1.0;
                darkOpSlider.Value = 0.55;
                lightOpSlider.Value = 1.0;
                SaveBackgroundSettings();
                log.AppendText("[OK] 已恢复为内置默认背景\r\n");
                refreshThumbs();
                ApplyShellColors();
                InvalidateConfigCache(); // 背景设置已变化 → 失效缓存
            }, 130);
            resetBgBtn.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(resetBgBtn, Dock.Right);
            titleRow.Children.Add(resetBgBtn);
            bgSp.Children.Add(titleRow);
            Grid.SetRow(titleRow, 0);

            // ===== 两列布局：左深色 | 右浅色，Star 列随容器自动均分宽度 =====

            var bgTwoCol = new Grid { Margin = new Thickness(12, 0, 12, 0) };  // 拉伸全宽（紧凑边距）
            bgTwoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 深色列 ★
            bgTwoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });   // 间距（固定）
            bgTwoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 浅色列 ★

            // ── 左列：深色模式（按钮行 / 预览图 Star 填充 / 透明度调整 Auto） ──
            var darkCol = new Grid();
            darkCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // [0] 选择背景按钮（图片上方，居中）
            darkCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // [1] 预览图占满剩余高度，最大化时填满，默认时自动让出滑块
            darkCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // [2] 透明度调整

            // 深色预览图：Viewbox Uniform 在 Star 行内完整显示，容器随可用空间拉伸
            // 不画自身边框，避免与外层 bgCard 边框叠加形成多余框线
            var darkThumb = new System.Windows.Controls.Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(0),
                ClipToBounds = true,
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var darkThumbImg = new Image { IsHitTestVisible = false };
            var darkViewbox = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Child = darkThumbImg };
            darkThumb.Child = darkViewbox;

            // 选择背景按钮：放在图片上方一行，与图片居中对齐
            var darkBtn = Btn("🌙 选择深色背景", false, () =>
            {
                // png/jpg/bmp/gif/webp 全支持（webp 走 System.Drawing 转码通道）
                var dlg = new OpenFileDialog
                {
                    Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*",
                    Title = "选择深色模式背景图"
                };
                if (dlg.ShowDialog() == true)
                {
                    var testImg = MainWindow.TryLoadImagePublic(dlg.FileName);
                    if (testImg == null)
                    {
                        string ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
                        if (ext == ".webp" && !MainWindow.IsWebpCodecAvailable())
                        {
                            // 自动后台安装 WebP 解码器，装完重试
                            log.AppendText("[*] 系统缺少 WebP 解码器，正在后台自动安装（约 1 分钟）...\r\n");
                            pb.Visibility = Visibility.Visible;
                            RunInBg(log, l =>
                            {
                                if (MainWindow.InstallWebpExtension(l))
                                {
                                    var retry = MainWindow.TryLoadImagePublic(dlg.FileName);
                                    if (retry != null)
                                    {
                                        _backgroundSettings.DarkPath = dlg.FileName;
                                        SaveBackgroundSettings();
                                        l("[OK] WebP 解码器已安装，背景已自动应用");
                                        try { Dispatcher.Invoke(() => { refreshThumbs(); ApplyShellColors(); }); } catch { /* 窗口已关闭，忽略 */ }
                                        return;
                                    }
                                    l("[FAIL] 解码器已安装但该图片仍无法加载（文件可能损坏）");
                                }
                                // 未装成功或仍失败——给手动指引
                                l("提示：可右键 webp → 打开方式 → 画图 → 另存为 PNG 后再选。");
                            }, "WebP 扩展安装中", () => { pb.Visibility = Visibility.Collapsed; InvalidateConfigCache(); });
                            return;
                        }
                        log.AppendText("[FAIL] 图片加载失败：" + Path.GetFileName(dlg.FileName) + "\r\n");
                        string hint = ext == ".webp"
                            ? "\n\n当前是 webp 格式。系统未安装 WebP 解码器。\n\n最快方案：右键 webp → 画图 → 另存为 PNG/JPG。"
                            : "\n\n请确认图片文件有效（png/jpg/bmp/gif 或 webp）。";
                        System.Windows.MessageBox.Show(this,
                            "图片加载失败。\n\n" + hint,
                            "背景图加载失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        return;
                    }
                    _backgroundSettings.DarkPath = dlg.FileName;
                    SaveBackgroundSettings();
                    log.AppendText("[OK] 深色背景已设置: " + Path.GetFileName(dlg.FileName) + "\r\n");
                    refreshThumbs();
                    ApplyShellColors();
                    InvalidateConfigCache(); // 背景设置已变化 → 失效缓存
                }
            }, 110);
            darkBtn.FontSize = 11;
            darkBtn.Padding = new Thickness(6, 3, 6, 3);
            darkBtn.Margin = new Thickness(0);
            var darkBtnBg = _btnSecondaryBg.Clone(); darkBtnBg.Opacity = 0.88; darkBtn.Background = darkBtnBg;

            darkBtn.HorizontalAlignment = HorizontalAlignment.Center;
            darkBtn.Margin = new Thickness(0, 0, 0, 6);
            darkCol.Children.Add(darkBtn);
            Grid.SetRow(darkBtn, 0);

            darkCol.Children.Add(darkThumb);
            Grid.SetRow(darkThumb, 1);

            // 透明度调整（滑块加长，视觉更舒展）
            var darkOpLbl = new TextBlock { Text = "透明度:", FontSize = 11.5, Foreground = _textMain, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            darkOpSlider = new System.Windows.Controls.Slider
            { Minimum = 0.1, Maximum = 1.0, Value = _backgroundSettings.DarkOpacity, TickFrequency = 0.05, IsSnapToTickEnabled = true, Width = 140, VerticalAlignment = VerticalAlignment.Center };
            var darkOpVal = new TextBlock { Text = _backgroundSettings.DarkOpacity.ToString("P0"), FontSize = 11.5, Foreground = _accent, VerticalAlignment = VerticalAlignment.Center, MinWidth = 34, Margin = new Thickness(4, 0, 0, 0) };
            darkOpSlider.ValueChanged += (s, e) =>
            {
                _backgroundSettings.DarkOpacity = darkOpSlider.Value;
                darkOpVal.Text = _backgroundSettings.DarkOpacity.ToString("P0");
                // 防抖落盘：拖动过程中只更新内存值与预览，停止拖动 400ms 后才真正写盘
                // （一次拖动会触发几十次 ValueChanged，逐次 SaveBackgroundSettings 等于几十次磁盘写）
                SaveBackgroundSettingsDebounced();
                darkThumbImg.Opacity = _backgroundSettings.DarkOpacity;
                BgImage.Opacity = _backgroundSettings.DarkOpacity;
            };
            var darkCtrlRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            darkCtrlRow.Children.Add(darkOpLbl); darkCtrlRow.Children.Add(darkOpSlider); darkCtrlRow.Children.Add(darkOpVal);
            darkCol.Children.Add(darkCtrlRow);
            Grid.SetRow(darkCtrlRow, 2);

            Grid.SetColumn(darkCol, 0);
            bgTwoCol.Children.Add(darkCol);

            // ── 右列：浅色模式（按钮行 / 预览图 Star 填充 / 透明度调整 Auto） ──
            var lightCol = new Grid();
            lightCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // [0] 选择背景按钮（图片上方，居中）
            lightCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // [1] 预览图占满剩余高度，最大化时填满，默认时自动让出滑块
            lightCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // [2] 透明度调整

            // 浅色预览图：Viewbox Uniform 在 Star 行内完整显示，容器随可用空间拉伸
            // 不画自身边框，避免与外层 bgCard 边框叠加形成多余框线
            var lightThumb = new System.Windows.Controls.Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(0),
                ClipToBounds = true,
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var lightThumbImg = new Image { IsHitTestVisible = false };
            var lightViewbox = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Child = lightThumbImg };
            lightThumb.Child = lightViewbox;

            // 选择背景按钮：放在图片上方一行，与图片居中对齐
            var lightBtn = Btn("☀️ 选择浅色背景", false, () =>
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*",
                    Title = "选择浅色模式背景图"
                };
                if (dlg.ShowDialog() == true)
                {
                    var testImg = MainWindow.TryLoadImagePublic(dlg.FileName);
                    if (testImg == null)
                    {
                        string ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
                        if (ext == ".webp" && !MainWindow.IsWebpCodecAvailable())
                        {
                            log.AppendText("[*] 系统缺少 WebP 解码器，正在后台自动安装（约 1 分钟）...\r\n");
                            pb.Visibility = Visibility.Visible;
                            RunInBg(log, l =>
                            {
                                if (MainWindow.InstallWebpExtension(l))
                                {
                                    var retry = MainWindow.TryLoadImagePublic(dlg.FileName);
                                    if (retry != null)
                                    {
                                        _backgroundSettings.LightPath = dlg.FileName;
                                        SaveBackgroundSettings();
                                        l("[OK] WebP 解码器已安装，背景已自动应用");
                                        try { Dispatcher.Invoke(() => { refreshThumbs(); ApplyShellColors(); }); } catch { /* 窗口已关闭，忽略 */ }
                                        return;
                                    }
                                    l("[FAIL] 解码器已安装但该图片仍无法加载（文件可能损坏）");
                                }
                                l("提示：可右键 webp → 打开方式 → 画图 → 另存为 PNG 后再选。");
                            }, "WebP 扩展安装中", () => { pb.Visibility = Visibility.Collapsed; InvalidateConfigCache(); });
                            return;
                        }
                        log.AppendText("[FAIL] 图片加载失败：" + Path.GetFileName(dlg.FileName) + "\r\n");
                        string hint = ext == ".webp"
                            ? "\n\n当前是 webp 格式。系统未安装 WebP 解码器。\n\n最快方案：右键 webp → 画图 → 另存为 PNG/JPG。"
                            : "\n\n请确认图片文件有效（png/jpg/bmp/gif 或 webp）。";
                        System.Windows.MessageBox.Show(this,
                            "图片加载失败。\n\n" + hint,
                            "背景图加载失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        return;
                    }
                    _backgroundSettings.LightPath = dlg.FileName;
                    SaveBackgroundSettings();
                    log.AppendText("[OK] 浅色背景已设置: " + Path.GetFileName(dlg.FileName) + "\r\n");
                    refreshThumbs();
                    ApplyShellColors();
                    InvalidateConfigCache(); // 背景设置已变化 → 失效缓存
                }
            }, 110);
            lightBtn.FontSize = 11;
            lightBtn.Padding = new Thickness(6, 3, 6, 3);
            lightBtn.Margin = new Thickness(0);
            var lightBtnBg = _btnSecondaryBg.Clone(); lightBtnBg.Opacity = 0.88; lightBtn.Background = lightBtnBg;

            lightBtn.HorizontalAlignment = HorizontalAlignment.Center;
            lightBtn.Margin = new Thickness(0, 0, 0, 6);
            lightCol.Children.Add(lightBtn);
            Grid.SetRow(lightBtn, 0);

            lightCol.Children.Add(lightThumb);
            Grid.SetRow(lightThumb, 1);

            // 透明度调整（滑块加长，视觉更舒展）
            var lightOpLbl = new TextBlock { Text = "透明度:", FontSize = 11.5, Foreground = _textMain, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            lightOpSlider = new System.Windows.Controls.Slider
            { Minimum = 0.1, Maximum = 1.0, Value = _backgroundSettings.LightOpacity, TickFrequency = 0.05, IsSnapToTickEnabled = true, Width = 140, VerticalAlignment = VerticalAlignment.Center };
            var lightOpVal = new TextBlock { Text = _backgroundSettings.LightOpacity.ToString("P0"), FontSize = 11.5, Foreground = _accent, VerticalAlignment = VerticalAlignment.Center, MinWidth = 34, Margin = new Thickness(4, 0, 0, 0) };
            lightOpSlider.ValueChanged += (s, e) =>
            {
                _backgroundSettings.LightOpacity = lightOpSlider.Value;
                lightOpVal.Text = _backgroundSettings.LightOpacity.ToString("P0");
                // 防抖落盘：同深色滑块，拖动中只更新内存与预览，停止 400ms 后写一次盘
                SaveBackgroundSettingsDebounced();
                lightThumbImg.Opacity = _backgroundSettings.LightOpacity;
                if (!_isDarkMode) BgImage.Opacity = _backgroundSettings.LightOpacity;
            };
            var lightCtrlRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            lightCtrlRow.Children.Add(lightOpLbl); lightCtrlRow.Children.Add(lightOpSlider); lightCtrlRow.Children.Add(lightOpVal);
            lightCol.Children.Add(lightCtrlRow);
            Grid.SetRow(lightCtrlRow, 2);

            Grid.SetColumn(lightCol, 2);
            bgTwoCol.Children.Add(lightCol);

            bgSp.Children.Add(bgTwoCol);
            Grid.SetRow(bgTwoCol, 1);

            // 刷新缩略图辅助方法（闭包捕获）
            refreshThumbs = () =>
            {
                var dImg = TryLoadImage(_backgroundSettings.DarkPath);
                if (dImg == null)
                {
                    try { dImg = new BitmapImage(new Uri("pack://application:,,,/系统清理与优化工具;component/background.png", UriKind.Absolute)); dImg.Freeze(); } catch { }
                }
                darkThumbImg.Source = dImg;
                darkThumbImg.Opacity = _backgroundSettings.DarkOpacity;

                var lImg = TryLoadImage(_backgroundSettings.LightPath);
                if (lImg == null)
                {
                    try { lImg = new BitmapImage(new Uri("pack://application:,,,/系统清理与优化工具;component/background-light.png", UriKind.Absolute)); lImg.Freeze(); } catch { }
                }
                lightThumbImg.Source = lImg;
                lightThumbImg.Opacity = _backgroundSettings.LightOpacity;
            };

            bgCard.Child = bgSp;
            Grid.SetRow(bgCard, r++);
            inner.Children.Add(bgCard);

            // 初始化缩略图
            refreshThumbs();
            Grid.SetRow(pb, r++);
            inner.Children.Add(pb);
            // 日志：包在固定高度容器中，物理截断，绝不膨胀
            Grid.SetRow(logClip, r++);  // row 4 (最后一行)
            inner.Children.Add(logClip);
            card.Child = inner;
            Grid.SetRow(card, rootRow++);  // Star 行：撑满剩余空间
            root.Children.Add(card);

            // 打开时默认列出配置目录下的所有 *.json → 写入日志
            // 抽成局部方法：首次构建与缓存命中（_configRefresh）共用同一数据加载，保证二次进页数据最新
            void ReloadConfigList()
            {
                AutoLoad(() =>
                {
                    try
                    {
                        var configs = ConfigBackup.ListConfigs();
                        string listText = "默认配置目录: " + ConfigBackup.ConfigDir + "\r\n" +
                            "已存配置 (" + configs.Count + " 个):\r\n" +
                            (configs.Count > 0 ? string.Join("\r\n", configs.Select(c => "  • " + c)) : "  (无)");
                        Dispatcher.Invoke(() => log.AppendText(listText + "\r\n"));
                    }
                    catch { }
                });
            }
            ReloadConfigList();

            // 稳健高度约束：绑定到 ContentArea.ActualHeight（只读 DP，自动跟随首帧+缩放，
            // 彻底消除"首次打开未填充 / 最大化后恢复默认尺寸内容漂移"两类时序 bug）
            BindRootHeightToViewport(root);

            // ---- 页面级缓存：首次构建完成后缓存整页；再次进入复用并仅刷新动态状态 ----
            // 仅在构建期间主题未变时写入缓存（避免把混入旧主题刷子的页面标记为可复用）
            if (buildDark == _isDarkMode)
            {
                _configCache.Set(root, buildDark);
                _configCache.SetRefresh(() =>
                {
                    // 复位动态状态（与旧版每次新建页面行为一致）：
                    // 清空日志、复位进度条、刷新默认路径/透明度滑块/背景缩略图，并重触发 AutoLoad 的配置列表加载
                    log.Clear();
                    pb.Visibility = Visibility.Collapsed;
                    pathInput.Text = ConfigBackup.ConfigDir;
                    darkOpSlider.Value = _backgroundSettings.DarkOpacity;
                    lightOpSlider.Value = _backgroundSettings.LightOpacity;
                    refreshThumbs();
                    ReloadConfigList();
                });
            }

            return root;
        }

        // ---------- 背景设置防抖落盘 ----------

        /// <summary>待触发的延时保存取消源（每次新变更都会取消上一次尚未执行的保存）。</summary>
        private System.Threading.CancellationTokenSource _bgSettingsSaveCts;

        /// <summary>
        /// 防抖保存背景设置：连续变更时只保留最后一次，停止变更 400ms 后才真正写盘。
        /// 修复：原实现在 Slider.ValueChanged 里直接 SaveBackgroundSettings()，一次拖动会触发几十次磁盘写。
        /// 采用 Task.Delay + CancellationToken 而非 DispatcherTimer：无需在页面卸载时手动 Stop，
        /// 也不会因页面缓存复用而累积计时器（DispatcherTimer 忘记 Stop 会一直持有页面对象，造成泄漏）。
        /// </summary>
        private void SaveBackgroundSettingsDebounced()
        {
            var prev = _bgSettingsSaveCts;
            _bgSettingsSaveCts = null;
            if (prev != null)
            {
                try { prev.Cancel(); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
                // CancellationTokenSource 持有内核计时器句柄，Cancel 后必须 Dispose 才能及时释放；
                // 一次拖动会产生几十个 CTS，不 Dispose 会持续占用句柄直到 GC。
                try { prev.Dispose(); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
            }
            var cts = new System.Threading.CancellationTokenSource();
            _bgSettingsSaveCts = cts;
            var token = cts.Token;
            System.Threading.Tasks.Task.Delay(400, token).ContinueWith(t =>
            {
                if (t.IsCanceled || token.IsCancellationRequested) return;
                try
                {
                    Dispatcher.Invoke(new Action(SaveBackgroundSettings));
                }
                catch { /* 窗口已关闭，忽略 */ }
                finally
                {
                    // 只对仍然在册的那一批 CTS 收尾，避免误 Dispose 掉后续新建的
                    if (_bgSettingsSaveCts == cts) _bgSettingsSaveCts = null;
                    try { cts.Dispose(); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
                }
            }, token);
        }

        // ---------- Config helpers ----------

        /// <summary>
        /// 导出源码目录安全校验：防止把盘符根目录、系统目录（Windows / Program Files / Program Files (x86) /
        /// ProgramData）或用户配置文件根目录本身当作导出目标，进而被无提示递归删除同名文件夹。
        /// </summary>
        /// <param name="dir">待写入/可能删除的目录（会先经 Path.GetFullPath 规范化）。</param>
        /// <param name="reason">校验不通过时的中文原因；通过时为 null。</param>
        private static bool IsSafeExtractDir(string dir, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(dir)) { reason = "路径为空"; return false; }

            string full;
            try { full = Path.GetFullPath(dir); }
            catch (Exception ex) { reason = "路径不合法：" + ex.Message; return false; }

            // 路径长度合理性：过短（如 "C:\"）或过长都直接拒绝
            if (full.Length < 4) { reason = "路径过短，疑似盘符或无效路径"; return false; }
            if (full.Length > 240) { reason = "路径过长（超过 240 字符）"; return false; }

            string root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root) || IsSameDir(full, root)) { reason = "不允许在盘符根目录下操作"; return false; }

            var protectedDirs = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),                        // C:\Windows
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),                   // Program Files
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),                // Program Files (x86)
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),          // ProgramData
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),                    // C:\Users\<me>
                Environment.GetFolderPath(Environment.SpecialFolder.System),                         // System32
            };
            try { protectedDirs.Add(Environment.GetEnvironmentVariable("ProgramW6432") ?? ""); }
            catch { }

            // 目标目录本身与它的父目录（用户实际选择的目录）都必须在受保护目录之外
            var candidates = new List<string> { full };
            try
            {
                string parent = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(parent)) candidates.Add(parent);
            }
            catch { }

            foreach (var p in protectedDirs)
            {
                if (string.IsNullOrEmpty(p)) continue;
                foreach (var c in candidates)
                {
                    // 拒绝位于受保护目录内部（含子目录）的目标，而不仅仅是完全相等。
                    // 例如 C:\Windows\Temp 也应被拒绝，防止通过子目录绕过校验。
                    if (IsSameDir(c, p) || IsSubDir(c, p))
                    { reason = "目标位于受保护的系统/用户目录内部：" + p; return false; }
                }
            }
            return true;
        }

        /// <summary>忽略大小写与结尾分隔符的目录相等比较。</summary>
        private static bool IsSameDir(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(TrimDirSep(a), TrimDirSep(b), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>判断 child 是否位于 parent 内部（忽略大小写、统一分隔符）。</summary>
        private static bool IsSubDir(string child, string parent)
        {
            if (string.IsNullOrEmpty(child) || string.IsNullOrEmpty(parent)) return false;
            string c = TrimDirSep(child) + Path.DirectorySeparatorChar;
            string p = TrimDirSep(parent) + Path.DirectorySeparatorChar;
            return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
        }

        private static string TrimDirSep(string p)
        {
            string s = p.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return s.Length == 0 ? p : s;
        }

        private ToolConfig CollectConfig()
        {
            var cfg = new ToolConfig();
            foreach (var t in Tweaks.All)
            {
                if (t.IsThreeState)
                {
                    TweakState st; try { st = t.GetState3(); } catch { st = TweakState.Default; }
                    cfg.TweakStates[t.Id] = st.ToString(); // "On"/"Off"/"Default"
                }
                else if (t.State()) cfg.EnabledTweaks.Add(t.Id);
            }
            return cfg;
        }

        private void ApplyConfig(ToolConfig cfg, TextBox log)
        {
            foreach (var t in Tweaks.All)
            {
                if (t.IsThreeState)
                {
                    // 三态项：仅当配置显式记录时才应用；缺省则不改动（保留系统现状）
                    if (cfg.TweakStates.TryGetValue(t.Id, out var sv) && Enum.TryParse<TweakState>(sv, out var st))
                    {
                        try { t.Apply3(st, s => log.AppendText(s + "\r\n")); }
                        catch (Exception ex) { log.AppendText("[!] " + t.Id + ": " + ex.Message + "\r\n"); }
                    }
                }
                else
                {
                    bool want = cfg.EnabledTweaks.Contains(t.Id);
                    bool has = t.State();
                    if (want && !has) t.Enable(s => log.AppendText(s + "\r\n"));
                    else if (!want && has) t.Disable(s => log.AppendText(s + "\r\n"));
                }
            }
            log.AppendText("[OK] 已按配置应用优化项\r\n");
        }
    }
}

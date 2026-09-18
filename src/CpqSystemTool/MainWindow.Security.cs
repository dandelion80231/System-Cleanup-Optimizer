using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using System.ServiceProcess;

namespace CpqSystemTool
{
    public partial class MainWindow
    {
        // =====================================================================
        //  Module: Defender（增强服务状态详情，参考 Win11EasyConfig）
        // =====================================================================

        // =====================================================================
        //  Module: 安全防护（Defender + 更新管理 合并页）
        // =====================================================================

        // 安全防护页缓存（与常用软件页/Appx 页同款模式）：首次构建完成后缓存整页外壳，
        // 再次进入复用外壳并仅后台重刷动态状态（Defender 状态/开关/按钮、防火墙、更新）。
        // 动态状态全部落在可重建/可复位的容器与属性上（defStatusHost/defWp/defToggles/fwStatusHost/ruleList/updateBtnHost、
        // ApplyPolicyMode 高亮、_lastDefAction、日志），故操作完成后无需失效，仅靠 _securityRefresh 重刷。
        private readonly PageCache<UIElement> _securityCache = new PageCache<UIElement>();

        // 安全防护页 TP 状态轮询定时器：TP 只能外部（安全中心）改，无事件可感知，靠轮询检测变化。
        // 页面重建（主题切换）时先 Stop 旧的再建新的，避免多个 timer 并存。
        private System.Windows.Threading.DispatcherTimer _tpPollTimer;

        private UIElement BuildSecurity()
        {
            // 记录本次构建时主题：缓存仅在主题一致时命中（主题切换会重建当前页，避免复用旧主题刷子的页面）
            bool buildDark = _isDarkMode;
            // 缓存命中且主题一致 → 复用已构建页面，仅后台重刷动态状态
            var cached = _securityCache.TryGet(buildDark);
            if (cached != null) return cached;

            var root = new StackPanel();
            root.Children.Add(Header("安全防护", "Windows Defender 防病毒与 Windows Update 更新管控。均为高风险操作，谨慎使用。"));

            // 关键：BuildSecurity 入口不再同步刷 Defender 状态缓存，而是先渲染骨架，
            // 后台线程跑一次 PowerShell 拿全部 5 个值，再填充 UI，避免切页时 UI 卡死。

            var pb = MakeProgress();
            var log = MakeLogBox();
            log.Height = 100;
            log.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var logBorder = WrapLogBox(log);

            // ===== 上：Windows Defender 卡片 =====
            var defCard = Card();
            var defInner = (StackPanel)defCard.Child;
            defInner.Children.Add(new Emoji.Wpf.TextBlock { Text = "🛡 Windows Defender", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });

            // 状态区（可刷新，禁用/恢复 WD 后重建而不丢日志）
            var defStatusHost = new StackPanel();
            defInner.Children.Add(defStatusHost);
            var defLoading = new TextBlock
            {
                Text = "正在检测 Defender 状态…",
                Foreground = _textDim,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 6)
            };
            defStatusHost.Children.Add(defLoading);

            void BuildDefenderStatus()
            {
                defStatusHost.Children.Clear();

                // 极简版：只显示一行总状态。详细 5 项由下方 toggle 区实时反映（避免视觉重复）。
                bool policyOff = Defender.IsDisabled();
                bool allOff = policyOff;
                bool fullyOk = Defender.LastOperationFullSuccess;
                var overallStatus = new TextBlock
                {
                    Text = allOff
                        ? (Defender.LastOperationFullSuccess ? "✓ 当前状态：实时保护已禁用" : "⚠ 当前状态：已禁用（部分失败）")
                        : "✓ 当前状态：正常运行",
                    Foreground = fullyOk ? _successGreen : _warnOrange,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                defStatusHost.Children.Add(overallStatus);

                var note = new Emoji.Wpf.TextBlock
                {
                    Text = allOff
                        ? "提示：下方 4 个开关可单独微调（无需重启）。⚠ 请勿重启——Windows 11 24H2+ 重启会还原 Defender 配置。恢复请点击右侧「一键恢复 WD」。"
                        : "提示：下方 4 个开关可单独切换（无需重启）。",
                    Foreground = fullyOk ? _textMain : _warnOrange,
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                defStatusHost.Children.Add(note);
            }

            // ===== 4 个核心开关（2×2 紧凑网格，与"状态行"同一视觉区块） =====
            // Grid(2★×2★) 承载 4 个 mkTog 复选框：第 1 行 实时保护/行为监控，第 2 行 云保护/样本提交。
            // 容器用 Grid 而非 StackPanel：SyncDefToggles 重建时 Clear() 后重新挂 4 项即可。
            var defToggles = new Grid { Margin = new Thickness(0, 10, 0, 4) };
            defToggles.RowDefinitions.Add(new RowDefinition());
            defToggles.RowDefinitions.Add(new RowDefinition());
            defToggles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            defToggles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            defInner.Children.Add(defToggles);

            // ===== 一键禁用/恢复（双路同步 Policies + ClearAllPolicies）=====
            // 等宽均分整行：Grid(2×★Star) + 按钮居中、保持原始大小（与安全防护更新按钮行一致）
            var defWp = new Grid { Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            defWp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            defWp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            defInner.Children.Add(defWp);

            // ===== 临时禁用/恢复（03/04 逻辑：仅 Set-MpPreference，不动 Policies 注册表）=====
            // 定位：比"一键禁用"更轻——适合"让位给某安装程序"场景，重启后自动还原，无需手动恢复。
            // 视觉上紧跟「一键禁用/恢复」成行（defWp 之下），次级样式（非 accent 填充）。
            var tempBar = new Grid { Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            tempBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tempBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            defInner.Children.Add(tempBar);

            // ===== 篡改防护(TP) 状态区（06 逻辑）=====
            // TP 开时，Windows 会拦截所有外部对 Defender 的运行时修改（含 Set-MpPreference），
            // 只有安全中心 GUI 能手动切换。本区只读 + 跳转，不让用户直接改 TP（改了也无效）。
            // 后台线程静默读 IsTamperProtected，读好后直接填充，不显示加载文本。
            // 视觉：独立区块，位于「临时禁用/恢复」之下，与上方保留呼吸间距。
            var tpHost = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            defInner.Children.Add(tpHost);

            // ===== 一键禁用/恢复按钮（填充状态与上方状态区同步，参考更新管理 RebuildUpdateButtons 模式） =====
            // 填充规则：哪个按钮代表"当前实际状态"，哪个就填充；点击后最后操作的按钮也填充
            string _lastDefAction = null;
            // 上一次读到的 TP 状态（供 RefreshTpStatus(force:false) 判断是否真的变化，避免无谓重建 UI）。
            // 声明位置须早于下方 tempBar 按钮闭包（那里会调用 RefreshTpStatus）——局部变量作用域自声明处起。
            bool? _lastTpOn = null;
            bool ShouldFillDef(string actionKey, bool stateDefault)
            {
                if (_lastDefAction != null)
                    return _lastDefAction == actionKey;
                return stateDefault;
            }
            void RebuildDefenderButtons()
            {
                bool disabled = Defender.IsDisabled();
                defWp.Children.Clear();
                var bDisable = Btn("✘ 一键禁用 WD", ShouldFillDef("disable", disabled), () =>
                {
                    // 危险操作确认
                    if (MessageBox.Show("确定要禁用 Windows Defender 吗？\n\n系统将失去实时病毒防护，此操作可在「一键恢复 WD」中还原。", "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return;
                    // 防重入：与其它耗时操作互斥
                    if (!OperationLock.TryEnter("一键禁用 Defender", out string busyBy))
                    {
                        MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    _lastDefAction = "disable";
                    RebuildDefenderButtons(); // 立即刷新高亮，给点击反馈
                    pb.Visibility = Visibility.Visible;
                    RunInBg(log, Defender.Disable, "已禁用 Defender", () => { OperationLock.Exit(); pb.Visibility = Visibility.Collapsed; SyncDefToggles(); BuildDefenderStatus(); RebuildDefenderButtons(); RefreshTpStatus(); });
                });
                bDisable.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(bDisable, 0);
                defWp.Children.Add(bDisable);
                var bEnable = Btn("✔ 一键恢复 WD", ShouldFillDef("restore", !disabled), () =>
                {
                    if (!OperationLock.TryEnter("一键恢复 Defender", out string busyBy))
                    {
                        MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    _lastDefAction = "restore";
                    RebuildDefenderButtons(); // 立即刷新高亮，给点击反馈
                    pb.Visibility = Visibility.Visible;
                    RunInBg(log, Defender.Enable, "已启用 Defender", () => { OperationLock.Exit(); pb.Visibility = Visibility.Collapsed; SyncDefToggles(); BuildDefenderStatus(); RebuildDefenderButtons(); RefreshTpStatus(); });
                });
                bEnable.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(bEnable, 1);
                defWp.Children.Add(bEnable);
            }

            // ===== 临时禁用/恢复按钮（挂到已声明的 tempBar） =====
            var bTempDisable = Btn("🔒 临时禁用 WD", false, () =>
            {
                // fix-CA：此前点击直接执行、无确认弹窗（与「一键禁用 WD」不一致）。增加确认，
                // 说明临时失去实时防护、重启后自动还原。
                if (MessageBox.Show("确定要临时禁用 Windows Defender 吗？\n\n• 临时失去实时病毒防护\n• 仅通过 Set-MpPreference 调整，不修改策略注册表\n• 重启或执行「临时恢复 WD」后自动还原\n\n是否继续？", "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
                if (!OperationLock.TryEnter("临时禁用 Defender", out string busyBy))
                {
                    MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                pb.Visibility = Visibility.Visible;
                RunInBg(log, Defender.TemporaryDisable, "已临时禁用 Defender", () => { OperationLock.Exit(); pb.Visibility = Visibility.Collapsed; SyncDefToggles(); BuildDefenderStatus(); RebuildDefenderButtons(); RefreshTpStatus(); });
            });
            bTempDisable.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(bTempDisable, 0);
            tempBar.Children.Add(bTempDisable);

            var bTempEnable = Btn("🔓 临时恢复 WD", false, () =>
            {
                if (!OperationLock.TryEnter("临时恢复 Defender", out string busyBy))
                {
                    MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                pb.Visibility = Visibility.Visible;
                RunInBg(log, Defender.TemporaryEnable, "已临时恢复 Defender", () => { OperationLock.Exit(); pb.Visibility = Visibility.Collapsed; SyncDefToggles(); BuildDefenderStatus(); RebuildDefenderButtons(); RefreshTpStatus(); });
            });
            bTempEnable.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(bTempEnable, 1);
            tempBar.Children.Add(bTempEnable);

            // 可复用的后台刷新函数见下方（RefreshTpStatus）

            // ============ 5 个独立 Defender 开关（每个 Get/Set 实时同步） ============
            // 用 PowerShell Set-MpPreference 官方 API，立即生效、不需要重启、不需要 TI 提权。
            // TP 开启时部分选项（云保护/样本提交/TP 本身）会被拦，UI 会在异步回调时回滚到 Get* 当前值。
            // 注：defToggles 已在上面声明（bDisable/bEnable 闭包需要）
            // 修复：refreshCache=true 时原实现在 UI 线程同步跑 PowerShell（Get-MpPreference），
            // 每次切换开关 / 一键禁用恢复 / 清理策略后都会冻结 UI 数秒。
            // 改为与本页初始加载（DefenderInitLoader）同款的后台刷新：后台线程刷缓存 → 回 UI 线程重建开关。
            // 不用 RunInBg 是因为它会先 log.Clear() 清掉刚写入的操作日志。
            void SyncDefToggles(bool refreshCache = true)
            {
                // 重建前刷一次缓存（Set 后值变了，缓存可能过期）；初始加载时已在后台刷好，传 false 避免重复阻塞 UI
                if (refreshCache)
                {
                    var syncDisp = Dispatcher;
                    new Thread(() =>
                    {
                        try { Defender.RefreshStatusCache(); }
                        catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
                        try { syncDisp.Invoke(new Action(BuildToggleList)); } catch { /* 窗口已关闭，忽略 */ }
                    }) { IsBackground = true, Name = "DefenderToggleSync" }.Start();
                    return;
                }
                BuildToggleList();
            }

            // mkTog 放在 BuildToggleList 函数体内（避免 click lambda 与 SyncDefToggles 互相引用的位置依赖）
            void BuildToggleList()
            {
                defToggles.Children.Clear();
                System.Func<string, Func<bool>, Action<bool, Action<string>>, System.Windows.Controls.CheckBox> mkTog = (label, getState, setter) =>
                {
                    bool initial = false;
                    try { initial = getState(); } catch (Exception ex) { DebugLog.Ignore(ex); }
                    var chk = new System.Windows.Controls.CheckBox
                    {
                        Content = label,
                        IsChecked = initial,
                        Foreground = _textMain,
                        FontSize = 13,
                        Cursor = Cursors.Hand,
                        Margin = new Thickness(0, 4, 0, 4)
                    };
                    chk.Click += (s, e) =>
                    {
                        if (!OperationLock.TryEnter(label, out string busyBy))
                        {
                            MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                        bool target = chk.IsChecked == true;
                        pb.Visibility = Visibility.Visible;
                        RunInBg(log, l => setter(target, l), (target ? "已启用 " : "已禁用 ") + label,
                            () =>
                            {
                                OperationLock.Exit();
                                pb.Visibility = Visibility.Collapsed;
                                SyncDefToggles();         // 重新读 Get* 刷新 toggle（Set 失败时自动回滚）
                                BuildDefenderStatus();    // 刷新"当前状态"行
                                RebuildDefenderButtons(); // 刷新一键禁用/恢复按钮填充
                            });
                    };
                    return chk;
                };

                // 4 个核心开关排成 2×2（defToggles 是 Grid：第 1 行 实时保护/行为监控，第 2 行 云保护/样本提交）
                var c0 = mkTog("实时保护（含开发人员驱动的保护）",
                    () => Defender.GetRealtime(), (b, l) => Defender.SetRealtime(b, l));
                Grid.SetRow(c0, 0); Grid.SetColumn(c0, 0);
                var c1 = mkTog("行为监控",
                    () => Defender.GetBehavior(), (b, l) => Defender.SetBehavior(b, l));
                Grid.SetRow(c1, 0); Grid.SetColumn(c1, 1);
                var c2 = mkTog("云提供的保护",
                    () => Defender.GetCloud(), (b, l) => Defender.SetCloud(b, l));
                Grid.SetRow(c2, 1); Grid.SetColumn(c2, 0);
                var c3 = mkTog("自动提交样本",
                    () => Defender.GetSampleSubmit(), (b, l) => Defender.SetSampleSubmit(b, l));
                Grid.SetRow(c3, 1); Grid.SetColumn(c3, 1);
                defToggles.Children.Add(c0);
                defToggles.Children.Add(c1);
                defToggles.Children.Add(c2);
                defToggles.Children.Add(c3);
                // 篡改防护(TP)不在开关区：TP 开时 Windows 拦截一切外部脚本对 Defender 的改动，只能手动开/关。
                // 下方独立「TP 状态区」显示真实状态 + 提供跳转安全中心按钮。

                // 修复（异步化后的状态一致性）：SyncDefToggles(true) 改为后台刷新后，开关列表是在
                // 后台线程刷完缓存、回到 UI 线程执行 BuildToggleList 时才重建的。像「清理策略残留」
                // 这类 Defender.ClearAllPolicies 只改注册表、不更新内存缓存的操作，调用点紧跟其后的
                // BuildDefenderStatus()/RebuildDefenderButtons() 仍会用清理前的旧缓存渲染，
                // 导致状态行停留在旧值、必须重新进页才更新。这里在开关重建完成后一并刷新状态与按钮，
                // 保证异步路径与原先同步路径的最终 UI 一致（同步路径下这两行会被多调一次，幂等无害）。
                BuildDefenderStatus();
                RebuildDefenderButtons();
            }

            // 可复用的后台刷新函数：进页首次 + 操作 onDone + 重进页 + 定时轮询 都调它
            // 每次重建 host 子项（tpHost.Clear() + 重新填充），避免累积
            // force=true（默认）：强制重建 UI，用于进页/操作后等必须刷新的场景。
            // force=false：供定时轮询使用——读到的 TP 状态与上次一致时直接返回，
            //   不做 Children.Clear() 重建，避免每轮轮询造成 UI 闪烁与无谓布局开销。
            void RefreshTpStatus(bool force = true)
            {
                // 定时轮询期间若正有耗时操作（禁用/恢复/切开关…）在跑，跳过本轮，
                // 避免后台刷新冲掉用户正在等待的操作结果 UI
                if (!force && OperationLock.IsBusy) return;
                var disp = Dispatcher;
                new Thread(() =>
                {
                    bool tpOn;
                    try { tpOn = Defender.IsTamperProtectionEnabled(); }
                    catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); tpOn = true; }
                    try { disp.Invoke(() =>
                    {
                        if (!force && _lastTpOn.HasValue && _lastTpOn.Value == tpOn)
                            return;             // 状态未变 → 保持现有 UI，不重建
                        _lastTpOn = tpOn;
                        tpHost.Children.Clear();
                        // 与上方 4 开关的 defToggles 同为「2 等宽列」结构：
                        // 第 0 列放标签，第 1 列放「方框+状态」，使方框左边缘与上方「行为监控」同一竖线。
                        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 0: 标签（左半列）
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 1: 状态（右半列）
                        // 第 0 列：标签
                        var tpLabel = new Emoji.Wpf.TextBlock
                        {
                            Text = "🛡 篡改防护 (TP)",
                            Foreground = _textMain,
                            FontSize = 13,
                            FontWeight = FontWeights.SemiBold,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        Grid.SetColumn(tpLabel, 0);
                        // 第 1 列：内部再分「方框（左对齐=与行为监控同竖线）… 按钮（右对齐到行尾）」
                        var rightBox = new Grid();
                        rightBox.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 方框+状态
                        rightBox.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 弹性间隔
                        rightBox.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 按钮
                        // 方框 + 状态文字（与上方 4 个开关同款 CheckBox：勾选=已开启，未勾=已关闭）
                        // 纯指示用途：IsHitTestVisible=false + Cursor=Arrow，TP 只能手动在安全中心改
                        var tpState = new System.Windows.Controls.CheckBox
                        {
                            Content = tpOn ? "已开启（外部脚本无法修改 Defender）" : "已关闭（外部脚本可正常改 Defender）",
                            IsChecked = tpOn,
                            IsHitTestVisible = false,
                            Cursor = Cursors.Arrow,
                            Foreground = tpOn ? _warnOrange : _successGreen,
                            FontSize = 12.5,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        Grid.SetColumn(tpState, 0);
                        var bOpenSc = Btn("🔗 打开安全中心", false, () => Defender.OpenSecurityCenter());
                        bOpenSc.VerticalAlignment = VerticalAlignment.Center;
                        Grid.SetColumn(bOpenSc, 2);
                        rightBox.Children.Add(tpState);
                        rightBox.Children.Add(bOpenSc);
                        Grid.SetColumn(rightBox, 1);
                        row.Children.Add(tpLabel);
                        row.Children.Add(rightBox);
                        tpHost.Children.Add(row);
                        tpHost.Children.Add(new TextBlock
                        {
                            Text = "提示：TP 开启时「一键/临时禁用」会被 Windows 拦截不生效。点「打开安全中心」直达病毒和威胁防护→管理设置页，向下找到「篡改防护」开关手动关闭即可。",
                            Foreground = _textDim,
                            FontSize = 11.5,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 0, 4)
                        });
                    }); } catch { /* 窗口已关闭，忽略 */ }
                }) { IsBackground = true, Name = "TpStatusLoader" }.Start();
            }

            // 进页首次后台读 TP
            RefreshTpStatus();

            // ===== TP 定时轮询（每 10s）=====
            // 原因：TP 只能用户在「Windows 安全中心」里手动改，本工具无任何事件可感知，
            //   而 4 个核心开关由本工具自己改（改完立即刷新），故只需对 TP 做外部变化检测。
            // 采用 force=false：状态未变不重建 UI（无闪烁）；OperationLock.IsBusy 时整轮跳过（不干扰用户操作）。
            // 页面重建（主题切换）时先停掉旧 timer，避免多个 timer 并存刷同一个 tpHost。
            if (_tpPollTimer != null) { _tpPollTimer.Stop(); _tpPollTimer = null; }
            _tpPollTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _tpPollTimer.Tick += (s, e) =>
            {
                try { RefreshTpStatus(false); }
                catch (Exception ex) { DebugLog.Ignore(ex); }
            };
            _tpPollTimer.Start();

            // 清理策略 + 诊断 Runtime 按钮同一行
            var policyBar = new Grid { Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            policyBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            policyBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            defInner.Children.Add(policyBar);

            // 底部两个动作按钮也加入「最后点击高亮」互斥组，和清理页操作按钮保持一致
            var bClear = Btn("🧹 清理策略残留", false, null);
            bClear.HorizontalAlignment = HorizontalAlignment.Stretch;
            bClear.Margin = new Thickness(0);
            Grid.SetColumn(bClear, 0);
            policyBar.Children.Add(bClear);

            var bDiag = Btn("🔍 诊断 Runtime 状态", false, null);
            bDiag.HorizontalAlignment = HorizontalAlignment.Stretch;
            bDiag.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(bDiag, 1);
            policyBar.Children.Add(bDiag);

            // 初始加载完成前禁用底部动作按钮，避免用户点击时触发同步阻塞
            bClear.IsEnabled = false;
            bDiag.IsEnabled = false;

            // 局部函数：切换底部两个按钮的高亮态（点击谁谁变 accent）
            void ApplyPolicyMode(Button sel)
            {
                foreach (var b in new[] { bClear, bDiag })
                {
                    if (b == null) continue;
                    bool on = b == sel;
                    b.Background = on ? _accent : _btnSecondaryBg;
                    b.Foreground = on ? _btnPrimaryFg : _btnSecondaryFg;
                    b.BorderBrush = on ? Brushes.Transparent : _panelBorder;
                    b.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                }
            }

            bClear.Click += (s, e) =>
            {
                // 破坏性清理：需确认
                if (MessageBox.Show("确定要清理 Windows Defender 策略残留吗？\n\n将移除注册表中的残留策略配置，可能影响当前生效的保护设置。", "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
                if (!OperationLock.TryEnter("清理策略残留", out string busyBy))
                {
                    MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                ApplyPolicyMode(bClear);
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l => Defender.ClearAllPolicies(l), "策略已清理", () =>
                {
                    OperationLock.Exit();
                    pb.Visibility = Visibility.Collapsed;
                    SyncDefToggles();
                    BuildDefenderStatus();
                    RebuildDefenderButtons();
                });
            };
            bDiag.Click += (s, e) =>
            {
                if (!OperationLock.TryEnter("诊断 Runtime", out string busyBy))
                {
                    MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                ApplyPolicyMode(bDiag);
                pb.Visibility = Visibility.Visible;
                RunInBg(log, Defender.DiagnoseRuntime, "诊断完成", () =>
                {
                    OperationLock.Exit();
                    pb.Visibility = Visibility.Collapsed;
                });
            };

            // ===== 注册表快照回滚区（07 逻辑） =====
            // 改 Policies / 服务 Start 前可先做快照；出问题选快照 reg import 回滚。
            // 快照文件统一存 cpq-tool\安全防护\regbackup\（安全防护页自己的数据），文件名含 tag+时间戳+键名。
            var snapHost = new StackPanel { Margin = new Thickness(0, 10, 0, 4) };
            defInner.Children.Add(snapHost);
            snapHost.Children.Add(new Emoji.Wpf.TextBlock
            {
                Text = "📋 注册表快照 / 回滚",
                Foreground = _accent,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 6)
            });
            // 一行三列：下拉框占左半（2★），「备份当前」「回滚所选」共用右半（各 1★）——布局整齐
            var snapRow = new Grid { Margin = new Thickness(0, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            snapRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            snapRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            snapRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            snapHost.Children.Add(snapRow);

            var snapCombo = new ComboBox
            {
                MinHeight = 28,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Background = _inputBg,
                Foreground = _textMain,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1)
            };
            Grid.SetColumn(snapCombo, 0);
            snapRow.Children.Add(snapCombo);

            // 快照列表提示（空列表时显示），独立容器防累积：
            // RefreshSnapshotList 只重建它的子项，避免反复清/加导致 TextBlock 累积
            var snapHintHost = new StackPanel { Margin = new Thickness(0, 6, 0, 2) };
            snapHost.Children.Add(snapHintHost);

            void RefreshSnapshotList()
            {
                snapHintHost.Children.Clear();
                var list = Defender.ListSnapshots();
                snapCombo.Items.Clear();
                foreach (var f in list)
                    snapCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = System.IO.Path.GetFileName(f), Tag = f });
                if (list.Count == 0)
                    snapHintHost.Children.Add(new TextBlock { Text = "（暂无快照，可先点「备份当前」生成 BEFORE 快照）", Foreground = _textDim, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 2) });
            }

            // 「备份当前」「回滚所选」挂在 snapRow 右半（col1/col2，与下拉框同一行）
            var bBackup = Btn("💾 备份当前", false, null);
            bBackup.HorizontalAlignment = HorizontalAlignment.Stretch;
            bBackup.Margin = new Thickness(0);
            Grid.SetColumn(bBackup, 1);
            snapRow.Children.Add(bBackup);
            var bRollback = Btn("⏪ 回滚所选", false, null);
            bRollback.HorizontalAlignment = HorizontalAlignment.Stretch;
            bRollback.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(bRollback, 2);
            snapRow.Children.Add(bRollback);

            // 列表首次加载：后台取一次
            var snapDisp = Dispatcher;
            new Thread(() =>
            {
                try { snapDisp.Invoke(() => { RefreshSnapshotList(); }); } catch { }
            }) { IsBackground = true, Name = "SnapListLoader" }.Start();

            bBackup.Click += (s, e) =>
            {
                if (!OperationLock.TryEnter("备份注册表快照", out string busyBy))
                {
                    MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l =>
                {
                    var dir = Defender.BackupSnapshots("BEFORE", l);
                    if (dir != "")
                        Dispatcher.Invoke(() => RefreshSnapshotList());
                }, "快照已备份", () =>
                {
                    OperationLock.Exit();
                    pb.Visibility = Visibility.Collapsed;
                });
            };

            bRollback.Click += (s, e) =>
            {
                if (!(snapCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item) || item.Tag == null)
                {
                    MessageBox.Show(this, "请先从列表中选择要回滚的快照文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var f = item.Tag as string;
                if (string.IsNullOrEmpty(f)) return;
                // 破坏性回滚：需确认
                if (MessageBox.Show("确定要用所选快照回滚注册表吗？\n\n将 reg import 还原该文件内容（服务 Start / Defender 策略值）。若涉及服务状态，需重启或重启服务后生效。", "确认回滚", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
                if (!OperationLock.TryEnter("回滚注册表快照", out string busyBy))
                {
                    MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l =>
                {
                    var ok = Defender.RestoreFromSnapshot(f, l);
                }, "快照已回滚", () =>
                {
                    OperationLock.Exit();
                    pb.Visibility = Visibility.Collapsed;
                    Dispatcher.Invoke(() => RefreshSnapshotList());
                });
            };

            // 默认不高亮底部动作按钮
            ApplyPolicyMode(null);

            // ===== Defender 状态初始化（方案 2：三级策略，首屏初值 O(1) 同步读，后台 PowerShell 校正） =====
            // 切页卡顿根因：末屏 new Thread 跑 RefreshStatusCache（spawn PowerShell 取 4 值，本机 1-2s），
            // PowerShell 返回才 Dispatcher.Invoke 填 BuildDefenderStatus——首次进页状态行比骨架晚 1-2s。
            // 修复：
            //   ① CacheValid==true（本会话已刷过）→ 缓存值【立即同步】填状态行（0 延迟），后台静默 RefreshStatusCache 校正；
            //   ② 首次进页（CacheValid==false）→ 先 SeedCacheFromPrefs 用注册表 Prefs O(1) 读实时/行为初值，
            //      立刻同步渲染"实时保护已禁用"（与页面切换同步出现，不等 PowerShell）；
            //      同时后台 RefreshStatusCache 拿全部 4 个真实值后覆盖一次（云保护/样本提交校正为准确值）；
            //   ③ Prefs 键/值都不存在（Defender 从未被改过）→ SeedCacheFromPrefs 返回 false，退回原 loading 占位后台加载。
            pb.Visibility = Visibility.Visible;
            var disp = Dispatcher;
            if (Defender.CacheValid)
            {
                // 缓存已就绪：立即同步填充，状态行与页面切换同步出现，不等 PowerShell
                defStatusHost.Children.Remove(defLoading);
                BuildDefenderStatus();
                RebuildDefenderButtons();
                SyncDefToggles(false);
                bClear.IsEnabled = true;
                bDiag.IsEnabled = true;
                pb.Visibility = Visibility.Collapsed;
                // 后台静默刷新到最新值（若期间有禁用/恢复操作改变了状态），拿到后覆盖一次
                new Thread(() =>
                {
                    try { Defender.RefreshStatusCache(); }
                    catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
                    try { disp.Invoke(() =>
                    {
                        BuildDefenderStatus();
                        RebuildDefenderButtons();
                        SyncDefToggles(false);
                    }); } catch { /* 窗口已关闭，忽略 */ }
                }) { IsBackground = true, Name = "DefenderRefreshSilent" }.Start();
            }
            else
            {
                bool seeded = false;
                try { seeded = Defender.SeedCacheFromPrefs(); }
                catch (Exception ex) { DebugLog.Ignore(ex); }
                if (seeded)
                {
                    // 首屏初值已同步：立即渲染"实时保护已禁用/正常"（0 延迟，与切页同步），
                    // 后台再 RefreshStatusCache 取全部 4 个真实值覆盖一次（云保护/样本提交由默认校正为准确）
                    defStatusHost.Children.Remove(defLoading);
                    BuildDefenderStatus();
                    RebuildDefenderButtons();
                    SyncDefToggles(false);
                    bClear.IsEnabled = true;
                    bDiag.IsEnabled = true;
                    pb.Visibility = Visibility.Collapsed;
                    new Thread(() =>
                    {
                        try { Defender.RefreshStatusCache(); }
                        catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
                        try { disp.Invoke(() =>
                        {
                            BuildDefenderStatus();
                            RebuildDefenderButtons();
                            SyncDefToggles(false);
                        }); } catch { /* 窗口已关闭，忽略 */ }
                    }) { IsBackground = true, Name = "DefenderSeedCorrect" }.Start();
                }
                else
                {
                    // Prefs 读不到（Defender 从未被改过）：保留 loading 占位，后台 PowerShell 拉取后填充
                    new Thread(() =>
                    {
                        try { Defender.RefreshStatusCache(); }
                        catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
                        try { disp.Invoke(() =>
                        {
                            defStatusHost.Children.Remove(defLoading);
                            BuildDefenderStatus();
                            RebuildDefenderButtons();
                            SyncDefToggles(false);
                            bClear.IsEnabled = true;
                            bDiag.IsEnabled = true;
                            pb.Visibility = Visibility.Collapsed;
                        }); } catch { /* 窗口已关闭，忽略 */ }
                    }) { IsBackground = true, Name = "DefenderInitLoader" }.Start();
                }
            }

            root.Children.Add(defCard);

            // ===== 中：Windows Defender 防火墙卡片 =====
            var fwCard = Card();
            var fwInner = (StackPanel)fwCard.Child;
            fwInner.Children.Add(new Emoji.Wpf.TextBlock { Text = "🛡 Windows Defender 防火墙", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });

            // 状态区（异步加载）
            var fwStatusHost = new StackPanel();
            fwInner.Children.Add(fwStatusHost);
            fwStatusHost.Children.Add(new TextBlock { Text = "正在检测防火墙状态…", Foreground = _textDim, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) });

            var fwProfileMap = new Dictionary<string, string> { ["Domain"] = "域", ["Private"] = "专用", ["Public"] = "公用" };
            void BuildFirewallStatus(List<FirewallCore.ProfileInfo> preset = null)
            {
                fwStatusHost.Children.Clear();
                var profiles = preset ?? FirewallCore.GetProfiles();
                if (profiles == null || profiles.Count == 0)
                {
                    fwStatusHost.Children.Add(new Emoji.Wpf.TextBlock { Text = "🚨 未能读取防火墙状态（请查看下方日志了解具体原因）", Foreground = _warnOrange, FontSize = 13, TextWrapping = TextWrapping.Wrap });
                    return;
                }
                foreach (var p in profiles)
                {
                    var cn = fwProfileMap.ContainsKey(p.Name) ? fwProfileMap[p.Name] : p.Name;
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.Children.Add(new TextBlock { Text = cn + " 配置文件", Foreground = _textMain, VerticalAlignment = VerticalAlignment.Center });
                    var tbState = new Emoji.Wpf.TextBlock
                    {
                        Text = p.Enabled ? "● 已开启" : "○ 已关闭",
                        Foreground = p.Enabled ? _successGreen : _warnOrange,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(tbState, 1);
                    row.Children.Add(tbState);
                    fwStatusHost.Children.Add(row);
                }
            }

            // 操作按钮行：打开高级安全 + 刷新状态
            var fwBtnRow = new Grid { Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            fwBtnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fwBtnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bOpenFw = Btn("🔧 打开高级安全", false, () => FirewallCore.OpenAdvanced());
            bOpenFw.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(bOpenFw, 0);
            fwBtnRow.Children.Add(bOpenFw);
            var bRefreshFw = Btn("🔄 刷新状态", false, null);
            bRefreshFw.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(bRefreshFw, 1);
            fwBtnRow.Children.Add(bRefreshFw);
            fwInner.Children.Add(fwBtnRow);

            // 规则管理面板
            fwInner.Children.Add(new Emoji.Wpf.TextBlock { Text = "🔧 防火墙规则管理", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 13, Margin = new Thickness(0, 10, 0, 6) });

            var TELEMETRY_HOSTS = new[] { "vortex-win.data.microsoft.com", "settings-win.data.microsoft.com", "watson.telemetry.microsoft.com", "telemetry.microsoft.com", "oca.telemetry.microsoft.com" };

            // 添加常用规则按钮行（4 列均分：阻止 SearchHost / 阻止遥测 / 移除 SearchHost / 移除选中）
            var ruleAddBar = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            ruleAddBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ruleAddBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ruleAddBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ruleAddBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bBlockSearch = Btn("➕ 阻止 SearchHost 联网", false, null);
            bBlockSearch.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(bBlockSearch, 0);
            ruleAddBar.Children.Add(bBlockSearch);
            var bBlockTele = Btn("➕ 阻止遥测域", false, null);
            bBlockTele.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(bBlockTele, 1);
            ruleAddBar.Children.Add(bBlockTele);
            var bRemoveSearch = Btn("➖ 移除 SearchHost 规则", false, null);
            bRemoveSearch.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(bRemoveSearch, 2);
            ruleAddBar.Children.Add(bRemoveSearch);
            var bRemoveSel = Btn("🗑 移除选中规则", false, null);
            bRemoveSel.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(bRemoveSel, 3);
            ruleAddBar.Children.Add(bRemoveSel);
            fwInner.Children.Add(ruleAddBar);

            // 规则列表 DataTemplate：单 TextBlock 绑整个对象（自动 ToString「名称 [入站/允许]」）。
            // TextWrapping=Wrap 使超长规则名自动折两行、短名单行；绑整对象绕过「字段绑定空白」坑，内容始终可见。
            var ruleItemTemplate = new DataTemplate();
            var ruleTb = new FrameworkElementFactory(typeof(TextBlock));
            ruleTb.SetValue(TextBlock.TextProperty, new Binding());
            ruleTb.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            ruleTb.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            ruleTb.SetValue(TextBlock.MarginProperty, new Thickness(0, 1, 0, 1));
            ruleItemTemplate.VisualTree = ruleTb;

            var ruleList = new System.Windows.Controls.ListBox
            {
                Background = Brushes.Transparent,
                Foreground = _textMain,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 8, 0, 0),
                MaxHeight = 180,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemTemplate = ruleItemTemplate
            };
            // 强制禁用水平滚动（DependencyProperty SetValue 绕过附加属性的"中间层"）
            ruleList.SetValue(System.Windows.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            fwInner.Children.Add(ruleList);

            // 空状态提示：若 PowerShell 执行失败，真实错误会输出到日志，这里不再盲目归因于权限
            var ruleEmptyHint = new TextBlock
            {
                Text = "未获取到防火墙规则（请查看下方日志了解具体原因）。",
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00)),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed
            };
            fwInner.Children.Add(ruleEmptyHint);

            void LoadFirewallData()
            {
                pb.Visibility = Visibility.Visible;
                var d = Dispatcher;
                new Thread(() =>
                {
                    // 后台线程经 Dispatcher 封送写日志，避免跨线程访问 UI；FirewallCore 内部已兜底，此处不再静默吞错
                    Action<string> flog = s => { try { d.Invoke(() => log.AppendText("[防火墙] " + s + "\r\n")); } catch { /* 窗口已关闭，忽略 */ } };
                    var profiles = FirewallCore.GetProfiles(flog);
                    var rules = FirewallCore.ListRules(flog);
                    try { d.Invoke(() =>
                    {
                        BuildFirewallStatus(profiles);
                        var ruleSrc = rules ?? new List<FirewallCore.RuleInfo>();
                        ruleList.ItemsSource = ruleSrc;
                        ruleEmptyHint.Visibility = ruleSrc.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                        pb.Visibility = Visibility.Collapsed;
                    }); } catch { /* 窗口已关闭，忽略 */ }
                }) { IsBackground = true, Name = "FirewallLoader" }.Start();
            }

            bRefreshFw.Click += (s, e) => LoadFirewallData();
            bBlockSearch.Click += (s, e) =>
            {
                // fix-CB：此前添加规则直接写防火墙、无确认弹窗。增加确认，说明将新增出站阻止规则及影响。
                if (MessageBox.Show("确定要添加「阻止 SearchHost 联网」出站规则吗？\n\n• 将新增防火墙出站阻止规则，阻止 SearchHost.exe 联网\n• 不影响已联网连接，后续联网请求将被拦截\n• 可通过「移除 SearchHost 规则」恢复\n\n是否继续？", "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
                if (!OperationLock.TryEnter("添加防火墙规则", out string busyBy))
                {
                    MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                pb.Visibility = Visibility.Visible;
                RunInBg(log, PrivacyCore.AddSearchFirewallRule, "已添加阻止 SearchHost 规则", () => { OperationLock.Exit(); pb.Visibility = Visibility.Collapsed; LoadFirewallData(); });
            };
            bBlockTele.Click += (s, e) =>
            {
                // fix-CB：此前添加规则直接写防火墙、无确认弹窗。增加确认，说明将新增出站阻止规则及影响。
                if (MessageBox.Show("确定要添加「阻止遥测域」出站规则吗？\n\n• 将新增防火墙出站阻止规则，阻止已知 Windows 遥测域名连接\n• 可能影响依赖这些域名的功能或软件更新\n• 可在规则列表中选中后「移除选中规则」恢复\n\n是否继续？", "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
                if (!OperationLock.TryEnter("添加防火墙规则", out string busyBy))
                {
                    MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l => FirewallCore.AddBlockAddressRule("阻止Windows遥测域", TELEMETRY_HOSTS, l), "已添加阻止遥测域规则", () => { OperationLock.Exit(); pb.Visibility = Visibility.Collapsed; LoadFirewallData(); });
            };
            bRemoveSearch.Click += (s, e) =>
            {
                // 删除类操作：需确认
                if (MessageBox.Show("确定要移除「SearchHost」防火墙规则吗？\n\n移除后 Windows Search 将恢复联网。", "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
                if (!OperationLock.TryEnter("移除防火墙规则", out string busyBy))
                {
                    MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                pb.Visibility = Visibility.Visible;
                RunInBg(log, PrivacyCore.RemoveSearchFirewallRule, "已移除 SearchHost 规则", () => { OperationLock.Exit(); pb.Visibility = Visibility.Collapsed; LoadFirewallData(); });
            };
            bRemoveSel.Click += (s, e) =>
            {
                var src = ruleList.ItemsSource as System.Collections.IList;
                if (src == null || src.Count == 0)
                {
                    System.Windows.MessageBox.Show(this, "未获取到防火墙规则列表。请查看页面下方日志了解具体原因，若提示访问被拒绝则需以管理员身份运行程序。", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
                var sel = ruleList.SelectedItem as FirewallCore.RuleInfo;
                if (sel == null) { System.Windows.MessageBox.Show(this, "请先在列表中选择一条规则", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning); return; }
                // 删除类操作：需确认
                if (MessageBox.Show("确定要移除防火墙规则「" + sel.DisplayName + "」吗？\n\n此操作不可撤销，将删除该规则。", "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
                if (!OperationLock.TryEnter("移除防火墙规则", out string busyBy))
                {
                    MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l => FirewallCore.RemoveRule(sel.DisplayName, l), "已移除规则: " + sel.DisplayName, () => { OperationLock.Exit(); pb.Visibility = Visibility.Collapsed; LoadFirewallData(); });
            };

            root.Children.Add(fwCard);
            // 打开页面时静默加载防火墙状态与规则列表
            LoadFirewallData();

            // ===== 下：Windows 更新管理卡片 =====
            var updCard = Card();
            var updInner = (StackPanel)updCard.Child;
            updInner.Children.Add(new TextBlock { Text = "⬇ Windows 更新", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });

            // Windows 更新卡片状态改为异步加载：避免切页时同步调用 reg.exe / PowerShell 阻塞 UI。
            // 先以默认值渲染骨架按钮，后台线程读取真实状态后再刷新高亮。
            var updateState = (blocked: false, paused: false, metered: false);

            void LoadUpdateState()
            {
                var d = Dispatcher;
                new Thread(() =>
                {
                    bool b = false, p = false, m = false;
                    try { b = Updater.IsUpdatesBlocked(); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
                    try { p = Updater.IsLongPaused(); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
                    try { m = MeteredConnection.IsMetered(); } catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
                    try { d.Invoke(() =>
                    {
                        updateState = (b, p, m);
                        RebuildUpdateButtons();
                    }); } catch { /* 窗口已关闭，忽略 */ }
                }) { IsBackground = true, Name = "UpdateStateLoader" }.Start();
            }

            bool ShouldFill(string actionKey, bool stateDefault)
            {
                if (_lastUpdateAction != null)
                    return _lastUpdateAction == actionKey;
                return stateDefault;
            }

            var updateBtnHost = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            for (int ci = 0; ci < 6; ci++)
                updateBtnHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            updInner.Children.Add(updateBtnHost);

            // 更新操作：写操作完成后重新异步读取真实状态并刷新按钮高亮；读操作保留日志内容。
            void RunUpdate(string actionKey, Action<Action<string>> work, string label, bool navWhenDone = true)
            {
                if (!OperationLock.TryEnter("更新管理", out string busyBy))
                {
                    MessageBox.Show("已有" + busyBy + "操作正在运行，请先完成再执行。", "操作冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                _lastUpdateAction = actionKey;
                pb.Visibility = Visibility.Visible;
                RunInBg(log, work, label, () =>
                {
                    OperationLock.Exit();
                    pb.Visibility = Visibility.Collapsed;
                    if (navWhenDone) LoadUpdateState();
                });
            }

            void RebuildUpdateButtons()
            {
                updateBtnHost.Children.Clear();
                void AddBtn(string text, bool primary, Action onClick, int col)
                {
                    var b = Btn(text, primary, onClick);
                    b.HorizontalAlignment = HorizontalAlignment.Center;   // 按钮保持原始大小，居中于列
                    b.Margin = new Thickness(0);
                    Grid.SetColumn(b, col);
                    updateBtnHost.Children.Add(b);
                }
                AddBtn("禁用更新", ShouldFill("block", updateState.blocked), () => RunUpdate("block", Updater.BlockUpdates, "已禁用更新"), 0);
                AddBtn("恢复更新", ShouldFill("restore", !updateState.blocked), () => RunUpdate("restore", Updater.RestoreUpdates, "已恢复更新"), 1);
                AddBtn("长期暂停(10000天)", ShouldFill("pause", updateState.paused), () => RunUpdate("pause", Updater.AllowLongPause, "已设置长期暂停"), 2);
                AddBtn("查看更新状态", ShouldFill("status", false), () => RunUpdate("status", Updater.UpdateStatus, "状态已刷新", false), 3);
                AddBtn("计量连接 · 切换", ShouldFill("metered-toggle", updateState.metered), () => RunUpdate("metered-toggle", MeteredConnection.ToggleMetered, "计量连接已切换"), 4);
                AddBtn("计量连接 · 状态", ShouldFill("metered-status", false), () => RunUpdate("metered-status", MeteredConnection.MeteredStatus, "状态已刷新", false), 5);
            }
            RebuildUpdateButtons();
            LoadUpdateState();

            root.Children.Add(updCard);
            root.Children.Add(pb);
            root.Children.Add(logBorder);

            // ---- 页面级缓存：首次构建完成后缓存整页；再次进入复用并仅刷新动态状态 ----
            // 仅在构建期间主题未变时写入缓存（避免把混入旧主题刷子的页面标记为可复用）
            if (buildDark == _isDarkMode)
            {
                _securityCache.Set(root, buildDark);
                _securityCache.SetRefresh(() =>
                {
                    // 复位动态状态（与每次新建页面行为一致）：
                    // 清空日志、复位 Defender 按钮「最后点击」高亮与底部策略按钮高亮；
                    // 后台重刷 Defender 状态缓存后就地重绘状态区/开关/一键按钮，另重刷防火墙与更新状态
                    log.Clear();
                    _lastDefAction = null;
                    ApplyPolicyMode(null);
                    pb.Visibility = Visibility.Visible;
                    var dispR = Dispatcher;
                    new Thread(() =>
                    {
                        try { Defender.RefreshStatusCache(); }
                        catch (Exception caughtEx) { DebugLog.Ignore(caughtEx); }
                        try { dispR.Invoke(() =>
                        {
                            pb.Visibility = Visibility.Collapsed;
                            BuildDefenderStatus();
                            RebuildDefenderButtons();
                            SyncDefToggles(false);
                        }); } catch { /* 窗口已关闭，忽略 */ }
                    }) { IsBackground = true, Name = "SecurityRefreshLoader" }.Start();
                    RefreshTpStatus();   // 重刷 TP 状态（TP 只能外部改，重进页必须重读；此前漏掉导致"只有重启软件才刷新"）
                    LoadFirewallData();  // 重刷防火墙配置文件状态 + 规则列表（含空状态提示）
                    LoadUpdateState();   // 重刷 Windows 更新按钮高亮（保留 _lastUpdateAction 字段语义）
                });
            }

            return root;
        }

    }
}

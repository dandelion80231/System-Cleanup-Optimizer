using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
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

        private UIElement BuildSecurity()
        {
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
            defInner.Children.Add(new TextBlock { Text = "🛡 Windows Defender", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });

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
                bool allOn = !policyOff;
                bool allOff = policyOff;
                bool fullyOk = (allOn || allOff) && Defender.LastOperationFullSuccess;
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

                var note = new TextBlock
                {
                    Text = allOff
                        ? "提示：下方 5 个开关可单独微调（无需重启）。⚠ 请勿重启——Windows 11 24H2+ 重启会还原 Defender 配置。恢复请点击右侧「一键恢复 WD」。"
                        : "提示：下方 5 个开关可单独切换（无需重启）。",
                    Foreground = fullyOk ? _textMain : _warnOrange,
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                defStatusHost.Children.Add(note);
            }

            // 等宽均分整行：Grid(2×★Star) + 按钮居中、保持原始大小（与安全防护更新按钮行一致）
            var defWp = new Grid { Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            defWp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            defWp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            defInner.Children.Add(defWp);

            // 前移 defToggles 声明：bDisable/bEnable 的 onDone 闭包会调 SyncDefToggles，
            // SyncDefToggles 内部要引用 defToggles——必须先声明
            var defToggles = new StackPanel { Margin = new Thickness(0, 10, 0, 4) };
            defInner.Children.Add(defToggles);

            // Defender 按钮：填充状态与上方状态区同步（参考更新管理 RebuildUpdateButtons 模式）
            // 填充规则：哪个按钮代表"当前实际状态"，哪个就填充；点击后最后操作的按钮也填充
            string _lastDefAction = null;
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
                    _lastDefAction = "disable";
                    RebuildDefenderButtons(); // 立即刷新高亮，给点击反馈
                    pb.Visibility = Visibility.Visible;
                    RunInBg(log, Defender.Disable, "已禁用 Defender", () => { pb.Visibility = Visibility.Collapsed; SyncDefToggles(); BuildDefenderStatus(); RebuildDefenderButtons(); });
                });
                bDisable.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(bDisable, 0);
                defWp.Children.Add(bDisable);
                var bEnable = Btn("✔ 一键恢复 WD", ShouldFillDef("restore", !disabled), () =>
                {
                    _lastDefAction = "restore";
                    RebuildDefenderButtons(); // 立即刷新高亮，给点击反馈
                    pb.Visibility = Visibility.Visible;
                    RunInBg(log, Defender.Enable, "已启用 Defender", () => { pb.Visibility = Visibility.Collapsed; SyncDefToggles(); BuildDefenderStatus(); RebuildDefenderButtons(); });
                });
                bEnable.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(bEnable, 1);
                defWp.Children.Add(bEnable);
            }

            // ============ 5 个独立 Defender 开关（每个 Get/Set 实时同步） ============
            // 用 PowerShell Set-MpPreference 官方 API，立即生效、不需要重启、不需要 TI 提权。
            // TP 开启时部分选项（云保护/样本提交/TP 本身）会被拦，UI 会在异步回调时回滚到 Get* 当前值。
            // 注：defToggles 已在上面声明（bDisable/bEnable 闭包需要）
            // mkTog 放在 SyncDefToggles 函数体内（避免 click lambda 与 SyncDefToggles 互相引用的位置依赖）
            void SyncDefToggles(bool refreshCache = true)
            {
                // 重建前刷一次缓存（Set 后值变了，缓存可能过期）；初始加载时已在后台刷好，传 false 避免重复阻塞 UI
                if (refreshCache) Defender.RefreshStatusCache();
                defToggles.Children.Clear();
                System.Func<string, Func<bool>, Action<bool, Action<string>>, System.Windows.Controls.CheckBox> mkTog = (label, getState, setter) =>
                {
                    bool initial = false;
                    try { initial = getState(); } catch { }
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
                        bool target = chk.IsChecked == true;
                        pb.Visibility = Visibility.Visible;
                        RunInBg(log, l => setter(target, l), (target ? "已启用 " : "已禁用 ") + label,
                            () =>
                            {
                                pb.Visibility = Visibility.Collapsed;
                                SyncDefToggles();         // 重新读 Get* 刷新 toggle（Set 失败时自动回滚）
                                BuildDefenderStatus();    // 刷新"当前状态"行
                                RebuildDefenderButtons(); // 刷新一键禁用/恢复按钮填充
                            });
                    };
                    return chk;
                };

                defToggles.Children.Add(mkTog("实时保护（含开发人员驱动的保护）",
                    () => Defender.GetRealtime(), (b, l) => Defender.SetRealtime(b, l)));
                defToggles.Children.Add(mkTog("行为监控",
                    () => Defender.GetBehavior(), (b, l) => Defender.SetBehavior(b, l)));
                defToggles.Children.Add(mkTog("云提供的保护",
                    () => Defender.GetCloud(), (b, l) => Defender.SetCloud(b, l)));
                defToggles.Children.Add(mkTog("自动提交样本",
                    () => Defender.GetSampleSubmit(), (b, l) => Defender.SetSampleSubmit(b, l)));
                defToggles.Children.Add(mkTog("篡改防护（关后其它被锁开关才可改）",
                    () => Defender.GetTamper(), (b, l) => Defender.SetTamper(b, l)));
            }

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
                ApplyPolicyMode(bClear);
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l => Defender.ClearAllPolicies(l), "策略已清理", () =>
                {
                    pb.Visibility = Visibility.Collapsed;
                    SyncDefToggles();
                    BuildDefenderStatus();
                    RebuildDefenderButtons();
                });
            };
            bDiag.Click += (s, e) =>
            {
                ApplyPolicyMode(bDiag);
                pb.Visibility = Visibility.Visible;
                RunInBg(log, Defender.DiagnoseRuntime, "诊断完成", () =>
                {
                    pb.Visibility = Visibility.Collapsed;
                });
            };

            // 默认不高亮底部动作按钮
            ApplyPolicyMode(null);

            // 后台一次性拉取 Defender 状态，避免切页卡顿；UI 先显示骨架，缓存好后瞬间填充
            pb.Visibility = Visibility.Visible;
            var disp = Dispatcher;
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

            root.Children.Add(defCard);

            // ===== 中：Windows Defender 防火墙卡片 =====
            var fwCard = Card();
            var fwInner = (StackPanel)fwCard.Child;
            fwInner.Children.Add(new TextBlock { Text = "🛡 Windows Defender 防火墙", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });

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
                    fwStatusHost.Children.Add(new TextBlock { Text = "⚠ 未能读取防火墙状态（请查看下方日志了解具体原因）", Foreground = _warnOrange, FontSize = 13, TextWrapping = TextWrapping.Wrap });
                    return;
                }
                foreach (var p in profiles)
                {
                    var cn = fwProfileMap.ContainsKey(p.Name) ? fwProfileMap[p.Name] : p.Name;
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.Children.Add(new TextBlock { Text = cn + " 配置文件", Foreground = _textMain, VerticalAlignment = VerticalAlignment.Center });
                    var tbState = new TextBlock
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
            fwInner.Children.Add(new TextBlock { Text = "🔧 防火墙规则管理", FontWeight = FontWeights.Bold, Foreground = _accent, FontSize = 13, Margin = new Thickness(0, 10, 0, 6) });

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

            // 规则列表
            var ruleList = new System.Windows.Controls.ListBox
            {
                Background = Brushes.Transparent,
                Foreground = _textMain,
                BorderBrush = _panelBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 8, 0, 0),
                MaxHeight = 180
            };
            var ruleScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = ruleList, MaxHeight = 180 };
            fwInner.Children.Add(ruleScroll);

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
                pb.Visibility = Visibility.Visible;
                RunInBg(log, PrivacyCore.AddSearchFirewallRule, "已添加阻止 SearchHost 规则", () => { pb.Visibility = Visibility.Collapsed; LoadFirewallData(); });
            };
            bBlockTele.Click += (s, e) =>
            {
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l => FirewallCore.AddBlockAddressRule("阻止Windows遥测域", TELEMETRY_HOSTS, l), "已添加阻止遥测域规则", () => { pb.Visibility = Visibility.Collapsed; LoadFirewallData(); });
            };
            bRemoveSearch.Click += (s, e) =>
            {
                pb.Visibility = Visibility.Visible;
                RunInBg(log, PrivacyCore.RemoveSearchFirewallRule, "已移除 SearchHost 规则", () => { pb.Visibility = Visibility.Collapsed; LoadFirewallData(); });
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
                pb.Visibility = Visibility.Visible;
                RunInBg(log, l => FirewallCore.RemoveRule(sel.DisplayName, l), "已移除规则: " + sel.DisplayName, () => { pb.Visibility = Visibility.Collapsed; LoadFirewallData(); });
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
            void RunUpdate(Action<Action<string>> work, string label, bool navWhenDone = true)
            {
                pb.Visibility = Visibility.Visible;
                RunInBg(log, work, label, () =>
                {
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
                AddBtn("禁用更新", ShouldFill("block", updateState.blocked), () => { _lastUpdateAction = "block"; RunUpdate(Updater.BlockUpdates, "已禁用更新"); }, 0);
                AddBtn("恢复更新", ShouldFill("restore", !updateState.blocked), () => { _lastUpdateAction = "restore"; RunUpdate(Updater.RestoreUpdates, "已恢复更新"); }, 1);
                AddBtn("长期暂停(10000天)", ShouldFill("pause", updateState.paused), () => { _lastUpdateAction = "pause"; RunUpdate(Updater.AllowLongPause, "已设置长期暂停"); }, 2);
                AddBtn("查看更新状态", ShouldFill("status", false), () => { _lastUpdateAction = "status"; RunUpdate(Updater.UpdateStatus, "状态已刷新", false); }, 3);
                AddBtn("计量连接 · 切换", ShouldFill("metered-toggle", updateState.metered), () => { _lastUpdateAction = "metered-toggle"; RunUpdate(MeteredConnection.ToggleMetered, "计量连接已切换"); }, 4);
                AddBtn("计量连接 · 状态", ShouldFill("metered-status", false), () => { _lastUpdateAction = "metered-status"; RunUpdate(MeteredConnection.MeteredStatus, "状态已刷新", false); }, 5);
            }
            RebuildUpdateButtons();
            LoadUpdateState();

            root.Children.Add(updCard);
            root.Children.Add(pb);
            root.Children.Add(logBorder);
            return root;
        }

        private static bool CheckServiceExists(string name)
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name, false)) return k != null; }
            catch { return false; }
        }

        /// <summary>查询服务进程是否真正在运行（使用 ServiceController，非注册表值）</summary>
        private static bool CheckServiceRunning(string name)
        {
            try { return new ServiceController(name).Status == ServiceControllerStatus.Running; }
            catch { return false; }
        }

        private static bool ServiceStartDisabled(string name)
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name, false)) { var v = k?.GetValue("Start"); return v != null && (int)v == 4; } }
            catch { return false; }
        }

        private static bool CheckTamperProtection()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender\Features", false))
                { var v = k?.GetValue("TamperProtection"); return v != null && ((int)v == 1 || (int)v == 5); }
            }
            catch { return false; }
        }
    }
}

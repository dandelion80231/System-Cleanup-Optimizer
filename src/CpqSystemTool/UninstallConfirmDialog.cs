using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CpqSystemTool
{
    /// <summary>
    /// 「确认卸载 Office 及关联组件」二次确认弹窗（替换原 MessageBox 的 Yes/No）。
    /// 提供三个可勾选选项：Office（全量，始终显示）、OneDrive、Teams（后两者仅在本机已安装时显示）。
    /// 视觉复用主界面圆角阴影卡（DialogChrome.Apply），与 InstallPathDialog 同源。
    /// 偏好（OneDrive / Teams 勾选状态）由调用方读写 uninstall_prefs.json，本弹窗只负责收集本次勾选。
    /// </summary>
    internal class UninstallConfirmDialog : Window
    {
        public bool UninstallOffice { get; private set; }
        public bool UninstallOneDrive { get; private set; }
        public bool UninstallTeams { get; private set; }
        /// <summary>用户是否勾选卸载「新版 Outlook（Store/AppX 应用）」。仅当本机检测到新版时显示；默认勾选 = 卸载。</summary>
        public bool UninstallNewOutlook { get; private set; }

        private readonly CheckBox _officeChk;
        private readonly CheckBox _oneDriveChk;
        private readonly CheckBox _teamsChk;
        private readonly CheckBox _newOutlookChk;

        /// <param name="owner">主窗口引用，用于读取主题颜色并复用 Btn 基元。</param>
        /// <param name="officeInstalled">本机是否检测到 Office（决定 Office 项是否可用/默认勾选）。</param>
        /// <param name="oneDriveInstalled">本机是否检测到 OneDrive 客户端（决定该选项是否显示；可能是 ODT 套件自带或独立手装）。</param>
        /// <param name="teamsInstalled">本机是否检测到 Teams（决定该选项是否显示）。</param>
        /// <param name="oneDriveDefault">OneDrive 项默认勾选（本机检测到 OneDrive 客户端→默认勾；未检测到→默认不勾；一旦做过选择即被记住）。</param>
        /// <param name="teamsDefault">Teams 项默认勾选（来自上次持久化偏好）。</param>
        public UninstallConfirmDialog(MainWindow owner,
            bool officeInstalled, bool oneDriveInstalled, bool teamsInstalled,
            bool newOutlookInstalled,
            bool oneDriveDefault, bool teamsDefault)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Width = 540;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            var fg = owner?._textMain ?? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
            var dim = owner?._textDim ?? new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            var panelBorder = owner?._panelBorder ?? new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x3C));
            var windowBg = owner?._windowBg ?? new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x1E));
            var warn = owner?._warnOrange ?? new SolidColorBrush(Color.FromRgb(0xB4, 0x5A, 0x22));
            var danger = owner?._dangerRed ?? new SolidColorBrush(Color.FromRgb(0xE5, 0x4D, 0x4D));

            Background = Brushes.Transparent;
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) DialogResult = false; };

            DialogChrome.Apply(this, owner);

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

            stack.Children.Add(DialogChrome.BuildTitleBar(this, "确认卸载 Office 及关联组件", fg, dim, danger, panelBorder));

            var body = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            body.Children.Add(new TextBlock
            {
                Text = "此操作将卸载下方「已勾选且本机已安装」的组件，并清理各自残留目录与注册表，不可恢复。\n（仅勾选但实际未安装的组件会被自动跳过。）",
                FontSize = 12,
                Foreground = dim,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
                Margin = new Thickness(0, 0, 0, 14)
            });

            // Office（全量）：始终显示；本机已安装则默认勾选，未检测到则禁用并提示跳过
            _officeChk = new CheckBox
            {
                Content = officeInstalled ? "卸载 Office（全量）" : "卸载 Office（未检测到本机 Office，将自动跳过）",
                FontSize = 12.5,
                Foreground = fg,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8),
                IsChecked = officeInstalled,
                IsEnabled = officeInstalled
            };
            body.Children.Add(_officeChk);

            // 卸载新版 Outlook（Store/AppX 应用，仅本机已安装时显示，默认勾选 = 卸载）
            if (newOutlookInstalled)
            {
                _newOutlookChk = new CheckBox
                {
                    Content = "卸载新版 Outlook（Microsoft Store 应用，C2R 套件无法管理，需在此单独移除）",
                    FontSize = 12.5,
                    Foreground = fg,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 8),
                    IsChecked = true
                };
                body.Children.Add(_newOutlookChk);
            }

            // OneDrive（仅本机检测到客户端时显示；本机存在即默认勾选，全量卸载删除本机实际存在的组件）
            if (oneDriveInstalled)
            {
                _oneDriveChk = new CheckBox
                {
                    Content = "卸载 OneDrive（本机检测到 OneDrive 客户端，保留同步数据目录）",
                    FontSize = 12.5,
                    Foreground = fg,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 8),
                    IsChecked = oneDriveDefault
                };
                body.Children.Add(_oneDriveChk);
            }

            // Teams（仅本机已安装时显示）
            if (teamsInstalled)
            {
                _teamsChk = new CheckBox
                {
                    Content = "卸载 Teams（机器级安装器 / 经典每用户版 / 新商店版）",
                    FontSize = 12.5,
                    Foreground = fg,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 8),
                    IsChecked = teamsDefault
                };
                body.Children.Add(_teamsChk);
            }

            body.Children.Add(new TextBlock
            {
                Text = BuildNoteText(officeInstalled, oneDriveInstalled, teamsInstalled, newOutlookInstalled, oneDriveDefault, teamsDefault),
                FontSize = 11,
                Foreground = warn,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 16,
                Margin = new Thickness(0, 0, 0, 14)
            });

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancelBtn = owner != null ? owner.Btn("取消", false, () => { DialogResult = false; }, 110)
                                           : new Button { Content = "取消", Width = 110 };
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            var okBtn = owner != null ? owner.Btn("确认卸载", true, () =>
            {
                UninstallOffice = _officeChk.IsChecked == true;
                UninstallOneDrive = _oneDriveChk != null && _oneDriveChk.IsChecked == true;
                UninstallTeams = _teamsChk != null && _teamsChk.IsChecked == true;
                UninstallNewOutlook = _newOutlookChk != null && _newOutlookChk.IsChecked == true;
                DialogResult = true;
            }, 120) : new Button { Content = "确认卸载", Width = 120 };
            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(okBtn);
            body.Children.Add(btnRow);

            stack.Children.Add(body);
            root.Child = stack;
            Content = root;
        }

        /// <summary>按本机实际显示项生成说明文案（只提及真实存在的组件）。</summary>
        private static string BuildNoteText(bool officeInstalled, bool oneDriveInstalled, bool teamsInstalled,
            bool newOutlookInstalled, bool oneDriveDefault, bool teamsDefault)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("说明：");
            sb.Append(officeInstalled
                ? "Office 默认勾选（按钮本意）。"
                : "本机未检测到 Office，该项为完整展示，将自动跳过。");
            if (newOutlookInstalled)
                sb.Append(" 新版 Outlook（Store 应用）默认勾选（卸载）；此选择不被记住，下次仍默认勾选。");
            if (oneDriveInstalled)
                sb.Append(" OneDrive 默认" + (oneDriveDefault ? "勾选" : "不勾选") + "（本机检测到客户端）；" + (oneDriveDefault ? "取消勾选" : "勾选") + "会被记住，下次默认" + (oneDriveDefault ? "不勾" : "勾") + "。" + " ");
            if (teamsInstalled)
                sb.Append(" Teams 默认" + (teamsDefault ? "勾选" : "不勾选") + "（沿用你上次的选择）。" + " ");
            if (!newOutlookInstalled && !oneDriveInstalled && !teamsInstalled)
                sb.Append("本机未检测到 OneDrive / Teams / 新版 Outlook，本次仅卸载 Office。");
            return sb.ToString().TrimEnd();
        }
    }
}

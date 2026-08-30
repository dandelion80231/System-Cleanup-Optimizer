using System;
using System.Windows;
using System.Windows.Controls;

namespace CpqSystemTool
{
    public partial class MainWindow
    {
        // =====================================================================
        //  Module: 驱动清理（Driver Store 管理）
        // =====================================================================

        // 缓存驱动清理页根节点，实现「启动后预加载、点击即呈现」。
        // DriverStorePanel 构造时会自动后台 Refresh()，用户切到该页时数据已就绪。
        private UIElement _cachedDriverStoreRoot;
        private DriverStorePanel _driverStorePanel;

        /// <summary>预加载驱动清理页：构造并缓存页面根节点，触发一次后台枚举（BuildDriverStore 本身幂等）。</summary>
        private void PreloadDriverStore()
        {
            BuildDriverStore();
            _driverStorePanel?.Refresh(); // 启动即后台枚举，用户切到该页时数据已就绪
        }

        /// <summary>清空驱动清理页缓存，使下次访问时重建（用于主题切换后应用新笔刷）。</summary>
        private void InvalidateDriverStoreCache() => _cachedDriverStoreRoot = null;

        private UIElement BuildDriverStore()
        {
            if (_cachedDriverStoreRoot != null) return _cachedDriverStoreRoot;

            // 说明文字放在圆角卡片【之外】（与维护工具页一致：说明在卡片上方，卡片内只放内容）
            // root 不再单独加边距，统一沿用 ContentArea 的 Margin(22,12,22,22)，
            // 使顶部副标题高度与清理优化页一致、圆角卡片底部贴近窗口（与 Appx 管理页一致）。
            var root = new Grid { Margin = new Thickness(0) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 说明文字（卡片外）
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 面板（卡片）主体撑满

            var header = Header("", "列出系统已安装的驱动包。标注「旧版可清」的多为该驱动的历史版本，可放心清理以节省空间；标注「在役·保护」的正在被设备使用，禁止删除；标注「启动关键」的若被删除可能导致系统无法启动，默认受保护。删除不可恢复，请按需勾选。");
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var panel = new DriverStorePanel(this, null);
            Grid.SetRow(panel, 1);
            root.Children.Add(panel);
            _driverStorePanel = panel;

            BindRootHeightToViewport(root);

            _cachedDriverStoreRoot = root;
            return root;
        }
    }
}

namespace CpqSystemTool
{
    /// <summary>
    /// 按钮「进行中」态助手（P1 抽冗余）：禁用 + 改标签，返回恢复委托。
    /// 把返回的 Action 作为 RunInBg 的 onDoneUi（或包一层）挂入——RunInBg 的成功/异常
    /// 两条路径都会调用 onDoneUi，按钮必然恢复，杜绝各页手写「异常路径忘了恢复按钮」的 bug 类。
    /// 驱动页 4 操作 / 维护页抓取 / Appx 卡片按钮原先各自手写 3 行样板，统一走这里。
    /// 原标签按 Mark 时捕获的字符串恢复（本项目 Btn() 一律字符串标签，安全）。
    /// </summary>
    public static class UiBusy
    {
        public static System.Action Mark(System.Windows.Controls.ContentControl c, string busyLabel)
        {
            var old = c.Content as string ?? "";
            c.IsEnabled = false;
            c.Content = busyLabel;
            return () => { c.IsEnabled = true; c.Content = old; };
        }
    }
}

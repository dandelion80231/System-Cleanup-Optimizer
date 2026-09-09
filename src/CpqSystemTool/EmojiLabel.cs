// 彩色 emoji 渲染统一入口 —— 基于 Emoji.Wpf 0.3.4（与 office-ui-prototype 同一方案）。
// WPF 文本管线不渲染彩色 emoji 字形；Emoji.Wpf.TextBlock 的 emoji 自动以原色图形渲染，
// 普通文字仍走 Foreground 画笔。本类收拢各处「含 emoji 的 TextBlock」创建点，避免散落全限定名。
using System;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace CpqSystemTool
{
    public static class EmojiLabel
    {
        /// <summary>
        /// 判定文本是否含 emoji 字符（彩色 emoji 主区间 U+1F300–U+1FAFF + 杂项符号 U+2600–U+27BF）。
        /// 用于按需选择控件：含 emoji 用 Emoji.Wpf.TextBlock，否则普通 TextBlock（避免无谓开销）。
        /// </summary>
        public static bool HasEmoji(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                ushort u = c;
                // BMP 区间内的杂项符号 / 星号 / VS16（0x2B50/0xFE0F 在 ushort 范围内有效）
                if ((u >= 0x2600 && u <= 0x27BF) || u == 0x2B50 || u == 0xFE0F)
                    return true;
                // 星表区（U+1F300–U+1FAFF）的彩色 emoji 在 C# 里是代理对（astral 平面），
                // 以 \uD83C-\uDB0F 高位 surrogate 出现，用下面区间兜住。
                if (u >= 0xD83C && u <= 0xDB0F) return true;
            }
            return false;
        }

        /// <summary>
        /// 创建「彩色 emoji + 普通文字混排」的 TextBlock。
        /// text 不含 emoji 时直接返回普通 TextBlock（零开销、行为与原有一致）；
        /// 含 emoji 时返回 Emoji.Wpf.TextBlock（emoji 原色图形，文字跟随 foreground / 继承）。
        /// </summary>
        public static System.Windows.Controls.TextBlock Create(
            string text,
            Brush foreground = null,
            double? fontSize = null,
            bool? bold = null,
            Thickness? margin = null,
            TextWrapping wrapping = TextWrapping.NoWrap,
            VerticalAlignment? verticalAlignment = null)
        {
            var tb = HasEmoji(text) ? new Emoji.Wpf.TextBlock() : new System.Windows.Controls.TextBlock();
            tb.Text = text;
            if (foreground != null) tb.Foreground = foreground;
            if (fontSize.HasValue) tb.FontSize = fontSize.Value;
            if (bold.HasValue) tb.FontWeight = bold.Value ? FontWeights.Bold : FontWeights.Normal;
            if (margin.HasValue) tb.Margin = margin.Value;
            if (wrapping != TextWrapping.NoWrap) tb.TextWrapping = wrapping;
            if (verticalAlignment.HasValue) tb.VerticalAlignment = verticalAlignment.Value;
            return tb;
        }
    }
}

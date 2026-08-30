#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
将 download.html 从「每个 panel 内部两栏」改为「页面级两栏」：
- 左侧卡片 .dl-main：版本标签 + 下载信息 panel
- 右侧卡片 .dl-changelog：「本版本更新」标题 + changelog panel
- 版本切换时左右联动（依赖 data-panel 属性）
"""
import re
from pathlib import Path

from _html_panels import find_balanced_end

SRC = Path(__file__).resolve().parent.parent / "site-src" / "download.html"


def extract_inner_content(lines, open_idx):
    """返回 div 开标签之后的内部内容行（不含开/闭标签本身）。"""
    close_idx = find_balanced_end(lines, open_idx)
    return lines[open_idx + 1:close_idx], close_idx


def convert():
    lines = SRC.read_text(encoding='utf-8').splitlines(keepends=True)
    out = []
    i = 0

    # 定位 .dl-card 容器
    card_open = None
    for idx, line in enumerate(lines):
        if 'class="dl-card reveal"' in line:
            card_open = idx
            break
    if card_open is None:
        raise ValueError("未找到 .dl-card reveal")
    card_close = find_balanced_end(lines, card_open)

    # 收集 dl-card 之前的内容
    out.extend(lines[:card_open])

    # 替换 dl-card 为 dl-layout
    card_line = lines[card_open]
    out.append(card_line.replace('class="dl-card reveal"', 'class="dl-layout reveal"'))

    # 在 dl-layout 内先放 dl-layout-row 开头和 dl-main 开头
    card_indent = re.match(r'^(\s*)', card_line).group(1)
    inner_indent = card_indent + '  '
    col_indent = inner_indent + '  '
    content_indent = col_indent + '  '

    out.append(inner_indent + '<div class="dl-layout-row">\n')
    out.append(col_indent + '<div class="dl-main">\n')

    # 处理 dl-card 内部：找到 dl-tabs、dl-panels、note、alt
    inner_lines = lines[card_open + 1:card_close]

    tabs_open = None
    tabs_close = None
    panels_open = None
    panels_close = None
    note_lines = []
    alt_lines = []

    for idx, line in enumerate(inner_lines):
        if 'class="dl-tabs"' in line and tabs_open is None:
            tabs_open = idx
            tabs_close = find_balanced_end(inner_lines, tabs_open)
        if 'class="dl-panels"' in line and panels_open is None:
            panels_open = idx
            panels_close = find_balanced_end(inner_lines, panels_open)

    if tabs_open is None or panels_open is None:
        raise ValueError("未找到 dl-tabs 或 dl-panels")

    # 处理完 panels 后，剩下的 note/alt 在 panels_close 之后
    for idx, line in enumerate(inner_lines[panels_close + 1:], start=panels_close + 1):
        if 'class="dl-note"' in line:
            note_lines.append(line)
        elif 'class="alt"' in line:
            alt_lines.append(line)

    tabs_lines = inner_lines[tabs_open:tabs_close + 1]

    # 输出 tabs（缩进增加一级）
    for line in tabs_lines:
        out.append(col_indent + line.lstrip())

    # 输出 dl-panels 开标签
    panels_open_line = inner_lines[panels_open]
    out.append(col_indent + panels_open_line.lstrip())

    # 解析每个 panel
    j = panels_open + 1
    download_panels = []
    chlog_panels = []
    while j < panels_close:
        line = inner_lines[j]
        if '<div class="dl-panel' in line and 'role="tabpanel"' in line:
            panel_open = j
            panel_close = find_balanced_end(inner_lines, panel_open)
            ver = re.search(r'data-panel="([^"]+)"', line).group(1)

            # 提取 panel 内部
            panel_inner = inner_lines[panel_open + 1:panel_close]

            # 找 left/right wrapper
            left_open = None
            right_open = None
            for k, l in enumerate(panel_inner):
                if 'class="dl-panel-left"' in l and left_open is None:
                    left_open = k
                if 'class="dl-panel-right"' in l and right_open is None:
                    right_open = k

            if left_open is None or right_open is None:
                raise ValueError(f"panel {ver} 缺少 left/right")

            left_content, _ = extract_inner_content(panel_inner, left_open)
            right_content, _ = extract_inner_content(panel_inner, right_open)

            # 右侧内容去掉 .dl-chlog-title，只保留 .chg-body
            chlog_body_lines = []
            title_open = None
            title_close = None
            for k, l in enumerate(right_content):
                if 'class="dl-chlog-title"' in l and title_open is None:
                    title_open = k
                    title_close = find_balanced_end(right_content, title_open)
                    continue
                if title_open is not None and k <= title_close:
                    continue
                chlog_body_lines.append(l)

            download_panels.append((ver, left_content, line, inner_lines[panel_close]))
            chlog_panels.append((ver, chlog_body_lines))

            j = panel_close + 1
        else:
            # 非 panel 行（如空行）直接跳过或保留
            j += 1

    # 输出下载 panels
    for idx, (ver, content, open_line, close_line) in enumerate(download_panels):
        active = ' active' if idx == 0 else ''
        # 重建 panel 开标签，保持 data-panel 和 role
        new_open = re.sub(r'class="dl-panel[^"]*"', f'class="dl-panel{active}"', open_line)
        new_open = re.sub(r'id="panel-[^"]*"', f'id="panel-{ver}"', new_open)
        out.append(content_indent + new_open.lstrip())
        for l in content:
            out.append(content_indent + '  ' + l.lstrip())
        out.append(content_indent + close_line.lstrip())

    # 输出 dl-panels 闭标签 和 dl-main 闭标签
    panels_close_line = inner_lines[panels_close]
    out.append(col_indent + panels_close_line.lstrip())
    out.append(col_indent + '</div>\n')

    # 输出右侧 dl-changelog 卡片
    out.append(col_indent + '<div class="dl-changelog">\n')
    out.append(content_indent + '<div class="dl-chlog-title-page">📋 本版本更新</div>\n')
    out.append(content_indent + '<div class="chlog-panels">\n')
    for idx, (ver, content) in enumerate(chlog_panels):
        active = ' active' if idx == 0 else ''
        out.append(content_indent + f'  <div class="chlog-panel{active}" data-panel="{ver}">\n')
        for l in content:
            out.append(content_indent + '    ' + l.lstrip())
        out.append(content_indent + f'  </div>\n')
    out.append(content_indent + '</div>\n')
    out.append(col_indent + '</div>\n')

    # 关闭 dl-layout-row
    out.append(inner_indent + '</div>\n')

    # 输出 note / alt（全宽）
    for line in note_lines:
        out.append(inner_indent + line.lstrip())
    for line in alt_lines:
        out.append(inner_indent + line.lstrip())

    # 关闭 dl-layout
    out.append(lines[card_close])

    # 收集 dl-card 之后的内容
    out.extend(lines[card_close + 1:])

    SRC.write_text(''.join(out), encoding='utf-8')
    print(f"converted: card_open={card_open + 1}, card_close={card_close + 1}")
    print(f"download panels: {len(download_panels)}, chlog panels: {len(chlog_panels)}")

    # 校验
    txt = SRC.read_text(encoding='utf-8')
    opens = len(re.findall(r'<div', txt))
    closes = len(re.findall(r'</div>', txt))
    print(f"div opens={opens}, closes={closes}, balanced={opens == closes}")
    print(f"dl-layout: {txt.count('dl-layout')}, dl-main: {txt.count('dl-main')}, dl-changelog: {txt.count('dl-changelog')}")
    print(f"chlog-panel: {txt.count('chlog-panel')}, dl-panel: {txt.count('dl-panel')}")


if __name__ == '__main__':
    convert()

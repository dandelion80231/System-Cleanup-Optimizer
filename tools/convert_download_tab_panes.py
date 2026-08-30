#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
把 download.html 的两栏结构改为「双标签页」布局：
- 版本 tab 保持顶部不变
- 每个 panel 内部上方出现两个标签：「下载」「本版更新」
- 下方左右并排两个内容区，整体占满宽度
"""
import re
from pathlib import Path

from _html_panels import find_balanced_end

SRC = Path(__file__).resolve().parent.parent / "site-src" / "download.html"


def convert():
    lines = SRC.read_text(encoding='utf-8').splitlines(keepends=True)
    out = []
    i = 0
    converted = 0

    while i < len(lines):
        line = lines[i]
        if '<div class="dl-panel' in line and 'role="tabpanel"' in line:
            panel_start = i
            panel_end = find_balanced_end(lines, panel_start)
            converted += 1

            # 收集 panel 内部所有行
            inner = lines[panel_start + 1:panel_end]

            # 找 left/right wrapper
            left_open = None
            left_close = None
            right_open = None
            right_close = None
            for j, l in enumerate(inner):
                if '<div class="dl-panel-left">' in l and left_open is None:
                    left_open = j
                    left_close = find_balanced_end(inner, left_open)
                if '<div class="dl-panel-right">' in l and right_open is None:
                    right_open = j
                    right_close = find_balanced_end(inner, right_open)

            if left_open is None or right_open is None:
                raise ValueError(f"panel {converted} 缺少 left/right wrapper")

            # 缩进
            panel_indent = re.match(r'^(\s*)', line).group(1)
            inner_indent = re.match(r'^(\s*)', inner[0]).group(1) if inner else panel_indent + '  '
            tab_indent = inner_indent + '  '

            # left 内容（去掉 wrapper 开/闭标签）
            left_content = inner[left_open + 1:left_close]
            # right 内容（去掉 wrapper 开/闭标签，也去掉 .dl-chlog-title，因为标签已单独放在上方）
            right_content = inner[right_open + 1:right_close]

            # 构造输出
            out.append(line)  # panel open
            out.append(inner_indent + '<div class="dl-panel-tabs">\n')
            out.append(tab_indent + '<div class="dl-panel-tab active">下载</div>\n')
            out.append(tab_indent + '<div class="dl-panel-tab active">本版更新</div>\n')
            out.append(inner_indent + '</div>\n')
            out.append(inner_indent + '<div class="dl-panel-panes">\n')
            out.append(tab_indent + '<div class="dl-panel-left">\n')
            for l in left_content:
                out.append(tab_indent + '  ' + l.lstrip())
            out.append(tab_indent + '</div>\n')
            out.append(tab_indent + '<div class="dl-panel-right">\n')
            for l in right_content:
                out.append(tab_indent + '  ' + l.lstrip())
            out.append(tab_indent + '</div>\n')
            out.append(inner_indent + '</div>\n')
            out.append(lines[panel_end])  # panel close

            i = panel_end + 1
        else:
            out.append(line)
            i += 1

    SRC.write_text(''.join(out), encoding='utf-8')
    print(f"converted panels: {converted}")
    txt = SRC.read_text(encoding='utf-8')
    print(f"dl-panel-tabs: {len(re.findall(r'class=\"dl-panel-tabs\"', txt))}")
    print(f"dl-panel-panes: {len(re.findall(r'class=\"dl-panel-panes\"', txt))}")
    print(f"div opens={len(re.findall(r'<div', txt))}, closes={len(re.findall(r'</div>', txt))}")


if __name__ == '__main__':
    convert()

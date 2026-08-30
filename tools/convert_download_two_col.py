#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
将 site-src/download.html 的 16 个 .dl-panel 从单栏流式改回两栏网格。
左栏：h2 / meta / btn / hash
右栏：dl-chlog-title / chg-body
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

            # 左栏：panel 开标签之后到 .hash 行（含）
            # 右栏：.dl-chlog-title 行到 .chg-body 结束（含）
            left_start = panel_start + 1
            right_end = panel_end - 1

            # 在 panel 内部找 .hash 行（左栏最后一行）和 .dl-chlog-title 行（右栏第一行）
            hash_idx = None
            title_idx = None
            chg_body_start = None
            chg_body_end = None

            for j in range(left_start, right_end + 1):
                l = lines[j]
                if '<div class="hash">' in l and hash_idx is None:
                    hash_idx = j
                if '<div class="dl-chlog-title">' in l and title_idx is None:
                    title_idx = j
                if '<div class="chg-body">' in l and chg_body_start is None:
                    chg_body_start = j

            if hash_idx is None or title_idx is None or chg_body_start is None:
                raise ValueError(f"panel {converted} 缺少 hash/title/chg-body："
                                 f"hash={hash_idx}, title={title_idx}, chg_body={chg_body_start}")

            # .chg-body 结束行（相对 panel 内部）
            chg_body_end = find_balanced_end(lines, chg_body_start)
            if chg_body_end >= panel_end:
                raise ValueError(f"panel {converted} 的 chg-body 超出 panel 范围")

            # 构造输出
            indent = re.match(r'^(\s*)', line).group(1)
            inner_indent = re.match(r'^(\s*)', lines[left_start]).group(1)
            # 保持内部缩进，wrapper 比 panel 内容多半级（两个空格）
            wrap_indent = inner_indent + '  '

            out.append(line)  # panel open
            out.append(inner_indent + '<div class="dl-panel-left">\n')
            for j in range(left_start, hash_idx + 1):
                out.append(wrap_indent + lines[j].lstrip())
            out.append(inner_indent + '</div>\n')
            out.append(inner_indent + '<div class="dl-panel-right">\n')
            for j in range(title_idx, chg_body_end + 1):
                out.append(wrap_indent + lines[j].lstrip())
            out.append(inner_indent + '</div>\n')
            out.append(lines[panel_end])  # panel close

            i = panel_end + 1
        else:
            out.append(line)
            i += 1

    SRC.write_text(''.join(out), encoding='utf-8')
    print(f"converted panels: {converted}")

    # 配平检查
    txt = SRC.read_text(encoding='utf-8')
    opens = len(re.findall(r'<div', txt))
    closes = len(re.findall(r'</div>', txt))
    print(f"div opens={opens}, closes={closes}, balanced={opens == closes}")

    wrappers = ['dl-panel-left', 'dl-panel-right']
    for cls in wrappers:
        n = len(re.findall(rf'class="{cls}"', txt))
        print(f"{cls}: {n}")


if __name__ == '__main__':
    convert()

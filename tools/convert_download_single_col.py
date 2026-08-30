#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
将 download.html 的 16 个 .dl-panel 从「两栏网格」改为「单栏流式」。

策略：基于行的精确块转换。
- 找到每个 `<div class="dl-panel` 开标签行（必须同时含 role="tabpanel"）
- 用栈式配平找到该 panel 对应的结束 `</div>` 行（处理同行多标签：
  如 `</div>          <div class="dl-panel" role="tabpanel" id="panel-v1.15">` 关本 panel + 开下一 panel）
- 在 panel 内部找到 `.dl-panel-left` 与 `.dl-panel-right` 两个 wrapper 的开标签行
- 分别用栈式配平找到各自结束 `</div>` 行
- 提取 left 内容（h2/meta/btn/hash）与 right 内容（dl-chlog-title + chg-body）
- 重写为单栏：open_line + left_content + right_content + close_line
- 去除 .dl-panel-left / .dl-panel-right 包裹 div
- 其余 HTML 完全不动
- footer（dl-note / alt）保持原样，本就在 .dl-panels 内正确层级

关键修复：当 panel_end 行是「</div> <div panel 下一>」同行时，
主循环必须把「下一 panel 开标签」重新作为 panel 起点处理（i 回退到 panel_end 行），
否则下一 panel 会被当成当前 panel 内容原样输出而不转 wrapper（导致只转一半）。
"""
import re
from pathlib import Path

from _html_panels import find_balanced_end, find_wrapper_open

SRC = Path(__file__).resolve().parent.parent / "site-src" / "download.html"

# single_col 使用非严格模式：同行多标签时允许找不到配平返回最后一行
_find_balanced = lambda lines, start_idx: find_balanced_end(lines, start_idx, strict=False)


def strip_blank(lst):
    while lst and lst[0].strip() == "":
        lst.pop(0)
    while lst and lst[-1].strip() == "":
        lst.pop()
    return lst


def main():
    with open(SRC, "r", encoding="utf-8") as f:
        lines = f.read().split("\n")

    out = []
    i = 0
    n = len(lines)
    converted = 0
    while i < n:
        line = lines[i]
        if 'class="dl-panel' in line and 'role="tabpanel"' in line:
            panel_start = i
            panel_end = _find_balanced(lines, panel_start)
            # 在 panel 内部找两个 wrapper
            left_start = find_wrapper_open(lines, panel_start + 1, panel_end, "dl-panel-left")
            right_start = find_wrapper_open(lines, panel_start + 1, panel_end, "dl-panel-right")
            left_end = _find_balanced(lines, left_start) if left_start is not None else None
            right_end = _find_balanced(lines, right_start) if right_start is not None else None

            left_content = lines[left_start + 1: left_end] if left_end else []
            right_content = lines[right_start + 1: right_end] if right_end else []

            lc = strip_blank(left_content[:])
            rc = strip_blank(right_content[:])

            # panel 开标签行可能本身是上一 panel 的 end 行（如 `</div> <div panel...>` 同行）：
            # 必须只输出后半段 <div panel...>，剥离前导 </div>（那个 </div> 已在上一 panel 的
            # m_open 分支输出过，否则会重复关闭导致 div 不平衡）。
            m_open_line = re.match(
                r'^(\s*)</div>\s+(<div\s+class="dl-panel"\s+role="tabpanel".*)$', line
            )
            if m_open_line:
                out.append(m_open_line.group(2).rstrip())
            else:
                out.append(line.rstrip())
            for x in lc:
                out.append(x.rstrip())
            for x in rc:
                out.append(x.rstrip())
            converted += 1

            # 处理 panel_end 行（本 panel 关闭；可能同开下一 panel 或关外层容器）
            end_line = lines[panel_end]
            # 拆出前导缩进与「</div>...」序列，以及行尾可能的 <div panel ...> 开标签
            m_open = re.search(r'(<div\s+class="dl-panel"\s+role="tabpanel".*)$', end_line)
            if m_open:
                # 行尾含下一 panel 开标签：输出其之前所有 </div>（本 panel 关闭 + 可能关 .dl-panels）
                prefix = end_line[:m_open.start()]
                closes = re.findall(r'</div>', prefix)
                indent = re.match(r'^(\s*)', prefix).group(1)
                for _ in closes:
                    out.append(indent + "</div>")
                # 下一 panel 开标签不在此输出，回退 i 让主循环重新以该行
                # 作为「下一 panel 起点」处理（find_balanced_end 会跳过前导 </div>）
                i = panel_end
                continue
            else:
                # 纯关闭行（可能含多个 </div>，如最后 panel 的 </div></div>）
                closes = re.findall(r'</div>', end_line)
                if closes:
                    indent = re.match(r'^(\s*)', end_line).group(1)
                    for _ in closes:
                        out.append(indent + "</div>")
                    i = panel_end + 1
                    continue
            # 兜底
            i = panel_end + 1 if panel_end is not None else i + 1
            continue
        out.append(lines[i])
        i += 1

    with open(SRC, "w", encoding="utf-8") as f:
        f.write("\n".join(out))
    print(f"converted panels: {converted}")


if __name__ == "__main__":
    main()

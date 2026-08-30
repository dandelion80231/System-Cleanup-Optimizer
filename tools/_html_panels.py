#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
下载页 HTML panel 转换脚本的公共工具。
提供基于行的 div 栈式配平查找，供 convert_download_*.py 共享。
"""
import re
from typing import List


def find_balanced_end(lines: List[str], start_idx: int, strict: bool = True) -> int:
    """从含 <div> 开标签的行开始，向下栈式配平找到对应 </div>。

    支持同行多个 <div>/</div>（如前一同级 panel 的结束 </div>
    与本级开标签同行）。默认 strict=True 会在未找到时抛异常；
    设 strict=False 则找不到时返回最后一行索引。
    """
    depth = 0
    for i in range(start_idx, len(lines)):
        line = lines[i]
        tags = list(re.finditer(r'<div[\s>]|</div>', line))
        for m in tags:
            if i == start_idx:
                # 仅计 start_idx 行中第一个 <div> 及其之后的标签；
                # 其之前的 </div>（如上一个 panel 的结束）不计入 depth
                first_open = None
                for t in tags:
                    if t.group() != '</div>':
                        first_open = t
                        break
                if first_open is not None and m.start() < first_open.start():
                    continue
            if m.group() == '</div>':
                depth -= 1
                if depth == 0 and i > start_idx:
                    return i
            else:
                depth += 1
    if strict:
        raise ValueError(f"未找到配平的 </div>，起始行 {start_idx}")
    return len(lines) - 1


def find_wrapper_open(lines: List[str], a: int, b: int, cls: str) -> int | None:
    """在 [a, b) 行范围内找含 class="cls" 的 div 开标签行。"""
    for i in range(a, b):
        if 'class="%s"' % cls in lines[i] and '<div' in lines[i]:
            return i
    return None

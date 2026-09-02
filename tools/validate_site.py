#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
validate_site.py — 下载页「两栏布局契约」强校验（替代 validate_html.py 的弱校验）。

validate_html.py 只查通用标签嵌套平衡，对「tab/panel/chlog 三处数量不一致」「active
不唯一」这种真正的布局破坏会判 OK。本脚本查契约本身。

检查项：
  1. div 开/闭平衡
  2. dl-layout=2 / dl-main=1 / dl-card(旧单栏)=0
  3. tab / panel / chlog 三者数量相同、顺序一致
  4. active 在三者都唯一且落在同一版本（否则右栏显示旧版本更新日志 / 下载按钮错位）
  5. 无废弃旧单栏类名（dl-meta/dl-actions/dl-btn/dl-desc/chlog-section）
  6. 每个版本在三处都有对应（panel id 存在）

用法：
  python tools/validate_site.py [path/to/download.html ...]   # 默认校验 site-src/download.html
退出码：0 通过 / 1 失败。
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEF = os.path.join(ROOT, "site-src", "download.html")

DEP = ["dl-meta", "dl-actions", "dl-btn ", "dl-desc", "chlog-section"]


def check(path):
    if not os.path.isfile(path):
        print("[ERR] 不存在: %s" % path)
        return False
    h = open(path, encoding="utf-8").read()
    errs = []
    o = len(re.findall(r"<div\b(?![^>]*/>)[^>]*?>", h))
    c = len(re.findall(r"</div\s*>", h))
    if o != c:
        errs.append("div 开/闭不平衡 %d/%d" % (o, c))
    if len(re.findall(r'class="dl-layout', h)) != 2:
        errs.append("dl-layout 应为 2，实际 %d" % len(re.findall(r'class="dl-layout', h)))
    if len(re.findall(r'class="dl-main"', h)) != 1:
        errs.append("dl-main 应为 1，实际 %d" % len(re.findall(r'class="dl-main"', h)))
    if len(re.findall(r'class="dl-card', h)) != 0:
        errs.append("出现旧单栏 dl-card 类 %d 处" % len(re.findall(r'class="dl-card"', h)))
    tabs = re.findall(r'class="dl-tab(?: active)?"[^>]*data-ver="([^"]+)"', h)
    panels = re.findall(r'class="dl-panel(?: active)?"[^>]*data-panel="([^"]+)"', h)
    chlogs = re.findall(r'class="chlog-panel(?: active)?"[^>]*data-panel="([^"]+)"', h)
    at = re.findall(r'class="dl-tab active"[^>]*data-ver="([^"]+)"', h)
    ap = re.findall(r'class="dl-panel active"[^>]*data-panel="([^"]+)"', h)
    ag = re.findall(r'class="chlog-panel active"[^>]*data-panel="([^"]+)"', h)
    if not (tabs == panels == chlogs):
        errs.append("三处不一致 tab=%d panel=%d chlog=%d" % (len(tabs), len(panels), len(chlogs)))
    else:
        if len(tabs) == 0:
            errs.append("没有任何版本")
        else:
            newest = max(tabs, key=lambda s: [int(x) for x in s[1:].split('.')])
            if tabs[0] != newest:
                errs.append("最新版本应排首位：首位=%s，最新=%s" % (tabs[0], newest))
    if not (len(at) == 1 and len(ap) == 1 and len(ag) == 1):
        errs.append("active 不唯一 tab=%d panel=%d chlog=%d" % (len(at), len(ap), len(ag)))
    elif not (at[0] == ap[0] == ag[0]):
        errs.append("active 未对齐 tab=%s panel=%s chlog=%s" % (at[0], ap[0], ag[0]))
    for d in DEP:
        if d in h:
            errs.append("废弃旧单栏类名: %s" % d)
    # panel id 完整性
    for p in panels:
        if 'id="panel-%s"' % p not in h:
            errs.append("panel 缺 id=panel-%s" % p)

    if errs:
        print("[FAIL] %s  (%d 字节)" % (path, len(h)))
        for e in errs:
            print("   - " + e)
        return False
    print("[OK]   %s  版本=%d  active=%s  div=%d/%d" % (path, len(tabs), at[0], o, c))
    return True


def main():
    files = sys.argv[1:] or [DEF]
    ok = all(check(f) for f in files)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()

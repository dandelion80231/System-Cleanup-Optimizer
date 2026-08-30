#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
PageCache 迁移后的注释润色（按行处理，保留各文件自身行尾 CRLF/LF）。
"""
import io
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "src", "CpqSystemTool")


def load(path):
    with io.open(path, "r", encoding="utf-8-sig", newline="") as f:
        data = f.read()
    eol = "\r\n" if "\r\n" in data else "\n"
    return data.split("\n"), eol


def save(path, lines, eol):
    with io.open(path, "w", encoding="utf-8-sig", newline="") as f:
        f.write(eol.join(lines))


def indent_of(ln):
    return ln[:len(ln) - len(ln.lstrip())]


def edit_config():
    fn = "MainWindow.Config.cs"
    p = os.path.join(ROOT, fn)
    lines, eol = load(p)
    out = []
    done_a = done_b = done_c = False
    i = 0
    while i < len(lines):
        ln = lines[i]
        s = ln.strip()

        # a) "修复" → 明确标注为已被结构消除的历史 bug
        if not done_a and s.startswith("/// 修复：此前各处置空只清"):
            ind = indent_of(ln)
            out.append(ind + "/// 历史 bug（收拢为 PageCache<UIElement> 后已从结构上不可能复发）：")
            out.append(ind + "/// 此前各处置空只清 _cachedConfigPage，漏清 _configRefresh / _configCacheKey，")
            done_a = True
            i += 1
            continue

        # b) 旧 bug 描述后面补一句"现在为什么不会复发"
        if not done_b and s.startswith("/// 页面虽会重建，但旧的刷新委托与缓存键仍残留"):
            ind = indent_of(ln)
            out.append(ln)
            out.append(ind + "/// 现在「失效」只有 Invalidate() 一个入口，页面、内容键、刷新委托必然一并清空。")
            done_b = True
            i += 1
            continue

        # c) 清掉 InvalidateConfigCache 体内迁移留下的多余空行
        if not done_c and s == "_configCache.Invalidate();":
            out.append(ln)
            i += 1
            while i < len(lines) and lines[i].strip() == "":
                i += 1
            done_c = True
            continue

        out.append(ln)
        i += 1
    save(p, out, eol)
    print("  %-24s 注释更新=%s 空行清理=%s" % (fn, done_a and done_b, done_c))


edit_config()
print("完成。")

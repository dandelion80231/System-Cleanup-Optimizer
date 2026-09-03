#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
sync_changelog_to_site.py — 让官网成为 CHANGELOG.md 的「单一数据来源」（补回此前遗漏的机制）

为什么需要它：
  此前 download.html 右栏「📋 本版本更新」与 changelog.html 时间线都是手抄进 HTML 的，
  改 CHANGELOG.md 不会自动同步到官网，迟早漂移。本脚本把这两处版本化内容统一从
  仓库根 CHANGELOG.md 重新生成，做到「改 CHANGELOG.md 一处 → 渲染+部署即同步」。

设计原则（避免旧 sync_changelog.py 破坏 HTML 结构的坑）：
  - 只刷新「内容」，绝不改动页面骨架。
  - download.html：读取现有 chlog-panel 的 data-panel 集合与顺序（即下载页三栏契约的
    右栏），对每个版本用 CHANGELOG.md 对应段落重建 <div class="chg-body"> 内部；
    tab/panel（左栏下载按钮+哈希）完全不动，因此三栏顺序/active 永远对齐。
  - changelog.html：读取现有 tl-item 的 ver 集合与顺序，重建每个 <ul> 内部 <li>。
  - 不在站点新增/删除任何版本：CHANGELOG.md 里没有对应下载 tab 的版本（如 v1.16.1
    热修）会被跳过，与现有站点设计保持一致。

Markdown → HTML 转换覆盖：
  > 摘要            -> <blockquote>
  ### 小标题        -> <h4>（download 面板）/ <li><strong>（timeline）
  - 条目           -> <li>
  行内 `code`        -> <code>code</code>
  行内 **bold**      -> <strong>bold</strong>

用法：
  python tools/sync_changelog_to_site.py            # 默认 dry-run，打印每个版本是否会变化
  python tools/sync_changelog_to_site.py --apply    # 真正写 site-src/download.html + changelog.html
  python tools/sync_changelog_to_site.py --apply --render   # 写完后顺带跑 render_site.py 生成 site-dist

依赖：仅标准库（div 配平借用 _html_panels.find_balanced_end）。
"""
import argparse
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
CHANGELOG = os.path.join(ROOT, "CHANGELOG.md")
DEF_DL = os.path.join(ROOT, "site-src", "download.html")
DEF_CL = os.path.join(ROOT, "site-src", "changelog.html")

sys.path.insert(0, HERE)
from _html_panels import find_balanced_end  # noqa: E402


# ---------------------------------------------------------------------------
# CHANGELOG.md 解析
# ---------------------------------------------------------------------------
def parse_changelog(path):
    """返回 [(ver, date, body_lines), ...]，按文件出现顺序（v1.19 在前）。"""
    with open(path, encoding="utf-8") as f:
        lines = f.read().split("\n")
    versions = []
    cur = None
    for line in lines:
        m = re.match(r"^##\s+\[(v[\d.]+)\]\s*-\s*(\d{4}-\d{2}-\d{2})", line)
        if m:
            if cur is not None:
                versions.append(cur)
            cur = (m.group(1), m.group(2), [])
            continue
        if cur is not None:
            cur[2].append(line)
    if cur is not None:
        versions.append(cur)
    return versions


def changelog_map(versions):
    return {v: (d, body) for (v, d, body) in versions}


# ---------------------------------------------------------------------------
# Markdown 行内转换（顺序：转义 -> code -> bold，保证 <code>/<strong> 不被二次转义）
# ---------------------------------------------------------------------------
def convert_inline(text):
    text = text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
    text = re.sub(r"`([^`]+)`", lambda m: "<code>%s</code>" % m.group(1), text)
    text = re.sub(r"\*\*([^*]+?)\*\*", lambda m: "<strong>%s</strong>" % m.group(1), text)
    return text


def indent(text, n):
    pad = " " * n
    return "\n".join((pad + ln if ln.strip() else ln) for ln in text.split("\n"))


# download 面板：blockquote + h4 + ul
def md_to_panel(body_lines):
    out = []
    in_ul = False
    bq = []

    def flush_bq():
        if bq:
            out.append("<blockquote>%s</blockquote>" % " ".join(bq).strip())
            bq.clear()

    def close_ul():
        nonlocal in_ul
        if in_ul:
            out.append("</ul>")
            in_ul = False

    for raw in body_lines:
        line = raw.rstrip("\n")
        if line.startswith("> "):
            bq.append(line[2:].strip())
            continue
        flush_bq()
        if line.startswith("### "):
            close_ul()
            out.append("<h4>%s</h4>" % convert_inline(line[4:].strip()))
            continue
        if line.startswith("- "):
            if not in_ul:
                out.append("<ul>")
                in_ul = True
            out.append("<li>%s</li>" % convert_inline(line[2:].strip()))
            continue
        if line.strip() == "":
            close_ul()
            continue
        close_ul()
        out.append("<p>%s</p>" % convert_inline(line.strip()))
    flush_bq()
    close_ul()
    return "\n".join(out)


# timeline：扁平 <li>（blockquote 作 em 引导项，### 作 strong 分组项，- 作普通项）
def md_to_timeline(body_lines):
    items = []
    bq = []

    def flush_bq():
        if bq:
            items.append("<li><em>%s</em></li>" % convert_inline(" ".join(bq).strip()))
            bq.clear()

    for raw in body_lines:
        line = raw.rstrip("\n")
        if line.startswith("> "):
            bq.append(line[2:].strip())
            continue
        flush_bq()
        if line.startswith("### "):
            items.append("<li><strong>%s</strong></li>" % convert_inline(line[4:].strip()))
            continue
        if line.startswith("- "):
            items.append("<li>%s</li>" % convert_inline(line[2:].strip()))
            continue
        # 空白/段落：timeline 内忽略
    flush_bq()
    return "\n".join(items)


# ---------------------------------------------------------------------------
# 注入
# ---------------------------------------------------------------------------
def sync_download(html, cmap):
    lines = html.split("\n")
    opens = [i for i, l in enumerate(lines) if 'class="chlog-panel' in l and "data-panel=" in l]
    changes = 0
    for i in reversed(opens):
        m = re.search(r'data-panel="([^"]+)"', lines[i])
        ver = m.group(1)
        close = find_balanced_end(lines, i)
        entry = cmap.get(ver)
        if entry is None:
            print("  ⚠️  download: 版本 %s 在 CHANGELOG.md 无对应段落，保留原内容" % ver)
            continue
        panel_html = md_to_panel(entry[1])
        inner = '  <div class="chg-body">\n' + indent(panel_html, 16) + "\n  </div>"
        new_block = lines[i] + "\n" + inner + "\n" + lines[close]
        lines[i:close + 1] = new_block.split("\n")
        changes += 1
    print("  download.html：刷新 %d 个 chlog-panel" % changes)
    return "\n".join(lines)


def sync_changelog(html, cmap):
    lines = html.split("\n")
    opens = [i for i, l in enumerate(lines) if '<div class="tl-item">' in l]
    changes = 0
    for i in reversed(opens):
        close = find_balanced_end(lines, i)
        m = re.search(r'<span class="ver">([^<]+)</span>', "\n".join(lines[i:close + 1]))
        if not m:
            print("  ⚠️  changelog: tl-item @%d 找不到 ver，跳过" % i)
            continue
        ver = m.group(1)
        entry = cmap.get(ver)
        if entry is None:
            print("  ⚠️  changelog: 版本 %s 在 CHANGELOG.md 无对应段落，保留原内容" % ver)
            continue
        date = entry[0]
        li = md_to_timeline(entry[1])
        inner = (
            '          <span class="ver">%s</span><span class="date">%s</span>\n'
            "          <ul>\n%s\n          </ul>"
        ) % (ver, date, indent(li, 12))
        new_block = lines[i] + "\n" + inner + "\n" + lines[close]
        lines[i:close + 1] = new_block.split("\n")
        changes += 1
    print("  changelog.html：刷新 %d 个 tl-item" % changes)
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# 校验
# ---------------------------------------------------------------------------
def div_counts(html):
    opens = len(re.findall(r"<div\b(?![^>]*/>)[^>]*?>", html))
    closes = len(re.findall(r"</div\s*>", html))
    return opens, closes


def contract(html):
    tabs = re.findall(r'class="dl-tab(?: active)?"[^>]*data-ver="([^"]+)"', html)
    panels = re.findall(r'class="dl-panel(?: active)?"[^>]*data-panel="([^"]+)"', html)
    chlogs = re.findall(r'class="chlog-panel(?: active)?"[^>]*data-panel="([^"]+)"', html)
    at = re.findall(r'class="dl-tab active"[^>]*data-ver="([^"]+)"', html)
    ap = re.findall(r'class="dl-panel active"[^>]*data-panel="([^"]+)"', html)
    ag = re.findall(r'class="chlog-panel active"[^>]*data-panel="([^"]+)"', html)
    return tabs, panels, chlogs, at, ap, ag


def validate_download(html):
    o, c = div_counts(html)
    tabs, panels, chlogs, at, ap, ag = contract(html)
    ok = True
    msgs = []
    if o != c:
        ok = False
        msgs.append("div 开/闭不平衡: %d/%d" % (o, c))
    if not (tabs == panels == chlogs):
        ok = False
        msgs.append("三处版本集合/顺序不一致: tab=%s panel=%s chlog=%s" % (tabs, panels, chlogs))
    if not (len(at) == 1 and len(ap) == 1 and len(ag) == 1 and at[0] == ap[0] == ag[0]):
        ok = False
        msgs.append("active 未唯一对齐: %s/%s/%s" % (at or ["-"], ap or ["-"], ag or ["-"]))
    return ok, msgs


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="真正写入 site-src（默认 dry-run）")
    ap.add_argument("--render", action="store_true", help="写入后顺带跑 render_site.py 生成 site-dist")
    ap.add_argument("--download-html", default=DEF_DL)
    ap.add_argument("--changelog-html", default=DEF_CL)
    ap.add_argument("--changelog-md", default=CHANGELOG)
    args = ap.parse_args()

    if not os.path.isfile(args.changelog_md):
        sys.exit("[FATAL] CHANGELOG.md 缺失: %s" % args.changelog_md)

    cmap = changelog_map(parse_changelog(args.changelog_md))
    print("[INFO] CHANGELOG.md 解析到 %d 个版本: %s" % (len(cmap), ", ".join(list(cmap.keys()))))

    dl_html = open(args.download_html, encoding="utf-8").read()
    cl_html = open(args.changelog_html, encoding="utf-8").read()

    new_dl = sync_download(dl_html, cmap)
    new_cl = sync_changelog(cl_html, cmap)

    # 校验
    ok, msgs = validate_download(new_dl)
    print("-" * 60)
    if ok:
        print("✅ download.html 三栏契约校验通过（div 平衡 + tab/panel/chlog 对齐 + active 唯一）")
    else:
        print("❌ download.html 校验失败：")
        for m in msgs:
            print("   - " + m)
        if args.apply:
            sys.exit(2)
    o, c = div_counts(new_cl)
    if o == c:
        print("✅ changelog.html div 平衡: %d/%d" % (o, c))
    else:
        print("❌ changelog.html div 不平衡: %d/%d" % (o, c))
        if args.apply:
            sys.exit(2)

    if not args.apply:
        print("-" * 60)
        print("🔍 DRY-RUN：未写入任何文件。确认无误后加 --apply（可再加 --render）。")
        return

    with open(args.download_html, "w", encoding="utf-8") as f:
        f.write(new_dl)
    with open(args.changelog_html, "w", encoding="utf-8") as f:
        f.write(new_cl)
    print("💾 已写入: %s, %s" % (args.download_html, args.changelog_html))

    if args.render:
        import subprocess
        r = subprocess.run([sys.executable, os.path.join(HERE, "render_site.py")],
                           cwd=ROOT)
        if r.returncode != 0:
            sys.exit("[FATAL] render_site.py 失败，site-dist 未更新")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
sync_changelog.py — 将仓库根目录 CHANGELOG.md 的完整内容同步到官网
                   site-src/changelog.html（时间线条目）与
                   site-src/download.html（每个 panel 右栏“本版更新”）。

设计原则（来自历史教训）：
  - 内容必须“与 CHANGELOG.md 完全一致”，不重编、不精简。
  - 用脚本自动提取，避免手工漏条 / 改坏结构。
  - CHANGELOG.md 的 v1.16 段内嵌了 v1.15 的真实内容（blockquote + ### 小节），
    因此 v1.15 的完整 changelog 从 v1.16 段中拆出，保证两页一致且忠于源文件。

输入：  D:/电脑桌面/cpq/CHANGELOG.md
        D:/电脑桌面/cpq/site-src/changelog.html   （模板，占位符 {{VER}} 等保留）
        D:/电脑桌面/cpq/site-src/download.html     （模板）
输出：  覆盖写回两个 HTML（仅替换 changelog 内容区域，head/nav/footer/占位符不动）
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # D:/电脑桌面/cpq
SRC = os.path.join(ROOT, "site-src")
CHANGELOG = os.path.join(ROOT, "CHANGELOG.md")
CHANGELOG_HTML = os.path.join(SRC, "changelog.html")
DOWNLOAD_HTML = os.path.join(SRC, "download.html")

# 下载页需要展示的版本（与 tab/panel 顺序一致，最新在前）
DL_VERSIONS = [
    "v1.16", "v1.15", "v1.14", "v1.13", "v1.12", "v1.11", "v1.10",
    "v1.09", "v1.08", "v1.07", "v1.06", "v1.05", "v1.04", "v1.03",
    "v1.02", "v1.01",
]


# ---------------------------------------------------------------------------
# 1. 解析 CHANGELOG.md
# ---------------------------------------------------------------------------
def parse_changelog():
    with open(CHANGELOG, encoding="utf-8") as f:
        text = f.read()

    # 按 '## [vX.YY] - DATE' 切分
    head_re = re.compile(r'^## \[(v[\d.]+)\] - (\d{4}-\d{2}-\d{2})\s*$', re.M)
    matches = list(head_re.finditer(text))
    blocks = {}
    dates = {}
    for i, m in enumerate(matches):
        ver = m.group(1)
        dates[ver] = m.group(2)
        start = m.end()
        end = matches[i + 1].start() if i + 1 < len(matches) else len(text)
        body = text[start:end].strip('\n')
        blocks[ver] = body

    # v1.16 段内嵌了 v1.15 的内容：从 '> 相对 v1.14' 这一行起属于 v1.15
    if "v1.16" in blocks:
        body = blocks["v1.16"]
        lines = body.split("\n")
        split_idx = None
        for i, ln in enumerate(lines):
            if ln.startswith("> 相对 v1.14"):
                split_idx = i
                break
        if split_idx is not None:
            own = "\n".join(lines[:split_idx]).strip("\n")
            v115 = "\n".join(lines[split_idx:]).strip("\n")
            blocks["v1.16"] = own
            blocks["v1.15"] = v115
            if "v1.15" not in dates:
                dates["v1.15"] = dates.get("v1.16", "")
    return blocks, dates


# ---------------------------------------------------------------------------
# 2. Markdown -> HTML（极简但忠实：### / > / - / ** / ` ）
# ---------------------------------------------------------------------------
def escape_html(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def inline(md):
    """行内格式：先转义，再处理 `code` 与 **bold**。"""
    s = escape_html(md)
    # code 优先（避免 code 内的 ** 被当 bold）
    s = re.sub(r'`([^`]+)`', lambda m: "<code>" + m.group(1) + "</code>", s)
    # bold
    s = re.sub(r'\*\*([^*]+)\*\*', r"<strong>\1</strong>", s)
    return s


def md_to_html(md):
    lines = md.split("\n")
    out = []
    i = 0
    n = len(lines)
    while i < n:
        ln = lines[i]
        # 空行
        if ln.strip() == "":
            i += 1
            continue
        # blockquote（连续 > 行）
        if ln.lstrip().startswith(">"):
            quote_lines = []
            while i < n and lines[i].lstrip().startswith(">"):
                q = lines[i].lstrip()[1:].lstrip()
                quote_lines.append(q)
                i += 1
            qtext = " ".join(quote_lines)
            out.append('<blockquote>' + inline(qtext) + "</blockquote>")
            continue
        # ### 小标题
        if ln.startswith("### "):
            out.append("<h4>" + inline(ln[4:].strip()) + "</h4>")
            i += 1
            continue
        # 列表（- 项，连续合并）
        if re.match(r'^\s*-\s+', ln):
            items = []
            while i < n and re.match(r'^\s*-\s+', lines[i]):
                item = re.sub(r'^\s*-\s+', "", lines[i])
                items.append("<li>" + inline(item) + "</li>")
                i += 1
            out.append("<ul>" + "".join(items) + "</ul>")
            continue
        # 普通段落
        out.append("<p>" + inline(ln) + "</p>")
        i += 1
    return "\n".join(out)


def version_body_html(blocks, ver):
    """返回该版本完整 changelog 的 HTML（包在 <div class="chg-body"> 中）。"""
    md = blocks.get(ver, "")
    if not md.strip():
        return '<div class="chg-body"><p>（暂无详细记录）</p></div>'
    return '<div class="chg-body">\n' + md_to_html(md) + "\n</div>"


# ---------------------------------------------------------------------------
# 3. 注入 changelog.html 的时间线
# ---------------------------------------------------------------------------
def build_timeline(blocks, dates):
    items = []
    # 按 DL_VERSIONS 顺序（最新在前），但 changelog 也用同序
    for ver in DL_VERSIONS:
        d = dates.get(ver, "")
        body = version_body_html(blocks, ver)
        items.append(
            '        <div class="tl-item">\n'
            f'          <span class="ver">{ver}</span><span class="date">{d}</span>\n'
            f"          {body}\n"
            "        </div>"
        )
    return "\n".join(items)


def inject_changelog(html, timeline_inner):
    # 替换 <div class="timeline reveal"> ... </div> 的内部（到 GitHub 按钮 div 之前）
    pat = re.compile(
        r'(<div class="timeline reveal">).*?(</div>\s*<div style="text-align:center;margin-top:8px;")',
        re.S,
    )
    m = pat.search(html)
    if not m:
        sys.exit("[FATAL] changelog.html 找不到 .timeline reveal 容器")
    new_html = html[: m.start(1) + len(m.group(1))] + "\n" + timeline_inner + "\n        " + html[m.start(2):]
    return new_html


# ---------------------------------------------------------------------------
# 4. 注入 download.html 每个 panel 的右栏
# ---------------------------------------------------------------------------
def inject_download(html, blocks):
    # 逐 panel 处理：用面板 id 切分每段，段内用已验证可靠的左栏/右栏正则替换旧 ul。
    # 右栏：保留 <div class="dl-panel-right"> + 标题，把其内的 <ul>...</ul> 换成完整 chg-body。
    left_re = re.compile(r'(<div class="dl-panel-left">.*?</div>\s*</div>)', re.S)
    right_re = re.compile(
        r'(<div class="dl-panel-right">\s*<div class="dl-chlog-title">.*?</div>\s*)'
        r'(<ul class="dl-chlog-list">.*?</ul>)',
        re.S,
    )

    # 计算每个 panel 的起止位置（按 DL_VERSIONS 顺序）
    starts = []
    for ver in DL_VERSIONS:
        idx = html.find(f'id="panel-{ver}"')
        if idx == -1:
            print(f"[WARN] download.html 找不到 panel-{ver}")
            continue
        starts.append((ver, idx))
    if not starts:
        return html
    # 每段 [start, next_start)
    ends = [s[1] for s in starts[1:]] + [len(html)]
    new_html = html
    replaced = 0
    # 从后往前替换，避免偏移影响前面的索引
    for (ver, start), end in reversed(list(zip(starts, ends))):
        seg = new_html[start:end]
        lm = left_re.search(seg)
        rm = right_re.search(seg)
        if not lm or not rm:
            print(f"[WARN] panel-{ver} 结构异常（left={bool(lm)} right={bool(rm)}），跳过")
            continue
        body = version_body_html(blocks, ver)
        new_seg = seg[: rm.start()] + rm.group(1) + body + "\n            " + seg[rm.end():]
        new_html = new_html[:start] + new_seg + new_html[end:]
        replaced += 1
    print(f"[INFO] download.html 实际替换 panel 数: {replaced}（期望 {len(DL_VERSIONS)}）")
    return new_html


# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------
def main():
    blocks, dates = parse_changelog()
    # 校验关键版本都在
    for v in DL_VERSIONS:
        if v not in blocks:
            print(f"[WARN] CHANGELOG.md 未解析到 {v}（v1.15 为内嵌拆分，正常）")

    # changelog.html
    with open(CHANGELOG_HTML, encoding="utf-8") as f:
        ch = f.read()
    timeline = build_timeline(blocks, dates)
    ch = inject_changelog(ch, timeline)
    with open(CHANGELOG_HTML, "w", encoding="utf-8") as f:
        f.write(ch)
    print(f"[OK] changelog.html 时间线已写入 {len(DL_VERSIONS)} 个版本")

    # download.html
    with open(DOWNLOAD_HTML, encoding="utf-8") as f:
        dh = f.read()
    dh = inject_download(dh, blocks)
    with open(DOWNLOAD_HTML, "w", encoding="utf-8") as f:
        f.write(dh)
    print(f"[OK] download.html {len(DL_VERSIONS)} 个 panel 右栏已写入")

    # 简单校验：确认几个关键标记存在
    with open(CHANGELOG_HTML, encoding="utf-8") as f:
        c = f.read()
    print(f"[CHECK] changelog 含 ### 小节数: {c.count('<h4>')}")
    print(f"[CHECK] changelog 含 blockquote 数: {c.count('<blockquote>')}")
    with open(DOWNLOAD_HTML, encoding="utf-8") as f:
        d = f.read()
    print(f"[CHECK] download 含 <h4> 数: {d.count('<h4>')}（应 = 16 版本各自小节数之和）")
    print(f"[CHECK] download 含 chg-body 数: {d.count('class=\"chg-body\"')}（应 = 16）")


if __name__ == "__main__":
    main()

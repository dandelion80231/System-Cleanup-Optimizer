#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
sync_changelog.py — 将 CHANGELOG.md 内容同步到官网
"""

import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "site-src")
CHANGELOG = os.path.join(ROOT, "CHANGELOG.md")
CHANGELOG_HTML = os.path.join(SRC, "changelog.html")
DOWNLOAD_HTML = os.path.join(SRC, "download.html")

ALL_VERSIONS = [
    "v1.17", "v1.16", "v1.15", "v1.14", "v1.13", "v1.12", "v1.11", "v1.10",
    "v1.09", "v1.08", "v1.07", "v1.06", "v1.05", "v1.04", "v1.03",
    "v1.02", "v1.01",
]


def parse_changelog():
    with open(CHANGELOG, encoding="utf-8") as f:
        text = f.read()

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

    # v1.16 段内嵌了 v1.15
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


def escape_html(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def inline(md):
    s = escape_html(md)
    s = re.sub(r'`([^`]+)`', lambda m: "<code>" + m.group(1) + "</code>", s)
    s = re.sub(r'\*\*([^*]+)\*\*', r"<strong>\1</strong>", s)
    return s


def md_to_html(md):
    lines = md.split("\n")
    out = []
    i = 0
    n = len(lines)
    while i < n:
        ln = lines[i]
        if ln.strip() == "":
            i += 1
            continue
        if ln.lstrip().startswith(">"):
            quote_lines = []
            while i < n and lines[i].lstrip().startswith(">"):
                q = lines[i].lstrip()[1:].lstrip()
                quote_lines.append(q)
                i += 1
            qtext = " ".join(quote_lines)
            out.append('<blockquote>' + inline(qtext) + "</blockquote>")
            continue
        if ln.startswith("### "):
            out.append("<h4>" + inline(ln[4:].strip()) + "</h4>")
            i += 1
            continue
        if re.match(r'^\s*-\s+', ln):
            items = []
            while i < n and re.match(r'^\s*-\s+', lines[i]):
                item = re.sub(r'^\s*-\s+', "", lines[i])
                items.append("<li>" + inline(item) + "</li>")
                i += 1
            out.append("<ul>" + "".join(items) + "</ul>")
            continue
        out.append("<p>" + inline(ln) + "</p>")
        i += 1
    return "\n".join(out)


def version_body_html(blocks, ver):
    md = blocks.get(ver, "")
    if not md.strip():
        return '<div class="chg-body"><p>（暂无详细记录）</p></div>'
    return '<div class="chg-body">\n' + md_to_html(md) + "\n</div>"


def update_changelog_html(html, blocks, dates):
    items = []
    for ver in ALL_VERSIONS:
        if ver not in blocks:
            continue
        d = dates.get(ver, "")
        body = version_body_html(blocks, ver)
        items.append(
            '        <div class="tl-item">\n'
            f'          <span class="ver">{ver}</span><span class="date">{d}</span>\n'
            f"          {body}\n"
            "        </div>"
        )
    timeline_inner = "\n".join(items)

    pat = re.compile(
        r'(<div class="timeline reveal in">).*?(</div>\s*</section>)',
        re.S,
    )
    m = pat.search(html)
    if not m:
        print("[FATAL] changelog.html: 找不到 .timeline 容器")
        return html
    return html[:m.start(1) + len(m.group(1))] + "\n" + timeline_inner + "\n        " + html[m.start(2):]


def update_download_html(html, blocks, dates):
    # 1. 添加 v1.17 tab（在 v1.16 tab 之前）
    tab_pattern = r'(<button[^>]*data-ver="v1\.16"[^>]*>)'
    match = re.search(tab_pattern, html)
    if match:
        v117_tab = '          <button class="dl-tab active" role="tab" aria-selected="true" aria-controls="panel-v1.17" tabindex="0" data-ver="v1.17">v1.17</button>\n'
        html = html[:match.start()] + v117_tab + html[match.start():]
        # 移除 v1.16 tab 的 active 类
        html = re.sub(
            r'(<button[^>]*data-ver="v1\.16"[^>]*class="dl-tab) active(")',
            r'\1\2',
            html
        )

    # 2. 添加 v1.17 dl-panel（在 v1.16 panel 之前）
    panel_pattern = r'(<div class="dl-panel"[^>]*data-panel="v1\.16"[^>]*>)'
    match = re.search(panel_pattern, html)
    if match:
        date = dates.get("v1.17", "2026-08-30")
        sha = "61293df5eceb813e235b7e567b7323e743583f64480ce6cdfc74e9eece0cf7ce"
        v117_panel = f'''            <div class="dl-panel active" role="tabpanel" id="panel-v1.17" data-panel="v1.17">
              <h3 class="dl-ver">下载 v1.17 <span style="font-size:14px;opacity:.75;font-weight:500;">（最新 · {date} · 5.06 MB）</span></h3>
              <p class="meta"><span>📦 单文件 exe</span><span>💾 5.06 MB</span><span>🪟 Win 10 / 11</span><span>🔓 开源免费</span></p>
              <a class="btn btn-primary" href="./系统清理与优化工具_v1.17.exe" download>⬇️ 下载 系统清理与优化工具_v1.17.exe</a>
              <div class="hash">SHA256: {sha}</div>
            </div>
'''
        html = html[:match.start()] + v117_panel + html[match.start():]

    # 3. 添加 v1.17 chlog-panel（在 v1.16 chlog-panel 之前）
    chlog_pattern = r'(<div class="chlog-panel"[^>]*data-panel="v1\.16"[^>]*>)'
    match = re.search(chlog_pattern, html)
    if match:
        body = version_body_html(blocks, "v1.17")
        v117_chlog = f'''              <div class="chlog-panel active" data-panel="v1.17">\n{body}\n              </div>
'''
        html = html[:match.start()] + v117_chlog + html[match.start():]

    return html


def main():
    blocks, dates = parse_changelog()

    for v in ALL_VERSIONS:
        if v not in blocks:
            print(f"[WARN] CHANGELOG.md 未找到 {v}")

    # 更新 changelog.html
    with open(CHANGELOG_HTML, encoding="utf-8") as f:
        ch = f.read()
    ch = update_changelog_html(ch, blocks, dates)
    with open(CHANGELOG_HTML, "w", encoding="utf-8") as f:
        f.write(ch)
    print(f"[OK] changelog.html 已更新")

    # 更新 download.html
    with open(DOWNLOAD_HTML, encoding="utf-8") as f:
        dh = f.read()
    dh = update_download_html(dh, blocks, dates)
    with open(DOWNLOAD_HTML, "w", encoding="utf-8") as f:
        f.write(dh)
    print(f"[OK] download.html 已更新")

    # 校验
    with open(CHANGELOG_HTML, encoding="utf-8") as f:
        c = f.read()
    print(f"[CHECK] changelog.html: <h4>={c.count('<h4>')}，blockquote={c.count('<blockquote>')}")

    with open(DOWNLOAD_HTML, encoding="utf-8") as f:
        d = f.read()
    print(f"[CHECK] download.html: <h4>={d.count('<h4>')}，chg-body={d.count('class=\"chg-body\"')}")


if __name__ == "__main__":
    main()

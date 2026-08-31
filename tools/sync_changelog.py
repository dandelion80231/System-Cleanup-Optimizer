#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
sync_changelog.py — 将 CHANGELOG.md 内容同步到官网
"""

import os
import re
from html.parser import HTMLParser

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "site-src")
CHANGELOG = os.path.join(ROOT, "CHANGELOG.md")
CHANGELOG_HTML = os.path.join(SRC, "changelog.html")
DOWNLOAD_HTML = os.path.join(SRC, "download.html")

ALL_VERSIONS = [
    "v1.18", "v1.17", "v1.16", "v1.15", "v1.14", "v1.13", "v1.12", "v1.11", "v1.10",
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
    """
    从 CHANGELOG.md 完整同步最新版本内容到 download.html
    只更新最新版本的信息，保留所有历史版本的 panel
    
    逻辑：
    1. 移除旧的版本 tab（动态生成的那一行）
    2. 移除旧的 dl-panel（最新版本的下载面板）
    3. 重建 chlog-panels（changelog 部分）
    4. 添加最新版本 tab（在 v1.16 之前）
    5. 添加最新版本 dl-panel（在 v1.16 之前）
    """
    # 确定最新版本（CHANGELOG 中第一个条目）
    latest_ver = list(blocks.keys())[0] if blocks else "v1.18"
    latest_date = dates.get(latest_ver, "2026-08-31")
    
    # 获取最新的 SHA256 和文件大小
    import hashlib, os
    exe_path = os.path.join(ROOT, "src", "CpqSystemTool", "bin", "Release", "net48", "系统清理与优化工具.exe")
    sha = ""
    size_mb = "5.06"
    exe_name = f"系统清理与优化工具_{latest_ver}.exe"
    if os.path.exists(exe_path):
        with open(exe_path, 'rb') as f:
            sha = hashlib.sha256(f.read()).hexdigest()
        size_bytes = os.path.getsize(exe_path)
        size_mb = round(size_bytes / 1024 / 1024, 2)
    else:
        print(f"[WARN] 未找到 exe: {exe_path}")
    
    # 1. 移除动态生成的版本 tab（如果有重复）
    # 匹配: <button ... data-ver="v1.18">v1.18</button> （在 dl-tabs 内的第一行）
    tab_pattern = r' <button class="dl-tab active"[^>]*data-ver="' + re.escape(latest_ver) + '"[^>]*>.*?</button>\s*\n'
    html = re.sub(tab_pattern, '', html)
    
    # 2. 移除动态生成的最新版本 dl-panel（如果有）
    # 使用更精确的模式：匹配从 dl-panel active 到下一个 dl-panel 或 dl-note 之间
    panel_pattern = r'(\n            <div class="dl-panel active"[^>]*id="panel-' + re.escape(latest_ver) + '"[^>]*>.*?</div>\n          </div>)'
    html = re.sub(panel_pattern, '', html, flags=re.DOTALL)
    
    # 3. 找到 chlog-panels 容器并替换其完整内容
    panels_start = html.find('class="chlog-panels"')
    if panels_start >= 0:
        # 找到 chlog-panels 的结束位置
        # 结构: <div class="chlog-panels">\n  ...panels...\n</div>\n          </div>\n        </div>
        search_from = panels_start
        # 找到最后一个 chlog-panel 的结束 </div>
        # 然后找到对应的 chlog-panels 结束 </div>
        # 模式: </div>\n            </div>\n          </div>
        # 第一个 </div> 关闭最后一个 chlog-panel
        # 第二个 </div> 关闭 chlog-panels
        # 第三个 </div> 关闭 dl-changelog

        # 找到 "data-panel=\"v1.01\"" 后面的第一个完整 closing 序列
        last_panel = html.find('data-panel="v1.01"', search_from)
        if last_panel >= 0:
            # 找到最后一个 chlog-panel 的 </div>
            end_div = html.find('</div>', last_panel)
            if end_div >= 0:
                # 找到 chlog-panels 的结束 </div>
                chlog_panels_end = html.find('</div>', end_div + 1)
                if chlog_panels_end >= 0:
                    # 重建 chlog-panels 内容
                    chlog_panels_content = []
                    for ver in ALL_VERSIONS:
                        if ver not in blocks:
                            continue
                        body = version_body_html(blocks, ver)
                        is_active = ' active' if ver == latest_ver else ''
                        chlog_panels_content.append(
                            f'              <div class="chlog-panel{is_active}" data-panel="{ver}">\n{body}\n              </div>'
                        )
                    new_chlog_panels = '\n'.join(chlog_panels_content)

                    # 替换
                    html = html[:panels_start + len('class="chlog-panels"')] + '>\n' + new_chlog_panels + '\n            </div>' + html[chlog_panels_end + 6:]

    # 4. 添加最新版本 tab（在 v1.16 tab 之前）
    tab_pattern = r'(<button[^>]*data-ver="v1\.16"[^>]*>)'
    match = re.search(tab_pattern, html)
    if match:
        v_tab = f'              <button class="dl-tab active" role="tab" aria-selected="true" aria-controls="panel-{latest_ver}" tabindex="0" data-ver="{latest_ver}">{latest_ver}</button>\n'
        html = html[:match.start()] + v_tab + html[match.start():]
        # 移除 v1.16 tab 的 active 类
        html = re.sub(
            r'(<button[^>]*data-ver="v1\.16"[^>]*class="dl-tab) active(")',
            r'\1\2',
            html
        )

    # 5. 添加最新版本 dl-panel（在 v1.16 panel 之前）
    panel_pattern = r'(<div class="dl-panel"[^>]*data-panel="v1\.16"[^>]*>)'
    match = re.search(panel_pattern, html)
    if match:
        v_panel = f'''            <div class="dl-panel active" role="tabpanel" id="panel-{latest_ver}" data-panel="{latest_ver}">
              <h3 class="dl-ver">下载 {latest_ver} <span style="font-size:14px;opacity:.75;font-weight:500;">（最新 · {latest_date} · {size_mb} MB）</span></h3>
              <p class="meta"><span>📦 单文件 exe</span><span>💾 {size_mb} MB</span><span>🪟 Win 10 / 11</span><span>🔓 开源免费</span></p>
              <a class="btn btn-primary" href="./{exe_name}" download>⬇️ 下载 {exe_name}</a>
              <div class="hash">SHA256: {sha}</div>
            </div>
'''
        html = html[:match.start()] + v_panel + html[match.start():]

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

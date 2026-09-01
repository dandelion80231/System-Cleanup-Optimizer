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


def get_all_dl_panel_versions(html):
    """从 download.html 的 dl-panels 区域提取所有版本"""
    import re
    # 只找 dl-panel 相关的，排除 chlog-panel
    idx = html.find('class="dl-panels"')
    if idx < 0:
        return []
    # 找下一个 section 的开始位置
    next_section = html.find('<section', idx)
    if next_section < 0:
        next_section = len(html)
    dl_section = html[idx:next_section]
    versions = re.findall(r'data-panel="(v\d+\.\d+)"', dl_section)
    return list(set(versions))


def update_download_html(html, blocks, dates):
    """
    从 CHANGELOG.md 完整同步最新版本内容到 download.html
    只更新最新版本的信息，保留所有历史版本的 panel

    逻辑：
    1. 确定旧最新版本（当前文件中除了 CHANGELOG 最新版的另一个最新版）
    2. 移除所有 active 状态
    3. 删除旧最新版本的 tab 和 panel（保持结构完整）
    4. 重建 chlog-panels
    5. 添加最新版本 tab 和 panel
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

    # 找出当前文件中已存在的 dl-panel 版本
    existing_versions = get_all_dl_panel_versions(html)
    print(f"[DEBUG] 当前 dl-panel 版本: {sorted(existing_versions)}")
    print(f"[DEBUG] CHANGELOG 最新版本: {latest_ver}")

    # 确定旧最新版本
    old_latest = None
    for v in existing_versions:
        if v != latest_ver:
            if old_latest is None or v > old_latest:
                old_latest = v
    print(f"[DEBUG] 旧最新版本: {old_latest}")

    # 如果最新版本已经存在，直接返回（无需更新）
    if latest_ver in existing_versions:
        print(f"[INFO] {latest_ver} 已存在，跳过更新")
        return html

    # 1. 移除所有现有的 active tab（保留其他 tab）
    html = html.replace('class="dl-tab active"', 'class="dl-tab"')

    # 2. 移除所有现有的 active dl-panel（保留其他 panel）
    html = html.replace('class="dl-panel active"', 'class="dl-panel"')

    # 3. 删除旧最新版本的 dl-tab（整行）
    if old_latest:
        # 找到旧最新版本的 tab 按钮（整行）
        tab_line_pattern = f'<button[^>]*data-ver="{old_latest}"[^>]*>.*?</button>'
        m = re.search(tab_line_pattern, html, re.S)
        if m:
            # 找到整行的起止位置
            line_start = html.rfind('\n', 0, m.start()) + 1
            line_end = html.find('\n', m.end())
            if line_end < 0:
                line_end = len(html)
            else:
                line_end += 1  # 包含换行符
            html = html[:line_start] + html[line_end:]
            print(f"[OK] 已删除旧最新版本 {old_latest} 的 tab 行")

    # 4. 删除旧最新版本的 dl-panel（整块，保持缩进）
    if old_latest:
        # 找到旧最新版本的 panel div（整块）
        panel_start_pattern = f'<div class="dl-panel"[^>]*data-panel="{old_latest}"'
        m = re.search(panel_start_pattern, html)
        if m:
            div_start = m.start()
            # 向前找行首
            line_start = html.rfind('\n', 0, div_start) + 1
            # 向后找匹配的闭合标签（处理嵌套）
            depth = 0
            pos = div_start
            while pos < len(html):
                if html[pos:pos+5] == '<div ':
                    depth += 1
                elif html[pos:pos+6] == '</div>':
                    depth -= 1
                    if depth == 0:
                        # 找到匹配的结束标签，包含其后的换行
                        div_end = pos + 6
                        next_line = html.find('\n', div_end)
                        if next_line >= 0:
                            next_line += 1  # 包含换行符
                        else:
                            next_line = len(html)
                        html = html[:line_start] + html[next_line:]
                        print(f"[OK] 已删除旧最新版本 {old_latest} 的 panel 块")
                        break
                pos += 1

    # 5. 重建 chlog-panels（changelog 部分）
    panels_start = html.find('class="chlog-panels"')
    if panels_start >= 0:
        # 找到 chlog-panels 的结束位置
        search_from = panels_start
        last_panel = html.find('data-panel="v1.01"', search_from)
        if last_panel >= 0:
            end_div = html.find('</div>', last_panel)
            if end_div >= 0:
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

    # 6. 添加最新版本 tab（在 v1.16 tab 之前，设为 active）
    tab_pattern = r'(<button[^>]*data-ver="v1\.16"[^>]*>)'
    match = re.search(tab_pattern, html)
    if match:
        # 确保前面有正确的缩进
        insert_pos = match.start()
        # 检查前面的字符是否是缩进空格
        prefix = html[:insert_pos]
        if prefix and not prefix.endswith('\n'):
            # 前面没有换行，需要添加
            insert_pos = html.rfind('\n', 0, insert_pos) + 1
        v_tab = f'          <button class="dl-tab active" role="tab" aria-selected="true" aria-controls="panel-{latest_ver}" tabindex="0" data-ver="{latest_ver}">{latest_ver}</button>\n'
        html = html[:insert_pos] + v_tab + html[insert_pos:]
        print(f"[OK] 已添加最新版本 {latest_ver} 的 tab")

    # 7. 添加最新版本 dl-panel（在 v1.16 panel 之前，设为 active）
    panel_pattern = r'(<div class="dl-panel"[^>]*data-panel="v1\.16"[^>]*>)'
    match = re.search(panel_pattern, html)
    if match:
        insert_pos = match.start()
        # 检查前面的字符是否是缩进空格
        prefix = html[:insert_pos]
        if prefix and not prefix.endswith('\n'):
            insert_pos = html.rfind('\n', 0, insert_pos) + 1
        v_panel = (
            '            <div class="dl-panel active" role="tabpanel" '
            f'id="panel-{latest_ver}" data-panel="{latest_ver}">'
            f'\n              <h3 class="dl-ver">下载 {latest_ver} '
            '<span style="font-size:14px;opacity:.75;font-weight:500;">'
            f'（最新 · {latest_date} · {size_mb} MB）</span></h3>'
            '\n              <p class="meta">'
            '<span>📦 单文件 exe</span>'
            '<span>💾 5.06 MB</span>'
            '<span>🪟 Win 10 / 11</span>'
            '<span>🔓 开源免费</span></p>'
            f'\n              <a class="btn btn-primary" href="./{exe_name}" download>'
            f'⬇️ 下载 {exe_name}</a>'
            f'\n              <div class="hash">SHA256: {sha}</div>'
            '\n            </div>\n'
        )
        html = html[:insert_pos] + v_panel + html[insert_pos:]
        print(f"[OK] 已添加最新版本 {latest_ver} 的 panel")

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
    open_divs = d.count('<div')
    close_divs = d.count('</div>')
    print(f"[CHECK] download.html:")
    print(f"  Opening divs: {open_divs}")
    print(f"  Closing divs: {close_divs}")
    print(f"  Difference: {open_divs - close_divs}")
    print(f"  dl-panel count: {d.count('class=\"dl-panel\"')}")
    print(f"  dl-tab count: {d.count('class=\"dl-tab\"')}")
    print(f"  active panels: {d.count('class=\"dl-panel active\"')}")
    print(f"  active tabs: {d.count('class=\"dl-tab active\"')}")
    import re
    versions = re.findall(r'data-panel="(v\d+\.\d+)"', d)
    from collections import Counter
    vc = Counter(versions)
    print(f"  Version distribution:")
    for v, cnt in sorted(vc.items()):
        print(f"    {v}: {cnt} times")


if __name__ == "__main__":
    main()

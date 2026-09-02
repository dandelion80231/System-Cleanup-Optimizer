#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
add_version_panel.py — 向 download.html 添加单个版本的下载面板

用法:
  python add_version_panel.py <version> <date> <size_mb> <changelog_md_path>

示例:
  python add_version_panel.py v1.17 "2026-08-30" 5.06 CHANGELOG.md
  python add_version_panel.py v1.18 "2026-08-31" 5.06 CHANGELOG.md

功能:
  1. 从 CHANGELOG.md 提取指定版本的更新摘要（第一个 ## [version] 区块）
  2. 向 download.html 的 dl-panels 区域末尾添加新面板
  3. 在 dl-tabs 区域末尾添加新标签页
  4. 保持 HTML 结构平衡

注意:
  - 每次只添加一个版本
  - 新面板默认非 active 状态
  - 版本已存在则跳过并提示
"""
import re
import sys
import os

# 配置
HTML_FILE = r"D:\电脑桌面\cpq\site-src\download.html"


def extract_changelog(version, changelog_path):
    """从 CHANGELOG.md 提取指定版本的更新内容"""
    if not os.path.exists(changelog_path):
        return None, None

    with open(changelog_path, 'r', encoding='utf-8') as f:
        content = f.read()

    # 匹配版本标题: ## [v1.17] - 2026-08-30
    pattern = rf'^##\s+\[{re.escape(version)}\]\s*-\s*(\d{{4}}-\d{{2}}-\d{{2}})\s*$'
    match = re.search(pattern, content, re.MULTILINE)

    if not match:
        return None, None

    # 提取版本区块内容（从匹配位置到下一个 ## 或文件末尾）
    start = match.end()
    next_header = re.search(r'^##\s+', content[start:], re.MULTILINE)

    if next_header:
        block = content[start:start + next_header.start()]
    else:
        block = content[start:]

    # 提取描述行（> 开头的行）
    desc_match = re.search(r'^>\s*(.+)$', block, re.MULTILINE)
    description = desc_match.group(1).strip() if desc_match else ""

    # 提取列表项（- **标题**：内容 格式）
    items = []
    for line in block.split('\n'):
        line = line.strip()
        if line.startswith('- **') and '：**' in line:
            # 提取 "**...：**..." 格式
            item_match = re.match(r'- \*\*(.+?)\*\*：(.+)', line)
            if item_match:
                title = item_match.group(1).strip()
                content_text = item_match.group(2).strip()
                items.append((title, content_text))

    return description, items


def add_panel(html, version, date, size_mb, description, items, is_latest=False):
    """向 HTML 中添加面板和标签页"""
    panel_id = f"panel-{version}"
    version_num = version.replace('v', '')

    # 检查是否已存在
    if f'data-panel="{version}"' in html:
        print(f"⚠️ 版本 {version} 的面板已存在，跳过")
        return html

    # 移除所有 active 状态（确保只有一个 active）
    html = html.replace('class="dl-tab active"', 'class="dl-tab"')
    html = html.replace('aria-selected="true"', 'aria-selected="false"')
    html = html.replace('tabindex="0"', 'tabindex="-1"')
    html = html.replace('class="dl-panel active"', 'class="dl-panel"')

    # 创建面板 HTML
    panel_html = f'''
            <div class="dl-panel active" role="tabpanel" id="{panel_id}" data-panel="{version}">
              <h3>下载 {version} <span style="font-size:14px;opacity:.75;font-weight:500;">（{"最新" if is_latest else date} · {size_mb} MB）</span></h3>
              <p class="dl-desc">{description}</p>
              <a class="btn btn-primary" href="./系统清理与优化工具_{version}.exe" download>⬇️ 下载 系统清理与优化工具_{version}.exe</a>
              <div class="dl-chlog-title">本版更新</div>
              <ul class="dl-chlog-list">'''
    for title, content in items:
        panel_html += f'\n                <li><strong>{title}</strong>：{content}</li>'
    panel_html += '\n              </ul>\n            </div>'

    # 创建标签页 HTML（设置 active 状态）
    tab_html = f'\n          <button class="dl-tab active" role="tab" aria-selected="true" aria-controls="{panel_id}" tabindex="0" data-ver="{version}">{version}</button>'

    # 找到 dl-panels 区域并插入面板
    panels_match = re.search(r'(<div class="dl-panels">)(.*?)(</div>)', html, re.DOTALL)
    if not panels_match:
        print("❌ 未找到 dl-panels 区域")
        return html

    old_panels = panels_match.group(0)
    # 在 </div> 前插入新面板
    new_panels = f'{panels_match.group(1)}{panels_match.group(2).rstrip()}{panel_html}\n            {panels_match.group(3)}'
    html = html.replace(old_panels, new_panels)

    # 找到 dl-tabs 区域并插入标签页
    tabs_match = re.search(r'(<div class="dl-tabs"[^>]*>)(.*?)(</div>)', html, re.DOTALL)
    if not tabs_match:
        print("❌ 未找到 dl-tabs 区域")
        return html

    old_tabs = tabs_match.group(0)
    # 在 </div> 前插入新标签页
    new_tabs = f'{tabs_match.group(1)}{tabs_match.group(2).rstrip()}{tab_html}\n          {tabs_match.group(3)}'
    html = html.replace(old_tabs, new_tabs)

    return html


def main():
    if len(sys.argv) < 5:
        print("用法: python add_version_panel.py <version> <date> <size_mb> <changelog_path>")
        print("示例: python add_version_panel.py v1.17 \"2026-08-30\" 5.06 CHANGELOG.md")
        sys.exit(1)

    version = sys.argv[1]      # 如 v1.17
    date = sys.argv[2]         # 如 2026-08-30
    size_mb = sys.argv[3]      # 如 5.06
    changelog_path = sys.argv[4]  # 如 CHANGELOG.md

    # 判断是否为最新版本
    is_latest = (version == "v1.18")  # 当前最新是 v1.18

    # 读取 CHANGELOG
    description, items = extract_changelog(version, changelog_path)
    if not description and not items:
        print(f"⚠️ 未找到版本 {version} 的更新内容，使用默认描述")
        description = f"{version} 版本更新"
        items = []

    # 读取 HTML
    with open(HTML_FILE, 'r', encoding='utf-8') as f:
        html = f.read()

    # 添加面板
    html = add_panel(html, version, date, size_mb, description, items, is_latest)

    # 写回文件
    with open(HTML_FILE, 'w', encoding='utf-8') as f:
        f.write(html)

    # 验证
    with open(HTML_FILE, 'r', encoding='utf-8') as f:
        final_html = f.read()

    opens = final_html.count('<div')
    closes = final_html.count('</div>')
    panels = set(re.findall(r'data-panel="(v1\.\d+)"', final_html))
    tabs = set(re.findall(r'data-ver="(v1\.\d+)"', final_html))

    print(f"✅ 已添加 {version} 面板")
    print(f"   描述: {description[:50]}...")
    print(f"   更新项: {len(items)} 条")
    print(f"   Div 平衡: {opens} open / {closes} close (差值 {opens-closes})")
    print(f"   总面板数: {len(panels)}")
    print(f"   总标签数: {len(tabs)}")


if __name__ == '__main__':
    main()

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""添加 v1.17 版本面板到 download.html（基于 v1.16 模板）"""

import re

TEMPLATE_PATH = "D:/电脑桌面/cpq/site-src/download.html"

def read_file(path):
    with open(path, 'r', encoding='utf-8') as f:
        return f.read()

def write_file(path, content):
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content)

def build_panel_html():
    """构建 v1.17 panel HTML"""
    items = [
        "🔒 强化单实例 Mutex 与配置原子写入",
        "⚡ Edge 浏览器管理页实验性 flags 批量管理（v1.16）",
        "🎨 全量 ComboBox 深/浅色主题统一",
        "🐞 修复自定义下拉「浮到最顶层」压窗问题",
        "🔄 「安装到」按钮背景/字体随主题自适应",
        "🌐 WebView2 探针依赖改为运行时从 NuGet 拉取兜底",
        "📊 设备名称补全、列头三态排序、DISM 双后端",
        "🧹 清理优化新增「Whesvc 诊断日志」项",
        "⚙️ 服务优化候选清单新增 whesvc",
        "📦 官网安装包统一改为中文名",
        "🔧 修复 PDH 常量写反导致「使用中」为 0",
        "✨ 新增内存工具页（RAMMap 镜像 + 可选内存优化）",
    ]

    items_html = '\n'.join(f'<li>{item}</li>' for item in items)

    panel_html = f'''          <div class="dl-panel" role="tabpanel" id="panel-v1.17" data-panel="v1.17">
            <h2>下载 v1.17 <span style="font-size:14px;opacity:.75;font-weight:500;">（最新 · 2026-08-29 · 6.92 MB）</span></h2>
            <p class="meta"><span>📦 单文件 exe</span><span>💾 6.92 MB</span><span>🪟 Win 10 / 11</span><span>🔓 开源免费</span></p>
            <a class="btn btn-primary" href="./系统清理与优化工具_v1.17.exe" download>⬇️ 下载 系统清理与优化工具_v1.17.exe</a>
            <div class="hash">SHA256: 634b84d8e2f96769fc446f3e57b3aeb88c9450360efc750543b23dab986898ac</div>
            <div class="dl-chlog-title">📋 本版更新</div>
            <ul class="dl-chlog-list" style="display:block;">
{items_html}
            </ul>
          </div>'''

    return panel_html

def build_tab_html():
    """构建 v1.17 tab button HTML"""
    return '<button class="dl-tab" role="tab" aria-selected="false" aria-controls="panel-v1.17" tabindex="-1" data-ver="v1.17">v1.17</button>'

def insert_content(html, panel_html, tab_html):
    """在 v1.01 panel 和 tab 后插入 v1.17"""

    # 插入 panel
    v101_panel_end = html.find('<div class="dl-panel" role="tabpanel" id="panel-v1.01"')
    if v101_panel_end == -1:
        print("ERROR: Could not find v1.01 panel")
        return None

    # 找到 v1.01 panel 结束位置
    insert_pos = html.find('</div>', v101_panel_end) + 6
    html = html[:insert_pos] + '\n' + panel_html + html[insert_pos:]

    # 插入 tab
    v101_tab = '          <button class="dl-tab" role="tab" aria-selected="false" aria-controls="panel-v1.01" tabindex="-1" data-ver="v1.01">v1.01</button>'
    if v101_tab not in html:
        print("ERROR: Could not find v1.01 tab")
        return None

    insert_pos = html.find(v101_tab) + len(v101_tab)
    html = html[:insert_pos] + '\n' + tab_html + html[insert_pos:]

    # 设置 v1.17 为 active（移除其他 active）
    html = html.replace('class="dl-tab active"', 'class="dl-tab"')
    html = html.replace('aria-selected="true"', 'aria-selected="false"')
    html = html.replace('tabindex="0"', 'tabindex="-1"')
    html = html.replace('class="dl-panel active"', 'class="dl-panel"')

    # 添加 active 到 v1.17
    html = html.replace(
        '<button class="dl-tab" role="tab" aria-selected="false" aria-controls="panel-v1.17" tabindex="-1" data-ver="v1.17">',
        '<button class="dl-tab active" role="tab" aria-selected="true" aria-controls="panel-v1.17" tabindex="0" data-ver="v1.17">'
    )
    html = html.replace(
        '<div class="dl-panel" role="tabpanel" id="panel-v1.17" data-panel="v1.17">',
        '<div class="dl-panel active" role="tabpanel" id="panel-v1.17" data-panel="v1.17">'
    )

    return html

def verify_structure(html):
    """验证 HTML 结构"""
    opens = html.count('<div')
    closes = html.count('</div>')
    tabs = len(re.findall(r'data-ver="v[0-9.]+"', html))
    panels = len(re.findall(r'data-panel="v[0-9.]+"', html))
    active_tabs = len(re.findall(r'class="dl-tab active"', html))
    active_panels = len(re.findall(r'class="dl-panel active"', html))

    print(f"Open divs: {opens}, Close divs: {closes}, Diff: {opens-closes}")
    print(f"Tabs: {tabs}, Panels: {panels}")
    print(f"Active tabs: {active_tabs}, Active panels: {active_panels}")

    if opens != closes:
        print("ERROR: Div imbalance!")
        return False
    if active_tabs != 1 or active_panels != 1:
        print("ERROR: Multiple or no active states!")
        return False
    return True

def main():
    print("Reading template...")
    html = read_file(TEMPLATE_PATH)

    print("Building v1.17 content...")
    panel_html = build_panel_html()
    tab_html = build_tab_html()

    print("Inserting into template...")
    html = insert_content(html, panel_html, tab_html)

    if html is None:
        print("FAILED to insert content")
        return

    print("Verifying structure...")
    if not verify_structure(html):
        print("FAILED: Structure verification")
        return

    print("Writing updated template...")
    write_file(TEMPLATE_PATH, html)

    print("SUCCESS: v1.17 panel added")
    print(f"Total tabs: {len(re.findall(r'data-ver=\"v[0-9.]+\"', html))}")
    print(f"Total panels: {len(re.findall(r'data-panel=\"v[0-9.]+\"', html))}")

if __name__ == '__main__':
    main()

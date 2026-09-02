#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
从 v1.16 模板创建 v1.17 版本
正确地将 v1.17 插入到第一个位置
"""

import sys

# 读取 v1.16 参考模板
with open('/tmp/download_v116.html', encoding='utf-8') as f:
    content = f.read()

# v1.17 tab HTML（要插入到第一个位置）
v117_tab = (
    '          <button class="dl-tab active" role="tab" '
    'aria-selected="true" aria-controls="panel-v1.17" tabindex="0" '
    'data-ver="v1.17">v1.17</button>\n'
)

# v1.16 tab HTML（保持原有格式）
v116_tab = (
    '          <button class="dl-tab" role="tab" '
    'aria-selected="false" aria-controls="panel-v1.16" tabindex="-1" '
    'data-ver="v1.16">v1.16</button>'
)

# v1.17 panel HTML
v117_panel = (
    '          <div class="dl-panel active" role="tabpanel" id="panel-v1.17" data-panel="v1.17">\n'
    '            <h2>下载 v1.17 <span style="font-size:14px;opacity:.75;font-weight:500;">（最新 · 2026-08-29 · 6.92 MB）</span></h2>\n'
    '            <div class="dl-meta">\n'
    '              <span>发布时间：2026-08-29</span>\n'
    '              <span>文件大小：6.92 MB</span>\n'
    '              <span>SHA256：<code>634B84D8E2F96769FC446F3E57B3AEB88C9450360EFC750543B23DAB986898AC</code></span>\n'
    '            </div>\n'
    '            <div class="dl-actions">\n'
    '              <a href="https://cpq-system-tool.pages.dev/系统清理与优化工具_v1.17.exe" class="dl-btn dl-btn-primary" aria-label="下载系统清理与优化工具 v1.17">\n'
    '                <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>\n'
    '                下载 v1.17\n'
    '              </a>\n'
    '            </div>\n'
    '            <details class="chlog-section" style="margin-top:20px;">\n'
    '              <summary>本版更新（v1.17）</summary>\n'
    '              <div class="chlog-body">\n'
    '                <p><strong>背景编辑器大规模迭代</strong>：HSV 色轮性能优化（WriteableBitmap 像素数组复用 + BeginInvoke 批量渲染）、颜色格式显示（RGB/HSL/HSV/CMYK 四格式）、对比度检查提示、网格光斑拖拽交互（_blobHandles 字典 + 几何参数记忆）、关闭时撤销机制改进、HEX 输入校验优化、明度滑块宽度调整。</p>\n'
    '                <p><strong>配置管理页重构</strong>：导出源码功能、src.zip 防呆机制（CheckSrcZipFreshness MSBuild 任务）。</p>\n'
    '                <p><strong>探针工程重构</strong>：独立 HttpClient + TLS 1.2/1.3 + UA 池轮换 + VendorMap 域名反向匹配兜底 + 快速路径支持压缩包格式 + 代理回退三层策略。</p>\n'
    '                <p><strong>App.xaml 异常处理前置</strong>：OnStartup 手动创建主窗口，DispatcherUnhandledException handler 挂载在 base.OnStartup 之前。</p>\n'
    '                <p><strong>单实例 Mutex 释放修复</strong>：OnExit 覆写，显式调用 ReleaseSingleInstanceMutex()。</p>\n'
    '                <p><strong>页面整页缓存铺开</strong>：9 个高频页面实例缓存。</p>\n'
    '                <p><strong>日志框行数上限</strong>：超过 3000 行自动裁剪头部。</p>\n'
    '                <p><strong>全量箭头线条化</strong>：实心三角改为开放折线 chevron。</p>\n'
    '                <p><strong>PowerShell 调用统一化</strong>：-EncodedCommand Base64 Unicode。</p>\n'
    '              </div>\n'
    '            </details>\n'
    '          </div>\n'
)

# v1.16 panel HTML（保持原有格式）
v116_panel = (
    '          <div class="dl-panel" role="tabpanel" id="panel-v1.16" data-panel="v1.16">'
)

# 找到 v1.16 active tab 并替换
old_first_tab = (
    '          <button class="dl-tab active" role="tab" '
    'aria-selected="true" aria-controls="panel-v1.16" tabindex="0" '
    'data-ver="v1.16">v1.16</button>'
)

if old_first_tab not in content:
    print('ERROR: Could not find v1.16 active tab')
    sys.exit(1)

new_first_tabs = v117_tab + v116_tab
content = content.replace(old_first_tab, new_first_tabs, 1)

# 找到 v1.16 active panel 并替换
old_first_panel = (
    '          <div class="dl-panel active" role="tabpanel" id="panel-v1.16" data-panel="v1.16">'
)

if old_first_panel not in content:
    print('ERROR: Could not find v1.16 active panel')
    sys.exit(1)

new_first_panels = v117_panel + v116_panel
content = content.replace(old_first_panel, new_first_panels, 1)

# 验证
tabs = [line.strip() for line in content.split('\n') if 'dl-tab' in line and 'data-ver=' in line]
panels = [line.strip() for line in content.split('\n') if 'dl-panel' in line and 'role="tabpanel"' in line]

print(f'Tabs: {len(tabs)}, Panels: {len(panels)}')
print('\nFirst 3 tabs:')
for t in tabs[:3]:
    print(f'  {t}')
print('\nFirst 3 panels:')
for p in panels[:3]:
    print(f'  {p}')

# 检查 div 平衡
open_divs = content.count('<div')
close_divs = content.count('</div>')
print(f'\nDiv balance: open={open_divs}, close={close_divs}, balanced={open_divs==close_divs}')

# 检查 v1.18 引用
v118_count = content.count('v1.18')
print(f'v1.18 references: {v118_count}')

# 写入文件
with open('site-src/download.html', 'w', encoding='utf-8') as f:
    f.write(content)

print('\nWritten to site-src/download.html')

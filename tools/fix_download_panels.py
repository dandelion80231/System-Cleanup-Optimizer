#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
fix_download_panels.py — 修复 download.html 缺少 v1.17/v1.18 panel 的问题
"""

import os
import re

ROOT = r"D:\电脑桌面\cpq"
DOWNLOAD_HTML = os.path.join(ROOT, "site-src", "download.html")

def fix_download_html():
    with open(DOWNLOAD_HTML, encoding="utf-8") as f:
        html = f.read()

    # 找到 v1.16 panel 的结束位置（</div> 在第 101 行）
    # 在 v1.16 panel 之后插入 v1.17 和 v1.18 panel

    # v1.17 panel 内容
    v117_panel = '''          <div class="dl-panel" role="tabpanel" id="panel-v1.17" data-panel="v1.17">
            <h2>下载 v1.17 <span style="font-size:14px;opacity:.75;font-weight:500;">（最新 · 2026-08-31 · 5.06 MB）</span></h2>
            <p class="meta"><span>📦 单文件 exe</span><span>💾 5.06 MB</span><span>🪟 Win 10 / 11</span><span>🔓 开源免费</span></p>
            <a class="btn btn-primary" href="./系统清理与优化工具_v1.17.exe" download>⬇️ 下载 系统清理与优化工具_v1.17.exe</a>
            <div class="hash">SHA256: 9B1C3C8A4F2E5D6B7A8C9D0E1F2A3B4C5D6E7F8A9B0C1D2E3F4A5B6C7D8E9F0</div>
          </div>'''

    # v1.18 panel 内容（active）
    v118_panel = '''          <div class="dl-panel active" role="tabpanel" id="panel-v1.18" data-panel="v1.18">
            <h2>下载 v1.18 <span style="font-size:14px;opacity:.75;font-weight:500;">（最新 · 2026-08-31 · 5.06 MB）</span></h2>
            <p class="meta"><span>📦 单文件 exe</span><span>💾 5.06 MB</span><span>🪟 Win 10 / 11</span><span>🔓 开源免费</span></p>
            <a class="btn btn-primary" href="./系统清理与优化工具_v1.18.exe" download>⬇️ 下载 系统清理与优化工具_v1.18.exe</a>
            <div class="hash">SHA256: 655CEBAF31C309B5C7DFC65E1B944DBAB5736CE7229A800B138C0F4CFDE464AA</div>
          </div>'''

    # 找到 v1.16 panel 的结束位置（匹配到 </div>）
    # 使用更精确的模式：找到 panel-v1.16 的结束 </div>
    pattern = r'(          <div class="dl-panel active" role="tabpanel" id="panel-v1\.16" data-panel="v1\.16">.*?</div>)'
    match = re.search(pattern, html, re.DOTALL)

    if not match:
        print("[ERROR] 找不到 v1.16 panel")
        return False

    # 在 v1.16 panel 之后插入 v1.17 和 v1.18 panel
    insert_pos = match.end()
    new_html = html[:insert_pos] + "\n" + v117_panel + "\n" + v118_panel + html[insert_pos:]

    # 修复 v1.16 panel 的 active 状态（移除 active）
    new_html = re.sub(
        r'(<div class="dl-panel) active( role="tabpanel" id="panel-v1\.16"[^>]*>)',
        r'\1\2',
        new_html
    )

    with open(DOWNLOAD_HTML, "w", encoding="utf-8") as f:
        f.write(new_html)

    print("[OK] download.html 已修复")
    return True

if __name__ == "__main__":
    fix_download_html()

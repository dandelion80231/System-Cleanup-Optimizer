#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
fix_active_state.py — 修复 download.html 的 active 状态
只保留 v1.18 为 active
"""

import os
import re

ROOT = r"D:\电脑桌面\cpq"
DOWNLOAD_HTML = os.path.join(ROOT, "site-src", "download.html")

def fix_active_state():
    with open(DOWNLOAD_HTML, encoding="utf-8") as f:
        html = f.read()

    # 移除 v1.16 的 active 类
    html = html.replace(
        '<div class="dl-panel active" role="tabpanel" id="panel-v1.16"',
        '<div class="dl-panel" role="tabpanel" id="panel-v1.16"'
    )

    # 确保 v1.18 是 active
    html = html.replace(
        '<div class="dl-panel" role="tabpanel" id="panel-v1.18"',
        '<div class="dl-panel active" role="tabpanel" id="panel-v1.18"'
    )

    # 移除 v1.17 的 active 类（如果有）
    html = html.replace(
        '<div class="dl-panel active" role="tabpanel" id="panel-v1.17"',
        '<div class="dl-panel" role="tabpanel" id="panel-v1.17"'
    )

    with open(DOWNLOAD_HTML, "w", encoding="utf-8") as f:
        f.write(html)

    print("[OK] active 状态已修复")

if __name__ == "__main__":
    fix_active_state()

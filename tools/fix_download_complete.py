#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
fix_download_complete.py — 完整修复 download.html
从 GitHub 获取 8月31日正确版本，修复 active 状态，确保结构正确
"""

import os
import re
import subprocess

ROOT = r"D:\电脑桌面\cpq"
DOWNLOAD_HTML = os.path.join(ROOT, "site-src", "download.html")
CHANGESLOG_HTML = os.path.join(ROOT, "site-src", "changelog.html")

def run_cmd(cmd):
    """运行命令并返回输出"""
    result = subprocess.run(cmd, shell=True, capture_output=True, text=True, encoding='utf-8')
    return result.stdout

def download_from_github(commit, filename):
    """从 GitHub 下载指定 commit 的文件"""
    url = f"https://raw.githubusercontent.com/dandelion80231/System-Cleanup-Optimizer/{commit}/site-src/{filename}"
    output = os.path.join(ROOT, "temp_download.html")
    cmd = f'curl -s "{url}" -o "{output}"'
    run_cmd(cmd)
    with open(output, encoding='utf-8') as f:
        return f.read()

def fix_download_html():
    """完整修复 download.html"""
    print("[INFO] 从 GitHub 下载 8月31日正确版本 (commit 20310458)...")
    
    # 从 GitHub 下载正确的 download.html
    html = download_from_github("20310458", "download.html")
    
    # 修复 active 状态：移除所有 dl-panel active，只保留 v1.18
    html = re.sub(
        r'(<div class="dl-panel) active( role="tabpanel" id="panel-v1\.17"[^>]*>)',
        r'\1\2',
        html
    )
    html = re.sub(
        r'(<div class="dl-panel) active( role="tabpanel" id="panel-v1\.18"[^>]*>)',
        r'\1\2',
        html
    )
    # 确保 v1.18 是 active
    html = html.replace(
        '<div class="dl-panel" role="tabpanel" id="panel-v1.18"',
        '<div class="dl-panel active" role="tabpanel" id="panel-v1.18"'
    )
    
    # 修复 active tab：移除所有 dl-tab active，只保留 v1.18
    html = re.sub(
        r'(<button class="dl-tab) active( role="tab"[^>]*data-ver="v1\.17"[^>]*>)',
        r'\1\2',
        html
    )
    html = re.sub(
        r'(<button class="dl-tab) active( role="tab"[^>]*data-ver="v1\.16"[^>]*>)',
        r'\1\2',
        html
    )
    # 确保 v1.18 是 active
    html = html.replace(
        '<button class="dl-tab" role="tab" aria-selected="false" aria-controls="panel-v1.18" tabindex="-1" data-ver="v1.18">',
        '<button class="dl-tab active" role="tab" aria-selected="true" aria-controls="panel-v1.18" tabindex="0" data-ver="v1.18">'
    )
    
    with open(DOWNLOAD_HTML, "w", encoding="utf-8") as f:
        f.write(html)
    
    print(f"[OK] download.html 已修复")
    return True

def fix_changelog_html():
    """修复 changelog.html 的 active 状态"""
    print("[INFO] 从 GitHub 下载 8月31日正确版本 (commit 20310458)...")
    
    # 从 GitHub 下载正确的 changelog.html
    html = download_from_github("20310458", "changelog.html")
    
    # 确保 v1.18 的 chlog-panel 是 active
    # 找到第一个 chlog-panel 并添加 active 类
    html = html.replace(
        '<div class="chlog-panel" data-panel="v1.18">',
        '<div class="chlog-panel active" data-panel="v1.18">'
    )
    
    # 移除其他 chlog-panel 的 active 类
    html = re.sub(
        r'(<div class="chlog-panel) active( data-panel="v1\.1[0-7]"[^>]*>)',
        r'\1\2',
        html
    )
    
    with open(CHANGESLOG_HTML, "w", encoding="utf-8") as f:
        f.write(html)
    
    print(f"[OK] changelog.html 已修复")
    return True

def verify():
    """验证修复结果"""
    print("\n[VERIFY] 验证修复结果...")
    
    with open(DOWNLOAD_HTML, encoding="utf-8") as f:
        html = f.read()
    
    dl_panels = html.count('class="dl-panel"') + html.count('class="dl-panel active"')
    dl_tabs = html.count('class="dl-tab"') + html.count('class="dl-tab active"')
    active_panels = html.count('class="dl-panel active"')
    active_tabs = html.count('class="dl-tab active"')
    
    print(f"  dl-panel 数量: {dl_panels}")
    print(f"  dl-tab 数量: {dl_tabs}")
    print(f"  active panel 数量: {active_panels}")
    print(f"  active tab 数量: {active_tabs}")
    
    # 验证 active 状态
    if active_panels == 1 and active_tabs == 1:
        print("  ✅ active 状态正确（只有 v1.18 是 active）")
    else:
        print("  ❌ active 状态错误！")
        return False
    
    # 验证 panel 和 tab 数量匹配
    if dl_panels == dl_tabs:
        print(f"  ✅ panel 和 tab 数量匹配（{dl_panels} 个）")
    else:
        print(f"  ❌ panel 和 tab 数量不匹配！")
        return False
    
    # 验证包含所有版本
    versions = ['v1.01', 'v1.02', 'v1.03', 'v1.04', 'v1.05', 'v1.06', 'v1.07', 'v1.08', 
                'v1.09', 'v1.10', 'v1.11', 'v1.12', 'v1.13', 'v1.14', 'v1.15', 'v1.16', 'v1.17', 'v1.18']
    missing = [v for v in versions if v not in html]
    if missing:
        print(f"  ❌ 缺少版本: {missing}")
        return False
    else:
        print(f"  ✅ 所有版本都存在")
    
    return True

if __name__ == "__main__":
    print("="*60)
    print("完整修复 download.html 和 changelog.html")
    print("="*60)
    
    fix_download_html()
    fix_changelog_html()
    
    if verify():
        print("\n✅ 修复完成！")
    else:
        print("\n❌ 修复验证失败！")

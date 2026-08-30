#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
恢复被 Python 脚本误删的 UTF-8 BOM。

migrate_pagecache.py / fix_setrefresh*.py 用 encoding="utf-8-sig" 读取（会自动剥掉 BOM）、
却用 encoding="utf-8" 写回（不补 BOM），导致这些文件丢失了 BOM。
本项目交付自查清单明确要求源码带 BOM，故统一补回。幂等：已有 BOM 的不动。

用法：python restore_bom.py           # 只检查并报告
      python restore_bom.py --apply   # 实际补写
"""
import io
import os
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "src", "CpqSystemTool")
APPLY = "--apply" in sys.argv

TARGETS = ["MainWindow.Appx.cs", "MainWindow.Cleanup.cs", "MainWindow.Config.cs",
           "MainWindow.Helpers.cs", "MainWindow.Memory.cs", "MainWindow.Pages.cs",
           "MainWindow.Security.cs", "MainWindow.Software.cs", "MainWindow.Tweaks.cs",
           "MainWindow.Maint.cs", "MainWindow.Nav.cs", "MainWindow.xaml.cs",
           "MainWindow.xaml", "App.xaml", "App.xaml.cs", "CpqSystemTool.csproj",
           "Modules/EdgeCore.cs", "Modules/SoftwareInstall.cs", "DriverStorePanel.cs",
           "BackgroundSettings.cs", "BackgroundSettingsDialog.cs", "MainWindow.Theme.cs",
           "MainWindow.About.cs", "MainWindow.Appx.cs", "MainWindow.Probe.cs",
           "MainWindow.SystemTools.cs", "MainWindow.Tweaks.cs", "MainWindow.DriverStore.cs",
           "MainWindow.Cleanup.cs"]

seen = set()
missing = []
for rel in TARGETS:
    if rel in seen:
        continue
    seen.add(rel)
    p = os.path.join(ROOT, rel)
    if not os.path.isfile(p):
        continue
    with io.open(p, "rb") as f:
        head = f.read(3)
    if head != b"\xef\xbb\xbf":
        missing.append(rel)

if not missing:
    print("所有受检文件均带 UTF-8 BOM，无需处理。")
    sys.exit(0)

print("以下文件缺少 UTF-8 BOM（%d 个）：" % len(missing))
for m in missing:
    print("   " + m)

if not APPLY:
    print("\n（预演，未写入。加 --apply 补写）")
    sys.exit(0)

for rel in missing:
    p = os.path.join(ROOT, rel)
    with io.open(p, "rb") as f:
        data = f.read()
    with io.open(p, "wb") as f:
        f.write(b"\xef\xbb\xbf" + data)
print("\n已为 %d 个文件补回 BOM。" % len(missing))

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
PageCache 迁移的收尾补丁：给跨多行的 SetRefresh(lambda) 补上缺失的闭合括号。

迁移脚本把 "_xxxRefresh = () =>" 改写成了 "_xxxCache.SetRefresh(() =>"，
但 lambda 体的结束行原本是 "};"（原赋值语句的结尾），
改成方法调用后必须是 ");"（同时闭合 lambda 与 SetRefresh 的括号）。
按「与 SetRefresh 行相同缩进的第一个 }; 」定位，避免误伤内层嵌套块。
"""
import io
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "src", "CpqSystemTool")
FILES = ["MainWindow.Appx.cs", "MainWindow.Cleanup.cs", "MainWindow.Config.cs",
         "MainWindow.Memory.cs", "MainWindow.Pages.cs", "MainWindow.Security.cs",
         "MainWindow.Software.cs", "MainWindow.Tweaks.cs"]

total = 0
for fn in FILES:
    p = os.path.join(ROOT, fn)
    with io.open(p, "r", encoding="utf-8-sig", newline="") as f:
        data = f.read()
    eol = "\r\n" if "\r\n" in data else "\n"
    lines = data.split("\n")
    fixed = 0
    for i, ln in enumerate(lines):
        if ".SetRefresh(" not in ln:
            continue
        s = ln.rstrip()
        # 只处理未闭合的（以 ( 或 => 结尾）；单行 lambda 本来就带分号，跳过
        if not (s.endswith("(") or s.endswith("=>")):
            continue
        indent = ln[:len(ln) - len(ln.lstrip())]
        for j in range(i + 1, len(lines)):
            lj = lines[j]
            if lj.strip() == "};" and lj[:len(lj) - len(lj.lstrip())] == indent:
                lines[j] = indent + ");"
                fixed += 1
                break
        else:
            print("[WARN] %s:%d 未找到配对的 };" % (fn, i + 1))
    if fixed:
        with io.open(p, "w", encoding="utf-8", newline="") as f:
            f.write(eol.join(lines))
        print("  %-24s 补全 %d 处 SetRefresh 闭合" % (fn, fixed))
        total += fixed
print("合计补全 %d 处" % total)

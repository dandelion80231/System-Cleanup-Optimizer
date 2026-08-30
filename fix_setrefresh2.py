#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
修正上一版 fix_setrefresh.py 的错误：把 SetRefresh(lambda) 的结束行补成 "});"。

原行是 "};" = "}"(闭合 lambda 体) + ";"(结束赋值语句)。
改成方法调用后需要 "}"(闭合 lambda 体) + ")"(闭合 SetRefresh) + ";" = "});"。
上一版误写成了 ");"，把 lambda 体的 "}" 吃掉了，导致 CS1513。

本脚本幂等：只把同缩进的 "};" 或 ");" 归一为 "});"，已是 "});" 的不动。
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
        if not (s.endswith("(") or s.endswith("=>")):
            continue
        indent = ln[:len(ln) - len(ln.lstrip())]
        for j in range(i + 1, len(lines)):
            lj = lines[j]
            lj_indent = lj[:len(lj) - len(lj.lstrip())]
            st = lj.strip()
            if lj_indent == indent and st in ("};", ");", "});"):
                want = indent + "});"
                if lj != want:
                    lines[j] = want
                    fixed += 1
                break
        else:
            print("[WARN] %s:%d 未找到配对的结束行" % (fn, i + 1))
    if fixed:
        with io.open(p, "w", encoding="utf-8", newline="") as f:
            f.write(eol.join(lines))
        print("  %-24s 修正 %d 处结束行 → });" % (fn, fixed))
        total += fixed
print("合计修正 %d 处" % total)

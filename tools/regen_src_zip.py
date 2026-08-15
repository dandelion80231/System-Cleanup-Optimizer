#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
regen_src_zip.py — 重新生成源码披露包 src/CpqSystemTool/src.zip。

用途：把 src/CpqSystemTool/ 下的源文件 + 仓库根 README.md 打成一个 zip，
作为内嵌资源随 exe 一起发布（license 合规的源码披露）。

排除规则：
  - 构建产物目录：bin, obj, .vs, packages, .git
  - 资源本身：src.zip（避免自包含）
  - 其它以 '.' 开头的隐藏目录

zip 内部为扁平结构（无顶层 CpqSystemTool/ 前缀），README.md 放在最后。
压缩方式：ZIP_DEFLATED。
"""
import os
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # 仓库根
SRC_ROOT = os.path.join(ROOT, "src", "CpqSystemTool")
README = os.path.join(ROOT, "README.md")
OUT = os.path.join(SRC_ROOT, "src.zip")

EXCLUDE_DIRS = {"bin", "obj", ".vs", "packages", ".git"}


def collect_files():
    files = []
    for dp, dn, fn in os.walk(SRC_ROOT):
        # 原地过滤子目录，保持遍历顺序
        dn[:] = sorted(d for d in dn if d not in EXCLUDE_DIRS and not d.startswith("."))
        for f in sorted(fn):
            if f == "src.zip":
                continue
            full = os.path.join(dp, f)
            rel = os.path.relpath(full, SRC_ROOT).replace(os.sep, "/")
            files.append(rel)
    files.sort()  # 顶层文件 + 各子目录按字典序
    # 追加根 README.md（最后）
    if os.path.isfile(README):
        files.append("README.md")
    else:
        print("[warn] 根 README.md 不存在，跳过", file=sys.stderr)
    return files


def main():
    files = collect_files()
    tmp = OUT + ".tmp"
    with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as z:
        for rel in files:
            src = README if rel == "README.md" else os.path.join(SRC_ROOT, rel)
            with open(src, "rb") as fh:
                data = fh.read()
            zi = zipfile.ZipInfo(rel)
            zi.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(zi, data)
    os.replace(tmp, OUT)
    print("generated %s with %d entries" % (OUT, len(files)))


if __name__ == "__main__":
    main()

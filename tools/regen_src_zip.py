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
  - 编辑器/脚本留下的备份与临时文件：*.bak、*.bak.*、*.orig、*.tmp、*.log
    （2026-08-30 发现：源码目录里堆积的 `MainWindow.Maint.cs.bak.20260830_105720` 之类
     被一并打进披露包，既撑大体积又泄漏中间快照，故显式排除）

zip 内部为扁平结构（无顶层 CpqSystemTool/ 前缀），README.md 放在最后。
压缩方式：ZIP_DEFLATED。
"""
import os
import sys
import zipfile
import fnmatch

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # 仓库根
SRC_ROOT = os.path.join(ROOT, "src", "CpqSystemTool")
README = os.path.join(ROOT, "README.md")
OUT = os.path.join(SRC_ROOT, "src.zip")

EXCLUDE_DIRS = {"bin", "obj", ".vs", "packages", ".git"}

# 顶层资源文件与构建产物不进入源码披露包（它们是内嵌资源或 exe 输出）
EXCLUDE_TOP_LEVEL = {
    "src.zip",          # 避免自包含
    "*.exe",            # 构建产物
    "*.pdb",            # 调试符号
    "*.png",            # 背景图等资源
    "*.ico",            # 图标
}

# 需要排除的备份/临时文件后缀（小写比对）。
# 注意 `.bak.20260830_105720` 这类带时间戳的备份没有统一的「扩展名」，
# 故不能只看 os.path.splitext，需要按「是否含 .bak. 标记」整体判断。
BAK_MARKERS = (".bak.", ".orig.", ".rej.", ".tmp.")
EXCLUDE_SUFFIXES = (".bak", ".orig", ".rej", ".tmp", ".log")


def is_top_level_excluded(filename):
    """判断顶层文件是否应排除（资源/构建产物）。"""
    for pattern in EXCLUDE_TOP_LEVEL:
        if fnmatch.fnmatch(filename.lower(), pattern.lower()):
            return True
    return False


def is_junk(filename):
    """判断是否为不应进入披露包的备份/临时文件。"""
    low = filename.lower()
    if low in ("src.zip", "src.zip.tmp"):
        return True
    for marker in BAK_MARKERS:
        if marker in low:
            return True
    return low.endswith(EXCLUDE_SUFFIXES)


def collect_files():
    files = []
    for dp, dn, fn in os.walk(SRC_ROOT):
        # 原地过滤子目录，保持遍历顺序
        dn[:] = sorted(d for d in dn if d not in EXCLUDE_DIRS and not d.startswith("."))
        for f in sorted(fn):
            # 顶层文件：排除资源/构建产物
            rel = os.path.relpath(os.path.join(dp, f), SRC_ROOT)
            if rel == f and is_top_level_excluded(f):
                continue
            if is_junk(f):
                continue
            full = os.path.join(dp, f)
            rel = rel.replace(os.sep, "/")
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

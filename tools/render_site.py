#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
render_site.py — 部署前占位符渲染器（site-src 模板 → site-dist 产物）

新流程（模板与产物分离，单一来源 = version.json）：
    1. 改 site-src/version.json 一处（version/date/name/url/size/sha256）—— 它是唯一真源，已纳入 git
    2. 跑本脚本：读 site-src/version.json + site-src/ 下五个带占位符模板
                  → 用 version.json 替换占位符写出 HTML 到 site-dist/
                  → 同时把 version.json 原样复制到 site-dist/（部署用）
    3. 部署 site-dist/（如 deploy_site.py）

这样下次升版只改 site-src/version.json 一处，再渲染+部署即可；site-src 模板长期保留占位符。
site-dist/ 是渲染产物（被 .gitignore 忽略，不入库）。

占位符映射：
    {{VER}}     -> version      (如 v1.10)
    {{DATE}}    -> date         (如 2026-08-18)
    {{SIZE_MB}} -> size_mb      (size/1024/1024，保留 2 位，字符串)
    {{SHA256}}  -> sha256

健壮性：
    - version.json 缺失 / 缺字段 -> 报错退出 (非 0)
    - 任一模板文件缺失 -> 报错退出 (非 0)
"""

import hashlib
import json
import os
import re
import shutil
import sys

# site-src = 模板（占位符），site-dist = 产物（部署用）
HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(HERE)
SITE_SRC = os.environ.get("SITE_SRC", os.path.join(PROJECT_ROOT, "site-src"))
SITE_DIST = os.environ.get("SITE_DIST", os.path.join(PROJECT_ROOT, "site-dist"))

# CSS 唯一源（受 git 跟踪）→ 渲染产物 site-dist/style.{sha256前12}.css
CSS_SRC = os.path.join(PROJECT_ROOT, "site-css", "style.css")
# JS 唯一源（受 git 跟踪）→ 渲染产物 site-dist/script.{sha256前12}.js
JS_SRC = os.path.join(PROJECT_ROOT, "site-js", "script.js")

TEMPLATE_FILES = ["index.html", "download.html", "changelog.html", "features.html", "about.html"]

REQUIRED_FIELDS = ["version", "date", "name", "url", "size", "sha256"]


def load_version():
    # 唯一真源 version.json 位于 site-src/（受 git 跟踪）；site-dist/ 里的副本由本脚本复制生成
    path = os.path.join(SITE_SRC, "version.json")
    if not os.path.isfile(path):
        sys.exit(f"[FATAL] version.json 缺失（应在 site-src/ 下）: {path}")
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
    except Exception as e:
        sys.exit(f"[FATAL] version.json 解析失败: {e}")

    missing = [k for k in REQUIRED_FIELDS if k not in data]
    if missing:
        sys.exit(f"[FATAL] version.json 缺少字段: {', '.join(missing)}")

    size = data["size"]
    try:
        size_mb = round(float(size) / 1024.0 / 1024.0, 2)
    except (TypeError, ValueError):
        sys.exit(f"[FATAL] version.json 的 size 字段不是合法数字: {size!r}")

    data["size_mb"] = f"{size_mb:g}"  # 6.83 / 7 等，去掉多余尾零
    return data


def render(data):
    mapping = {
        "{{VER}}": str(data["version"]),
        "{{DATE}}": str(data["date"]),
        "{{SIZE_MB}}": str(data["size_mb"]),
        "{{SHA256}}": str(data["sha256"]),
    }
    for name in TEMPLATE_FILES:
        src = os.path.join(SITE_SRC, name)
        dst = os.path.join(SITE_DIST, name)
        if not os.path.isfile(src):
            sys.exit(f"[FATAL] 模板缺失: {src}")

        with open(src, encoding="utf-8") as f:
            html = f.read()

        # 统计替换前占位符数量，便于核验是否有残留
        before = {ph: html.count(ph) for ph in mapping}
        for ph, val in mapping.items():
            if ph in html:
                html = html.replace(ph, val)

        with open(dst, "w", encoding="utf-8") as f:
            f.write(html)

        applied = sum(before.values())
        print(f"[RENDER] {name}: VER={data['version']}  DATE={data['date']}  SIZE_MB={data['size_mb']}  (替换占位符 {applied} 处)")

    # 把唯一真源 version.json 原样复制到 site-dist/（部署用）
    src_ver = os.path.join(SITE_SRC, "version.json")
    dst_ver = os.path.join(SITE_DIST, "version.json")
    shutil.copyfile(src_ver, dst_ver)
    print(f"[COPY] version.json -> {dst_ver}")


def process_asset(src_path, kind, prefix, ref_re, tag_fmt):
    """通用资产渲染：把 src_path 复制为 site-dist/{prefix}.{hash12}.ext，
    清理旧产物，并同步更新 site-src 各 HTML 的引用标签。

    kind: 人类可读类型（CSS/JS），用于日志。
    prefix: 文件名前缀，如 style / script。
    ref_re: 匹配旧引用的正则（含一个捕获组）。
    tag_fmt: 新标签模板，如 '<link rel="stylesheet" href="{new_name}">'。
    返回 new_name；若源文件缺失返回 None。
    """
    if not os.path.isfile(src_path):
        print(f"[SKIP] {kind} 源缺失：{src_path}")
        return None
    with open(src_path, "rb") as f:
        data = f.read()
    h = hashlib.sha256(data).hexdigest()[:12]
    ext = os.path.splitext(src_path)[1].lstrip(".")
    new_name = f"{prefix}.{h}.{ext}"
    dst = os.path.join(SITE_DIST, new_name)
    with open(dst, "wb") as f:
        f.write(data)
    # 清理旧产物（保留 _worker.js 等非 {prefix}.{hash}.{ext} 文件）
    for fname in os.listdir(SITE_DIST):
        if fname.startswith(f"{prefix}.") and fname.endswith(f".{ext}") and fname != new_name:
            old = os.path.join(SITE_DIST, fname)
            os.remove(old)
            print(f"[CLEANUP] 删除旧 {kind}: {fname}")
    # 同步更新 site-src HTML 引用
    ref_re = re.compile(ref_re)
    for name in TEMPLATE_FILES:
        src = os.path.join(SITE_SRC, name)
        if not os.path.isfile(src):
            continue
        with open(src, encoding="utf-8") as f:
            html = f.read()
        m = ref_re.search(html)
        if m:
            old_ref = m.group(1)
            html_new = ref_re.sub(tag_fmt.format(new_name=new_name), html)
            with open(src, "w", encoding="utf-8") as f:
                f.write(html_new)
            print(f"[UPDATE-{kind}-REF] {name}: {old_ref} -> {new_name}")
    print(f"[RENDER-{kind}] {src_path} -> {dst} ({len(data)} bytes, hash={h[:12]})")
    return new_name


def process_css():
    return process_asset(
        CSS_SRC, "CSS", "style",
        r'<link\s+rel="stylesheet"\s+href="(style\.[0-9a-f]{12}\.css)">',
        '<link rel="stylesheet" href="{new_name}">',
    )


def process_js():
    return process_asset(
        JS_SRC, "JS", "script",
        r'<script\s+src="(script\.[0-9a-f]{12}\.js)"></script>',
        '<script src="{new_name}"></script>',
    )


def main():
    data = load_version()
    print(f"[INFO] 模板源: {SITE_SRC}")
    print(f"[INFO] 产物目录: {SITE_DIST}")
    print(f"[INFO] version={data['version']}  date={data['date']}  name={data['name']}  size={data['size']} ({data['size_mb']} MB)")
    process_css()  # 先处理 CSS（写 site-dist/style.{hash}.css + 同步 site-src HTML href）
    process_js()   # 再处理 JS（写 site-dist/script.{hash}.js + 同步 site-src HTML src）
    render(data)
    print("[DONE] 模板渲染完成，site-dist 五个 HTML 已无占位符。")


if __name__ == "__main__":
    main()

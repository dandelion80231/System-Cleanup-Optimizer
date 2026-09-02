#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
add_site_version.py — 官网「下载页」加新版本的唯一可信脚本（替代已废弃的
add_v117_only.py / add_version_panel.py / create_v117_template.py / sync_changelog.py）。

为什么需要它：下载页采用「两栏布局」契约，加版本必须**同时**改三处且保持
顺序一致、active 唯一对齐，否则右栏更新日志空白 / 下载按钮错位：
  - 左栏 tab    : <button class="dl-tab" ... data-ver="vX.XX">
  - 左栏 panel  : <div class="dl-panel" ... data-panel="vX.XX">
  - 右栏 chlog  : <div class="chlog-panel" data-panel="vX.XX">
老 4 个脚本的共同死穴：只加左栏两处、不建右栏 chlog-panel、active 错位，
且打印 "OK" 自欺欺人。本脚本在**写文件前**做完整契约自检，失败绝不写入。

用法（默认 dry-run，不落盘）：
  python tools/add_site_version.py \
      --version v1.18 --date 2026-09-10 --size 5432100 \
      --sha256 <64位hex> --changelog _tmp/changelog_v1.18.html

真正写入（会先备份 download.html 到 .bak-<时间戳>）：
  ... 同上  --apply

可选覆盖路径（沙箱/测试）：
  --download-html <path>  --version-json <path>  --versions-json <path>

依赖：仅标准库。
"""
import argparse
import json
import os
import re
import sys
from datetime import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
DEF_DL = os.path.join(ROOT, "site-src", "download.html")
DEF_VER = os.path.join(ROOT, "site-src", "version.json")
DEF_VERS = os.path.join(ROOT, "site-src", "versions.json")

ANCHOR_TABS = '<div class="dl-tabs" role="tablist" aria-label="选择下载版本">'
ANCHOR_PANELS = '<div class="dl-panels">'
ANCHOR_CHLOGS = '<div class="chlog-panels">'

DEP_DEPRECATED = ["dl-meta", "dl-actions", "dl-btn ", "dl-desc", "chlog-section", "dl-card"]


def fail(msg):
    print("❌ " + msg)
    sys.exit(2)


def div_counts(html):
    opens = len(re.findall(r"<div\b(?![^>]*/>)[^>]*?>", html))
    closes = len(re.findall(r"</div\s*>", html))
    return opens, closes


def contract(html):
    tabs = re.findall(r'class="dl-tab(?: active)?"[^>]*data-ver="([^"]+)"', html)
    panels = re.findall(r'class="dl-panel(?: active)?"[^>]*data-panel="([^"]+)"', html)
    chlogs = re.findall(r'class="chlog-panel(?: active)?"[^>]*data-panel="([^"]+)"', html)
    at = re.findall(r'class="dl-tab active"[^>]*data-ver="([^"]+)"', html)
    ap = re.findall(r'class="dl-panel active"[^>]*data-panel="([^"]+)"', html)
    ag = re.findall(r'class="chlog-panel active"[^>]*data-panel="([^"]+)"', html)
    return tabs, panels, chlogs, at, ap, ag


def validate(html, expect_new, old_counts):
    """返回 (ok, list_of_msgs)。expect_new=新版本号；old_counts=(t,p,c) 加前计数。"""
    msgs = []
    tabs, panels, chlogs, at, ap, ag = contract(html)
    o, c = div_counts(html)
    ok = True
    if o != c:
        ok = False
        msgs.append("div 开/闭不平衡: %d / %d" % (o, c))
    if len(tabs) != old_counts[0] + 1 or len(panels) != old_counts[1] + 1 or len(chlogs) != old_counts[2] + 1:
        ok = False
        msgs.append("三处计数未恰好 +1: tab=%d panel=%d chlog=%d (加前 %s)" % (len(tabs), len(panels), len(chlogs), old_counts))
    if not (tabs == panels == chlogs):
        ok = False
        msgs.append("三处版本集合/顺序不一致")
    if tabs and tabs[0] != expect_new:
        ok = False
        msgs.append("新版本未排首位: %s" % (tabs[0] if tabs else "-"))
    if not (len(at) == 1 and len(ap) == 1 and len(ag) == 1 and at[0] == ap[0] == ag[0] == expect_new):
        ok = False
        msgs.append("active 未唯一对齐到新版本: tab=%s panel=%s chlog=%s" % (at or ["-"], ap or ["-"], ag or ["-"]))
    if 'id="panel-%s"' % expect_new not in html:
        ok = False
        msgs.append("缺少 panel-%s" % expect_new)
    for d in DEP_DEPRECATED:
        if d in html:
            ok = False
            msgs.append("出现废弃旧单栏类名: %s" % d)
    return ok, msgs


def insert_after(html, anchor, block):
    idx = html.find(anchor)
    if idx < 0:
        fail("找不到插入锚点: %s" % anchor)
    cut = idx + len(anchor)
    return html[:cut] + "\n" + block.rstrip("\n") + "\n" + html[cut:]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", required=True, help="如 v1.18（两段式 vX.YY）")
    ap.add_argument("--date", required=True, help="如 2026-09-10")
    ap.add_argument("--size", required=True, type=int, help="exe 字节数")
    ap.add_argument("--sha256", required=True, help="64 位十六进制")
    ap.add_argument("--exe", default=None, help="exe 文件名，默认 系统清理与优化工具_<version>.exe")
    ap.add_argument("--changelog", required=True, help="含本版更新日志内部 HTML 的文件（<blockquote>+<h4>+<ul>）")
    ap.add_argument("--apply", action="store_true", help="真正写入（默认 dry-run）")
    ap.add_argument("--download-html", default=DEF_DL)
    ap.add_argument("--version-json", default=DEF_VER)
    ap.add_argument("--versions-json", default=DEF_VERS)
    args = ap.parse_args()

    ver = args.version
    if not re.match(r"^v\d+\.\d+$", ver):
        fail("--version 必须形如 v1.18")
    if re.match(r"^v\d+\.\d{1}$", ver):
        print("⚠️ 版本段为单数字（%s），本项目历史均为两段式 vX.YY（如 v1.04），请确认无意为之" % ver)
    if not re.match(r"^\d{4}-\d{2}-\d{2}$", args.date):
        fail("--date 必须形如 2026-09-10")
    if not re.match(r"^[0-9a-f]{64}$", args.sha256):
        fail("--sha256 必须是 64 位十六进制")
    if args.size <= 0:
        fail("--size 必须为正")
    exe = args.exe or ("系统清理与优化工具_%s.exe" % ver)
    if not os.path.isfile(args.changelog):
        fail("changelog 文件不存在: %s" % args.changelog)
    body = open(args.changelog, encoding="utf-8").read().strip()
    if len(body) < 20:
        fail("changelog 内容过短，疑似为空")

    if not os.path.isfile(args.download_html):
        fail("download.html 不存在: %s" % args.download_html)

    html = open(args.download_html, encoding="utf-8").read()

    # 当前状态
    tabs, panels, chlogs, at, ap, ag = contract(html)
    if not (tabs and panels and chlogs):
        fail("读取现有契约失败，文件可能已损坏")
    if not (at and ap and ag and at[0] == ap[0] == ag[0]):
        fail("现有文件 active 未唯一对齐（tab=%s panel=%s chlog=%s），先修源文件" % (at or ["-"], ap or ["-"], ag or ["-"]))
    old = at[0]
    if ver in tabs:
        fail("版本 %s 已存在，拒绝重复添加" % ver)
    if len(tabs) != len(panels) or len(panels) != len(chlogs):
        fail("现有文件三处计数就不一致（%d/%d/%d），先修源文件" % (len(tabs), len(panels), len(chlogs)))
    old_counts = (len(tabs), len(panels), len(chlogs))
    size_mb = round(args.size / 1024.0 / 1024.0, 2)

    print("读入: 现有 %d 个版本，当前 active=%s，新版本=%s (%d B / %.2f MB)" % (len(tabs), old, ver, args.size, size_mb))

    # ---- 1) 降级旧 latest（去掉 active + aria/tabindex + 「最新」徽标）----
    html = re.sub(
        r'<button class="dl-tab active" role="tab" aria-selected="true" aria-controls="panel-([^"]+)" tabindex="0" data-ver="([^"]+)">([^<]*)</button>',
        r'<button class="dl-tab" role="tab" aria-selected="false" aria-controls="panel-\1" tabindex="-1" data-ver="\2">\3</button>',
        html, count=1)
    html = re.sub(
        r'<div class="dl-panel active" role="tabpanel" id="panel-([^"]+)" data-panel="([^"]+)">',
        r'<div class="dl-panel" role="tabpanel" id="panel-\1" data-panel="\2">',
        html, count=1)
    html = re.sub(
        r'<div class="chlog-panel active" data-panel="([^"]+)">',
        r'<div class="chlog-panel" data-panel="\1">',
        html, count=1)
    html = re.sub(r'(<h3 class="dl-ver">[^<]*<span[^>]*>)（最新 · ', r"\1（", html, count=1)
    # 顶部「本站直接托管 v1.01 – vX.XX 全部 exe」文案
    html = re.sub(r'本站直接托管 v1\.01\s*[–-]\s*v1\.\d+ 全部 exe',
                  "本站直接托管 v1.01 – %s 全部 exe" % ver, html, count=1)

    # ---- 2) 插入三处新版本 ----
    tab_block = ('          <button class="dl-tab active" role="tab" aria-selected="true" '
                 'aria-controls="panel-%s" tabindex="0" data-ver="%s">%s</button>' % (ver, ver, ver))
    panel_block = (
        '            <div class="dl-panel active" role="tabpanel" id="panel-%s" data-panel="%s">\n'
        '              <h3 class="dl-ver">下载 %s <span style="font-size:14px;opacity:.75;font-weight:500;">（最新 · %s · %.2f MB）</span></h3>\n'
        '              <p class="meta"><span>📦 单文件 exe</span><span>💾 %.2f MB</span><span>🪟 Win 10 / 11</span><span>🔓 开源免费</span></p>\n'
        '              <a class="btn btn-primary" href="./%s" download>⬇️ 下载 %s</a>\n'
        '              <div class="hash">SHA256: %s</div>\n'
        '            </div>' % (ver, ver, ver, args.date, size_mb, size_mb, exe, exe, args.sha256))
    chlog_block = (
        '              <div class="chlog-panel active" data-panel="%s">\n'
        '                <div class="chg-body">\n%s\n'
        '                </div>\n'
        '              </div>' % (ver, body))

    html = insert_after(html, ANCHOR_TABS, tab_block)
    html = insert_after(html, ANCHOR_PANELS, panel_block)
    html = insert_after(html, ANCHOR_CHLOGS, chlog_block)

    # ---- 3) 写 version.json / versions.json（本地真源，供 render_site 渲染其它页横幅）----
    ver_json_changed = False
    if os.path.isfile(args.version_json):
        vj = json.load(open(args.version_json, encoding="utf-8"))
        vj.update({
            "version": ver, "date": args.date, "name": exe,
            "url": "https://cpq-system-tool.pages.dev/%s" % exe,
            "size": args.size, "sha256": args.sha256,
        })
        ver_json_changed = True
    if os.path.isfile(args.versions_json):
        vlist = json.load(open(args.versions_json, encoding="utf-8"))
        for e in vlist["versions"]:
            e["is_latest"] = False
        vlist["versions"].insert(0, {
            "version": ver, "date": args.date, "size_mb": size_mb,
            "sha256": args.sha256, "is_latest": True,
        })
        vers_json_changed = True

    # ---- 4) 自检 ----
    ok, msgs = validate(html, ver, old_counts)
    print("-" * 60)
    if ok:
        tabs2, panels2, chlogs2, at2, ap2, ag2 = contract(html)
        o, c = div_counts(html)
        print("✅ 契约自检通过")
        print("   div 开/闭      : %d / %d" % (o, c))
        print("   tab/panel/chlog: %d / %d / %d（已 +1）" % (len(tabs2), len(panels2), len(chlogs2)))
        print("   active 三处    : %s / %s / %s（唯一对齐）" % (at2[0], ap2[0], ag2[0]))
        print("   版本顺序首位   : %s" % tabs2[0])
    else:
        print("❌ 契约自检失败，未写入任何文件：")
        for m in msgs:
            print("   - " + m)
        sys.exit(2)

    if not args.apply:
        print("-" * 60)
        print("🔍 DRY-RUN：未写入任何文件。确认无误后加 --apply 真正写入。")
        print("   将写入: %s (+ .bak 备份), version.json, versions.json" % args.download_html)
        return

    # ---- 5) 真正写入 ----
    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
    bak = args.download_html + ".bak-" + ts
    import shutil
    shutil.copyfile(args.download_html, bak)
    open(args.download_html, "w", encoding="utf-8").write(html)
    print("💾 已备份原文件: %s" % bak)
    print("💾 已写入: %s" % args.download_html)
    if ver_json_changed:
        with open(args.version_json, "w", encoding="utf-8") as f:
            json.dump(vj, f, ensure_ascii=False, indent=2)
            f.write("\n")
        print("💾 已更新 version.json -> %s" % ver)
    if vers_json_changed:
        with open(args.versions_json, "w", encoding="utf-8") as f:
            json.dump(vlist, f, ensure_ascii=False, indent=2)
            f.write("\n")
        print("💾 已更新 versions.json -> %s" % ver)
    print("✅ 完成。下一步：python tools/validate_site.py 复核，再 render_site.py + deploy_site.py。")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
把 9 处页面缓存样板（_cachedXxxPage / _xxxCacheDark / _xxxCacheKey / _xxxRefresh）
统一迁移到 PageCache<UIElement>。

严格模式：任何一处没按预期匹配到，立即报错退出，绝不静默跳过。
用法：python migrate_pagecache.py          # 预演
      python migrate_pagecache.py --apply  # 实际写入
"""
import io
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "src", "CpqSystemTool")
DRY = "--apply" not in sys.argv

SITES = [
    # file, pageVar, keyVar, darkVar, refreshVar, cacheVar, contentKeyExpr(or None)
    ("MainWindow.Appx.cs", "_cachedAppxPage", "_appxCacheKey", "_appxCacheDark", "_appxRefresh", "_appxCache",
     'string.Join("|", AppxManager.Catalog.Select(c => c.PackageFamily))'),
    ("MainWindow.Appx.cs", "_cachedAppxRawPage", "_appxRawCacheKey", "_appxRawCacheDark", "_appxRawRefresh", "_appxRawCache", None),
    ("MainWindow.Cleanup.cs", "_cachedCleanupPage", "_cleanupCacheKey", "_cleanupCacheDark", "_cleanupRefresh", "_cleanupCache", None),
    ("MainWindow.Config.cs", "_cachedConfigPage", "_configCacheKey", "_configCacheDark", "_configRefresh", "_configCache", None),
    ("MainWindow.Memory.cs", "_cachedMemoryPage", "_memoryCacheKey", "_memoryCacheDark", "_memoryRefresh", "_memoryCache", None),
    ("MainWindow.Pages.cs", "_cachedServicesPage", "_servicesCacheKey", "_servicesCacheDark", "_servicesRefresh", "_servicesCache", None),
    ("MainWindow.Security.cs", "_cachedSecurityPage", "_securityCacheKey", "_securityCacheDark", "_securityRefresh", "_securityCache", None),
    ("MainWindow.Software.cs", "_cachedSoftwarePage", "_softwareCacheKey", "_softwareCacheDark", "_softwareRefresh", "_softwareCache",
     'string.Join("|", SoftwareInstall.GetEffectiveList().Select(s => s.Id))'),
    ("MainWindow.Tweaks.cs", "_cachedTweaksPage", "_tweaksCacheKey", "_tweaksCacheDark", "_tweaksRefresh", "_tweaksCache", None),
]

DECL = object()   # 标记：此处插入新的 PageCache 字段声明
DELETE = object()  # 标记：整行删除


def load(path):
    with io.open(path, "r", encoding="utf-8-sig", newline="") as f:
        data = f.read()
    return data.split("\n"), ("\r\n" if "\r\n" in data else "\n")


def process(fname, site):
    path = os.path.join(ROOT, fname)
    _f, page, key, dark, refresh, cache, content_key = site
    lines, eol = load(path)
    n = len(lines)

    # ---------- 1) 定位 4 个字段声明 ----------
    decl_idx = {}
    pat = r"^private\s+(?:UIElement|string|bool|Action)\s+(%s)\s*;\s*$" % "|".join(
        re.escape(v) for v in (page, key, dark, refresh))
    for i, ln in enumerate(lines):
        m = re.match(pat, ln.strip())
        if m:
            var = m.group(1)
            if var in decl_idx:
                raise SystemExit("[FAIL] %s: 字段 %s 重复声明" % (fname, var))
            decl_idx[var] = i
    for v in (page, key, dark, refresh):
        if v not in decl_idx:
            raise SystemExit("[FAIL] %s: 未找到字段声明 %s" % (fname, v))
    decl_pos = decl_idx[page]

    # ---------- 2) 命中判断块 → TryGet 两行 ----------
    start = None
    for i, ln in enumerate(lines):
        if re.match(r"^\s*if\s*\(\s*" + re.escape(page) + r"\s*!=\s*null\s*&&\s*"
                    + re.escape(dark) + r"\s*==\s*buildDark", ln):
            start = i
            break
    if start is None:
        raise SystemExit("[FAIL] %s: 未找到命中判断块" % fname)
    end = None
    for j in range(start, min(start + 14, n)):
        s = lines[j].strip()
        if s == key + " = null;" or s == "InvalidateConfigCache();":
            end = j
            break
    if end is None:
        raise SystemExit("[FAIL] %s: 命中判断块未找到结束行" % fname)

    key_arg = (", " + content_key) if content_key else ""
    lines[start:end + 1] = [
        "            var cached = %s.TryGet(buildDark%s);" % (cache, key_arg),
        "            if (cached != null) return cached;",
    ]
    shift = 2 - (end - start + 1)

    # ---------- 3) 逐行改写 ----------
    out = []
    n_decl = n_set = n_inv = n_del = 0
    for ln in lines:
        s = ln.strip()
        indent = ln[:len(ln) - len(ln.lstrip())]

        # (a) 行内替换：多处把几个缓存置空写在同一个 lambda 单行里
        if page in ln:
            ln = re.sub(re.escape(page) + r"\s*=\s*null", "%s.Invalidate()" % cache, ln)
        if key in ln:
            ln = re.sub(r"\s*" + re.escape(key) + r"\s*=\s*null\s*;", "", ln)
        if refresh in ln:
            ln = re.sub(r"\s*" + re.escape(refresh) + r"\s*=\s*null\s*;", "", ln)
        s = ln.strip()

        # (b) 字段声明行
        m = re.match(pat, s)
        if m:
            out.append(DECL if m.group(1) == page else DELETE)
            n_decl += 1
            continue

        # (c) _xxxCacheDark = ...;  主题标记已由 PageCache.Set/TryGet 维护 → 删行
        if re.match(r"^" + re.escape(dark) + r"\s*=\s*.+?;\s*$", s):
            out.append(DELETE)
            n_del += 1
            continue

        # (d) _cachedXxxPage = <expr>;
        m = re.match(r"^" + re.escape(page) + r"\s*=\s*(.+?);\s*$", s)
        if m:
            rhs = m.group(1).strip()
            if rhs == "null":
                out.append(indent + "%s.Invalidate();" % cache)
                n_inv += 1
            else:
                out.append(indent + "%s.Set(%s, buildDark);" % (cache, rhs))
                n_set += 1
            continue

        # (e) _xxxCacheKey = <expr>;   （可能带行尾注释，如 Tweaks 那行）
        m = re.match(r"^" + re.escape(key) + r"\s*=\s*(.+?);\s*(//.*)?$", s)
        if m:
            rhs = m.group(1).strip()
            # 常量键（如 "cleanup" / "tweaks"）是冗余的：PageCache.Set() 内部已把 _key 置为 "1"
            # 表示"已构建过"，命中判断只看 _key != null。故这类行直接删除。
            # 动态键（Appx 目录 / 常用软件清单）才需要转成 SetContentKey，用于内容变化时失效重建。
            if rhs == "null" or (rhs.startswith('"') and rhs.endswith('"')):
                out.append(DELETE)
                n_del += 1
            else:
                out.append(indent + "%s.SetContentKey(%s);" % (cache, rhs))
                n_set += 1
            continue

        # (f) _xxxRefresh = <expr>   （lambda 常跨多行，行尾没有分号）
        m = re.match(r"^" + re.escape(refresh) + r"\s*=\s*(.*)$", s)
        if m:
            rhs = m.group(1).strip()
            if rhs == "null;" or rhs == "null":
                out.append(DELETE)
                n_del += 1
            else:
                out.append(indent + "%s.SetRefresh(%s" % (cache, rhs))
            continue

        # (g) 兜底：还有残留引用就报错，绝不静默放过
        #     注释行里提到旧字段名是允许的（那些注释解释"为什么"，会在迁移后另行润色），
        #     只有真实代码里还引用旧字段才算失败。
        if s.startswith("//") or s.startswith("///"):
            out.append(ln)
            continue
        for v in (page, key, dark, refresh):
            if re.search(r"\b" + re.escape(v) + r"\b", ln):
                raise SystemExit("[FAIL] %s: 残留引用 %s -> %s" % (fname, v, ln.strip()))

        out.append(ln)

    # ---------- 4) 组装：DECL 处插入新字段，DELETE 行丢弃 ----------
    pos = decl_pos + (shift if decl_pos > end else 0)
    if out[pos] is not DECL:
        raise SystemExit("[FAIL] %s: 声明插入点错位（索引 %d 处不是字段声明）" % (fname, pos))
    out[pos] = "        private readonly PageCache<UIElement> %s = new PageCache<UIElement>();" % cache
    final = [x for x in out if x is not DELETE]
    if n_decl != 4:
        raise SystemExit("[FAIL] %s: 期待 4 个字段声明，实际 %d" % (fname, n_decl))

    if not DRY:
        with io.open(path, "w", encoding="utf-8", newline="") as f:
            f.write(eol.join(final))

    return "  %-24s %-18s 声明4→1 · 判断块%d行→2行 · Set %d · Invalidate %d · 删行 %d" % (
        fname, cache, end - start + 1, n_set, n_inv, n_del)


print("=== PageCache 迁移（%s）===" % ("实际写入" if not DRY else "预演，未修改文件"))
for site in SITES:
    print(process(site[0], site))
print("\n共 %d 处站点，全部匹配成功。" % len(SITES))
if DRY:
    print("加 --apply 实际执行。")

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
PageCache 迁移后的注释润色：更新仍在描述旧字段名的注释，并清掉迁移留下的多余空行。
幂等：已处理过则跳过。
"""
import io
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "src", "CpqSystemTool")

EDITS = [
    ("MainWindow.Config.cs",
     "        /// 修复：此前各处置空只清 _cachedConfigPage，漏清 _configRefresh / _configCacheKey，\n",
     "        /// 历史 bug（收拢为 PageCache<UIElement> 后已从结构上不可能复发）：\n"
     "        /// 此前各处置空只清 _cachedConfigPage，漏清 _configRefresh / _configCacheKey，\n"),
    ("MainWindow.Config.cs",
     "        /// 页面虽会重建，但旧的刷新委托与缓存键仍残留，可能把已丢弃页面的刷新逻辑用到新页面上。\n"
     "        /// </summary>\n"
     "        private void InvalidateConfigCache()\n"
     "        {\n"
     "            _configCache.Invalidate();\n"
     "\n"
     "\n"
     "        }\n",
     "        /// 页面虽会重建，但旧的刷新委托与缓存键仍残留，可能把已丢弃页面的刷新逻辑用到新页面上。\n"
     "        /// 现在「失效」只有 Invalidate() 一个入口，页面、内容键、刷新委托必然一并清空。\n"
     "        /// </summary>\n"
     "        private void InvalidateConfigCache()\n"
     "        {\n"
     "            _configCache.Invalidate();\n"
     "        }\n"),
    ("MainWindow.Tweaks.cs",
     "        // 避免每次导航重建 116 项 × 多控件。主题一致性由 _tweaksCacheDark 保证。\n",
     "        // 避免每次导航重建 116 项 × 多控件。主题一致性由 PageCache 内部记录的构建期主题保证。\n"),
]

total = 0
for fn, old, new in EDITS:
    p = os.path.join(ROOT, fn)
    with io.open(p, "r", encoding="utf-8-sig", newline="") as f:
        data = f.read()
    if old in data:
        data = data.replace(old, new, 1)
        with io.open(p, "w", encoding="utf-8-sig", newline="") as f:
            f.write(data)
        print("  %-24s 已更新注释" % fn)
        total += 1
    elif new in data:
        print("  %-24s 已处理，跳过" % fn)
    else:
        print("  [WARN] %-20s 未匹配到目标片段" % fn)
print("完成，共更新 %d 处。" % total)

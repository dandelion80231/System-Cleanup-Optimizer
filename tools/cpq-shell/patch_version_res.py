# -*- coding: utf-8 -*-
# tools/cpq-shell/patch_version_res.py
# 把 version.res 里的 RT_VERSION 数据块替换为手工生成的标准 VS_VERSION_INFO。
# 背景：老版 SDK rc.exe（10.0.10011）用简单 END 语法编译 VS_VERSION_INFO 时产生损坏结构
#（wValueLength 溢出 0xFEEF、固定头字段错位），Windows 文件属性页解析失败 → 全字段为空。
# 本脚本：
#   1. 在 .res 中定位 RT_VERSION 数据块（1.0.x.x 版本字节特征，块尾到文件尾）
#   2. 用 Python 按 MSDN VS_VERSIONINFO 标准结构生成新数据（0804 中文语言块）
#   3. 原长替换（尾部补零，.res 内其他资源/偏移不变；图标资源保留）
# 用法：python patch_version_res.py <version.res> <filever=1,0,24,0> [ProductName] [FileDescription] [Copyright] [Company] [InternalName] [OriginalFilename]
import sys, struct, io, os

def main():
    res_path = sys.argv[1]
    ver = sys.argv[2] if len(sys.argv) > 2 else "1,0,24,0"
    product = sys.argv[3] if len(sys.argv) > 3 else "系统清理与优化工具"
    desc = sys.argv[4] if len(sys.argv) > 4 else "面向 Windows 10/11 的一体化系统清理、优化与维护工具（智能外壳）"
    copyright = sys.argv[5] if len(sys.argv) > 5 else "Copyright © 2026"
    company = sys.argv[6] if len(sys.argv) > 6 else "dandelion80231"
    internal = sys.argv[7] if len(sys.argv) > 7 else "cpq-shell"
    original = sys.argv[8] if len(sys.argv) > 8 else "cpq-shell.exe"
    fver = sys.argv[9] if len(sys.argv) > 9 else ver

    data = bytearray(io.open(res_path, "rb").read())

    # 定位 RT_VERSION 数据块：文件版本字节（MS,LS 小端）前 8B = 数据块起点
    p1, p2, p3, p4 = [int(x) for x in fver.split(",")]
    pat = struct.pack("<2I", (p1 << 16) | p2, (p3 << 16) | p4)
    off = data.find(pat)
    if off < 8:
        print("patch_version_res: RT_VERSION 数据块未找到（版本特征 %s 不在 %s）" % (fver, res_path))
        return 1
    start = off - 8
    old_len = len(data) - start
    print("RT_VERSION 数据块 @%d, 长 %d B（占文件尾）" % (start, old_len))

    def u16s(s):
        return s.encode("utf-16-le")

    def aligned(b):
        return b + b"\x00" * ((-len(b)) % 4)

    # ---- 变量部分 ----
    pairs_def = [
        ("CompanyName", company),
        ("FileDescription", desc),
        ("InternalName", internal),
        ("LegalCopyright", copyright),
        ("OriginalFilename", original),
        ("ProductName", product),
        ("ProductVersion", fver.replace(",", ".")),
    ]
    # 键值对：u32 Length（键值 UTF-16 字节含两端 \0，各 4B 对齐）+ 键 + 值；块尾 4B 0
    pairs = b""
    for k, v in pairs_def:
        kb = aligned(u16s(k) + b"\x00\x00")
        vb = aligned(u16s(v) + b"\x00\x00")
        pairs += struct.pack("<I", len(kb) + len(vb)) + kb + vb
    pairs += struct.pack("<I", 0)
    # 语言块 "0804 04B0"（Simplified Chinese / Unicode）
    # 口径对齐 MS VS 编译器（cmd.exe 实证）：Value=1（非 0）；section Length = 语言块 + 28
    pairs = b""
    for k, v in pairs_def:
        kb = aligned(u16s(k) + b"\x00\x00")
        vb = aligned(u16s(v) + b"\x00\x00")
        pairs += struct.pack("<I", len(kb) + len(vb)) + kb + vb
    pairs += struct.pack("<I", 0)
    lb_len = len(pairs)
    lang_block = struct.pack("<II", lb_len, 1) + aligned(u16s("0804 04B0") + b"\x00\x00") + pairs
    # StringFileInfo 段：Length = 语言块 + 28（段头 8 + 段名 16 + 对齐 4）；Value = 1
    sf = struct.pack("<II", lb_len + 28, 1) + aligned(u16s("StringFileInfo") + b"\x00\x00") + lang_block
    # VarFileInfo 段
    trans = aligned(u16s("Translation") + b"\x00\x00") + struct.pack("<HH", 0x0804, 0x04B0) + struct.pack("<I", 0)
    var_block = struct.pack("<II", len(trans), 0) + aligned(u16s("VarFileInfo") + b"\x00\x00") + trans
    variable = aligned(sf) + struct.pack("<I", 0)

    # ---- 固定头（64B）----
    ms = (p1 << 16) | p2
    ls = (p3 << 16) | p4
    q1, q2, q3, q4 = [int(x) for x in ver.split(",")]
    pms = (q1 << 16) | q2
    pls = (q3 << 16) | q4
    total = 64 + len(variable)
    fixed = struct.pack(
        "<4H14I",
        total, 0xFEEF, 0x0002, 0,              # wLength wValueLength(VS 编译器哨兵值 0xFEEF，cmd.exe 实证) wFileType(VFT_APP) wFileSubtype
        0x3F, 0x00, ms, ls, pms, pls,               # dwFileFlagsMask dwFileFlags dwFileVersionMS/LS dwProductVersionMS/LS
        0x3F, 0x00, 0x040004, 0x0002, 0, 0, 0, 0,  # dwFileFlagsMask2 dwFileFlags2 dwFileOS dwFileType dwFileSubtype + 填充
    )
    assert len(fixed) == 64, len(fixed)
    payload = fixed + variable
    assert len(payload) <= old_len, "新 payload %d 超过原块 %d" % (len(payload), old_len)
    payload += b"\x00" * (old_len - len(payload))
    data[start:start + old_len] = payload
    with open(res_path, "wb") as f:
        f.write(bytes(data))
    print("已写入标准 VS_VERSION_INFO（%d B 结构 + 补零至 %d B）" % (len(fixed + variable), old_len))
    return 0

if __name__ == "__main__":
    sys.exit(main())

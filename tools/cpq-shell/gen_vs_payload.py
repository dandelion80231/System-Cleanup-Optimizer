# -*- coding: utf-8 -*-
# tools/cpq-shell/gen_vs_payload.py — 生成标准 VS_VERSION_INFO 二进制（供 rebuild_rsrc.py 嵌入）
# 用法：python gen_vs_payload.py <out.bin> <filever=1,0,24,0>
import sys, struct

def main():
    out_path, filever = sys.argv[1], sys.argv[2]
    product = sys.argv[3] if len(sys.argv) > 3 else "系统清理与优化工具"
    desc = sys.argv[4] if len(sys.argv) > 4 else "面向 Windows 10/11 的一体化系统清理、优化与维护工具（智能外壳）"
    copyright = sys.argv[5] if len(sys.argv) > 5 else "Copyright © 2026"
    company = sys.argv[6] if len(sys.argv) > 6 else "dandelion80231"
    internal = sys.argv[7] if len(sys.argv) > 7 else "cpq-shell"
    original = sys.argv[8] if len(sys.argv) > 8 else "cpq-shell.exe"

    def u16s(s):
        return s.encode("utf-16-le")
    def aligned(b):
        return b + b"\x00" * ((-len(b)) % 4)

    p1, p2, p3, p4 = [int(x) for x in filever.split(",")]
    ms = (p1 << 16) | p2
    ls = (p3 << 16) | p4
    pairs_def = [
        ("CompanyName", company),
        ("FileDescription", desc),
        ("InternalName", internal),
        ("LegalCopyright", copyright),
        ("OriginalFilename", original),
        ("ProductName", product),
        ("ProductVersion", filever.replace(",", ".")),
    ]
    pairs_b = b""
    for k, v in pairs_def:
        kb = aligned(u16s(k) + b"\x00\x00")
        vb = aligned(u16s(v) + b"\x00\x00")
        pairs_b += struct.pack("<I", len(kb) + len(vb)) + kb + vb
    pairs_b += struct.pack("<I", 0)
    lb_len = len(pairs_b)
    lang_block = struct.pack("<II", lb_len, 1) + aligned(u16s("0804 04B0") + b"\x00\x00") + pairs_b
    sf = struct.pack("<II", lb_len + 28, 1) + aligned(u16s("StringFileInfo") + b"\x00\x00") + lang_block
    variable = aligned(sf) + struct.pack("<I", 0)
    total = 64 + len(variable)
    fixed = struct.pack(
        "<4H14I",
        total, 0xFEEF, 0x0002, 0,
        0x3F, 0x00, ms, ls, ms, ls,
        0x3F, 0x00, 0x040004, 0x0002, 0, 0, 0, 0,
    )
    payload = fixed + variable
    open(out_path, "wb").write(payload)
    print("vs payload ok: %d B" % len(payload))

if __name__ == "__main__":
    main()

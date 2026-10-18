# -*- coding: utf-8 -*-
# tools/cpq-shell/gen_classic_res.py
# 生成「经典 v0」Win32 资源文件（.res）：图标（来自 .ico）+ RT_VERSION（标准 VS_VERSION_INFO 手工二进制）。
# 背景：老 SDK rc.exe 输出「现代」.res（数据段格式），而 Framework csc 期望经典 v0 格式 →
# 两者兼容时 csc 把资源目录数据指针写错（FindResource 全 0，文件属性全空）。
# 经典 v0 布局：
#   +0: u32 0 (签名)
#   +4: u32 资源数
#   +8: 每资源 12B: u32 name_id | u32 type(高16=语言,低16=类型ID) | u32 data_offset(文件内偏移)
#   数据区（紧随条目表，按表序）: 每块 u32 size + u32 ts + u32 ptr + u32 size(重复, 老格式怪癖) + bytes
# 用法：python gen_classic_res.py <out.res> <brush.ico> <filever=1,0,24,0>
import sys, struct, io

def ico_images(ico_path):
    d = io.open(ico_path, "rb").read()
    r, t, n = struct.unpack_from("<HHH", d, 0)
    assert t == 1, "not an .ico"
    out = []
    base = 6
    for i in range(n):
        w, h, colors, reserved, planes, bpp, size, off = struct.unpack_from("<BBBBHHII", d, base + i*16)
        out.append(d[off + base:off + base + size])
    return out

def group_icon(n):
    # GROUPICONDIR: u16 reserved, u16 type(1), u16 count + N × (u8 w,h,colors,reserved + u16 planes,bitcount + u32 bytes_in_res + u32 image_number)
    hdr = struct.pack("<3H", 0, 1, n)
    entries = b""
    for i in range(n):
        entries += struct.pack("<4BHHII", 0, 0, 0, 0, 1, 32, 0, i + 1)
    return hdr + entries

def vs_version_info(filever_dot, pairs):
    def u16s(s):
        return s.encode("utf-16-le")
    def aligned(b):
        return b + b"\x00" * ((-len(b)) % 4)
    p1, p2, p3, p4 = [int(x) for x in filever_dot.split(",")]
    ms = (p1 << 16) | p2
    ls = (p3 << 16) | p4
    pairs_b = b""
    for k, v in pairs:
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
    return fixed + variable

def main():
    out_path, ico_path, filever = sys.argv[1], sys.argv[2], sys.argv[3]
    product = sys.argv[4] if len(sys.argv) > 4 else "系统清理与优化工具"
    desc = sys.argv[5] if len(sys.argv) > 5 else "面向 Windows 10/11 的一体化系统清理、优化与维护工具（智能外壳）"
    copyright = sys.argv[6] if len(sys.argv) > 6 else "Copyright © 2026"
    company = sys.argv[7] if len(sys.argv) > 7 else "dandelion80231"
    internal = sys.argv[8] if len(sys.argv) > 8 else "cpq-shell"
    original = sys.argv[9] if len(sys.argv) > 9 else "cpq-shell.exe"

    images = ico_images(ico_path)
    n = len(images)
    group = group_icon(n)
    version = vs_version_info(
        filever,
        [
            ("CompanyName", company),
            ("FileDescription", desc),
            ("InternalName", internal),
            ("LegalCopyright", copyright),
            ("OriginalFilename", original),
            ("ProductName", product),
            ("ProductVersion", filever.replace(",", ".")),
        ],
    )
    # 布局：条目表 (N+2)*12 + 数据区（顺序：N 个图标、group、version）
    table = 8 + 12 * (n + 2)
    data_off = table
    offsets = []
    for b in images:
        offsets.append(data_off)
        data_off += 16 + len(b)
    offsets.append(data_off)
    data_off += 16 + len(group)
    offsets.append(data_off)
    data_off += 16 + len(version)

    buf = io.BytesIO()
    buf.write(struct.pack("<2I", 0, n + 2))
    for i in range(n):
        buf.write(struct.pack("<3I", i + 1, 3, offsets[i]))  # RT_ICON
    buf.write(struct.pack("<3I", 1, 14, offsets[n]))          # RT_GROUP_ICON id=1
    buf.write(struct.pack("<3I", 1, 16, offsets[n + 1]))      # RT_VERSION id=1
    for b in list(images) + [group, version]:
        buf.write(struct.pack("<4I", len(b), 0, 0, len(b)))
        buf.write(b)
    io.open(out_path, "wb").write(buf.getvalue())
    print("classic res ok: %d icons + group + version, total %d B" % (n, buf.tell()))

if __name__ == "__main__":
    main()

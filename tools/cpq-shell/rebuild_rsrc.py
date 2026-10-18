# -*- coding: utf-8 -*-
# tools/cpq-shell/rebuild_rsrc.py
# 把 csc 生成 exe 的资源段（.rsrc）重建为标准 Win32 布局：
# 资源目录树（leaf OffsetToData 直接指向数据字节，相对 .rsrc 起点）+ 原始数据区。
# 数据 = brush.ico 的 11 张图标（RT_ICON 1..11）+ GROUP_ICON（rid 1）+ 标准 VS_VERSION_INFO（RT_VERSION rid 1, lang 0）。
# 用法：python rebuild_rsrc.py <exe> <brush.ico> <vs_payload.bin>
import sys, struct, io

RT_ICON, RT_GROUP_ICON, RT_VERSION = 3, 14, 16
LANG = 0x0000  # 语言中性（全平台可见）

def parse_exe(data):
    pe_off = struct.unpack_from("<I", data, 0x3C)[0]
    coff = pe_off + 4
    num_sec, = struct.unpack_from("<H", data, coff + 2)
    soo, = struct.unpack_from("<H", data, coff + 16)
    sec_tab = coff + 20 + soo
    rsrc = None
    for i in range(num_sec):
        e = sec_tab + 40 * i
        vsz, va, rsz, rptr = struct.unpack_from("<4I", data, e + 8)
        if va != 0 and rsz > 50000 and rptr > 0:
            rsrc = (va, rsz, rptr, e)
    return rsrc

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
    hdr = struct.pack("<3H", 0, 1, n)
    entries = b"".join(struct.pack("<4BHHII", 0, 0, 0, 0, 1, 32, 0, i + 1) for i in range(n))
    return hdr + entries

def dir_header(ids):
    # Characteristics(4) + TimeDateStamp(4) + MajorVer(2) + MinorVer(2) + NamedCount(2) + IdCount(2)
    return struct.pack("<2I4H", 0, 0, 0, 0, 0, ids)

def entry(name_id, off):
    return struct.pack("<2I", name_id, off)

def main():
    exe_path, ico_path, payload_path = sys.argv[1], sys.argv[2], sys.argv[3]
    data = bytearray(open(exe_path, "rb").read())
    vs = open(payload_path, "rb").read()
    icons = ico_images(ico_path)
    n = len(icons)
    group = group_icon(n)
    blocks = list(icons) + [group, vs]  # 13 个数据块

    rsrc = parse_exe(bytes(data))
    if rsrc is None:
        print("rebuild_rsrc: 未找到资源段")
        return 1
    rsrc_va, rsrc_rsz, rsrc_rptr, rsrc_hdr = rsrc

    # ---- 数据区（4B 对齐，每块 16B 头：Size/Time/Ptr/NumFixups + bytes，对齐 cmd.exe 标准布局）----
    data_region = b""
    offsets = []
    for b in blocks:
        offsets.append(len(data_region))
        data_region += struct.pack("<4I", len(b), 0, 0, 0) + b + b"\x00" * ((4 - (len(b) % 4)) % 4)

    # ---- 目录树布局 ----
    # top(16+8*3) | type×3(16+8) | icon_rid(16+8*n) | icon_leaves×n(16+8) | group_leaf(16+8) | version_leaf(16+8)
    top_off = 0
    type_off = top_off + 16 + 8 * 3
    icon_rid_off = type_off + 3 * (16 + 8)
    icon_leaves_off = icon_rid_off + 16 + 8 * n
    group_leaf_off = icon_leaves_off + n * (16 + 8)
    version_leaf_off = group_leaf_off + 16 + 8
    dirs_total = version_leaf_off + 16 + 8
    data_off = dirs_total

    out = bytearray()
    # top
    out += dir_header(3)
    out += entry(RT_ICON, type_off + 0)
    out += entry(RT_GROUP_ICON, type_off + 24)
    out += entry(RT_VERSION, type_off + 48)
    # type dirs（child 指向下一级目录的起点（头部 16B 处））
    out += dir_header(1) + entry(1, icon_rid_off)                 # RT_ICON → icon rid 目录
    out += dir_header(1) + entry(1, group_leaf_off)              # RT_GROUP_ICON → group 语言目录
    out += dir_header(1) + entry(1, version_leaf_off)            # RT_VERSION → version 语言目录
    # icon rid 目录：rid 1..n → 各自语言目录
    out += dir_header(n)
    for rid in range(1, n + 1):
        out += entry(rid, icon_leaves_off + 24 * (rid - 1))
    # icon 语言目录
    for rid in range(1, n + 1):
        out += dir_header(1) + entry(LANG, data_off + offsets[rid - 1])
    # group 语言目录
    out += dir_header(1) + entry(LANG, data_off + offsets[n])
    # version 语言目录
    out += dir_header(1) + entry(LANG, data_off + offsets[n + 1])
    assert len(out) == dirs_total, "layout %d != %d" % (len(out), dirs_total)

    new_rsrc = bytes(out) + data_region
    # .rsrc 为末节：替换并截断（或追加）文件尾
    new_len = len(new_rsrc)
    data = data[:rsrc_rptr] + new_rsrc
    # 节表：VirtualSize + RawSize
    struct.pack_into("<I", data, rsrc_hdr + 8, new_len)
    struct.pack_into("<I", data, rsrc_hdr + 16, new_len)
    open(exe_path, "wb").write(bytes(data))
    print("rebuild_rsrc: 标准 .rsrc %d B（目录 %d + 数据 %d，%d 资源），文件 %d B" % (
        new_len, dirs_total, len(data_region), 13, len(data)))
    return 0

if __name__ == "__main__":
    sys.exit(main())

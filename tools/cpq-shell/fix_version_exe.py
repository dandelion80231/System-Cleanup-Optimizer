# -*- coding: utf-8 -*-
# tools/cpq-shell/fix_version_exe.py
# 修复 csc 生成 exe 中 RT_VERSION 资源目录 OffsetToData 指针错位（指向数据区首个图标块），
# 导致 FindResource/属性页全空。把该字段改为 version payload 在 .rsrc 内的真实偏移。
# 用法：python fix_version_exe.py <exe>
import sys, struct

def main():
    exe_path = sys.argv[1]
    data = bytearray(open(exe_path, "rb").read())
    # PE 解析
    pe_off = struct.unpack_from("<I", data, 0x3C)[0]
    coff = pe_off + 4
    num_sec, = struct.unpack_from("<H", data, coff + 2)
    soo, = struct.unpack_from("<H", data, coff + 16)
    opt = coff + 20
    sec_tab = opt + soo
    rsrc_va = rsrc_raw = rsrc_rsz = None
    for i in range(num_sec):
        e = sec_tab + 40 * i
        vsz, va, rsz, rptr = struct.unpack_from("<4I", data, e + 8)
        if va != 0 and rsz > 50000 and rptr > 0:
            rsrc_va, rsrc_raw, rsrc_rsz = va, rptr, rsz
    if rsrc_raw is None:
        print("fix_version_exe: 未找到资源段")
        return 1
    # 定位 VS payload：wValueLength=0xFEEF 哨兵 + wLength 合理 + 后续含 StringFileInfo(UTF-16) + 位于 .rsrc 段内
    sent = struct.pack("<H", 0xFEEF)
    sf_u16 = "StringFileInfo".encode("utf-16-le")
    payload_off = None
    i = 0
    while True:
        i = data.find(sent, i)
        if i < 0:
            break
        rel = i - 2 - rsrc_raw
        if 0 <= rel < rsrc_rsz:
            wlen, = struct.unpack_from("<H", data, i - 2)
            if 200 < wlen < 20000 and data[i:i + wlen].find(sf_u16) >= 0:
                payload_off = i - 2  # 记录最后一个（version 是数据区末尾资源）
        i += 2
    if payload_off is None:
        print("fix_version_exe: 未找到 .rsrc 内的有效 VS payload")
        return 1
    payload_rel = payload_off - rsrc_raw
    # 找 RT_VERSION leaf 的 OffsetToData 字段
    res_dir = rsrc_raw
    nn, ni = struct.unpack_from("<2H", data, res_dir + 12)
    target = None
    for i in range(nn + ni):
        e = res_dir + 16 + i * 8
        h, child = struct.unpack_from("<2I", data, e)
        if (h & 0xFFFF) != 16:
            continue
        co = res_dir + (child & 0x007FFFFF)
        nn2, ni2 = struct.unpack_from("<2H", data, co + 12)
        for k in range(nn2 + ni2):
            ee = co + 16 + k * 8
            h2, ch2 = struct.unpack_from("<2I", data, ee)
            if (h2 & 0xFFFF) != 1:
                continue
            lo = res_dir + (ch2 & 0x007FFFFF)
            nn3, ni3 = struct.unpack_from("<2H", data, lo + 12)
            for m in range(nn3 + ni3):
                elm = lo + 16 + m * 8
                target = elm + 4
                break
    if target is None:
        print("fix_version_exe: 未找到 RT_VERSION leaf")
        return 1
    old = struct.unpack_from("<I", data, target)[0]
    print("fix_version_exe: leaf cur=0x%x payload_rel=0x%x" % (old, payload_rel))
    if old == payload_rel:
        print("fix_version_exe: 指针已正确")
        return 0
    struct.pack_into("<I", data, target, payload_rel)
    open(exe_path, "wb").write(bytes(data))
    print("fix_version_exe: RT_VERSION 指针 0x%x -> 0x%x" % (old, payload_rel))
    return 0

if __name__ == "__main__":
    sys.exit(main())

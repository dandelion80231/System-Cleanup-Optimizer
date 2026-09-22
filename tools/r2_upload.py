# -*- coding: utf-8 -*-
"""R2 覆写上传（SigV4，region=auto / 终止串 aws4_request，R2 专属 canonical 结构：
header 块每行带 \\n（含末尾），再补一个空行，然后才是 SignedHeaders 行）。
该结构取自 R2 403 响应 <CanonicalRequestBytes> 逐字节验证（2026-09-22）。

本机 IPv4 到 Cloudflare 被 RST 时自动切 IPv6（候选存活探测）。

用法:
    python r2_upload.py <本地文件> <R2键名(可含中文)> [期望SHA256]
例:
    python r2_upload.py "C:\\cpq-builds\\shellproj\\CpqShell\\bin\\cpq-shell.exe" "系统清理与优化工具_v1.23.exe" 994fe9b8...
"""
import sys, hashlib, hmac, datetime, urllib.parse, urllib.request, urllib.error, socket as _sock

ACC = "fb0b2ada3007992934b696ba57e89f54"
AK = "cc3200d2df65f0ca81d6133577348aae"
SK = "efb7d83315f067c3e10a561b87edc3b9e2100f09ac6f2a909ebe76543a3c750d"
BUCKET = "mvp"


def connect_robust(hostport, timeout=_sock._GLOBAL_DEFAULT_TIMEOUT, source_address=None):
    """v4+v6 候选逐一探活，第一个连通者复用（CF IPv4 被 GFW RST 时自动走 IPv6）。"""
    host, port = hostport
    cands = []
    for fam in (_sock.AF_INET, _sock.AF_INET6):
        try:
            cands += [(fam, r[4][0]) for r in _sock.getaddrinfo(host, port, fam, _sock.SOCK_STREAM)]
        except OSError:
            pass
    last = None
    for fam, ip in cands:
        s = _sock.socket(fam, _sock.SOCK_STREAM)
        s.settimeout(90)
        try:
            s.connect((ip, port))
            if timeout is not _sock._GLOBAL_DEFAULT_TIMEOUT:
                s.settimeout(timeout)
            return s
        except OSError as e:
            last = e
            s.close()
    raise last


_sock.create_connection = connect_robust


def main():
    if len(sys.argv) < 3:
        print("usage: r2_upload.py <file> <r2-key> [expect-sha256]")
        raise SystemExit(2)
    file, key = sys.argv[1], sys.argv[2]
    expect = sys.argv[3].lower() if len(sys.argv) > 3 else None

    data = open(file, "rb").read()
    payload = hashlib.sha256(data).hexdigest()
    if expect and payload != expect:
        print("SHA mismatch: local %s vs expect %s" % (payload[:16], expect[:16]))
        raise SystemExit(1)

    host = ACC + ".r2.cloudflarestorage.com"
    enc = urllib.parse.quote(key, safe="")
    now = datetime.datetime.now(datetime.timezone.utc)
    amz, ds = now.strftime("%Y%m%dT%H%M%SZ"), now.strftime("%Y%m%d")
    scope = "%s/auto/s3/aws4_request" % ds

    hdrs = {"host": host, "x-amz-content-sha256": payload, "x-amz-date": amz}
    sh = ";".join(sorted(hdrs))
    ch = "".join("%s:%s\n" % (k, v) for k, v in sorted(hdrs.items()))  # 每行含尾部 \n
    # R2 专属：ch(带尾 \n) + 空行 + SignedHeaders
    canon = "PUT\n" + "/%s/%s" % (BUCKET, enc) + "\n\n" + ch + "\n" + sh + "\n" + payload
    canon_hash = hashlib.sha256(canon.encode()).hexdigest()
    sts = "AWS4-HMAC-SHA256\n%s\n%s\n%s" % (amz, scope, canon_hash)

    def H(k, d):
        return hmac.new(k, d, hashlib.sha256).digest()
    k = H(b"AWS4" + SK.encode(), ds.encode())
    k = H(k, b"auto")
    k = H(k, b"s3")
    k = H(k, b"aws4_request")
    sig = hmac.new(k, sts.encode(), hashlib.sha256).hexdigest()

    req = urllib.request.Request(
        "https://%s/%s/%s" % (host, BUCKET, enc), data=data, method="PUT",
        headers={x: y for x, y in hdrs.items() if x != "host"})
    req.add_header("Authorization",
                   "AWS4-HMAC-SHA256 Credential=%s/%s, SignedHeaders=%s, Signature=%s" % (AK, scope, sh, sig))
    try:
        with urllib.request.urlopen(req, timeout=300) as r:
            print("PUT:", r.status, "etag:", r.headers.get("ETag"))
    except urllib.error.HTTPError as e:
        print("PUT HTTP", e.code, e.read().decode(errors="replace")[:300])
        raise SystemExit(1)
    print("sha256:", payload)


if __name__ == "__main__":
    main()

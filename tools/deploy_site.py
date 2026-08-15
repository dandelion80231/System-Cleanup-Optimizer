#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Cloudflare Pages Direct Upload deploy tool (no wrangler / npm needed).

Implements the REAL 4-step Direct Upload flow that Wrangler uses:
  1. POST /accounts/{acct}/pages/projects/{proj}/upload-token   (API token)  -> JWT
  2. POST /pages/assets/upload                                  (JWT)        -> upload blobs
  3. POST /pages/assets/upsert-hashes                           (JWT)        -> register hashes
  4. POST /accounts/{acct}/pages/projects/{proj}/deployments    (API token)  -> manifest

CRITICAL: asset key = blake3( base64(content) + extension_without_dot ).hex()[:32]
Cloudflare's serving layer derives this key independently, so SHA-256 (or any other
hash) in the manifest produces a deployment that "succeeds" but 404/500s forever.

Usage:
  python deploy_site.py <ACCOUNT_ID> <API_TOKEN> [SITE_DIR] [PROJECT_NAME]
Environment fallbacks: CF_ACCOUNT_ID, CF_API_TOKEN
Default SITE_DIR: site-dist  (sibling of this script's parent dir)
Default PROJECT_NAME: cpq-system-tool
Proxy: honors HTTPS_PROXY / HTTP_PROXY env vars (e.g. Watt Toolkit 127.0.0.1:26561).
"""
import os
import sys
import json
import ssl
import base64
import blake3
import urllib.request
import urllib.error

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_SITE_DIR = os.path.join(os.path.dirname(HERE), "site-dist")
DEFAULT_PROJECT = "cpq-system-tool"
API = "https://api.cloudflare.com/client/v4"


def cf_hash(data: bytes, rel_path: str) -> str:
    ext = os.path.splitext(rel_path)[1][1:]  # "index.html" -> "html"
    b64 = base64.b64encode(data)             # ASCII bytes
    return blake3.blake3(b64 + ext.encode("ascii")).hexdigest()[:32]


def http_json(method, url, token, body=None, is_jwt=False):
    data = json.dumps(body).encode("utf-8") if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    auth = ("Bearer " + token) if is_jwt else ("Bearer " + token)
    req.add_header("Authorization", auth)
    if data is not None:
        req.add_header("Content-Type", "application/json")
    ctx = ssl.create_default_context()
    ctx.check_revocation = False
    opener = urllib.request.build_opener(urllib.request.HTTPSHandler(context=ctx))
    resp = opener.open(req, timeout=300)
    return resp.status, json.loads(resp.read().decode("utf-8"))


def main():
    account_id = sys.argv[1] if len(sys.argv) > 1 else os.environ.get("CF_ACCOUNT_ID")
    token = sys.argv[2] if len(sys.argv) > 2 else os.environ.get("CF_API_TOKEN")
    site_dir = sys.argv[3] if len(sys.argv) > 3 else DEFAULT_SITE_DIR
    project = sys.argv[4] if len(sys.argv) > 4 else DEFAULT_PROJECT
    if not account_id or not token:
        print("ERROR: ACCOUNT_ID and API_TOKEN required.", file=sys.stderr)
        sys.exit(2)
    site_dir = os.path.abspath(site_dir)
    if not os.path.isdir(site_dir):
        print("ERROR: site dir not found:", site_dir, file=sys.stderr)
        sys.exit(2)

    proxy = (os.environ.get("HTTPS_PROXY") or os.environ.get("https_proxy")
             or os.environ.get("HTTP_PROXY") or os.environ.get("http_proxy"))
    if proxy:
        os.environ["HTTPS_PROXY"] = proxy
        os.environ["HTTP_PROXY"] = proxy

    # collect files
    entries = []
    for name in sorted(os.listdir(site_dir)):
        p = os.path.join(site_dir, name)
        if os.path.isfile(p):
            ctype = "application/octet-stream"
            if name.endswith(".html"):
                ctype = "text/html; charset=utf-8"
            elif name.endswith(".css"):
                ctype = "text/css; charset=utf-8"
            elif name.endswith(".js"):
                ctype = "application/javascript"
            entries.append((name, p, ctype))

    # compute blake3 keys + base64 content
    manifest = {}
    upload_items = []
    for name, p, ctype in entries:
        with open(p, "rb") as f:
            content = f.read()
        key = cf_hash(content, name)
        manifest["/" + name] = key
        upload_items.append({
            "key": key,
            "value": base64.b64encode(content).decode("ascii"),
            "metadata": {"contentType": ctype},
            "base64": True,
        })
        print("  /%s  -> blake3 %s  (%d bytes)" % (name, key, len(content)))

    # Step 1: upload token (GET)
    s, j = http_json("GET",
                     "%s/accounts/%s/pages/projects/%s/upload-token" % (API, account_id, project),
                     token)
    if not j.get("success"):
        print("upload-token failed:", s, j); sys.exit(1)
    jwt = j["result"]["jwt"]
    print("Step1 upload-token OK")

    # Step 2: upload blobs
    s, j = http_json("POST", "%s/pages/assets/upload" % API, jwt, upload_items, is_jwt=True)
    print("Step2 assets/upload:", s, "success=", j.get("success"))
    if not j.get("success"):
        print(j); sys.exit(1)

    # Step 3: upsert hashes
    s, j = http_json("POST", "%s/pages/assets/upsert-hashes" % API, jwt,
                     {"hashes": [it["key"] for it in upload_items]}, is_jwt=True)
    print("Step3 upsert-hashes:", s, "success=", j.get("success"))
    if not j.get("success"):
        print(j); sys.exit(1)

    # Step 4: create deployment (multipart manifest only)
    boundary = b"----cpqCloudflarePagesBoundary7Q3k9XyZ"
    crlf = b"\r\n"
    parts = [b"--" + boundary,
             b'Content-Disposition: form-data; name="manifest"',
             b"Content-Type: application/json", b"",
             json.dumps(manifest).encode("utf-8"),
             b"--" + boundary + b"--", b""]
    body = crlf.join(parts)
    url = "%s/accounts/%s/pages/projects/%s/deployments" % (API, account_id, project)
    req = urllib.request.Request(url, data=body, method="POST")
    req.add_header("Authorization", "Bearer " + token)
    req.add_header("Content-Type", "multipart/form-data; boundary=" + boundary.decode())
    ctx = ssl.create_default_context(); ctx.check_revocation = False
    opener = urllib.request.build_opener(urllib.request.HTTPSHandler(context=ctx))
    resp = opener.open(req, timeout=300)
    out = json.loads(resp.read().decode("utf-8"))
    print("Step4 deployments:", resp.status, "success=", out.get("success"))
    r = out.get("result", {})
    print("deployment id:", r.get("short_id"), "url:", r.get("url"))


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""读取本机私有 CF token 文件，注入环境变量后调用 deploy_site.py（token 不出现在命令行参数）。"""
import os
import subprocess
import sys

TOKEN_FILE = r"C:\Users\000\.workbuddy\cf_api_token.md"
DEPLOY = r"D:\电脑桌面\cpq\tools\deploy_site.py"
VENV_PY = r"C:\Users\000\.workbuddy\binaries\python\envs\default\Scripts\python.exe"

def read_token():
    data = {}
    with open(TOKEN_FILE, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line.startswith("CF_ACCOUNT_ID="):
                data["CF_ACCOUNT_ID"] = line.split("=", 1)[1].strip()
            elif line.startswith("CF_API_TOKEN="):
                data["CF_API_TOKEN"] = line.split("=", 1)[1].strip()
    if "CF_ACCOUNT_ID" not in data or "CF_API_TOKEN" not in data:
        sys.exit("FATAL: token file missing required keys")
    return data

def main():
    tok = read_token()
    env = os.environ.copy()
    env["CF_ACCOUNT_ID"] = tok["CF_ACCOUNT_ID"]
    env["CF_API_TOKEN"] = tok["CF_API_TOKEN"]
    env["HTTPS_PROXY"] = "127.0.0.1:26561"
    env["HTTP_PROXY"] = "127.0.0.1:26561"
    # 用 venv python 运行 deploy（含 blake3）
    r = subprocess.run([VENV_PY, DEPLOY], env=env, cwd=r"D:\电脑桌面\cpq")
    sys.exit(r.returncode)

if __name__ == "__main__":
    main()

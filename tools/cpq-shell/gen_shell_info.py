# -*- coding: utf-8 -*-
# 从主 csproj 读取产品元数据与版本 → 生成 tools/cpq-shell/ShellInfo.generated.cs：
# 外壳（csc 直编）与主程序的「文件详细信息」（文件版本/产品版本/说明/产品名）保持一致。
# 发版链在 build_shell.bat 之前运行本脚本（先 Copy-Item tools/cpq-shell/* 到 shellproj 再编译）。
import re, os
root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
csproj = open(os.path.join(root, "src", "CpqSystemTool", "CpqSystemTool.csproj"), encoding="utf-8").read()

def prop(name, default=""):
    m = re.search(r"<%s>([^<]*)</%s>" % (name, name), csproj)
    return (m.group(1).strip() if m and m.group(1).strip() else default)

ver = prop("AssemblyVersion", "0.0.0.0")
info = prop("InformationalVersion", ver)
tmpl = """// 由 tools/cpq-shell/gen_shell_info.py 生成（随主程序版本同步，勿手改；披露包以 zero 模板归一化）
using System.Reflection;

[assembly: AssemblyTitle("%s")]
[assembly: AssemblyProduct("%s")]
[assembly: AssemblyDescription("%s（智能外壳）")]
[assembly: AssemblyCopyright("%s")]
[assembly: AssemblyVersion("%s")]
[assembly: AssemblyFileVersion("%s")]
[assembly: AssemblyInformationalVersion("v%s")]
"""
out = tmpl % (
    prop("AssemblyTitle", "系统清理与优化工具"),
    prop("AssemblyProduct", "系统清理与优化工具"),
    prop("AssemblyDescription", "面向 Windows 10/11 的一体化系统清理、优化与维护工具"),
    prop("AssemblyCopyright", "Copyright © 2026"),
    ver, ver, ((info.split(".")[0] + "." + info.split(".")[2]) if len(info.split(".")) >= 3 else info),
)
p = os.path.join(root, "tools", "cpq-shell", "ShellInfo.generated.cs")
open(p, "w", encoding="utf-8").write(out)
print("ShellInfo.generated.cs ok: %s / %s" % (ver, info))

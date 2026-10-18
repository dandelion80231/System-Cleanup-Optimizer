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
rc = """// 由 tools/cpq-shell/gen_shell_info.py 生成（随主程序版本同步，勿手改；UTF-8 BOM）
// （VERSIONINFO 为 rc 自有语法，无需 C 头文件；winver.h 需要 SDK INCLUDE 环境）

1 ICON "brush.ico"

1 VERSIONINFO
  FILEVERSION %(filever)s
  PRODUCTVERSION %(filever)s
  FILEFLAGSMASK 0x3fL
  FILEFLAGS 0x0L
  FILEOS 0x40004L
  FILETYPE 0x1L
  FILESUBTYPE 0x0L
BEGIN
  BLOCK "StringFileInfo"
  BEGIN
    BLOCK "0804 04B0"
    BEGIN
      VALUE "CompanyName", "dandelion80231\\0"
      VALUE "FileDescription", "%(desc)s（智能外壳）\\0"
      VALUE "InternalName", "cpq-shell\\0"
      VALUE "LegalCopyright", "%(copyright)s\\0"
      VALUE "OriginalFilename", "cpq-shell.exe\\0"
      VALUE "ProductName", "%(product)s\\0"
      VALUE "ProductVersion", "%(filever)s\\0"
    END
  END
  BLOCK "VarFileInfo"
  BEGIN
    VALUE "Translation", 0x0804, 1200
  END
END
""" % {
    "filever": ver.replace(".", ","),
    "desc": prop("AssemblyDescription", "面向 Windows 10/11 的一体化系统清理、优化与维护工具"),
    "copyright": prop("AssemblyCopyright", "Copyright © 2026"),
    "product": prop("AssemblyProduct", "系统清理与优化工具"),
}
open(os.path.join(os.path.dirname(p), "version.rc"), "w", encoding="utf-16").write(rc)   # UTF-16 源（老 SDK rc.exe 无法解析 UTF-8 BOM 中的中文/©）
print("ShellInfo.generated.cs ok: %s / %s; version.rc ok (0804 中文块)" % (ver, info))






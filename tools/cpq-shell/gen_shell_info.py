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
resx = """<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:import namespace="http://www.w3.org/XML/1998/namespace" />
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="metadata">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" />
              </xsd:sequence>
              <xsd:attribute name="name" use="required" type="xsd:string" />
              <xsd:attribute name="type" type="xsd:string" />
              <xsd:attribute name="mimetype" type="xsd:string" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="assembly">
            <xsd:complexType>
              <xsd:attribute name="alias" type="xsd:string" />
              <xsd:attribute name="name" type="xsd:string" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" msdata:Ordinal="1" />
              <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="2" />
              <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="3" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="resheader">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e088</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e088</value>
  </resheader>
  <assembly alias="System.Windows.Forms" name="System.Windows.Forms" />
  <data name="1" xml:space="preserve">
    <value>
      <VS_VERSION_INFO>
        <fileversion>%(fileversion)s</fileversion>
        <productversion>%(fileversion)s</productversion>
        <fileflags>0x0L</fileflags>
        <fileos>0x40004L</fileos>
        <filetype>0x1L</filetype>
        <filesizereserved>0x0L</filesizereserved>
        <filesubtypereserved>0x0L</filesubtypereserved>
        <BEGIN>
          <VarFileInfo>
            <Translation>
              <language>0804</language>
              <codepage>1200</codepage>
            </Translation>
          </VarFileInfo>
          <StringFileInfo>
            <0804 04B0>
              <CompanyName>dandelion80231</CompanyName>
              <FileDescription>%(desc)s（智能外壳）</FileDescription>
              <InternalName>cpq-shell</InternalName>
              <LegalCopyright>%(copyright)s</LegalCopyright>
              <OriginalFilename>cpq-shell.exe</OriginalFilename>
              <ProductName>%(product)s</ProductName>
              <ProductVersion>%(info)s</ProductVersion>
            </0804 04B0>
          </StringFileInfo>
        </END>
      </VS_VERSION_INFO>
    </value>
  </data>
</root>
""" % {
    "fileversion": ver,
    "info": info,
    "desc": prop("AssemblyDescription", "面向 Windows 10/11 的一体化系统清理、优化与维护工具"),
    "copyright": prop("AssemblyCopyright", "Copyright © 2026"),
    "product": prop("AssemblyProduct", "系统清理与优化工具"),
}
open(os.path.join(os.path.dirname(p), "version.resx"), "w", encoding="utf-8").write(resx)
print("ShellInfo.generated.cs ok: %s / %s; version.resx ok (0804 中文块)" % (ver, info))

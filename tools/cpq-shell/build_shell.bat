@echo off
cd /d D:\cpq-builds\shellproj\CpqShell
if not exist bin mkdir bin
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\resgen.exe" version.resx version.resources
echo RESGEN_EXIT %errorlevel%
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /out:bin\cpq-shell.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/resource:D:\cpq-builds\shell-payload\cpq_main_v122.exe,CpqShell.payload.main" "/resource:D:\cpq-builds\shell-payload\WebView2Loader.dll,CpqShell.payload.loader" /win32res:version.resources /win32icon:brush.ico Program.cs MainExeSha.generated.cs ShellInfo.generated.cs
echo CSC_EXIT %errorlevel%

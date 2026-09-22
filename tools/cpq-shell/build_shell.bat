@echo off
cd /d C:\cpq-builds\shellproj\CpqShell
if not exist bin mkdir bin
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /out:bin\cpq-shell.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/resource:C:\cpq-builds\shell-payload\cpq_main_v122.exe,CpqShell.payload.main" "/resource:C:\cpq-builds\shell-payload\WebView2Loader.dll,CpqShell.payload.loader" /win32icon:brush.ico Program.cs
echo CSC_EXIT %errorlevel%

@echo off
cd /d D:\cpq-builds\shellproj\CpqShell
if not exist bin mkdir bin
rem VS_VERSION_INFO ??????????/???csc ?????Windows SDK rc.exe ?? version.rc
set "RC_EXE="
for /f "delims=" %%v in ('dir /b /ad "C:\Program Files (x86)\Windows Kits\10\bin" 2^>nul ^| sort /r') do (
  if exist "C:\Program Files (x86)\Windows Kits\10\bin\%%v\x64\rc.exe" if not defined RC_EXE set "RC_EXE=C:\Program Files (x86)\Windows Kits\10\bin\%%v\x64\rc.exe"
)
set "WIN32RES="
set "WIN32ICON=/win32icon:brush.ico"
if defined RC_EXE (
  "%RC_EXE%" /r version.rc
  echo RC_EXIT %%errorlevel%%
  if exist version.res (
  set "WIN32RES=/win32res:version.res"
  rem 图标已编入 version.res（rc 的 ICON 行）；/win32res 与 /win32icon 互斥（CS1565）
  set "WIN32ICON="
)
) else (
  echo [warn] ??? Windows SDK rc.exe??? VS_VERSION_INFO ?????????? ShellInfo.generated.cs ???
)
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /out:bin\cpq-shell.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/resource:D:\cpq-builds\shell-payload\cpq_main_v122.exe,CpqShell.payload.main" "/resource:D:\cpq-builds\shell-payload\WebView2Loader.dll,CpqShell.payload.loader" %WIN32RES% %WIN32ICON% Program.cs MainExeSha.generated.cs ShellInfo.generated.cs
echo CSC_EXIT %errorlevel%


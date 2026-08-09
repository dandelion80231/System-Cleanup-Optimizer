#Requires -Version 5.1
<#
.SYNOPSIS
    official_exe_finder 探针的本地依赖安装脚本（含检测，不重复下载）。
.DESCRIPTION
    检测优先级（均为“先检测再下载”，已就绪的绝不重复下载）：
    1) Node：系统 PATH 的 node > 本地 .tools\node\node.exe > 下载到 .tools\
    2) Playwright：node_modules\playwright 已存在则跳过 npm install
    3) Chromium：node_modules\{playwright-core,playwright}\.local-browsers\chromium-* 已存在则跳过下载
#>

$ErrorActionPreference = "Stop"

# 强制 stdout 使用 UTF-8，避免在“Beta: 使用 UTF-8”系统上重定向输出出现乱码
$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# 以脚本所在目录为基准（无论从哪里调用都能正确定位）
$base = Split-Path -Parent $MyInvocation.MyCommand.Definition

function Write-Step($msg) { Write-Host ("[*] " + $msg) -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host ("[OK] " + $msg) -ForegroundColor Green }
function Write-Err($msg)  { Write-Host ("[!] " + $msg) -ForegroundColor Red }

$nodeExe = $null
$npmCmd  = $null
$npxCmd  = $null

try {
    Write-Step ("脚本基准目录: " + $base)

    # ---------- 1) 选择 Node（系统 PATH 优先，避免重复下载 Node）----------
    $sysNode = $null
    try { $sysNode = (Get-Command node -ErrorAction SilentlyContinue).Source } catch { }
    $localNodeExe = Join-Path $base ".tools\node\node.exe"
    $localNpm = Join-Path $base ".tools\node\npm.cmd"
    $localNpx = Join-Path $base ".tools\node\npx.cmd"
    $nodeDir = Join-Path $base ".tools\node"

    if ($sysNode -and (Test-Path $sysNode)) {
        Write-Ok ("检测到系统 Node: " + $sysNode + "，优先使用，不下载到项目。")
        $nodeExe = $sysNode
        $npmCmd = "npm"
        $npxCmd = "npx"
    }
    elseif (Test-Path $localNodeExe) {
        Write-Ok ("检测到本地 Node: " + $localNodeExe)
        $nodeExe = $localNodeExe
        $npmCmd = $localNpm
        $npxCmd = $localNpx
    }
    else {
        Write-Step "未检测到 Node（系统 PATH 与本地均无），开始下载 Windows x64 便携版……"
        # 查询 nodejs.org 最新 LTS 版本（lts 字段非 false 即为长期支持版）
        $index = Invoke-RestMethod -Uri "https://nodejs.org/dist/index.json" -UseBasicParsing
        $latest = $index | Where-Object { $_.lts -and $_.lts -ne $false } | Select-Object -First 1
        if (-not $latest) { $latest = $index | Select-Object -First 1 }
        $ver = $latest.version
        $zipUrl = ("https://nodejs.org/dist/" + $ver + "/node-" + $ver + "-win-x64.zip")
        $tmpZip = Join-Path $env:TEMP ("node-" + [guid]::NewGuid().ToString("N") + ".zip")

        Write-Step ("下载: " + $zipUrl)
        Invoke-WebRequest -Uri $zipUrl -OutFile $tmpZip -UseBasicParsing

        $extract = Join-Path $base ".tools\_node_tmp"
        if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
        Expand-Archive -Path $tmpZip -DestinationPath $extract -Force

        # 便携包内层目录名为 node-<ver>-win-x64
        $inner = Join-Path $extract ("node-" + $ver + "-win-x64")
        if (-not (Test-Path $inner)) {
            $inner = (Get-ChildItem $extract | Select-Object -First 1).FullName
        }
        if (-not (Test-Path $nodeDir)) { New-Item -ItemType Directory -Path $nodeDir | Out-Null }

        # 把内层 node 目录内容搬到 .tools\node（让 .tools\node\node.exe 可用）
        Get-ChildItem $inner | ForEach-Object {
            Move-Item $_.FullName (Join-Path $nodeDir $_.Name) -Force
        }
        Remove-Item $extract -Recurse -Force
        Remove-Item $tmpZip -Force

        if (-not (Test-Path $localNodeExe)) {
            throw "Node 解压后未找到 node.exe，安装失败。"
        }
        $nodeExe = $localNodeExe
        $npmCmd = $localNpm
        $npxCmd = $localNpx
        Write-Ok ("Node " + $ver + " 已安装到 " + $localNodeExe)
    }

    # 验证 Node 可用
    & $nodeExe --version 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Node 不可用，请检查环境。" }

    # ---------- 2) npm install（检测 node_modules\playwright）----------
    $playwrightPkg = Join-Path $base "node_modules\playwright"
    if (Test-Path $playwrightPkg) {
        Write-Ok "Playwright 已安装（node_modules\playwright 存在），跳过 npm install。"
    }
    else {
        Write-Step "在 probes 目录执行 npm install（安装 playwright）……"
        $env:PLAYWRIGHT_BROWSERS_PATH = "0"
        & $npmCmd install 2>&1 | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) {
            throw ("npm install 失败（退出码 " + $LASTEXITCODE + "）。")
        }
        Write-Ok "npm 依赖安装完成。"
    }

    # ---------- 3) 安装 Chromium（检测 .local-browsers\chromium-*）----------
    $browsersRoots = @(
        (Join-Path $base "node_modules\playwright-core\.local-browsers"),
        (Join-Path $base "node_modules\playwright\.local-browsers")
    )
    $chromiumInstalled = $false
    foreach ($br in $browsersRoots) {
        if (Test-Path $br) {
            if ((Get-ChildItem $br -Directory -Filter "chromium-*").Count -gt 0) { $chromiumInstalled = $true; break }
        }
    }
    $env:PLAYWRIGHT_BROWSERS_PATH = "0"
    if ($chromiumInstalled) {
        Write-Ok "Chromium 已安装（.local-browsers\chromium-* 存在），跳过下载。"
    }
    else {
        Write-Step "安装 Chromium 浏览器（PLAYWRIGHT_BROWSERS_PATH=0，装到本地）……"
        & $npxCmd playwright install chromium 2>&1 | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) {
            throw ("playwright install chromium 失败（退出码 " + $LASTEXITCODE + "）。")
        }
        Write-Ok "Chromium 安装完成。"
    }

    Write-Ok "全部依赖就绪，可以回到「维护工具」页点击「抓取直链」了。"
    exit 0
}
catch {
    Write-Err ("依赖安装失败：" + $_.Exception.Message)
    exit 1
}

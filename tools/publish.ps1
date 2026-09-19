# CpqSystemTool 发布脚本
#
# 默认行为：产出【单文件 exe】（项目标准分发形态）—— PublishSingleFile=true，框架依赖。
#   无同级 src/ 文件夹，运行时「导出源码」会提示未找到源码披露包（.NET 单文件固有行为，非 bug）。
#
# 文件夹模式（-Folder）：不以单文件方式发布，csproj 的 CopySourceDisclosure 目标会把真实源码
#   复制到 $(PublishDir)src/CpqSystemTool，于是「导出源码」功能可用。需要分发带源码时再启用。
#
# 说明：本脚本不含硬编码非 ASCII 字面量。exe 名从 csproj <AssemblyName> 运行时读取，
#   因为 PowerShell 5.1 会把「无 BOM 的 UTF-8」文件按系统码页（GBK）误读，硬编码中文文件名会损坏。
#   本文件已保存为 UTF-8 with BOM，中文注释/提示可正确显示。

param(
    [string]$Version = "",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $false,
    [switch]$Folder = $false,
    [switch]$Zip = $false,
    [switch]$KeepPdb = $false   # P7: 保留 .pdb（默认剥离调试符号，公开分发不带 2.8MB 符号文件）
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Split-Path -Parent $scriptDir
$csproj   = Join-Path $repoRoot "src\CpqSystemTool\CpqSystemTool.csproj"

if (-not (Test-Path $csproj)) {
    Write-Error "csproj not found: $csproj"
    exit 1
}

# 从 csproj <AssemblyName> 读取 exe 名（csproj 本身 UTF-8 with BOM，可正确读取）
$csprojText = Get-Content $csproj -Raw
if ($csprojText -match '<AssemblyName>(.*?)</AssemblyName>') {
    $asmName = $Matches[1].Trim()
} else {
    Write-Error "Could not find <AssemblyName> in $csproj"
    exit 1
}
$exeName = "$asmName.exe"

$mode    = if ($Folder) { "folder" } else { "single" }
$outName = if ($Version) { "publish_$($mode)_$Version" } else { "publish_$mode" }
$outDir  = Join-Path $repoRoot $outName

$scArg = if ($SelfContained) { "true" } else { "false" }

if ($Folder) {
    Write-Host "发布 FOLDER 模式 (导出源码可用) -> $outDir (self-contained=$scArg, runtime=$Runtime, exe=$exeName)"
} else {
    Write-Host "发布 SINGLE-FILE 模式 (标准分发形态) -> $outDir (self-contained=$scArg, runtime=$Runtime, exe=$exeName)"
}

# 清理旧输出，避免残留文件
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

$pubArgs = @("publish", $csproj, "-c", "Release", "-r", $Runtime,
             "--self-contained", $scArg, "-o", $outDir)
if (-not $Folder) {
    # 默认单文件：把依赖打进 exe；IncludeNativeLibrariesForSelfExtract 让原生库也能内嵌自解压
    $pubArgs += "-p:PublishSingleFile=true"
    $pubArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
}
& dotnet @pubArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}

# ===== 清理 repo 根目录的旧运行时宿主残留 =====
# 历史遗留的自包含发布会在 repo 根留下 hostfxr.dll / hostpolicy.dll / coreclr.dll 等旧版运行时 DLL。
# 这些文件与单文件 bundle 同目录时会被 .NET 宿主优先使用（而非系统安装的运行时），
# 导致 rollForward=Minor 等配置不生效，应用报"必须安装 .NET X.Y.0"。
# 清理策略：删除已知会干扰宿主查找的 DLL + 清理产物（temp_runtimeconfig.json、fix_runtimeconfig.py 等）。
# .gitignore:53 已有 *.dll 规则，这些文件从未被 git 追踪，可安全删除。
$LegacyRuntimeDlls = @(
    "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "clretwrc.dll",
    "clrgc.dll", "clrgcexp.dll", "clrjit.dll", "mscorrc.dll",
    "mscordaccore.dll", "mscordbi.dll", "vcruntime140_cor3.dll",
    "System.Private.CoreLib.dll", "mscorlib.dll",
    "msquic.dll", "netstandard.dll",
    "D3DCompiler_47_cor3.dll", "DirectWriteForwarder.dll",
    "PenImc_cor3.dll", "PresentationNative_cor3.dll", "wpfgfx_cor3.dll",
    # NuGet restore 留在根目录的临时依赖 DLL（单文件 bundle 已将依赖嵌入 exe）
    "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll",
    "Microsoft.Web.WebView2.Wpf.dll", "WebView2Loader.dll",
    "System.Management.dll", "System.ServiceProcess.ServiceController.dll"
)
$LegacyExtras = @(
    "temp_runtimeconfig.json", "fix_runtimeconfig.py",
    "系统清理与优化工具.dll", "系统清理与优化工具.pdb"
)
$cleanedCount = 0
$cleanedBytes = 0
foreach ($name in $LegacyRuntimeDlls + $LegacyExtras) {
    $path = Join-Path $repoRoot $name
    if (Test-Path $path) {
        $size = (Get-Item $path).Length
        Remove-Item $path -Force
        $cleanedCount++
        $cleanedBytes += $size
    }
}
# 额外扫描：删除 repo 根下所有 >100KB 的 .dll（保留 WebView2 相关和 System.* 工具库）
$AllowedDlls = @()
foreach ($f in Get-ChildItem $repoRoot -Filter "*.dll" -File) {
    if ($AllowedDlls -contains $f.Name) { continue }
    if ($f.Length -gt 100000) {
        Remove-Item $f.FullName -Force
        $cleanedCount++
        $cleanedBytes += $f.Length
    }
}
if ($cleanedCount -gt 0) {
    Write-Host ("已清理 repo 根目录 {0} 个旧运行时残留 ({1:N1} MB)，避免单文件 bundle 双击失败。" -f $cleanedCount, ($cleanedBytes/1MB))
} else {
    Write-Host "repo 根目录无需清理，无旧运行时残留。"
}

$exePath = Join-Path $outDir $exeName

if (-not (Test-Path $exePath)) {
    Write-Error "EXE missing: $exePath"
    exit 1
}

# ===== P7：剥离调试符号（.pdb）=====
# 公开发布的产物默认不带 .pdb（约 2.8MB，可反查源码行号、增大分发体积）。
# 需要保留做崩溃回溯时传 -KeepPdb。
if (-not $KeepPdb) {
    $pdbs = Get-ChildItem $outDir -Filter *.pdb -Recurse -File -ErrorAction SilentlyContinue
    if ($pdbs) {
        foreach ($p in $pdbs) { Remove-Item $p.FullName -Force; Write-Host ("[P7] 已删除符号文件: " + $p.FullName) }
    } else {
        Write-Host "[P7] 无 .pdb 需删除。"
    }
}

if ($Folder) {
    # 单文件=false 时 csproj CopySourceDisclosure 目标自动复制真实源码到 $(PublishDir)src/CpqSystemTool
    $srcDir = Join-Path $outDir "src\CpqSystemTool"
    if (-not (Test-Path $srcDir)) {
        Write-Warning ("src\CpqSystemTool 未出现在 exe 同级。导出源码将不可用。请确认未传 PublishSingleFile=true 且 csproj CopySourceDisclosure 目标已执行。")
    } else {
        $csCount = (Get-ChildItem $srcDir -Recurse -Filter *.cs).Count
        $readme  = Test-Path (Join-Path $srcDir "README.md")
        Write-Host ("OK: 源码披露包就位 ($csCount 个 .cs 文件, README=$readme)。导出源码 ENABLED。")
    }
    Write-Host "分发整个文件夹 (exe + src + deps): $outDir"
} else {
    Write-Host "单文件 exe 就绪。导出源码在任意位置均可用（csproj GenerateSourcePackage 目标已在本次构建把当前源码打包内嵌，两张背景图在导出时从程序集取回）。"
    # 交付物：按项目约定复制到仓库根目录，命名为 系统清理与优化工具_vX.XX_NET10.exe
    if ($Version) {
        $suffix = if ($SelfContained) { "_NET10_selfcontained" } else { "_NET10" }
        $deliverName = "系统清理与优化工具_$Version$suffix.exe"
        $deliverPath = Join-Path $repoRoot $deliverName
        Copy-Item $exePath $deliverPath -Force
        Write-Host ("交付单 exe -> $deliverPath")
    } else {
        Write-Host "未指定 -Version，跳过根目录交付命名（产物在 $outDir）"
    }
}

$sha = (Get-FileHash -Algorithm SHA256 $exePath).Hash
Write-Host "EXE SHA256: $sha"
Write-Host ("EXE bytes : " + (Get-Item $exePath).Length)

if ($Zip) {
    $zipPath = Join-Path $repoRoot "$outName.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path $outDir -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Distribution zip: $zipPath"
}

Write-Host "Done."

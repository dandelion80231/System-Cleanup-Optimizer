<#
  sign.ps1 —— CpqSystemTool 构建产物 Authenticode 签名 + RFC3161 时间戳
  用途：拿到公开可信代码签名证书（PFX）后，对生成的 exe 做签名，消除 SmartScreen「未知发布者」拦截。
  重要：签名无法解除 Defender 对 Trojan:Win32/Bearfoos.A!ml 的判定（那是恶意软件检测层），
        只能解决信誉/SmartScreen 层。请配合「移除禁用 Defender 功能 + 微软误报申诉」一起做。

  依赖（二选一）：
    (a) Windows SDK 自带的 signtool.exe  （推荐，路径在 C:\Program Files (x86)\Windows Kits\10\bin\...\signtool.exe）
    (b) osslsigncode  （跨平台，可从 https://github.com/mtrojnar/osslsigncode 获取）

  用法：
    .\sign.ps1 -ExePath "D:\电脑桌面\cpq\系统清理与优化工具_v1.03.exe" -PfxPath "D:\cert.pfx" -PfxPass "你的密码"
#>
param(
    [Parameter(Mandatory = $true)]  [string] $ExePath,
    [Parameter(Mandatory = $true)]  [string] $PfxPath,
    [string] $PfxPass = "",
    # RFC3161 时间戳服务（公开、免费）。签名必须带时间戳，否则证书过期后签名失效。
    [string] $TimeStampUrl = "http://timestamp.digicert.com",
    [string] $TimeStampUrl2 = "http://timestamp.sectigo.com"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ExePath))  { Write-Error "exe 不存在: $ExePath"; exit 1 }
if (-not (Test-Path $PfxPath))  { Write-Error "证书不存在: $PfxPath"; exit 1 }

# ---- 1) 定位签名工具 ----
$signtool = @(Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue) |
            Sort-Object FullName | Select-Object -First 1
$ossl = Get-Command osslsigncode -ErrorAction SilentlyContinue

function Invoke-Sign {
    param([string]$Ts)
    if ($signtool) {
        Write-Host ">> signtool: $($signtool.FullName)"
        & $signtool.FullName sign /fd sha256 /tr "$Ts" /td sha256 /f "$PfxPath" /p "$PfxPass" "$ExePath"
        return $LASTEXITCODE
    } elseif ($ossl) {
        Write-Host ">> osslsigncode"
        $tmp = "$ExePath.tmp_signed"
        & osslsigncode sign -pkcs12 "$PfxPath" -pass "$PfxPass" -t "$Ts" -h sha256 -in "$ExePath" -out "$tmp"
        if ($LASTEXITCODE -eq 0) { Move-Item "$tmp" "$ExePath" -Force }
        return $LASTEXITCODE
    } else {
        Write-Error "找不到 signtool.exe 也没装 osslsigncode。请安装 Windows SDK，或用 scoop/winget 装 osslsigncode。"; exit 1
    }
}

# ---- 2) 签名（主时间戳失败则回退备用） ----
$rc = Invoke-Sign $TimeStampUrl
if ($rc -ne 0 -and $TimeStampUrl2) {
    Write-Warning "主时间戳服务失败，尝试备用: $TimeStampUrl2"
    $rc = Invoke-Sign $TimeStampUrl2
}
if ($rc -ne 0) { Write-Error "签名失败 (exit=$rc)"; exit 1 }

# ---- 3) 校验 ----
Write-Host ">> 校验签名:"
if ($signtool) {
    & $signtool.FullName verify /pa "$ExePath"
} else {
    & osslsigncode verify "$ExePath"
}
Write-Host "完成: $ExePath"

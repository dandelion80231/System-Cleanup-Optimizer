# clear_edge_policies.ps1
# 清除导致 edge://management 显示「由你的组织管理」的 Edge 组策略（HKCU + HKLM）。
#
# 用法：直接双击本文件即可。脚本会：
#   1) 若当前不是管理员，自动请求提权（用于清理 HKLM）；
#   2) 无论是否提权，都以「真实登录用户」身份清理其 HKCU
#      （提权后 HKCU: 会映射到管理员配置单元，故这里改用显式 SID 定位真实用户，避免清错配置单元）；
#   3) 清理完成后暂停，方便查看结果。

$ErrorActionPreference = 'Continue'

# ---- 1) 非管理员时自动提权（仅用于 HKLM 部分；HKCU 通过显式 SID 定位，不受提权影响）----
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]"Administrator")
if (-not $isAdmin) {
    try {
        Start-Process -FilePath (Get-Process -Id $pid).Path -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File `"$PSCommandPath`""
        # 已尝试以管理员身份启动新窗口完成工作，本窗口直接退出
        exit
    } catch {
        Write-Host "请求管理员权限失败（可能被取消），将以当前用户身份仅清理 HKCU。" -ForegroundColor Yellow
        # 继续在当前上下文执行（仅 HKCU 生效，HKLM 部分可能失败）
    }
}

# ---- 2) 获取真实登录用户 SID（提权后仍可定位其 HKCU）----
function Get-RealUserSid {
    try {
        $proc = Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" -ErrorAction Stop | Select-Object -First 1
        if ($proc) {
            $owner = Invoke-CimMethod -InputObject $proc -MethodName GetOwner -ErrorAction Stop
            if ($owner -and $owner.SID) { return $owner.SID }
        }
    } catch { }
    # 兜底：未提权时当前身份即为真实用户
    return [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
}

$sid = Get-RealUserSid
$hkcuEdge     = "Registry::HKEY_USERS\$sid\SOFTWARE\Policies\Microsoft\Edge"
$hkcuEdgeRec = "Registry::HKEY_USERS\$sid\SOFTWARE\Policies\Microsoft\Edge\Recommended"

function Remove-EdgeKey {
    param([string]$Label, [string]$Path)
    try {
        if (Test-Path $Path) {
            Remove-Item -Path $Path -Recurse -Force -ErrorAction Stop
            if (Test-Path $Path) {
                Write-Host "[!] $Label 删除后仍残留（可能被 DACL/所有者拒绝），请手动用 regedit 删除：$Path" -ForegroundColor Yellow
            } else {
                Write-Host "[OK] 已删除 $Label" -ForegroundColor Green
            }
        } else {
            Write-Host "[*] $Label 不存在，跳过" -ForegroundColor Gray
        }
    } catch {
        Write-Host "[!] 删除 $Label 失败: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "=== 清除 Edge 组策略（edge://management 限制）===" -ForegroundColor Cyan
Write-Host "目标用户 SID: $sid"

# HKCU（当前登录用户，必须清——本机生效项多在此）
Remove-EdgeKey -Label "HKCU\Policies\Microsoft\Edge"                 -Path $hkcuEdge
Remove-EdgeKey -Label "HKCU\Policies\Microsoft\Edge\Recommended"    -Path $hkcuEdgeRec

# HKLM（本项目代码写入处；需管理员，上方已尝试提权）
Remove-EdgeKey -Label "HKLM\Policies\Microsoft\Edge"                -Path "HKLM:\SOFTWARE\Policies\Microsoft\Edge"
Remove-EdgeKey -Label "HKLM\Policies\Microsoft\Edge\Recommended"    -Path "HKLM:\SOFTWARE\Policies\Microsoft\Edge\Recommended"
Remove-EdgeKey -Label "HKLM(WOW6432)\Policies\Microsoft\Edge"       -Path "HKLM:\SOFTWARE\WOW6432Node\Policies\Microsoft\Edge"

Write-Host ""
Write-Host "=== 完成 ===" -ForegroundColor Cyan
Write-Host "请完全退出并重启 Microsoft Edge（建议重启系统），然后访问 edge://management/ 确认已恢复为「未受管理」。"
Write-Host "若仍有残留，可在 regedit 中手动检查："
Write-Host "  HKEY_CURRENT_USER\SOFTWARE\Policies\Microsoft\Edge"
Write-Host "  HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Edge"
Write-Host ""
Write-Host "提示：本工具（系统清理与优化工具）的「Edge优化」功能组与更新/启动增强会写入这些策略。"
Write-Host "      后续若再用本工具启用相关开关，edge://management 会再次显示「由组织管理」——这是组策略的固有表现。"

# 暂停，避免双击运行时窗口一闪而过
Write-Host ""
Write-Host "按任意键退出..." -ForegroundColor Gray
[void][System.Console]::ReadKey($true)

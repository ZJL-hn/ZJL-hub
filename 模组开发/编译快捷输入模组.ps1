# Build 快捷输入模组 QuickInputMod.dll
# 前置：先编译主程序 虚拟键盘模拟器.exe（下面会检查）。产物放到 ..\mods\（应用启动时自动扫描）。
$ErrorActionPreference = "Stop"
$d = Split-Path -Parent $MyInvocation.MyCommand.Path   # 本脚本位于 模组开发 目录
Set-Location $d
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$exe = Join-Path $d "..\虚拟键盘模拟器.exe"
if (-not (Test-Path $exe)) { throw "main app not found; build 虚拟键盘模拟器.exe first" }

& $csc /nologo /target:library /out:"$d\..\mods\QuickInputMod.dll" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:"$exe" `
    "$d\快捷输入模组 QuickInputMod.cs"
Write-Output "QuickInputMod built: $d\..\mods\QuickInputMod.dll (restart the app to load it)"

# Build MusicMod.dll (or the sample mod AutoEnterMod, see notes at bottom).
# Prereq: build the main app 虚拟键盘模拟器.exe first (checked below).
$ErrorActionPreference = "Stop"
$d = Split-Path -Parent $MyInvocation.MyCommand.Path   # this script lives in the 模组开发 folder
Set-Location $d
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$exe = Join-Path $d "..\虚拟键盘模拟器.exe"            # main app is one level up
if (-not (Test-Path $exe)) { throw "main app not found; build 虚拟键盘模拟器.exe first" }

# MusicMod: compile to ..\mods\MusicMod.dll (the app scans exe-dir \mods\*.dll on startup)
& $csc /nologo /target:library /out:"$d\..\mods\MusicMod.dll" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:"$exe" `
    "$d\音乐模组 MusicMod.cs"
Write-Output "MusicMod built: $d\..\mods\MusicMod.dll (restart the app to load it)"

# To build the sample mod instead, change the last argument to 示例模组 AutoEnterMod.cs
# and the output name to ..\mods\AutoEnterMod.dll (AutoEnterMod does not use System.Windows.Forms).

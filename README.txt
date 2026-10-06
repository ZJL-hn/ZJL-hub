========================================================
  Virtual Keyboard Simulator — Project Notes (plain text)
========================================================

A Windows desktop application: define custom keyboard key
sequences (press time / release time / press duration), a
draggable timeline, visual recording (auto-generates a
sequence as you type), real key injection, a global emergency
stop, and mod extension support (built-in Music mod: import
a MIDI and auto-generate a performance sequence; Quick Input
mod: timed text input and quick sequence arrangement with
simultaneous-key support; a mod's UI can be injected directly
into the main window's right-hand panel without any popup).

Tech: C# / .NET 4.0 (WinForms), compiled with the Windows
built-in csc, zero third-party dependencies, compatible with
Win7 / 10 / 11 (Win7 needs .NET 4.0+; Win10/11 work out of
the box).

--------------------------------------------------------
1. Directory Structure Overview
--------------------------------------------------------
  virtual-keyboard-simulator/
  |-- 虚拟键盘模拟器.exe     Main program (double-click to run)
  |-- setup.exe              Installer (for distribution)
  |-- README.md              Markdown notes
  |-- README.txt             This plain-text notes
  |-- README.html            Web notes (open in a browser)
  |-- app\                   All main-program source (for compile)
  |-- mods\                  Mod runtime directory (auto-scan *.dll)
  |-- 模组开发\              Mod dev materials (manual/interface/examples/source)
  |-- MIDI 乐谱\             Ready-made MIDI music files
  |-- 工具\                  MIDI utility tools (add/simplify songs)

--------------------------------------------------------
2. File / Directory Details
--------------------------------------------------------
[Root directory]
  - 虚拟键盘模拟器.exe (main program)
      The application itself; double-click to launch, no install
      needed. Features: 104-key virtual keyboard, step list
      (press/release timeline), draggable timeline table,
      recording, playback (auto-minimize + delay countdown),
      Ctrl+F9 global emergency stop, anti-mistouch switch, mod
      management.
  - setup.exe (installer)
      Installation wizard that installs the main program + Music
      mod to a chosen directory, creates a shortcut, and supports
      uninstall. Use this when distributing to others.
  - setup.cs (installer source)
      Source code of setup.exe (look here if you want to modify
      the installer).
  - README.md / README.txt / README.html (notes)
      Three formats of the same project notes; pick any one.

[app\ main-program source, 4 files]
  - VkCore.cs    Core logic: key injection, 104-key key table,
                 recording global keyboard hook, millisecond
                 playback scheduling, timeline step data.
  - Views.cs     UI components: virtual keyboard button view,
                 step grid, drag-to-change timing.
  - MainForm.cs  Main window: overall layout, toolbar, menus.
  - ModApi.cs    Mod system: mod interfaces, loader, mod
                 management window.

  (Compile the main program after modifying source:
   see README.html or section 5 below.)

[mods\ mod runtime directory]
  - MusicMod.dll (Music mod, compiled)
  - QuickInputMod.dll (Quick Input mod, compiled)
      Timed input / quick arrangement of key sequences; its UI
      is injected directly into the main window's right-hand
      panel. Loaded automatically when the main program starts
      and scans this directory. This directory must not be
      renamed or moved; mods must be under the mods\ folder next
      to the exe.
  (mods\ only holds compiled .dll files; source lives in the
   "模组开发" directory. After adding a mod or recompiling,
   restart the application.)

[模组开发\ mod development materials]
  - 模组开发手册.md   Mod authoring manual: interface reference,
                      steps to write a mod, how to compile, Win7
                      compatibility and C#5 syntax limits.
  - 模组接口 ModApi.cs  Mod-system interface source (same copy as
                      app\). Includes the UI injection interfaces:
                      IUiMod (UI injected directly into the main
                      window's right-hand panel) +
                      IVkContext.AddModUi/RemoveModUi.
  - 示例模组 AutoEnterMod.cs  Minimal example mod source.
  - 音乐模组 MusicMod.cs      Music mod source: three playing modes,
                      MIDI parse & import, BPM-linked speed%.
  - 快捷输入模组 QuickInputMod.cs
                      Quick Input mod source: ① timed text input;
                      ② quick arrangement of key sequences
                      (key+duration+snap+wait), with
                      simultaneous-key chords; implements IUiMod,
                      UI injected directly into the main window's
                      right-hand tab.
  - 编译模组.ps1       One-click compile script: compiles the
                      Music mod into ..\mods\.
  - 编译快捷输入模组.ps1
                      One-click compile script: compiles the
                      Quick Input mod into ..\mods\.

[MIDI 乐谱\ ready-made MIDI music]
  - He's a Pirate 主旋律.mid   "Pirates of the Caribbean" main melody
                      (recommended).
  - He's a Pirate 复杂版.mid   "Pirates of the Caribbean" complex version.
  - Let It Be 主旋律.mid       The Beatles' "Let It Be".
  - Yesterday 主旋律.mid       The Beatles' "Yesterday".
  - 月亮代表我的心 主旋律.mid  Teresa Teng classic (Chinese song).
  - 邓丽君 主旋律.mid          Teresa Teng track (title to be confirmed).
  - caribbean_melody.mid       Early leftover version (the two above
                      are recommended).
  Usage: app -> mod management -> Music mod -> import -> pick a .mid.

[工具\ MIDI utility tools]
  - _check.exe/_check.cs  Check a MIDI's structure (notes/black
                          keys/range etc.).
  - _simp.exe/_simp.cs    Simplifier: reduce a complex MIDI to a
                          single-track main melody.
  - _checkmod.cs/_checkmod.exe
              Quick Input mod verification tool: 49 assertions
              validating timed allocation, arrangement & chord
              timing, mod .dll loading, and UI injection
              (auto-inject on load / remove on unload / panel
              structure).

--------------------------------------------------------
3. Quick Start
--------------------------------------------------------
  1. Launch: double-click "虚拟键盘模拟器.exe".
  2. Build a sequence: click keys on the virtual keyboard to add
     steps automatically; or click "录制" and simply type to
     auto-generate press/release sequences; drag in the timeline
     to adjust times.
  3. Play: set speed%/loop/delay -> click "运行". The app
     auto-minimizes and injects the sequence into the target
     window; press Ctrl+F9 for a global emergency stop.
  4. Music performance: "模组管理" -> Music mod -> "操作/导入" ->
     pick a MIDI -> choose play mode/instrument/BPM -> play.
  5. Quick input: once the Quick Input mod is loaded, its UI
     appears directly in the main window's right-hand "快捷输入"
     tab (no popup). ① timed-input tab: fill characters/total
     time (optional per-key duration)/snap -> generate; or
     ② quick-arrangement tab: fill key/duration/snap/wait row by
     row (use + to join multiple keys on one row for a chord) ->
     generate. Then bring the target app to the foreground and
     click "运行".

--------------------------------------------------------
4. Installation & Distribution
--------------------------------------------------------
  - Install: double-click setup.exe, choose a directory; it
    automatically installs the main program + Music mod and
    creates a shortcut.
  - Silent install: setup.exe /install <directory>
  - Uninstall: Control Panel -> Programs -> Uninstall
    "虚拟键盘模拟器", or setup.exe /uninstall
  - Portable distribution: just copy "虚拟键盘模拟器.exe" + the
    mods\ folder.

--------------------------------------------------------
5. Development & Mod Extension
--------------------------------------------------------
  Modify the main program:
    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
    /nologo /target:winexe /optimize+ /out:虚拟键盘模拟器.exe
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll
    /r:System.Windows.Forms.dll
    app\VkCore.cs app\Views.cs app\MainForm.cs app\ModApi.cs

  Write a new mod:
    First read "模组开发\模组开发手册.md", follow "示例模组
    AutoEnterMod.cs" to write code, compile with "模组开发\编译模组
    .ps1", drop the .dll into mods\, restart the app, and load &
    verify via "模组管理".

  Modify the Quick Input mod:
    Edit "模组开发\快捷输入模组 QuickInputMod.cs", compile with
    "模组开发\编译快捷输入模组.ps1", restart the app; after logic
    changes run "工具\_checkmod.exe" for regression checks.

  Inject a mod UI directly into the main program:
    Implement IUiMod (UiTitle/UiContent); after loading it
    automatically appears as a right-hand tab (no "操作/导入…"
    popup) and is removed automatically on unload. The mod panel
    uses a dark, borderless style, auto-shrinks on narrow windows
    without covering the main UI, and has a collapse/expand
    button on its right edge for temporarily hiding it. See
    "模组开发\模组开发手册.md" 3.6.

========================================================

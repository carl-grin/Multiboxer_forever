# Multibox Forever

Windows 11 desktop helper for Carl Grin’s multibox setup: one **Host** PC broadcasts selected Ctrl+Alt chords over the LAN; one or more **Listener** PCs inject the matching bare keystrokes (no Ctrl/Alt).

This is an explicit, visible WinForms app — not a background/stealth tool.

## Supported chords (v1)

On the **Host**, press:

| Chord | Protocol token | Listener injects |
| --- | --- | --- |
| `Ctrl+Alt+0` … `Ctrl+Alt+9` | `0`–`9` | Top-row number key `VK_0`…`VK_9` |
| `Ctrl+Alt+-` | `-` | Main keyboard Minus (`VK_OEM_MINUS`, US `-_` key) |
| `Ctrl+Alt+=` | `+` | Main keyboard Plus/Equals (`VK_OEM_PLUS`, US `=/+` key) |

Notes:

- Numpad 0–9, numpad Minus, and numpad Plus on the host are also accepted and mapped to the same tokens.
- The listener always injects the **main-keyboard** virtual keys above, with **no modifiers**.
- For Plus, the physical US key is **Equals/Plus** (`=/+`). Injection sends that key alone (so Notepad would type `=` unless Shift is already down for another reason). Games that bind by virtual key still see `VK_OEM_PLUS`.
- The host does **not** swallow the chord; Windows keeps its normal behavior for that keypress.

## Network

- Transport: TCP on your LAN
- Default port: **47211**
- Host listens (`0.0.0.0`); listeners connect by IP + port
- One host → one or more listeners
- Wire format: one ASCII token per line, e.g. `7\n`, `-\n`, `+\n`

## Requirements

- Windows 11 (Win32 hooks + `SendInput`)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or newer SDK that can target `net8.0-windows`)

## Build & run (Windows 11)

```powershell
cd path\to\repo
dotnet restore MultiboxForever.sln
dotnet build MultiboxForever.sln -c Release
dotnet run --project MultiboxForever -c Release
```

Publish a self-contained exe (optional):

```powershell
dotnet publish MultiboxForever -c Release -r win-x64 --self-contained true -o .\publish
.\publish\MultiboxForever.exe
```

### Host PC

1. Run the app → choose **Host (broadcaster)**.
2. Confirm port (default `47211`) → **Start host**.
3. Note this PC’s LAN IP (`ipconfig`).
4. Allow the app through Windows Firewall when prompted (private networks).
5. Press `Ctrl+Alt` plus `0`–`9`, `-`, or `=` to broadcast.

### Listener PC(s)

1. Run the same app → choose **Listener**.
2. Enter the host IP and port → **Connect**.
3. Focus the game/window that should receive keys.
4. When the host presses a chord, only that bare key is injected.

## Cross-compile note (non-Windows)

This repo targets `net8.0-windows` with Windows Forms. Restore/build for `win-x64` from Linux/macOS usually fails because the Windows Desktop Pack is Windows-only. Author and edit anywhere; **build and run on Windows 11**.

## Project layout

```
MultiboxForever.sln
MultiboxForever/
  Program.cs          Entry point
  MainForm.cs         Host / Listener UI
  KeyboardHook.cs     WH_KEYBOARD_LL (Ctrl+Alt chords)
  KeyInjector.cs      SendInput (bare key)
  HostServer.cs       TCP broadcaster
  ListenerClient.cs   TCP receiver
  Protocol.cs         Port, tokens, VK mapping
  NativeMethods.cs    P/Invoke
```

# RetroBar Shizuku custom build

This is a personal integration of https://github.com/shizuku30/RetroBar.
No changes or pull requests are submitted to the upstream repository.

## Included changes

Base: `dremin/RetroBar` master `69f36278150cb39bc37d3237e3178926734c6804`.

| Upstream PR | Included revision | Purpose |
| --- | --- | --- |
| [1473](https://github.com/dremin/RetroBar/pull/1473) | `38ceae2033392e9f1823fa39777ae92aec78ba9d` | Windows 7 Aero-inspired theme and images |
| [1504](https://github.com/dremin/RetroBar/pull/1504) | `4552f85922a8bba934be65d4f9bc37c009fe3bd6` | Restore the network tray icon on Windows 11 24H2 and newer |
| [1505](https://github.com/dremin/RetroBar/pull/1505) | `0fa0c069587e69484700e288f28406f70edaa825` | `RetroBar.exe /exit` for graceful shutdown |
| [1487](https://github.com/dremin/RetroBar/pull/1487) | `53cbca1aebda9825a501de05f0d2d22a2faa7870` | Wait up to 60 seconds for Explorer during startup |

Local integration fixes:

- Correct the Win7 taskbar texture and pressed-clock image paths to included assets.
- Use the included start-orb sprite at high DPI, rather than a nonexistent scaled file.
- Mark the version with the `shizuku` prerelease suffix.
- Add Simplified Chinese labels for the new network icon tooltip and menu.

The new network icon uses modern Windows glyphs. Network, volume, battery,
and third-party tray icons are NOT replaced with Windows 7 artwork.
No additional Windows 7 system DLLs are required for this build.

## Use

Exit the old RetroBar normally using its context menu before launching this build.
Do not force-kill it: normal exit restores the Windows taskbar.
The existing RetroBar executable is not replaced by the build script.

Run `RetroBar.exe` from the package. In RetroBar Properties select the theme
named `Windows7`. Existing settings in `%LOCALAPPDATA%\RetroBar` are reused;
this build does not force a different theme or reset preferences.
The Win7 theme changes the taskbar appearance, not the actual Windows Start menu.

The default package targets Windows 10/11 x64 and includes the .NET 10 runtime.
It does not need a separate .NET installation. Single-file resources are extracted
by .NET on first launch. The program is not code-signed.

## Build

Requirements: Windows and the .NET 10 SDK. Run from this checkout:

```powershell
./build-custom.ps1
```

The executable, license, build metadata, and ZIP are written beneath `artifacts`.
The script only cleans its own selected package directory inside `artifacts`.

For a smaller package that requires an installed .NET Desktop Runtime:

```powershell
./build-custom.ps1 -FrameworkDependent
```

An older-OS build can be made with `-Framework net6.0-windows`. .NET 6 is
out of support; use .NET 10 on Windows 10/11. Other supported runtime IDs are
`win-x86` and `win-arm64` (.NET 10 only for ARM64). Tests run on the build host,
not on the cross-published architecture.

## Verification

```powershell
dotnet build RetroBar.sln -c Release
dotnet run --project tests/RetroBar.SmokeTests -c Release -f net10.0-windows -- .
dotnet run --project tests/RetroBar.SmokeTests -c Release -f net6.0-windows -- .
```

Smoke tests decode all referenced Win7 images, load the loose theme dictionary,
seal its styles, render representative control templates at 100/150/200%, render
nine network icon states, check network state selection and native layouts, and
initialize/dispose the network monitor. They do not launch RetroBar or replace
the desktop taskbar.

The following larger changes were deliberately left out: grouped tasks (#1433),
per-monitor/layout redesign (#1503), and low-level mouse-hook replacement (#1413).
The optional crisp text/start-icon changes (#1402/#1405) have upstream conflicts
and were also left out of this focused build.

Live Wi-Fi reconnect, Ethernet unplug, sleep/resume, tray interactions, and
multi-monitor behavior still require desktop testing. The source PRs remain
unmerged upstream, and #1504 is still a draft.

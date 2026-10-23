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
- Remove the Win7 theme's non-native hover pin buttons in both the tray and overflow popup; keep the normal hover highlights.

## Themed system tray icons

The **Match system icons to theme** checkbox is in Properties > Notification
area and is on by default. Toggling it restores/replaces the original images
immediately, without restarting. The setting is localized in English and
Simplified Chinese.

| Taskbar theme | Icon pack |
| --- | --- |
| Windows 98 and earlier, System classic, older custom classic themes | Windows 98 SE |
| Windows Me, Windows 2000 | Windows 2000 SP4 |
| All XP themes, System XP, Watercolor/Whistler | Windows XP SP3 |
| Longhorn, Vista Aero/Basic/Classic, System Vista | Windows Vista SP2 |
| Windows7; newer modern custom styles | Windows 7 SP1 |

Custom theme names use the nearest historical style. Unrecognized glass themes
use Vista; unrecognized classic themes use Win98. A custom theme may specify a
`SystemNotifyIconTheme` string resource (`Win98`, `Win2000`, `WinXP`, `Vista`,
`Win7`). Explicitly pre-98 theme names always use Win98.

Only the Windows network, volume, power/battery, and safely-remove-hardware GUIDs
are replaced. Third-party icons and unsupported system icons retain their own
images. Callback windows, messages, tooltips, visibility, ordering, and mouse
actions are unchanged. The customization dialog shows the same replacement
images. Colored themed images are never passed through the grayscale inversion
shader; their inversion checkboxes are disabled while replacement is on.

Network status uses the existing event-driven monitor, shared with the synthetic
Win11 network icon. Core Audio callbacks track mute, volume, and default-device
changes without changing audio settings. Windows power notifications update
battery/AC/charging state. Unavailable status or assets fall back to native images.
Old packs use their native coarse volume/battery levels; Win98/2000 approximate
Wi-Fi with their original connection icons because they have no matching Wi-Fi
signal set. An unavailable WLAN signal query does not imply maximum signal.
Win98/2000 have no modern Internet-access warning badge; their generic connected
image and the original status tooltip are retained for local-only connectivity.

All 587 selected image variants are embedded, so installed builds need no old
system DLLs, mounted installation media, or K: drive. Only original icon images
and cropped/composited native battery-strip cells are bundled, never executable
Windows DLLs. Resource IDs and source hashes are in `Resources/TrayIcons/sources.json`.
The supplied resources remain their original owners' material, for this personal
build. Intermediate extraction/analysis data stays in `K:\tmp\RetroBar-icons-20261023`.

Resource preparation, when the original extraction/cache is available:

```powershell
python tools/prepare_tray_icon_assets.py --source artifacts/system-icon-resources --cache K:/tmp/RetroBar-icons-20261023 --output RetroBar/Resources/TrayIcons
```

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

The executable, license, build metadata, and ZIP are written beneath `artifacts`
under `RetroBar-Shizuku-ThemedIcons-...`, alongside the older Win7-only package.
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

The themed-icon tests cover every bundled theme, all icon states and seven pixel
sizes (1,547 combinations), original-image fallback, live theme/checkbox changes,
third-party preservation, native callback-data preservation, image subscription
cleanup, shared network-monitor ownership, live read-only Core Audio and power,
English/Chinese settings layout, and five icon packs at 100/150/200% rendering.
Test setting changes are isolated in-process with persistence disabled.

The following larger changes were deliberately left out: grouped tasks (#1433),
per-monitor/layout redesign (#1503), and low-level mouse-hook replacement (#1413).
The optional crisp text/start-icon changes (#1402/#1405) have upstream conflicts
and were also left out of this focused build.

Live Wi-Fi reconnect, Ethernet unplug, sleep/resume, tray interactions, and
multi-monitor behavior still require desktop testing. The source PRs remain
unmerged upstream, and #1504 is still a draft.

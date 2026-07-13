<h1 align="center">🎵 FluentTune</h1>

<p align="center">
  A modern, Fluent-design music widget for the Windows 11 taskbar.<br>
  See and control whatever is playing on your PC — Spotify, YouTube, any app — with a glassy
  flyout, a live audio spectrum on the taskbar, and colors that adapt to the album art.
</p>

<p align="center">
  <img src="assets/hero.png" alt="FluentTune flyout and taskbar spectrum" width="480">
</p>

<p align="center">
  <a href="https://github.com/psanchezp2/FluentTune/releases/latest/download/FluentTune.zip">
    <img src="https://img.shields.io/badge/Download-FluentTune%20v1.0.0%20(free)-2ea44f?style=for-the-badge&logo=windows&logoColor=white" alt="Download FluentTune">
  </a>
</p>

<p align="center">
  <b><a href="https://github.com/psanchezp2/FluentTune/releases/latest/download/FluentTune.zip">⬇ Download the app</a></b> — free &amp; self-contained (no .NET install needed) · Windows 10/11 64-bit
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 8">
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows&logoColor=white" alt="Windows 10/11">
  <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License">
</p>

---

## ✨ Features

- **Now-playing flyout** — album art, title/artist, transport controls (previous / play-pause / next) and system volume, in a translucent *liquid-glass* card.
- **Album-color adaptation** — the whole UI (glow, accent, controls, spectrum) recolors to match the current song's cover.
- **Taskbar audio spectrum** — a live, glowing spectrum visualizer sits on the left of the taskbar next to a mini now-playing. It reacts to the audio and eases down smoothly when you pause.
- **Ocean-wave progress bar** — an Android-style wavy seek bar that pulses with the music. Drag to seek.
- **Click to open** — click the taskbar widget to toggle the flyout.
- **System-tray resident** — show, *start with Windows*, and quit from the tray menu.
- **Works with any player** — reads the Windows media session (GSMTC), so it controls Spotify, browsers, and more.

## 📷 Screenshots

<table>
  <tr>
    <td align="center">
      <img src="assets/spectrum.png" width="380"><br>
      <sub>Live, compact taskbar spectrum + mini player</sub>
    </td>
    <td align="center">
      <img src="assets/waveform.png" width="380"><br>
      <sub>Ocean-wave progress bar (drag to seek)</sub>
    </td>
  </tr>
</table>

> The whole UI recolors to match the current album's cover.

## 🚀 Getting started

### Download & run
1. **[⬇ Download FluentTune.zip](https://github.com/psanchezp2/FluentTune/releases/latest/download/FluentTune.zip)** (or from the [Releases](../../releases) page).
2. Unzip and run **`FluentTune.exe`** — it's a self-contained build, so **no .NET install is required**.
3. A ♪ icon appears in the system tray. Play some music and a mini widget shows up on the left of the taskbar — click it to open the player.

Requires **Windows 10/11 (64-bit)**.

### Build from source
```bash
git clone https://github.com/psanchezp2/FluentTune.git
cd FluentTune
dotnet build -c Debug
dotnet run
```
Requires the **.NET 8 SDK** (with the Windows Desktop workload).

To produce the self-contained single-file executable:
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## 🛠️ Tech stack

| Area | Library |
|------|---------|
| Framework | .NET 8 **WPF** |
| Fluent UI | [WPF-UI](https://github.com/lepoco/wpfui) |
| Media read/control | Windows **GSMTC** (`GlobalSystemMediaTransportControlsSessionManager`) |
| Audio spectrum + volume | [NAudio](https://github.com/naudio/NAudio) (WASAPI loopback + Core Audio) |
| MVVM | CommunityToolkit.Mvvm |

## 📁 Project layout

```
FluentTune/
├── Controls/            WaveProgressBar (custom ocean-wave seek bar)
├── Services/            MediaService, VolumeService, AudioSpectrumService, ColorExtractor, StartupService
├── ViewModels/          NowPlayingViewModel
├── Converters/          PlayPauseSymbolConverter
├── MainWindow.xaml      The glass flyout
├── SpectrumWindow.xaml  The taskbar overlay (mini now-playing + spectrum)
└── App.xaml             Entry point
```

## 📝 License

Released under the [MIT License](LICENSE).

<sub>Built from scratch as an original take on the taskbar-music-widget idea.</sub>

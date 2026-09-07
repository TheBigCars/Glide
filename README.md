# Glide

A lightweight Windows app for controlling the Logitech PRO X SUPERLIGHT 2 without keeping G HUB open.

## Download

### **[Download Glide.exe](https://github.com/TheBigCars/Glide/releases/latest/download/Glide.exe)**

Download `Glide.exe`, then double-click it to run. There is no installer.

> Windows may show a SmartScreen warning because Glide is not code-signed yet. The executable is built directly from this repository by GitHub Actions.

## What Glide does

- Read and change DPI
- Read battery and charging status
- Change wireless polling rate from 125–8000 Hz
- Save settings to onboard mouse profiles
- Select and rename onboard profiles
- Check the installed mouse firmware against Logitech's published release notes
- Run without a tray app, background service, telemetry, or G HUB dependency for normal mouse controls

## Repository layout

```text
src/       App source and UI
assets/    Glide icon and mouse artwork
scripts/   Build and test scripts
tests/     Offline/device checks
tools/     Development helpers
docs/      Technical notes and protocol details
```

## Build from source

On Windows, open PowerShell in the repository and run:

```powershell
.\scripts\build.ps1
```

The executable will be created at:

```text
dist\Glide.exe
```

Run checks with:

```powershell
.\scripts\check.ps1
```

For the deeper HID++ implementation notes, hardware verification, save behavior, and firmware details, see [`docs/TECHNICAL.md`](docs/TECHNICAL.md).

# Glide · v0.3

Minimal Windows 11 application for the regular Logitech PRO X SUPERLIGHT 2.

## Open and use

Double-click **Glide.exe**. Drag the slider or type a DPI, then choose **Save DPI**. Enter also saves a valid edit. Unsupported typed values show the nearest supported value without silently changing your entry. DPI changes apply to X and Y together. Esc resets unsaved edits.

Device status is read once at startup and only again when you click **Refresh device**, save, or select a profile. There is no polling timer. The timestamp shows when the battery/status snapshot was taken; plug/unplug or charging changes require Refresh. Closing the app ends the process; there is no startup task, tray app, background service, telemetry, or G HUB dependency for mouse controls. Keep the executable in a writable folder for profile backups.

## Animated mouse and profiles

The left panel contains an original vector illustration shaped for the Superlight 2. It gently floats, lifts when hovered, and bounces when clicked. These effects run locally and issue no HID requests. The Animate checkbox disables motion; motion also stops when minimized, unfocused, or closing and respects Windows' animation preference.

The profile picker lists enabled profiles already stored on the mouse. Choose one and click **Use profile** to select it and verify the resulting DPI. Edit the active name and click **Rename** to store the new name on the mouse. If both DPI and name are edited, the button says **Save changes** and commits both. Save or reset drafts before switching profiles. A name-only save preserves separate X/Y sensitivities.

Creating additional profile slots, enabling disabled slots, button remapping, and importing/exporting profiles are not included yet.

## Firmware

The Firmware tab reads installed mouse firmware via HID++. **Check for updates** fetches Logitech's official G HUB release notes over HTTPS using TLS 1.2 with normal certificate validation. It extracts versions for the exact regular Superlight 2, excluding DEX/SE variants, and compares them locally. It sends no device ID or firmware value to Logitech. No network request runs at startup or while idle.

Verified installed firmware: **32.5.29**. It matches the newest exact-model version recognized in Logitech's release notes on September 6, 2026. This is a comparison with published notes, not a live firmware distribution feed; it cannot guarantee that no staged update exists. Network or parsing failures show Unknown.

**Direct automatic firmware installation is not implemented.** No verified standalone flash package and installation workflow for this exact model was established. The app offers Logitech's official G HUB page for installation through their updater. No updater is downloaded or installed automatically, and no firmware was flashed during development. The mouse controls remain independent of G HUB. Receiver firmware comparison is not included.

## Included

- Standalone window, no tray requirement or background service.
- DPI slider and numeric input.
- Battery percentage and charging status.
- Save DPI to the mouse's onboard memory so it survives closing the app.
- Wireless receiver operation and USB cable charging; no G HUB dependency.
- Wireless polling-rate selection from 125–8000 Hz, saved to the active onboard profile.
- Local accent and font choices from the bottom-left settings cog.
- A custom Glide executable/taskbar icon.

## Hardware discovery — September 6, 2026

The read-only `MouseProbe.exe` successfully communicated with the actual mouse through Windows HID APIs:

- Receiver: Logitech USB VID 046D / PID C54D.
- Mouse device name: PRO X 2, receiver slot 1, HID++ 4.2.
- Current X and Y sensitivity: 800 DPI.
- Battery response: `5F 08 00 00` (95%, status 0).
- Unified Battery feature 1004: index 06, version 5.
- Extended Adjustable DPI feature 2202: index 09.
- Onboard Profiles feature 8100: index 0D.
- Onboard mode: 1; active profile sector: 1; active DPI slot: 0.
- Onboard description: `01 07 01 05 01 05 10 00 FF 0A 04`.

## Verification

- Firmware-reported DPI choices: 100–44,000, 957 values with variable step sizes.
- Actual save test: 800 → 805 → 800. Every written profile byte and active DPI were verified. All 255 original profile bytes matched after restoring 800 DPI.
- Offline checks passed: standard CRC reference vector, variable DPI steps, malformed ranges, unrelated-byte preservation, and corrupt-profile rejection.
- UI checks passed: live startup, valid drafts, invalid text, out-of-range input, slider/input synchronization, and suppression of unchanged-value saves.
- The actual window was rendered and visually inspected with live readings.
- v0.2 profile selection test: profile 1 → 2 → 1, resulting DPI verified.
- v0.2 rename test: LOL → Controller test → LOL. Every original profile byte matched after restoration.
- The official online firmware comparison succeeded with installed/published versions both 32.5.29.
- The animation advanced while issuing zero HID queries; an additional 11-second idle interval also issued zero queries.
- Closing during an active device read cancelled the read and the complete application process exited within the test deadline.

Physical cable charging, unplug/replug recovery, and power-cycle persistence have not been exercised yet. Flash writes and active DPI are verified. USB connection support is implemented for the model's C09B identifier and still needs a physical test.

## Save behavior and current limits

Only the active default DPI stage of the current onboard profile is edited, plus the profile name when requested. Other stages, button mappings, lift-off distance, polling rate, and unrelated profile bytes remain unchanged. The current version requires onboard mode already enabled. Unknown formats, invalid checksums, or a mismatch between active DPI and the stored profile disable saving. Device identity and profile state are rechecked before mutations.

Before writing, the app rereads the profile, checks for concurrent changes, and saves its original bytes to a unique file in **backups**. It patches only both DPI values and the checksum, writes the sector, verifies every byte, reloads the same profile/stage, then verifies active DPI. Failed writes are reported as unverified and never silently retried. Backups are retained; a restore interface is not yet included.

Avoid simultaneous editing in Onboard Memory Manager. The app detects changes before writing, but independent device-control applications cannot share an atomic hardware lock.

## Build and test

Run `./build.ps1` to compile using the .NET Framework compiler installed with Windows. The app uses WPF and native Windows HID APIs; no SDK or downloaded packages are required.

- `./check.ps1`: offline checks.
- `./check.ps1 -Device`: also performs read-only hardware checks and a save rehearsal.
- `MouseChecks.exe --roundtrip`: explicitly writes a small DPI change and restores the original. Do not run concurrently with another profile editor.
- `MouseChecks.exe --profiles-roundtrip`: tests profile selection and renaming, restoring the original selected profile/name.
- `MouseController.exe --render <absolute-output.png>`: renders the live window off-screen.
- `MouseController.exe --ui-check <absolute-report.txt>`: checks input behavior without pressing Save.
- `MouseController.exe --render-firmware <absolute-output.png>`: renders the firmware screen after an official online check.
- `MouseController.exe --firmware-check <absolute-report.txt>`: tests the live official version check.
- `MouseController.exe --close-check <absolute-report.txt>`: initiates a read and closes; the caller must verify process exit.

Only one app instance opens at a time. Device work runs off the UI thread and device handles are closed after each operation. Closing during a read cancels it; closing during a save finishes the write and then automatically exits. There are no periodic reads, hidden helper processes, or startup registrations.

## Run the diagnostic

Build with `./build-probe.ps1` in PowerShell, then run `./MouseProbe.exe`.
Uses the Windows .NET Framework compiler already installed on this computer; no downloaded packages. The executable exits after discovery and does not install a service.

Windows device access was tested outside the development sandbox. The diagnostic uses native overlapped I/O with response deadlines. The sandbox prevented compilation of temporary compiler resources.

## Protocol references

Protocol interpretation was checked against the [Solaar HID++ implementation](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp20.py) and [extended DPI settings](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/settings_templates.py).

The [lowtech format-7 notes](https://github.com/orthory/lowtech) provided additional profile context. The DPI offsets were inferred from this mouse's profile and validated by the exact-byte save/restore test.

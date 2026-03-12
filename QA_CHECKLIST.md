# IntLimiter QA Checklist

## Setup / Installation
- [ ] Run `build_release.ps1` and verify `Releases/Setup.exe` is generated.
- [ ] Install `Setup.exe` and verify desktop shortcut is created.
- [ ] Verify `IntLimiter.App` launches automatically after install.

## UI / UX
- [ ] Open Dashboard page and verify smooth loading without crashes.
- [ ] Toggle Dark/Light mode in Settings and verify the UI updates correctly.
- [ ] Open Processes page and verify it lists networking applications.
- [ ] Verify unit dropdown changes the string formatting.

## Core Logic
- [ ] Verify ETW monitor fetches actual Upload/Download bytes.
- [ ] Run a fast download (e.g. speedtest) and verify `IntLimiter.App` reflects the traffic spike.
- [ ] Create a new Limit Rule globally and verify it persists after app restart.
- [ ] Create a per-process Limit Rule and verify it appears.

## Limitations (Expected)
- [ ] Ensure the Debug output logs `[RuleEngine] Awaiting signed WFP kernel driver`. Real traffic throttling will not drop packets until the native sys driver is installed.

## Updates
- [ ] Verify the Settings page's `Check for Updates` correctly queries GitHub and returns NO update if version matches.

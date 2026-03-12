# IntLimiter Architecture

## Overview
IntLimiter is designed as a native Windows desktop utility to monitor and manage network traffic on a per-process and global level. It is built using modern C# / .NET 8, implementing the Windows App SDK (WinUI 3) for the frontend to align perfectly with Windows 11 aesthetics.

## Component Architecture

The codebase is split into the following primary components:

### 1. IntLimiter.App (WinUI 3 Shell)
- **Role:** The host application and bootstrapper.
- **Responsibilities:** Setting up the DI container, applying themes (Light/Dark mode), handling application lifecycle, and hosting the main Window.

### 2. IntLimiter.UI
- **Role:** Presentation layer (MVVM).
- **Responsibilities:** Views, ViewModels, visual components, converters, and navigation. Uses `CommunityToolkit.Mvvm` for reactivity. Cleanly separated from the pure App logic.

### 3. IntLimiter.Core
- **Role:** Business logic, models, and interfaces.
- **Responsibilities:** Contains data models (`ProcessNetworkUsage`, `Rule`, `Settings`), abstractions (`ITrafficMonitor`, `IRuleEngine`), unit conversions, and core constants.

### 4. IntLimiter.Infrastructure
- **Role:** Data persistence and system-level abstractions.
- **Responsibilities:** Reading/writing JSON configuration files, structured logging implementation, and local app data storage.

### 5. IntLimiter.Monitoring
- **Role:** Network traffic inspection (Read-Only).
- **Responsibilities:** Implementing `ITrafficMonitor`. Uses `GetExtendedTcpTable`/`GetExtendedUdpTable` combined with ETW or polling `GetPerTcpConnectionEStats` to map active network connections to Process IDs and calculate Upload/Download rates.

### 6. IntLimiter.RateLimiting
- **Role:** Network traffic enforcement (Write).
- **Responsibilities:** Implementing `IRuleEngine`.
- **Important Engineering Truth:** Genuine per-process network throttling requires either a Windows Filtering Platform (WFP) Callout Driver (Kernel Mode, C/C++) or a specialized local proxy structure. In this project, `RateLimiting` will implement the *architectural boundary* for rules and QoS (Quality of Service) applying `tc` (Traffic Control API) where possible. Any missing kernel-mode capabilities will be cleanly documented as requiring signed drivers, and the C# API will provide the required hooks to communicate with them.

### 7. IntLimiter.Service
- **Role:** Elevated privileges boundary.
- **Responsibilities:** A lightweight background Windows Service or elevated process used to apply firewall or WFP rules that the regular user-mode App does not have privileges for.

### 8. IntLimiter.Updater
- **Role:** Application lifecycle and distribution.
- **Responsibilities:** Checking for new releases via GitHub tag comparisons, downloading deltas, and applying updates using Velopack.

## Flow of Data
1. **Monitoring:** A background ticker in `IntLimiter.Monitoring` queries the OS every X milliseconds, updating the current rate properties of running processes.
2. **UI Updates:** The `DashboardViewModel` and `ProcessesViewModel` subscribe to these events (or poll the central store) and marshal updates to the WinUI thread.
3. **Limiting:** The user adds a rule on the UI. The rule is validated, saved to disk via `IntLimiter.Infrastructure`, and passed to `IntLimiter.RateLimiting`.
4. **Enforcement:** `IntLimiter.RateLimiting` communicates with the OS (or `IntLimiter.Service`) to apply the new bandwidth caps.

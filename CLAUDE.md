# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CoreRipperX is a .NET 8 CPU stress-testing tool that evaluates CPU core performance and stability using AVX2 operations. It includes a CLI console app and a WPF desktop application with real-time hardware monitoring.

## Build Commands

```bash
# Build the entire solution
dotnet build CoreRipperX.sln

# Build specific projects
dotnet build CoreRipperX/CoreRipperX.csproj          # CLI app
dotnet build CoreRipperX.UI/CoreRipperX.UI.csproj    # WPF GUI app

# Run the CLI tool (time in seconds per core)
dotnet run --project CoreRipperX/CoreRipperX.csproj -- 30

# Run the WPF application
dotnet run --project CoreRipperX.UI/CoreRipperX.UI.csproj
```

## Architecture

### Solution Structure
- **CoreRipperX/** - CLI console application entry point
- **CoreRipperX.Core/** - Shared library with services. x64-only, unsafe code enabled
- **CoreRipperX.UI/** - WPF desktop application with Material Design
- **shims/** - LibreHardwareMonitor shim library from CapFrameX
- **reference/** - Git submodule containing CapFrameX reference implementation

### Key Services (in CoreRipperX.Core/Services/)
- **StressTestService** - AVX2 stress test implementation using System.Runtime.Intrinsics.X86.Avx2. Runs per-core or all-core tests with result validation to detect CPU instability. Supports processor groups for 64+ core systems.
- **HardwareMonitorService** - LibreHardwareMonitor wrapper for CPU sensors. Handles Intel (1-based) vs AMD (0-based) core numbering differences. Requires admin privileges.

### Data Flow
Services emit IObservable streams (System.Reactive) -> ViewModels subscribe and update observable properties (CommunityToolkit.Mvvm) -> XAML bindings reflect changes

## Technical Notes

- **x64-only**: Core library targets x64 exclusively due to low-level AVX2 operations
- **Admin required**: LibreHardwareMonitor needs admin privileges for full sensor access
- **Thread affinity**: Uses P/Invoke for SetThreadAffinityMask and GetCurrentThread
- **Processor groups**: Intel NUMA/64+ core systems require special handling in StressTestService

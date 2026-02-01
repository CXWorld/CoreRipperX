# Repository Guidelines

## Project Structure & Module Organization
- `CoreRipperX/`: CLI console app entry point.
- `CoreRipperX.Core/`: shared core library (x64 only, unsafe/AVX2 code).
- `CoreRipperX.UI/`: WPF desktop app (Material Design).
- `shims/`: local shim projects (e.g., LibreHardwareMonitor).
- `reference/`: CapFrameX reference submodule and related projects; treat as vendor code.

## Build, Test, and Development Commands
- Build solution: `dotnet build CoreRipperX.sln`
- Build CLI only: `dotnet build CoreRipperX/CoreRipperX.csproj`
- Build UI only: `dotnet build CoreRipperX.UI/CoreRipperX.UI.csproj`
- Run CLI (seconds per core): `dotnet run --project CoreRipperX/CoreRipperX.csproj -- 30`
- Run UI: `dotnet run --project CoreRipperX.UI/CoreRipperX.UI.csproj`

## Coding Style & Naming Conventions
- C# uses implicit usings and nullable enabled in project files.
- Keep AVX2 and affinity-related code in `CoreRipperX.Core` and avoid cross-layer leakage.
- Prefer clear, descriptive names for services and view models (e.g., `StressTestService`).

## Testing Guidelines
- No dedicated test project exists for CoreRipperX at the repo root.
- The `reference/` subtree includes upstream CapFrameX tests; they are not part of this solution’s normal build.
- If you add tests, keep them in a new `CoreRipperX.Tests/` project and run with `dotnet test`.

## Commit & Pull Request Guidelines
- Recent commits use short, lowercase summaries without prefixes (e.g., “fixed layout”).
- Keep commit messages concise and focused on one change.
- PRs should include: a short description, steps to verify (commands or manual checks), and screenshots for UI changes.

## Security & Configuration Notes
- Hardware monitoring may require admin privileges for full sensor access.
- UI build copies `CapFrameX.Hwinfo.dll` post-build; ensure the referenced binary exists for your configuration.

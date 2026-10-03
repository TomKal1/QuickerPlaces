## Babysitter

### Project workflow

- Use focused feature branches for changes; keep CI/CD configuration unchanged unless Thomas explicitly requests a change.
- Work with Thomas and AI agents through small, reviewable changes that preserve the existing WPF/MVVM and UI-free shared-code boundaries.
- Do not stage or commit files from the Pi agent folder. No commits are part of routine project setup or implementation unless Thomas explicitly asks.

### Performance and validation

- Before optimizing memory or responsiveness, identify representative workloads and capture a reproducible baseline. Include the app state/workload, Windows/.NET environment, measurement tool, and repeated results.
- Use .NET/WPF-appropriate profiling tools (for example Visual Studio Profiler, `dotnet-trace`, `dotnet-counters`, or PerfView); do not apply Electron-specific profiling guidance to this app.
- Compare before/after measurements under equivalent conditions. Report memory and responsiveness changes with measurement variability; do not claim an improvement without evidence.
- Add focused correctness/performance tests where feasible, preserve behavior, and run `dotnet test src/QuickerPlaces.sln` from `C:\QuickerPlaces\src`.
- Keep shared UI-free models and services free of WPF dependencies so they remain usable by portable tests and the `qp` CLI.

### Babysitter usage

- Check `babysitter --help` for installed commands; use project-relevant processes only after reviewing their definitions and adapting any framework-specific assumptions for .NET/WPF.
- Recommended approach: iterative convergence plus test-driven development for scoped implementation, with performance work gated by a measured baseline and equivalent post-change measurements.
- The desktop performance-optimization process is a reference, not a verbatim recipe: its current definition includes Electron-oriented steps. Use only after adapting the plan to this .NET/WPF application.
- No CI/CD integration is configured or requested.

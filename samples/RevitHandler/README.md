# QuickerPlaces sample Revit handler

A small Revit add-in that opens a **workshared central model as a new local** when QuickerPlaces asks it to. It is the reference implementation of the [Revit handler protocol](../../docs/revit-handler-protocol.md), and a template you can copy into your own add-in.

QuickerPlaces never references the Revit API. Revit has no out-of-process API, so the work (`WorksharingUtils.CreateNewLocal`, then `UIApplication.OpenAndActivateDocument`) has to run inside Revit. QuickerPlaces and the handler talk through small JSON files in `%LocalAppData%\QuickerPlaces\revit\`. If this README and the protocol document disagree, the protocol document is right and the sample has a bug.

What the handler does, and does not do:

- It registers itself (`handlers\`), announces that it is running (`instances\`), and watches `requests\<release>\quickerplaces.sample\`.
- For each request it checks the request, checks that the file is a central model saved in this Revit release, picks a free local file name, calls `CreateNewLocal`, and opens the local with the requested worksets.
- It **never** copies a central, overwrites or deletes a local, upgrades a model, saves, synchronises, relinquishes or closes a document. With no request waiting it does nothing.
- During the open it records every Revit dialog in the result file. It answers only dialogs on its allowlist, which ships **empty**. See "Dialogs" below.

## Layout

```
RevitHandler.sln
src/QuickerPlaces.RevitHandler/
  QuickerPlaces.RevitHandler.csproj    one project, ten configurations
  QuickerPlaces.RevitHandler.addin     Revit manifest
  Core/                                no Revit types: paths, JSON, claim, validation, local name, result, dialogs
  Revit/                               the glue: IExternalApplication, ExternalEvent handler, Revit calls
tests/QuickerPlaces.RevitHandler.Tests/   xUnit tests of Core (plain net10.0, no Revit needed)
```

This is its own solution. It is **not** part of `src\QuickerPlaces.sln`, so building and testing QuickerPlaces never needs the Revit API.

## Build

You need the .NET SDK (8 or later) and nuget.org access. No Revit installation is needed to build: the Revit API comes from the `Nice3point.Revit.Api.RevitAPI` and `RevitAPIUI` NuGet packages (API assemblies only, not the Nice3point Toolkit), and Revit's own DLLs are never copied to the output.

The configuration chooses the Revit release:

| Configuration | Revit | Target framework |
|---|---|---|
| `Debug R22`, `Release R22` | 2022 | `net48` |
| `Debug R23`, `Release R23` | 2023 | `net48` |
| `Debug R24`, `Release R24` | 2024 | `net48` |
| `Debug R25`, `Release R25` | 2025 | `net8.0-windows` |
| `Debug R26`, `Release R26` | 2026 | `net8.0-windows` |

```
dotnet build src/QuickerPlaces.RevitHandler -c "Release R25"
```

Plain `Debug` or `Release` builds for Revit 2025. In Visual Studio, open `RevitHandler.sln` and pick the configuration from the toolbar. The output is `src\QuickerPlaces.RevitHandler\bin\<configuration>\<framework>\`, holding `QuickerPlaces.RevitHandler.dll` and the `.addin` file.

On a machine without Windows (a build server, a Linux container) add `-p:EnableWindowsTargeting=true` if your SDK asks for it; the project already sets it.

## Install

Close Revit, then copy into the add-ins folder of the release you built for:

```
%AppData%\Autodesk\Revit\Addins\2025\QuickerPlaces.RevitHandler.addin
%AppData%\Autodesk\Revit\Addins\2025\QuickerPlaces.RevitHandler\QuickerPlaces.RevitHandler.dll
```

The `.addin` file names the DLL with a relative path (`QuickerPlaces.RevitHandler\QuickerPlaces.RevitHandler.dll`), relative to the folder holding the `.addin` file. Put the DLL somewhere else and edit `<Assembly>` to a full path instead. Use the DLL built for that release: a `net48` build for 2022 to 2024, a `net8.0-windows` build for 2025 and 2026.

Start Revit once. The handler writes `handlers\quickerplaces.sample-2025.json` under the protocol root, and QuickerPlaces lists it in Settings from then on.

### The unsigned add-in prompt

This sample is **not signed**. Revit therefore shows "Security - Unsigned Add-In" with **Always Load**, **Load Once** and **Do Not Load** the first time it loads it. The handler cannot answer its own prompt, because it is not loaded until the prompt is answered. Your options:

- Answer **Always Load** once (the choice is remembered per add-in).
- **Sign your own build** with your own certificate. Once Revit trusts the publisher, it loads signed add-ins from that publisher without asking, including after updates signed with the same certificate.
- Use QuickerPlaces' opt-in **Load Once** list, which presses *Load Once* (never *Always Load*) for add-ins you allowed beforehand, on a Revit that QuickerPlaces itself launched. See roadmap section 4.21.

## Dialogs

While it makes and opens the local (and only then) the handler listens to `UIApplication.DialogBoxShowing`. Every dialog is written to the result file's `dialogs` list with its `DialogId`, kind, message (at most 500 characters) and whether it was answered. A dialog is answered only if its `DialogId` is in `Core/DialogAllowlist.cs`. That list **ships empty**, so by default every dialog is shown to the user and recorded.

To grow the list, open real models in each release, read the `dialogs` in the result files (also noted in the log), and add an entry only for a dialog you have seen, with an answer that does not save, synchronise, relinquish, detach, upgrade, delete or close. The first candidate is in the file as a comment: `TaskDialog_Missing_Third_Party_Updater` answered `1001` ("Continue working with the file"). Matching is by `DialogId` only, never by message text, because the text depends on Revit's language.

Failures (warnings and errors raised while the model opens) are left to Revit. The handler does not delete warnings or resolve failures.

## Test

The tests cover everything that needs no Revit: reading the spec's example request, every refusal and its error code, claiming (including two claimers racing), choosing the local name, the result, registration and instance files, atomic writes, the `QUICKERPLACES_REVIT_ROOT` override and the empty allowlist.

```
dotnet test RevitHandler.sln -c "Release R25"
```

(The tests build against plain `net10.0` whichever configuration you pick; the Core files are compiled into the test project directly.)

### Testing in Revit

Use a **throwaway central model**, never a project file. Set `QUICKERPLACES_REVIT_ROOT` to a scratch folder in the environment of the Revit process, so the test never touches the real protocol folder:

```
set QUICKERPLACES_REVIT_ROOT=C:\Temp\qp-revit
"C:\Program Files\Autodesk\Revit 2025\Revit.exe"
```

Then write a request by hand (or let QuickerPlaces do it), for example `C:\Temp\qp-revit\requests\2025\quickerplaces.sample\4f1c2a9be0d34c7f8a61b0e5d27c9a13.json`, as in the protocol document. Write it to a temporary name first and rename it, so the handler never sees a half-written file. Watch for `...result.json` next to it, and read the handler's log in `logs\quickerplaces.sample-2025.log`.

Cases to check in each release (from "Testing a handler" in the protocol document):

- Cold start (the request is already there when Revit starts; also try with `QUICKERPLACES_REVIT_REQUEST=<requestId>` set).
- Revit already running.
- Two copies of one release running: exactly one handles the request.
- A local name that already exists: the new local gets `_<yyyyMMdd-HHmmss>`, the old file is untouched.
- An expired request: result `expired`, nothing opened.
- A central saved in another release: result `releaseMismatch`, nothing upgraded.
- A local file passed as `centralPath`: result `notCentral`.
- A non-workshared file: result `notCentral`.
- A missing `localFolder`: it is created; an uncreatable one gives `localFolderUnavailable`.
- A request naming another handler: result `invalidRequest`.

## Make it your own

1. In `Revit/App.cs`, change `HandlerId` (1 to 64 characters of `a-z`, `0-9`, `.` and `-`, stable across versions, for example `contoso.revittools`) and `DisplayName` (at most 80 characters, shown in QuickerPlaces' Settings).
2. In the `.addin` file, give the add-in your own `AddInId` GUID, `Name`, `VendorId` and `FullClassName`.
3. Copy the `Core` folder and the three files in `Revit` into your add-in. If your add-in already has an `IExternalApplication`, move the body of `OnStartup`, `OnShutdown` and the two event handlers from `App.cs` into it. The only requirements are: no work in `OnStartup`, no work on the watcher or timer threads except `ExternalEvent.Raise()`, and all Revit calls inside `IExternalEventHandler.Execute`.
4. Keep `Core` free of Revit types. It uses `System.Runtime.Serialization.Json` rather than `System.Text.Json` on purpose: loading a newer `System.Text.Json` into Revit 2022 to 2024 can conflict with another add-in's copy.
5. Keep the build layout (`RevitVersion` and `TargetFramework` per configuration, the `$(RevitVersion).*` package references with `ExcludeAssets="runtime"`) so one project builds for every release.

## License

MIT, like the rest of the repository: see [LICENSE](../../LICENSE).

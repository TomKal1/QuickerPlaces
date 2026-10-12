# Moving the sample handler into your own add-in

This guide moves the QuickerPlaces sample Revit handler into another Revit add-in (the **target add-in**), so that your add-in opens workshared central models as new locals when QuickerPlaces asks. It is written for a person or an AI coding agent doing the work.

Read these first:

- [Revit handler protocol](../../docs/revit-handler-protocol.md). It is the contract. If this guide, the sample and the protocol disagree, the protocol is right.
- [README.md](README.md) in this folder, for how the sample builds, installs and is tested.

## What you end up with

- The target add-in registers itself as a handler under **its own** `handlerId`, in every Revit release it loads in.
- QuickerPlaces lists it in Settings, and the user picks it per release instead of (or as well as) the sample.
- The target add-in's existing features do not change. With no request waiting, the handler does nothing.

## Choose a route

| Route | When | Work |
|---|---|---|
| **A. Merge** | The target add-in already has an `IExternalApplication` and you want one add-in to install. | Copy the handler code in and call it from the existing application class. |
| **B. Rename** | You want a separate, stand-alone handler add-in under your own name. | Copy this whole folder, then rename the project, namespace, IDs and manifests. |

Most of the steps are shared. The route only changes step 4.

## Before you start: decide these values

| Value | Rule | Example |
|---|---|---|
| `HandlerId` | 1 to 64 characters from `a-z`, `0-9`, `.` and `-`, starting with a letter or digit. It must stay the same across versions. Do not reuse `quickerplaces.sample`. | `contoso.revittools` |
| `DisplayName` | At most 80 characters. Shown in QuickerPlaces' Settings. | `Contoso Revit Tools` |
| Namespace | The target add-in's namespace for the handler code. | `Contoso.RevitTools.QuickerPlaces` |
| Revit releases | 2022 or later only. 2022 to 2024 use `net48`; 2025 and 2026 use `net8.0-windows`. | 2024–2026 |

Changing `HandlerId` later makes QuickerPlaces see a new handler, and users must pick it again in Settings.

## What to copy

From `src/QuickerPlaces.RevitHandler/`:

| Source | Contains | Copy? |
|---|---|---|
| `Core/*.cs` (16 files) | Protocol logic with no Revit types: paths, JSON, claiming, validation, local name, the old local (overwrite or rename), results, dialog allowlist, log. | Yes, unchanged except the namespace. |
| `Revit/OpenNewLocalHandler.cs` | The `IExternalEventHandler`. All Revit work starts here. | Yes. |
| `Revit/RevitSession.cs` | The only Revit API calls: `BasicFileInfo.Extract`, the open documents' `PathName`, `CreateNewLocal`, `OpenAndActivateDocument`, `DialogBoxShowing`. | Yes. |
| `Revit/App.cs` | The `IExternalApplication`: start-up, watcher, timer, shutdown. | Route B: yes. Route A: copy its fields and method bodies into your application class (step 4). |
| `QuickerPlaces.RevitHandler.addin`, `PackageContents.xml` | Manifests. | Route B only, then edit. Route A keeps the target's own manifests. |
| `tests/QuickerPlaces.RevitHandler.Tests/` | xUnit tests of `Core`. | Yes, recommended. |

Do not copy `bin/`, `obj/` or `.vs/`.

## Steps

### 1. Copy the files

Copy `Core/` and the `Revit/` files into the target project, for example into a `QuickerPlaces/` folder. Keep `Core` in its own folder. The test project compiles that folder by path.

### 2. Change the namespace

The sample uses `QuickerPlaces.RevitHandler` (Revit files) and `QuickerPlaces.RevitHandler.Core` (Core files). Replace both in every copied file, including the `using QuickerPlaces.RevitHandler.Core;` lines. Do not change anything else in `Core`.

### 3. Set the identity

In `App.cs` (route B) or your application class (route A), set:

```csharp
public const string HandlerId = "contoso.revittools";
public const string DisplayName = "Contoso Revit Tools";
```

The handler version written to the registration file comes from the assembly version of the class that calls `new HandlerContext(...)` (`typeof(App).Assembly`). In route A, change `typeof(App)` to your application class.

The protocol root (`%LocalAppData%\QuickerPlaces\revit\`), the `QUICKERPLACES_REVIT_ROOT` and `QUICKERPLACES_REVIT_REQUEST` variable names and the `"open-new-local"` action are part of the protocol. Do not rename them.

### 4a. Route A: call the handler from your existing application

Add the sample's private fields (`_uiApp`, `_context`, `_presence`, `_queue`, `_log`, `_event`, `_watcher`, `_timer`, `_stopped`, and the `RequestHintVariable` constant) to your `IExternalApplication`, then:

- **`OnStartup`:** append the body of the sample's `OnStartup` inside its own `try`/`catch`. A handler failure must not fail your add-in: in the `catch`, log and continue, and do not return `Result.Failed` for the whole add-in.
- **`OnShutdown`:** append the body of the sample's `OnShutdown`.
- **Methods:** copy `OnApplicationInitialized`, `OnFirstIdling`, `StartWatching`, `OnRequestFileEvent`, `OnTimer` and `RaiseEvent` unchanged.

If your add-in already subscribes to `ApplicationInitialized` or `Idling`, keep the handler's own subscriptions separate. The handler unsubscribes from `Idling` after the first call, because a subscribed `Idling` handler makes Revit call back more often.

### 4b. Route B: rename the stand-alone add-in

- Rename the project, `RootNamespace`, `AssemblyName` and the `.addin` file.
- In the `.addin` file, set a **new** `AddInId` GUID, `Name`, `FullClassName` (your namespace + `.App`), `VendorId`, `VendorDescription` and the `<Assembly>` path.
- In `PackageContents.xml`, set `Name`, `Description`, `Author`, `CompanyDetails`, each `AppName` and each `ModuleName` path. Remove the `Components` entries for releases you do not build.
- In the `.csproj`, rename `RevitBundleRoot`, the `QuickerPlaces.RevitHandler` folder names in the deploy target, and the `.addin` file name in the `None` and `Copy` items.

### 5. Check the build settings of the target project

The copied code needs these settings. Compare them with the sample's `.csproj`:

- **Language version.** The code uses nullable annotations (`string?`). `net48` projects default to C# 7.3, which rejects them. Set `<LangVersion>latest</LangVersion>` (or 8.0 or later). Set `<Nullable>enable</Nullable>`, or expect nullable warnings.
- **Target framework per release.** `net48` for 2022 to 2024, `net8.0-windows` for 2025 and 2026. A `net8.0` build does not load in Revit 2024, and a `net48` build does not load in Revit 2025.
- **Revit API references.** Reference `RevitAPI` and `RevitAPIUI` for the right release, and never copy them to the output. With NuGet: `Nice3point.Revit.Api.RevitAPI` and `RevitAPIUI` at `$(RevitVersion).*` with `ExcludeAssets="runtime"`. With direct references: `<Private>false</Private>`.
- **Platform.** `x64`.
- **JSON.** `Core` uses `System.Runtime.Serialization.Json` (`DataContractJsonSerializer`) on purpose. Do not change it to `System.Text.Json` or Newtonsoft.Json: another add-in's copy of those can conflict in Revit 2022 to 2024. If a `net48` build cannot find `DataContractJsonSerializer`, add `<Reference Include="System.Runtime.Serialization" />`.
- **Implicit usings.** The copied files have explicit `using` lines and build with implicit usings on or off.

If the target add-in uses `Debug R22` to `Release R26` configurations that set `RevitVersion` and `TargetFramework`, as the sample does, no build change is needed.

### 6. Move the tests

Copy `tests/QuickerPlaces.RevitHandler.Tests/` into the target's test folder, then:

- Point the `<Compile Include="...\Core\*.cs" .../>` link at the copied `Core` folder.
- Change the namespaces in the test files and the `HandlerId` and display name in `TestKit.cs`.
- In the test files, replace `"quickerplaces.sample"` inside the JSON strings with your `HandlerId`.

The test project targets plain `net10.0` and needs no Revit. If a `Core` file stops compiling there, it has picked up a Revit dependency. Move that code to `Revit/`.

### 7. Remove the sample from test machines

If the sample handler is also installed, both handlers load and both register. They do not compete for requests, because each one reads only its own request folder. Remove the sample anyway so the user does not pick the wrong handler. Delete its `.addin` file and folder from `%AppData%\Autodesk\Revit\Addins\<release>\`, or its bundle from the ApplicationPlugins folder.

## Rules that must survive the move

A port that breaks any of these is not a handler. They come from the protocol's "Safety rules" and "Handling a request" sections.

- Make locals only with `WorksharingUtils.CreateNewLocal`. Never copy a central.
- Never overwrite, delete, rename or move an existing local or any other file outside the protocol folder, with one exception: when a request carries `existingLocal`, the old local at the new local's own path and its `_backup` folder are deleted (`"overwrite"`) or renamed with their date (`"appendTimestamp"`), only through `Core/ExistingLocal.cs`, and refused with `localInUse` when that local is open in Revit or locked. Keep that file unchanged.
- Never upgrade a model. Refuse a central saved in another release.
- Never save, synchronise, relinquish, detach or close a document while handling a request.
- No work in `OnStartup` except writing the registration and instance files. The watcher and timer threads only call `ExternalEvent.Raise()`. All Revit API calls run inside `IExternalEventHandler.Execute`.
- Nothing may throw out of `Execute` into Revit.
- Keep `Core` free of Revit types.
- Keep the dialog allowlist (`Core/DialogAllowlist.cs`) empty until you have seen a dialog in a real result file. Match by `DialogId` only.

## Verify

1. Build every configuration you ship. For example: `dotnet build -c "Release R24"`, then R25 and R26.
2. Run the moved tests: `dotnet test <your test project>`.
3. Install the build for one release, set `QUICKERPLACES_REVIT_ROOT` to a scratch folder, and start that Revit. Check that the scratch folder now holds:
   - `handlers\<HandlerId>-<release>.json`, with your `displayName`, `"actions": ["open-new-local"]` and `"existingLocal": ["overwrite", "appendTimestamp"]`;
   - `instances\<HandlerId>-<release>-<processId>.json`, with a `readyUtc` once Revit has started;
   - `logs\<HandlerId>-<release>.log`, ending in `Ready.`
4. Write a request by hand, as in the README's "Testing in Revit", with `"handlerId"` set to your `HandlerId` and placed in `requests\<release>\<HandlerId>\`. Use a throwaway central model, never a project model. Check the result file and that the local opened.
5. Run the other cases in the README's "Testing in Revit" list in each release you ship, including a request with `"existingLocal": "overwrite"` and one with `"appendTimestamp"` when the local already exists (its `_backup` folder must be deleted or renamed with it), and both with that local open in Revit (the result must be `localInUse`, with nothing changed).
6. Without `QUICKERPLACES_REVIT_ROOT`, start Revit once, open QuickerPlaces' Settings and pick your handler for that release. Open a central from the Library.
7. Use your add-in's existing features. They must work as before, with and without a request waiting.

## Checklist for an AI agent

Use this as the task definition. Ask the user for any value in "Before you start" that you cannot find in the target repository. Do not guess `HandlerId`.

- [ ] Read the protocol document and this folder's README.
- [ ] Find the target add-in's `IExternalApplication`, `.addin` manifest, project file and test project. Choose route A or B and tell the user which.
- [ ] Copy `Core/` and the `Revit/` files. Change only the namespaces in `Core`.
- [ ] Set `HandlerId` and `DisplayName`. In route A, replace `typeof(App)`.
- [ ] Route A: merge the start-up, shutdown and event code into the existing application class, with handler failures caught and logged.
- [ ] Route B: new `AddInId` GUID; rename the manifest, bundle and deploy paths.
- [ ] Check language version, target framework per release, Revit API references (not copied to output) and `x64`.
- [ ] Move the tests and update `HandlerId` in them. All tests pass. (`QueueTests.TwoClaimersExactlyOneWins` also fails in the sample itself on Windows as of 2026-10-12; note it rather than chase it in the port.)
- [ ] A port made before 2026-10-12: also copy `Core/ExistingLocal.cs`, the `existingLocal`, `previousLocal` and `previousLocalPath` members of `Core/ProtocolModels.cs`, `ErrorCodes.LocalInUse`, the registration's `ExistingLocal` list (`Core/HandlerPresence.cs`), `RequestValidator.TryParseExistingLocal`, step 4 of `Core/RequestProcessor.cs`, and `IRevitSession.IsOpen` with its `RevitSession` body.
- [ ] Every shipped configuration builds.
- [ ] Search the target repository for `quickerplaces.sample`, `QuickerPlaces sample handler` and the sample's `AddInId` `d3a42712-c2b5-4e60-89d7-3e4d24a149d2`. None remains, except as a reference to this guide.
- [ ] List what the user must check in Revit (steps 3 to 7 of "Verify"). Do not claim the handler works in Revit until those checks pass.

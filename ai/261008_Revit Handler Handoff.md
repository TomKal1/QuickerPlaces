---
status: Revit release reading done and committed; dialog design and Thomas's four decisions recorded (RBP studied); next is the protocol spec, then the handler add-in that opens centrals as new locals
branch: feature/revit-file-info
head: the commit after e23ac46 that adds the dialog design (check git log)
date: 2026-10-08
---

# Revit handler handoff — next session

## Start here

Read, in this order:

1. `CLAUDE.md` (project workflow: focused branches, no commits unless Thomas asks, keep WPF/MVVM and UI-free boundaries).
2. Roadmap [§4.21 "Revit-safe opening"](260901_Professional%20Improvements%20Plan.md), from "Opening workshared central models, through a Revit handler add-in" to the end of the section. It holds the settled design: the safety rules, the file-based handler contract, what the handler does with a request, the sample add-in, and "Reading the saved release".
3. This handoff, for what is built, what Thomas asked for next, the dialog design (including what RevitBatchProcessor does) and Thomas's decisions.

Branch `feature/revit-file-info` was cut from `feature/more-file-kinds` (5ceda58, which added the Revit document kind) and is **not pushed**. Thomas sometimes pushes and merges himself; check `git log` and the remote before assuming.

## What is done (006043f)

- **Reading a file's release without Revit.** `src/QuickerPlaces/Services/Revit/RevitFileInfoReader.cs` opens a `.rvt`/`.rfa`/`.rte` read-only through OpenMcdf 3.2.0 (MPL-2.0, the app's first third-party package), reads the `BasicFileInfo` stream and parses its binary header: layout version, workshared byte, worksharing-type byte (0 central, 1 local, 2 in progress, 3 local just created), then length-prefixed UTF-16 strings (username, central path, format year, build, last save path). Only **Revit 2022 and later are supported** (Thomas, 2026-10-08): 2019–2021 report their release with `TooOld`, older layouts report `TooOld` with no release.
- **`qp revit info <file>`** prints the result as JSON.
- **Library Type column** shows "Revit 2025", "Revit 2021 (old)" or "Revit ?", with a tooltip naming central/local or why the release is unknown. `RevitReleaseCache` (one per app, created in `MainWindow`) answers from memory and checks files in the background: one read per file version (last write + length), answers trusted 30 s, a stalled read times out after 3 s and is never started twice.
- **Tests:** 1561 pass (`dotnet test src/QuickerPlaces.sln`), zero build warnings. Unit tests build stand-in compound files (`Tests/Fakes/RevitTestFiles.cs`); no real model is committed.
- **Checked on real files:** eight 2025 locals and their centrals, families/templates from 2021–2026, a 2008 sample, the Snowdon Towers 2025 sample; and in the running app (scratch `--data-root`, real clicks, UIA) the Type column and tooltips were correct.

Still unchecked from this work:

- Workshared central and local files saved in **2024 and 2026**, and a **central on the office network share** (timing and mapped-drive paths). To check: put copies named like `R2024 central.rvt` / `R2026 local.rvt` in a folder, set `QP_REVIT_SAMPLES` to it, run `dotnet test`. Never commit them.
- A moved or copied central still reports "central". Detecting one (recorded central path vs. the file's real path, after resolving mapped drives to UNC) belongs to the opening work below.
- In a narrow window, "Revit 2021 (old)" is cut off in the 100-px Type column; the tooltip has the full text.

## What Thomas asked for next

On 2026-10-08 Thomas asked to:

1. **Build a Revit add-in that drives this:** QuickerPlaces asks, the add-in opens a central as a new local inside Revit (`CreateNewLocal`, then `OpenAndActivateDocument`). It is the public sample handler in `samples/RevitHandler/` from §4.21, and must actually work, because it is both the guide for public users and Thomas's template for adding the same handler to his private WWTools add-in.
2. **Keep routing open:** QuickerPlaces must still be able to send requests to *another* add-in that implements the contract (WWTools privately, or anyone else's). The public repo never names or depends on WWTools in code. This is the handler registry in §4.21: handlers register what they can do, the user picks one per Revit release.
3. **Handle pop-ups when Revit opens.** The add-in, or QuickerPlaces around it, must get past dialogs that appear while Revit starts and while the model opens, including the **"load add-in" security prompts**. See "Dialogs" below. Thomas decided the open questions on 2026-10-08 ("Decisions" below).

## Suggested order of work

Each step is a small, reviewable change with tests where feasible. Use a new focused branch per step or per pair of steps (for example `feature/revit-handler-contract`, then `feature/revit-handler-sample`).

1. **Protocol spec. Drafted 2026-10-08** on `feature/revit-handler-contract` as [docs/revit-handler-protocol.md](../docs/revit-handler-protocol.md); Thomas to review. It changes three things from §4.21's sketch (listed at the end of the spec). Original brief: write `docs/revit-handler-protocol.md`, version 1, from §4.21: folder layout under `%LocalAppData%\QuickerPlaces\revit\` (`handlers\`, `instances\`, `requests\<release>\`), JSON fields, temp-name-then-rename writes, claim by rename to `.claimed-<pid>`, request expiry, result file, error codes, the `QUICKERPLACES_REVIT_REQUEST` environment variable for cold starts, process identity as PID plus start time, and the dialog rules below (what the handler may answer, what it logs into the result).
2. **QuickerPlaces side, UI-free, with tests:**
   - Contract records and JSON (System.Text.Json, like the stores).
   - Handler registry: read `handlers\*.json` and `instances\*.json`, ignore instances whose process is gone, report per release which handlers exist and which are live.
   - Request queue: write a request atomically, wait for the result file with a timeout, clean up, expire stale requests.
   - Installed-release discovery and `Revit.exe` paths. Reuse the approach (not the code) of Model Delta's `BatchRvtInstallation.cs` and RBP's `RevitVersion.cs`; RBP is GPL-3.0. Thomas has Revit 2021–2026 installed.
   - Choosing what to do for a file: release from `RevitReleaseCache`/reader; `TooOld` or not installed → refuse with a reason; central → handler request (or the verified command-line fallback); local or not workshared → launch that release with the file.
   - Expose it through `qp` as well (for example `qp revit open <file> [--dry-run]`), so it can be tested without the UI.
3. **Sample add-in** in `samples/RevitHandler/`, own solution, outside `src/QuickerPlaces.sln` (QuickerPlaces' build and tests must never need the Revit API):
   - Build layout like WWTools (`C:\WWTools\src\WWTools\WWTools.csproj`): `Debug/Release R22…R26` configurations setting `RevitVersion` and `TargetFramework` (`net48` for 2022–2024, `net8.0-windows` for 2025–2026), `Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI` at `$(RevitVersion).*`. Not Nice3point's Toolkit. §4.21 says R24–R26; widen to R22 since 2022 is now the oldest supported release.
   - `IExternalApplication`: write registration and instance files, watch the request folder, claim, and run the work from an `ExternalEvent` (or the first `Idling` after `ApplicationInitialized` on a cold start), never in `OnStartup`.
   - The five steps in §4.21 (validate; check `BasicFileInfo.Extract` is a central saved in this release; choose a non-colliding local name; `CreateNewLocal` + `OpenAndActivateDocument` with the requested worksets; write the result).
   - Keep parsing, validation, claiming and local-name choice free of Revit types and unit-test them in a small test project inside `samples/`.
   - README: build, install to `%AppData%\Autodesk\Revit\Addins\<release>\`, signing, and testing with a throwaway central.
4. **Dialog handling** (below, decided):
   - QuickerPlaces, UI-free: the dialog detector for a launched `Revit.exe` (enabled top-level `#32770` windows, title, static text, both kinds of buttons; progress windows without buttons are not "waiting"). First, record what each release's security prompt shows with UI Automation, so matching is built on evidence.
   - QuickerPlaces, UI-free: the manifest reader and the Load Once list (see "Load Once list" below), with tests using stand-in manifests and DLLs.
   - Handler: scoped `DialogBoxShowing` that logs every `DialogId` into the result and answers only the allowlist (empty at first, filled from observed opens).
5. **App UI:** for a Revit row, "Open" does the right thing for its kind; a central gets "Open as new local" (and the other choices §4.21 allows). Settings: per release, the handler to use (from the registry, or none) and the local folder (default `C:\REVIT_LOCAL20xx`, decided); the "Allow Load Once" switch (off by default) and the add-ins already allowed, each with a Remove button. Status while Revit starts ("Waiting for Revit 2025…", "Revit is waiting on a dialog", "The handler isn't loaded in Revit 2025").
6. **Manual verification** in 2022–2026 with a throwaway central: cold start; Revit already running; two copies of one release; existing local name; expired request; central saved in another release; a local passed as the central; handler not loaded; a second handler registered (routing); each dialog case below.

## Dialogs

Three kinds of dialog can stop an unattended open. They need different handling.

**1. Add-in security prompts ("Security – Unsigned Add-In": Always Load / Load Once / Do Not Load).** Revit shows these while it loads add-ins, for any add-in that isn't signed by a publisher the user trusts. Facts to design around:

- The handler add-in cannot answer its own prompt: it isn't loaded until the prompt is answered.
- **Signing removes the prompt.** Once a signed add-in's publisher is trusted ("Always Load" once), Revit loads it silently from then on, across updates signed with the same certificate. Thomas already has an Azure DevOps signing pipeline in the private `TomKal1/QuickerPlaces-Internal` repo; WWTools and the sample handler can be signed the same way. Public users would sign their own builds or answer the prompt once.
- Automatically clicking "Always Load" or "Load Once" for someone else's add-in **bypasses a security check** whose whole purpose is that a person decides. That needs Thomas's explicit decision; the recommendation is below.

**2. Dialogs while Revit starts, before add-ins can act** (licensing, sign-in, "What's new", recovery of a crashed session, the Home screen). These are outside any add-in's reach. QuickerPlaces can only see them as top-level windows of the `Revit.exe` process it launched.

**3. Dialogs while the model opens, inside Revit** (unresolved links or references, missing third-party updaters, element borrowing or worksharing notices, "local file is older than central", and so on). The handler can answer these:

- `UIControlledApplication.DialogBoxShowing` gives each Revit task dialog and message box with its `DialogId`; `OverrideResult` answers it. Subscribe only for the duration of one request, so normal Revit use is never affected.
- Warnings raised as failures go through an `IFailuresPreprocessor` or `Application.FailuresProcessing`.
- Some choices are made up front in `OpenOptions` (worksets; check which other options exist in each release's API rather than assuming).
- Answer only dialogs on an explicit list, by `DialogId`, with a safe answer recorded for each. Anything not on the list is left for the user, and its `DialogId` is written to the result file and a log so the list can grow from evidence. Never auto-answer anything that saves, synchronises, relinquishes, detaches, upgrades or deletes.
- The list must be built by observing real opens in each release; do not guess `DialogId`s.

**Recommendation for Thomas to confirm:**

- Sign the sample handler and WWTools. That removes the security prompt for them, which is the case that matters.
- For kind 1 and 2, QuickerPlaces **detects and reports** rather than clicks: while waiting for the handler's instance file, it watches the launched `Revit.exe` for top-level dialogs and says "Revit is waiting on a dialog: Security – Unsigned Add-In (MyAddin)". The user answers it; QuickerPlaces carries on when the handler appears.
- If Thomas still wants clicking: make it **opt-in and narrow**. Off by default; a user-maintained allowlist of add-in names (as the prompt shows them) for which QuickerPlaces may press **Load Once** (not Always Load); only for a Revit process QuickerPlaces itself launched, only during that launch, and logged every time. Verify the dialog's title, text and buttons in each release with UI Automation before relying on them.
- For kind 3, the allowlist-by-`DialogId` approach above, inside the handler.

RevitBatchProcessor solves the same problem for unattended batches with an external dialog-watching process; its approach is worth reading for knowledge (Thomas has the fork and `C:\Program Files\RevitBatchProcessor`), but its code is GPL-3.0 and must not be copied, and its batch behaviour (closing documents, deleting existing locals) is explicitly not what QuickerPlaces does.

### What RevitBatchProcessor does (read 2026-10-08, fork `TomKal1/RevitBatchProcessor` at 292bca4)

RBP handles pop-ups in two layers plus a launch handshake. Files: `BatchRvtUtil/Scripts/revit_dialog_detection.py`, `revit_dialog_util.py`, `revit_failure_handling.py`, `ui_automation_util.py`, `win32_user32.py`, `batch_rvt_monitor_util.py`, `monitor_revit_process.py`, `revit_process.py`, and `BatchRvtAddin20xx/BatchRvtAddinApplication.cs`.

**Launch handshake.** `BatchRvt.exe` starts `Revit.exe` itself (`UseShellExecute = false`, stdout/stderr redirected, working folder = the Revit folder) and passes the job through environment variables (`BATCHRVT__SCRIPTS_FOLDER_PATH`, `BATCHRVT__SCRIPT_FILE_PATH`, …, `RVT_ORIGIN=RBP`). The add-in's `OnStartup` only creates an `ExternalEvent` and raises it; the script host reads the variables and does nothing when they are missing, so the add-in is inert in a normal Revit session. The host also checks that the `BatchRvt.exe` that launched it is still alive, identified by PID plus process start time (so a reused PID is not mistaken for it).

**Layer 1, outside Revit (the "cheeky dialog dismisser").** Every 0.25 s while it waits, `BatchRvt.exe`:

- lists enabled top-level windows of class `#32770` that belong to the Revit PID;
- collects their buttons two ways: classic `Button` children (message boxes), and `DirectUIHWND` → `CtrlNotifySink` → `Button` (task dialogs, including command links such as "Always Load");
- matches the dialog by exact title and button count against a hard-coded table (English and Spanish only), and sends `BM_CLICK` with `SendMessage` to the chosen button, or `WM_CLOSE` for the Customer Involvement Program window;
- leaves progress windows alone ("Model Upgrade" with no buttons, "Load Link" with only "Cancel Link");
- for **any unknown dialog**, logs title and buttons and then clicks the first match from a priority list: OK, Close, No, **Always Load**, "Rhino 7", Ignore, Don't save, Relinquish, …

"Always Load" in that list is how RBP gets past the unsigned add-in security prompt, for its own add-in and for every other add-in, and it trusts the publisher permanently. The known-dialog table is mostly about closing documents: "Do not save the project", "Relinquish all elements and worksets", "Close the local file".

**Layer 2, inside Revit.** Around each scripted action RBP subscribes to `UIApplication.DialogBoxShowing` and unsubscribes in `finally`. It logs `Message`, `DialogType` and `DialogId`, then answers **every** dialog with `IDOK` (1), except `TaskDialog_Missing_Third_Party_Updater` → 1001 ("continue working with the file") and `TaskDialog_Location_Position_Changed` → 1002 ("do not save"). It also subscribes to `Application.FailuresProcessing` (and sets an `IFailuresPreprocessor` with forced modal handling on its own transactions): it deletes all warnings, resolves errors with UnlockConstraints, DetachElements or SkipElements, and otherwise rolls back.

**Watchdogs.** Timeouts for "script host never started", "file took too long" and "Revit did not exit", each of which kills Revit; an "unresponsive for more than 10 s" notice from polling the process.

### What QuickerPlaces takes from it, and what it does not

Use the approach (our own code, no GPL text):

- **Environment-variable handshake on a cold start.** When QuickerPlaces launches `Revit.exe` for a request, it sets one variable naming the request (for example `QUICKERPLACES_REVIT_REQUEST=<request id>`). The handler still serves the file-based queue, but the variable lets it pick up its request on the first `Idling` without waiting for a folder scan, and lets QuickerPlaces know the launched PID is the one to watch.
- **Process identity as PID plus start time** in instance files, so stale instance files are never matched to a reused PID.
- **The external watcher's window scan, for detecting and reporting only:** top-level enabled `#32770` windows of the launched PID; title, static text and both kinds of buttons. This is what "Revit is waiting on a dialog: Security – Unsigned Add-In (MyAddin)" needs. Progress windows without buttons are not reported as waiting.
- **Scoped `DialogBoxShowing`** in the handler, subscribed only for one request, logging every `DialogId`. Matches the plan above.
- The two `DialogId`s RBP answers are candidates for our allowlist, to be confirmed by observation: `TaskDialog_Missing_Third_Party_Updater` → continue (1001) is safe for an interactive open; `TaskDialog_Location_Position_Changed` → do not save (1002) appears on close/save, which the handler never does.

Do not take:

- **The fallback click for unknown dialogs, and the `IDOK`-to-everything default.** A person is sitting at Revit; an unknown dialog is theirs to answer.
- **Clicking "Always Load".** It bypasses the security check for any add-in and makes the trust permanent.
- **The close-document answers** (don't save, relinquish, close the local file). The handler never closes, saves or relinquishes.
- **Deleting warnings or detaching/skipping elements** in failure processing. Opening a local should leave failures to Revit's normal UI; the handler only logs them.
- **Matching by title text** for anything that is clicked. RBP's table is English and Spanish only; inside Revit, `DialogId` is language-independent. Titles are fine for reporting.
- **Killing Revit on a timeout.** It is the user's session. QuickerPlaces reports "still waiting" and lets the user cancel the request.

## Decisions (Thomas, 2026-10-08)

1. **Add-in security prompts: opt-in Load Once allowlist.** Off by default. Detect and report always runs. When the user turns it on, QuickerPlaces may press **Load Once** (never Always Load, never Do Not Load) on the security prompt only when the add-in in the prompt is on the user's allowlist (see "Load Once list" below), only for a `Revit.exe` QuickerPlaces launched, only until that launch's handler instance file appears (or the request ends), and it logs every click. Dialog title, text and buttons are verified per release with UI Automation before the click is enabled for that release; on any mismatch it reports instead of clicking.
2. **Signing: only WWTools.** The public sample handler stays unsigned; its README says to sign your own build or answer the prompt once (or put it on the Load Once list).
3. **Default local folder: per release, `C:\REVIT_LOCAL20xx`** (for example `C:\REVIT_LOCAL2025`), changeable per release in Settings.
4. **Direct open: yes** for locals and non-workshared files, by launching that release's `Revit.exe` with the file. Centrals always go through a handler; command-line opens of centrals stay off until verified per release (§4.21).

## Load Once list (designed and agreed 2026-10-08)

**Scope: this is not a Revit add-in manager.** QuickerPlaces never changes what Revit loads. It only answers one security prompt, once, for a launch it started, when the user said yes in advance.

In scope:

- Reading `.addin` manifests and bundle `PackageContents.xml` files, read-only.
- Checking the signature and SHA-256 hash of the DLL a manifest names.
- Storing the user's "allow Load Once" choices in QuickerPlaces' own settings.

Out of scope:

- Writing, renaming or moving `.addin` files (so no enabling or disabling add-ins).
- Installing, updating or uninstalling add-ins.
- Load order, conflicts, or start-up timing of add-ins.
- Anything for a `Revit.exe` that QuickerPlaces did not launch.

**How the list is built: learned from prompts, not typed, no inventory screen (first version).**

1. QuickerPlaces launches `Revit.exe` and its dialog detector sees a security prompt. The status line says "Revit is waiting: Unsigned Add-In 'DuctExporter'". The user answers the prompt in Revit, as today.
2. When the launch is done, QuickerPlaces offers "Allow Load Once for DuctExporter next time?". Only a yes adds an entry.
3. To fill the entry, QuickerPlaces finds the add-in's manifest for that release. Revit reads manifests from `%AppData%\Autodesk\Revit\Addins\<release>\`, `%ProgramData%\Autodesk\Revit\Addins\<release>\`, and `ApplicationPlugins\*.bundle\PackageContents.xml` under `%AppData%\Autodesk` and `%ProgramData%\Autodesk`. Each manifest gives `Name`, `Assembly`, `AddInId` and `VendorId`.
4. An entry stores: release, `AddInId`, `Name`, the full DLL path, and the DLL's SHA-256 when the user said yes. If no manifest matches the prompt, or more than one does, no entry is offered.

**When QuickerPlaces may click Load Once:** the "Allow Load Once" switch is on (off by default); the `Revit.exe` was launched by QuickerPlaces and that launch's handler instance file has not appeared yet; the prompt's title, text and buttons match what was recorded for that release; the add-in in the prompt matches an entry (name, DLL path and release); and the DLL's hash still matches. Every click is logged. Never Always Load, never Do Not Load.

**When a DLL changes** (an update), the hash no longer matches: QuickerPlaces does not click, clears the entry, and after the launch asks again ("DuctExporter changed since you allowed it — allow Load Once again?").

**Settings** shows only the switch and the entries already allowed, each with a Remove button. No list of every installed add-in. A read-only inventory can be added later if users ask for it.

**Not verified yet:** what the security prompt shows in each release. It is believed to show the add-in's `Name` and DLL path; record it with UI Automation in 2022–2026 before building the matching. If a release shows only the name, matching in that release falls back to name plus release, and only when exactly one manifest has that name.

Seen on Thomas's machine for Revit 2025 (2026-10-08), as test material: per-user manifests for WWTools, WWImport, DuctExporter, ModelDelta and BatchRvtAddin; all-user manifests for Autodesk add-ins, Bluebeam and BatchRvt; bundles ModelDelta and geeWiz.

## Constraints that still apply

- Never copy a central to make a local; never overwrite or delete a local; never upgrade a central through QuickerPlaces (§4.21 safety rules).
- No Autodesk assemblies in `src/QuickerPlaces.sln`. No WWTools name or dependency in public code.
- Keep shared services UI-free and linked into `qp` and the tests; the WPF layer only binds.
- Revit 2022 and later only.
- No commits unless Thomas asks; don't stage the Pi agent folder.

## Files touched in 006043f

- `src/QuickerPlaces/Services/Revit/RevitFileInfoReader.cs`, `RevitReleaseCache.cs`, `RevitLabels.cs`
- `src/QuickerPlaces/ViewModels/LibraryViewModel.cs` (row `RevitInfo`, `KindLabel`, `KindToolTip`; background lookup after each refresh)
- `src/QuickerPlaces/Views/MainWindow.xaml.cs` (shared cache), `Views/Panels/FileShelfPanel.xaml` (Type column width and tooltip)
- `src/QuickerPlaces.Cli/RevitCommands.cs`, `CliApp.cs`, `README.md`
- Tests: `RevitFileInfoReaderTests`, `RevitReleaseCacheTests`, `RevitLabelsTests`, `LibraryRevitReleaseTests`, `CliAppTests` (revit info), `Fakes/RevitTestFiles.cs`
- Projects: OpenMcdf 3.2.0 in the app, `qp` and tests; new files linked in `qp` and tests
- Docs: roadmap §4.21, root `README.md` (third-party package), `DocumentKind.cs` comment

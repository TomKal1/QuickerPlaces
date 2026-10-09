---
status: Steps 1–5 of the Revit handler plan built, reviewed and pushed; step 6 (manual verification in Revit 2022–2026 on Thomas's machine) is next
branch: feature/revit-handler-contract (pushed)
head: the commit that adds this handoff (check git log)
date: 2026-10-09
supersedes: 261008_Revit Handler Handoff.md (still the record of the design, RBP findings and Thomas's decisions)
---

# Revit handler handoff — verification

## Start here

1. `CLAUDE.md`.
2. [docs/revit-handler-protocol.md](../docs/revit-handler-protocol.md), version 1, still marked draft for Thomas's review.
3. [261008_Revit Handler Handoff.md](261008_Revit%20Handler%20Handoff.md) for the design, the dialog decisions and the Load Once list rules. Nothing in them changed.
4. This file: what is built, what was decided along the way, and what must be checked on Windows.

Everything below was built and unit-tested in a Linux container (`dotnet test` on the portable test project, the WPF app built with `-p:EnableWindowsTargeting=true`). **Nothing has run on Windows or in Revit yet.**

## What is built

| Commit | Step | What |
|---|---|---|
| 5926947 | 1 | Protocol spec, version 1 (unchanged since). |
| ac5d752 | 2 | Contract records and JSON, protocol folder with atomic writes, handler registry (live = PID + start time within 1 s), request queue (create, status, cancel, wait, expiry, clean-up). `Services/Revit/Handlers/`. |
| c309895 | 4 | Dialog detector (Win32, report only), waiting-dialog classification, per-release security-prompt signatures (ship empty), `qp revit dialogs <pid>`, read-only `.addin`/bundle manifest reader, Load Once entries, offers and the click decision. `Services/Revit/Dialogs/`, `AddIns/`. |
| 651a763 | 3 | Sample handler add-in, `samples/RevitHandler/`: R22–R26 Debug/Release, Revit-free core with 97 tests, scoped `DialogBoxShowing`, empty `DialogId` allowlist, README. |
| 24e40d8 | 2 | Installed releases and running Revits, per-release settings (schema 6), the open planner, the runner (request first, launch with `QUICKERPLACES_REVIT_REQUEST`/`_ROOT`, status, never ends Revit), `qp revit installs|handlers|open`. `Services/Revit/Opening/`. |
| e6e5b1f | 5 | Library: Open on a Revit row goes through `RevitOpenCoordinator` (status line + Cancel, "Open as new local" for centrals, dialog watching and the opt-in Load Once press). Settings → **Revit…** dialog: handler and local folder per release, Allow Load Once switch, allowed add-ins with Remove. Schema 7. |

Tests: 1905 in `src/QuickerPlaces.Tests` (1561 before this work), 97 in the sample. Zero build warnings in the app, `qp`, the tests and all ten sample configurations.

## Decisions made while building (for Thomas to confirm)

- **Copied centrals are refused.** A central whose recorded central path differs from where it is (mapped drives resolved to UNC; a local drive compared as itself) is refused with a message saying how to open it yourself. A SUBST drive is compared by its letter, so it can be refused wrongly.
- **"Handler not loaded"** is reported only for a Revit of that release that started more than 2 minutes ago without a live instance. A younger one counts as still starting.
- **Handler choice in `qp revit open`:** `--handler`, then the Settings choice, then the only registered handler that supports the action. If several qualify, it is an error listing them. The app uses only the Settings choice.
- **Waiting dialogs:** enabled, visible `#32770` windows with at least one visible button, except where every button starts with "Cancel" (progress, such as "Load Link"). This is English-only and errs towards reporting.
- **The sample's timer** lists the request folder every 2 s and raises the `ExternalEvent` only when a request is waiting. It does not raise on every tick.
- **Sample JSON** uses `DataContractJsonSerializer` (no package, so it loads safely beside other add-ins in Revit 2022–2024). It writes `/` as `\/`, which is valid JSON.
- **A Revit place still opens through Windows**, as before. See "Follow-ups".

## Step 6: manual verification (Windows, Revit 2022–2026)

Use a throwaway central, never a project model. Set `QUICKERPLACES_REVIT_ROOT` or run QuickerPlaces with `--data-root` so the real protocol folder isn't touched.

1. **Build and install the sample.** `dotnet build samples/RevitHandler/src/QuickerPlaces.RevitHandler -c "Release R25"`, then copy the DLL and `.addin` per the sample README. Answer the unsigned prompt once. Then `qp revit handlers` should list `quickerplaces.sample` for 2025, loaded.
2. **Record each release's security prompt** (needed before Load Once can ever press anything). Leave the prompt open, find Revit's PID, run `qp revit dialogs <pid> --release 2025`. From that output write `revit-security-prompts.json` next to `QuickerPlaces.exe`: `{"signatures":[{"release":2025,"verified":true,"title":"…","buttonTexts":["…","…","…"],"loadOnceButtonText":"…","namePattern":"(?<name>…)","pathPattern":null,"notes":"recorded 2026-10-.."}]}`. Also note whether the prompt shows the DLL path; if it does, set `pathPattern` with a `path` group. Do this for 2022–2026. Not verified: whether `WM_GETTEXT`/`BM_CLICK` reach task-dialog command links, and whether Revit's buttons report visible.
3. **Protocol cases** (sample handler, each release): cold start; Revit already running; two copies of one release (exactly one handles it); existing local name (gets a timestamp); expired request; central saved in another release (`releaseMismatch`); a local passed as the central (`notCentral`); a non-workshared file; missing local folder (created); handler not loaded; a second handler registered (routing by the Settings choice). `qp revit open <file>` drives these without the UI; `--dry-run` shows the plan.
4. **In-Revit dialogs:** every open writes the dialogs Revit raised into the result and the QuickerPlaces log (`Revit open of …: N dialog(s) raised: <DialogId> …`). Collect these per release. Only then add allowlist entries in `Core/DialogAllowlist.cs`, starting with `TaskDialog_Missing_Third_Party_Updater` → 1001 if observed.
5. **App UI:** Settings → Revit… lists installed releases and registered handlers ("loaded" / "not seen since …"), the local-folder default and Reset, validation, Save and the unsaved-changes prompt, tab order and screen reader. In the Library: Open on a central, local and family; "Open as new local" only on centrals; status text through a cold start; "Revit is waiting on a dialog: …" with a real dialog; Cancel; the "Allow Load Once for X next time?" question (only after a verified signature exists); a narrow window.
6. **Unchecked from the release reader** (from the earlier handoff): 2024 and 2026 workshared centrals and locals, and a central on the office share (`QP_REVIT_SAMPLES`).

## Follow-ups (not started)

- **Revit files saved as places, and in project sessions, still open through Windows' default handler**, which may pick the wrong release. Routing them through the coordinator means changing `PlaceLauncher`/`SessionLauncher` and recording the place open. That's a separate change.
- **Authenticode is not checked** for Load Once entries. Only the SHA-256 is.
- **Bundle `PackageContents.xml`** parsing follows Autodesk's documented format and has not been tried on the ModelDelta or geeWiz bundles.
- **`RevitReleaseCacheTests`**: `AfterAStalledReadFinishes_ANewOneCanStart` and `AStalledRead_TimesOut_KeepsGoing_AndIsNotStartedTwice` fail now and then under load. This predates this work (seen on the first baseline run) and is a timing test, not this feature.
- A reused PID for the Revit QuickerPlaces launched isn't detected; the request still expires after 10 minutes.

## Constraints that still apply

As in the 261008 handoff: never copy a central or overwrite a local, never upgrade, no Autodesk assemblies in `src/QuickerPlaces.sln`, no WWTools name in public code, UI-free services linked into `qp` and the tests, Revit 2022 and later only.

# Revit handler protocol, version 1

QuickerPlaces opens workshared Revit central models as new locals by asking a **Revit handler**: any Revit add-in that implements this protocol. QuickerPlaces never references the Revit API itself. Revit has no out-of-process API, so the work (`WorksharingUtils.CreateNewLocal`, then `UIApplication.OpenAndActivateDocument`) must run inside Revit.

The protocol uses plain JSON files in one folder per Windows user. There is no shared assembly, so a handler built for .NET Framework 4.8 (Revit 2022 to 2024) and one built for .NET 8 (Revit 2025 and 2026) implement it the same way.

The repository's sample handler in `samples/RevitHandler/` is the reference implementation. Where this document and the sample disagree, this document wins and the sample is a bug.

Status: version 1, draft (2026-10-08). Implemented on both sides on 2026-10-09 (QuickerPlaces in `src/QuickerPlaces/Services/Revit/Handlers/` and `Opening/`, the sample in `samples/RevitHandler/`), unit-tested without Revit; not yet run in a real Revit.

## Words used here

- **Release**: a Revit release as its four-digit year, written as a string: `"2025"`. Only `"2022"` and later are supported.
- **Handler**: one add-in implementing this protocol for one release. The same add-in loaded in 2024 and 2025 is two handlers with the same `handlerId`.
- **Instance**: one handler loaded in one running `Revit.exe`.
- **Request**: one job from QuickerPlaces, for example "open this central as a new local".
- **MUST**, **MUST NOT**, **SHOULD**, **MAY**: as in RFC 2119.

## Safety rules

A handler that breaks any of these does not implement the protocol.

1. A handler MUST NOT copy a central file to make a local. Locals are made only by `WorksharingUtils.CreateNewLocal`.
2. A handler MUST NOT overwrite, delete, rename or move an existing local, or any other existing file, outside the protocol folder.
3. A handler MUST NOT upgrade a model. It refuses a central saved in a different release than the running Revit.
4. A handler MUST NOT save, synchronise with central, relinquish, detach from central, or close a document while handling a request. The opened local stays open for the user.
5. A handler MUST NOT act on a request that has expired, or that it has not claimed.
6. A handler MUST do nothing when no request is waiting. Normal Revit use is never affected by a loaded handler.

## Folder layout

The root folder is `%LocalAppData%\QuickerPlaces\revit\`.

If the environment variable `QUICKERPLACES_REVIT_ROOT` is set in the Revit process to an absolute path, the handler MUST use that folder instead. QuickerPlaces sets it when it launches Revit with a non-default data folder (for example in tests). A handler never sets it.

```
revit\
  handlers\
    <handlerId>-<release>.json            registration: what a handler can do
  instances\
    <handlerId>-<release>-<processId>.json   a handler loaded in a running Revit
  requests\
    <release>\
      <handlerId>\
        <requestId>.json                  waiting request
        <requestId>.claimed-<processId>   claimed request, being handled
        <requestId>.result.json           result
```

Each handler reads only its own request folder, `requests\<release>\<handlerId>\`. QuickerPlaces chooses the handler per release (the user's choice in Settings), so two handlers loaded in the same Revit never compete for one request.

Readers MUST take identity from file contents, not from file names. File names exist to keep files apart; a `handlerId` may itself contain `-`.

A handler creates the folders it writes to if they do not exist.

## Files in general

- **Encoding**: UTF-8. Writers SHOULD omit a byte-order mark; readers MUST accept one.
- **JSON**: one object per file. Property names are camelCase. Readers MUST ignore properties they do not know. Optional properties may be missing or `null`.
- **Times**: UTC, ISO 8601 round-trip format with a `Z` suffix, for example `"2026-10-08T21:14:03.1234567Z"` (.NET format `"O"` on a UTC `DateTime`).
- **Paths**: absolute Windows paths, drive-letter or UNC, as strings.
- **Size**: readers MUST refuse a file larger than 64 KB.
- **Atomic writes**: a writer MUST write the full content to a temporary file in the same folder, named `.<final name>.<32 hex digits>.tmp`, then rename it to the final name. Readers MUST ignore names starting with `.` and names ending in `.tmp`. A writer that finds its own temporary file left behind (older than one hour) MAY delete it.
- **Identifiers**:
  - `handlerId`: 1 to 64 characters from `a-z`, `0-9`, `.` and `-`, starting with a letter or digit. Choose one that stays the same across versions of your add-in, for example `quickerplaces.sample` or `contoso.revittools`.
  - `requestId`: 32 lower-case hex digits (a GUID in .NET format `"N"`).

## Protocol version

Every file carries `"protocol": 1`.

- A handler writes the highest version it supports in its registration and instance files.
- QuickerPlaces sends a request at the highest version both sides support.
- Adding optional properties does not change the version. Anything a version-1 reader would misunderstand does.

## Registration

When the add-in starts (in `IExternalApplication.OnStartup` is fine, since it only writes a file), a handler writes or replaces `handlers\<handlerId>-<release>.json`:

```json
{
  "protocol": 1,
  "handlerId": "quickerplaces.sample",
  "displayName": "QuickerPlaces sample handler",
  "handlerVersion": "1.0.0",
  "revitRelease": "2025",
  "actions": ["open-new-local"],
  "writtenUtc": "2026-10-08T21:14:03.1234567Z"
}
```

| Property | Required | Meaning |
|---|---|---|
| `protocol` | yes | Highest protocol version supported. |
| `handlerId` | yes | See "Identifiers". |
| `displayName` | yes | Shown to the user in QuickerPlaces' Settings. At most 80 characters. |
| `handlerVersion` | no | The add-in's own version, for display and logs. |
| `revitRelease` | yes | The release this handler is loaded in. |
| `actions` | yes | Actions supported. Version 1 defines only `"open-new-local"`. |
| `writtenUtc` | yes | When the file was written. |

The registration file stays after Revit closes. It means "this handler has run in this release at least once". QuickerPlaces lists registrations in Settings, and says that a handler appears there only after Revit has run once with it loaded. Uninstalling a handler leaves its registration behind; QuickerPlaces shows a registration with no recent instance as "not seen since <date>".

## Instance

While it is loaded, a handler keeps `instances\<handlerId>-<release>-<processId>.json`:

```json
{
  "protocol": 1,
  "handlerId": "quickerplaces.sample",
  "revitRelease": "2025",
  "processId": 23816,
  "processStartUtc": "2026-10-08T21:13:41.5170000Z",
  "loadedUtc": "2026-10-08T21:14:03.1234567Z",
  "readyUtc": "2026-10-08T21:14:39.8800000Z"
}
```

| Property | Required | Meaning |
|---|---|---|
| `processId` | yes | `Process.GetCurrentProcess().Id` of `Revit.exe`. |
| `processStartUtc` | yes | `Process.GetCurrentProcess().StartTime`, as UTC. With `processId`, this identifies the process even after Windows reuses the ID. |
| `loadedUtc` | yes | When the handler was loaded (`OnStartup`). |
| `readyUtc` | no | When the handler can take requests: after the first `Idling` event that follows `ApplicationInitialized`. Missing means "loaded, Revit still starting". |

The handler writes the file in `OnStartup` without `readyUtc`, and rewrites it with `readyUtc` once ready. It deletes the file in `OnShutdown`.

QuickerPlaces treats an instance as **live** only when a process with that `processId` exists and its start time is within one second of `processStartUtc`. Other instance files are stale (Revit crashed or was ended); QuickerPlaces MAY delete stale instance files older than one hour.

## Requests

QuickerPlaces writes `requests\<release>\<handlerId>\<requestId>.json`:

```json
{
  "protocol": 1,
  "requestId": "4f1c2a9be0d34c7f8a61b0e5d27c9a13",
  "action": "open-new-local",
  "handlerId": "quickerplaces.sample",
  "revitRelease": "2025",
  "createdUtc": "2026-10-08T21:14:00.0000000Z",
  "expiresUtc": "2026-10-08T21:24:00.0000000Z",
  "centralPath": "\\\\server\\projects\\1234\\1234_Arch_Central.rvt",
  "localFolder": "C:\\REVIT_LOCAL2025",
  "worksets": "lastViewed"
}
```

| Property | Required | Meaning |
|---|---|---|
| `action` | yes | `"open-new-local"` in version 1. |
| `handlerId`, `revitRelease` | yes | Must match the handler and the folder. A handler refuses a request that names another handler or release. |
| `createdUtc` | yes | When QuickerPlaces wrote it. |
| `expiresUtc` | yes | After this time the request MUST NOT be acted on. QuickerPlaces sets 10 minutes after `createdUtc` by default, long enough for a cold start of Revit with a dialog or two. |
| `centralPath` | yes | The central model, as the user sees it in QuickerPlaces. The handler uses it as given and does not map drives. |
| `localFolder` | yes | Folder for the new local. QuickerPlaces' default is `C:\REVIT_LOCAL<release>`, changeable per release. |
| `worksets` | yes | `"lastViewed"`, `"all"` or `"none"`; see "Worksets". |

QuickerPlaces writes a request before it launches Revit, so a starting handler finds it.

**Cancelling.** Before a request is claimed, QuickerPlaces cancels it by deleting `<requestId>.json`. If the delete fails because the file is gone, it was claimed in the meantime. A claimed request cannot be cancelled; QuickerPlaces stops waiting and leaves the result for clean-up.

## Handling a request

### When to look

- **Revit already running:** the handler watches its request folder with a `FileSystemWatcher` and, as a backup for missed events, checks it every 2 seconds. When it sees a `<requestId>.json`, it raises an `ExternalEvent`.
- **Cold start:** the handler checks the folder on the first `Idling` event after `ApplicationInitialized`, and from then on as above.
- **Launched by QuickerPlaces:** QuickerPlaces sets `QUICKERPLACES_REVIT_REQUEST=<requestId>` in the environment of a `Revit.exe` it launches. The handler treats it as a hint to check its folder as soon as it is ready. It still handles requests only from the folder, and only as described here. If the named request is not in its folder, the handler logs that and does nothing else.

The handler MUST NOT do any work in `OnStartup`, from the watcher's thread, or from a timer thread. Revit API calls happen only in `IExternalEventHandler.Execute` (or in the `Idling` handler on a cold start).

### Steps, inside `Execute`

1. **Claim.** Rename `<requestId>.json` to `<requestId>.claimed-<processId>`, without overwriting. If the rename fails because the source is gone, another instance claimed it or QuickerPlaces cancelled it: stop, with no result. Claiming inside `Execute`, not in the watcher, means a claimed request is one Revit is actually working on. With two copies of one release running, exactly one rename succeeds.
2. **Check the request.** Read the claimed file. Refuse with a result (see "Error codes") if: the file cannot be read or is not valid JSON; `protocol` is higher than the handler supports; `action` is unknown; `handlerId` or `revitRelease` is not this handler's; the time now is after `expiresUtc`; `centralPath` is not an absolute path to an existing `.rvt` file; `worksets` is unknown.
3. **Check the central.** Call `BasicFileInfo.Extract(centralPath)`. Refuse if the file is not workshared, is a local (`IsLocal`), or is not central (`IsCentral` false), or if its saved release (`BasicFileInfo.Format`) differs from the running Revit's (`Application.VersionNumber`).
4. **Choose the local path.** `<localFolder>\<central file name without extension>_<Application.Username>.rvt`. Characters not valid in a file name are replaced by `_`. If that file exists, insert `_<yyyyMMdd-HHmmss>` (local time) before `.rvt`; if that exists too, add `-2`, `-3`, and so on. Never overwrite. Create `localFolder` if it does not exist; refuse if it cannot be created.
5. **Make and open the local.** `WorksharingUtils.CreateNewLocal(ModelPathUtils.ConvertUserVisiblePathToModelPath(centralPath), localModelPath)`, then `UIApplication.OpenAndActivateDocument(localModelPath, openOptions, false)` with the worksets option. Dialog handling (below) is active for this step only.
6. **Write the result**, then delete the claimed file.

Steps 1, 2 and 4, and writing the result, use no Revit types. A handler SHOULD keep them in code that can be unit-tested without Revit, as the sample does.

### Worksets

| `worksets` | `OpenOptions.SetOpenWorksetsConfiguration(new WorksetConfiguration(...))` |
|---|---|
| `"lastViewed"` | `WorksetConfigurationOption.OpenLastViewed` |
| `"all"` | `WorksetConfigurationOption.OpenAllWorksets` |
| `"none"` | `WorksetConfigurationOption.CloseAllWorksets` |

## Results

The handler writes `<requestId>.result.json` (atomically) next to the claimed file:

```json
{
  "protocol": 1,
  "requestId": "4f1c2a9be0d34c7f8a61b0e5d27c9a13",
  "handlerId": "quickerplaces.sample",
  "revitRelease": "2025",
  "processId": 23816,
  "finishedUtc": "2026-10-08T21:15:12.0000000Z",
  "ok": true,
  "localPath": "C:\\REVIT_LOCAL2025\\1234_Arch_Central_tkal.rvt",
  "errorCode": null,
  "message": null,
  "dialogs": [
    {
      "dialogId": "TaskDialog_Missing_Third_Party_Updater",
      "kind": "taskDialog",
      "message": "The model uses a third-party updater that is not installed...",
      "answered": true,
      "answer": 1001
    }
  ]
}
```

| Property | Required | Meaning |
|---|---|---|
| `ok` | yes | `true` only when the local was created and opened. |
| `localPath` | when ok | The new local. MAY also be set when `ok` is false, if the local was created but not opened (`openFailed`), so the user can open it. |
| `errorCode` | when not ok | See below. |
| `message` | when not ok | Readable, for the user, in the handler's language. At most 1000 characters. |
| `dialogs` | yes | Every dialog Revit raised during step 5, in order; empty if none. See "Dialogs inside Revit". |

QuickerPlaces reads the result, shows it, and deletes the result and any claimed file. QuickerPlaces deletes results and claimed files older than one day that it is no longer waiting for.

### Error codes

| `errorCode` | When |
|---|---|
| `invalidRequest` | Step 2: unreadable, not valid JSON, missing or malformed property, or another handler's or release's request. |
| `unsupportedProtocol` | `protocol` higher than the handler supports. |
| `unsupportedAction` | Unknown `action`. |
| `expired` | Time now is after `expiresUtc`. |
| `centralNotFound` | `centralPath` does not exist or cannot be reached. |
| `notCentral` | Not workshared, or a local, or not central. |
| `releaseMismatch` | Saved in a different release from the running Revit. `message` names both. |
| `localFolderUnavailable` | `localFolder` cannot be created or written. |
| `createLocalFailed` | `CreateNewLocal` threw. `message` has Revit's message. |
| `openFailed` | `OpenAndActivateDocument` threw, or the user cancelled a dialog that stopped the open. `localPath` is set. |
| `internalError` | Anything else. |

QuickerPlaces MUST treat an unknown `errorCode` like `internalError` and show `message`.

## Dialogs inside Revit

During step 5, and only then, the handler subscribes to `UIApplication.DialogBoxShowing` and unsubscribes in a `finally` block. Normal Revit use is never affected.

For every dialog raised, the handler adds an entry to `dialogs`:

| Property | Meaning |
|---|---|
| `dialogId` | `DialogBoxShowingEventArgs.DialogId`, or `null` if Revit gives none. |
| `kind` | `"taskDialog"`, `"messageBox"` or `"dialogBox"`, from the event-args type. |
| `message` | `TaskDialogShowingEventArgs.Message` or `MessageBoxShowingEventArgs.Message`, at most 500 characters; `null` for other kinds. |
| `answered` | Whether the handler called `OverrideResult`. |
| `answer` | The value passed to `OverrideResult`, or `null`. |

Rules for answering:

- A handler answers a dialog only when its `DialogId` is on the handler's own **allowlist**, with the answer recorded there. Anything else is left for the user: the handler does not call `OverrideResult`, and Revit shows the dialog.
- An allowlist entry MUST be based on observing that dialog in a real open in that release, not on guessing. The `dialogs` list in results is how evidence is collected.
- No allowlist entry may answer with a choice that saves, synchronises, relinquishes, detaches, upgrades, deletes or closes.
- Matching is by `DialogId` only, never by message text, because the text depends on Revit's language.

The sample handler's allowlist starts empty. The first candidate, to be confirmed by observation in each release, is `TaskDialog_Missing_Third_Party_Updater` answered with `1001` ("Continue working with the file").

Warnings and errors raised as failures during the open are left to Revit's normal handling. A handler MUST NOT delete warnings or resolve failures (for example by detaching or skipping elements) on the user's behalf.

## What QuickerPlaces does around this

These are QuickerPlaces' responsibilities. A handler does not need them, but handler authors should know what the user sees.

- **Choosing a handler.** Per release, the user picks one registered handler that lists the action, or none.
- **Launching.** QuickerPlaces writes the request first. If no live instance of the chosen handler exists for the release, it launches that release's `Revit.exe` itself, with `QUICKERPLACES_REVIT_REQUEST` (and `QUICKERPLACES_REVIT_ROOT` when needed) set.
- **"Handler not loaded".** If a `Revit.exe` of the release is running but has no live instance of the chosen handler, QuickerPlaces says so at once instead of waiting.
- **Status while waiting.** "Waiting for Revit 2025 to start" (no instance yet); "Revit 2025 is busy" (instance live and ready, request not claimed after 10 seconds); "Opening in Revit 2025" (claimed); "Revit is waiting on a dialog: <title>" (see next point).
- **Dialogs outside an add-in's reach.** While Revit starts, and for a `Revit.exe` QuickerPlaces launched, QuickerPlaces looks for enabled top-level dialog windows (class `#32770`) of that process and reports their title and buttons. Progress windows with no buttons are not reported as waiting. It does not click anything, with one exception the user must turn on: pressing **Load Once** on an add-in security prompt for an add-in the user has allowed beforehand. That exception is QuickerPlaces' own behaviour, not part of this protocol; see the QuickerPlaces roadmap §4.21.
- **Timeout.** When `expiresUtc` passes without a claim, QuickerPlaces deletes the request and tells the user. It never ends a Revit process.

## Testing a handler

- Use a throwaway central, never a project model.
- Set `QUICKERPLACES_REVIT_ROOT` to a scratch folder for the Revit process, so tests never touch the real protocol folder.
- Cases to check in each release: cold start; Revit already running; two copies of one release (exactly one handles the request); a local name that already exists; an expired request; a central saved in another release; a local passed as `centralPath`; a non-workshared file; a missing `localFolder`; a request naming another handler.

## Changes from the roadmap design

The roadmap (§4.21, 2026-10-08) sketched this protocol. Version 1 differs in three places, each to fix a case the sketch missed:

- Instance files include the `handlerId` (`<handlerId>-<release>-<processId>.json`), because two handlers loaded in one Revit would otherwise write the same file.
- Requests go in a folder per handler (`requests\<release>\<handlerId>\`), because two handlers loaded in one Revit would otherwise both try to claim every request.
- Requests carry `expiresUtc` set by QuickerPlaces instead of a fixed "few minutes", and the handler claims an expired request and answers `expired` rather than leaving it, so QuickerPlaces learns why.

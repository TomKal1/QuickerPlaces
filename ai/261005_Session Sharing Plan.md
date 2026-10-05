---
title: QuickerPlaces — Sharing a saved session with another user
status: implemented on branch ccr-f4ba4678-mo9ok7 (2026-10-05); the solution builds with 0 warnings and 1,394 tests pass on Linux; NOT yet run on Windows. The OneDrive registry reading (§4) and the §9 checklist are open
created: 2026-10-05
parent: ai/260928_PDF Project Sessions Plan.md
---

# Sharing a saved session with another user

## 1. The request

On 2026-10-05 the user asked for *"a way for a user to share their saved sessions with another QuickerPlaces user. It will need to be able to handle OneDrive files that the other user might not have synced or if it's a file in their personal OneDrive."*

A session is a name, tags and a list of full paths (`ProjectSession`). The paths are the problem: `C:\Users\alice\Contoso\Tower B - Documents\A-101.pdf` means nothing on Bob's PC, where the same SharePoint library syncs to `D:\Contoso\Tower B - Documents`, or isn't synced at all.

## 2. Decisions

| # | Decision | Why |
|---|---|---|
| S1 | **A file, not a service.** A session is shared as a `.qpsession` file the user sends by email, Teams or a shared folder. | No server, account or network code. It fits how places export already works. |
| S2 | **Each file carries every way to reach it**: the sender's path, its **web address** when it is in a synced OneDrive or SharePoint folder, and its **network path** when it is on a share or mapped drive. | The web address is the one thing that is the same on every PC, so the recipient's PC can turn it back into its own synced folder. |
| S3 | **Personal OneDrive files are included but flagged**, not left out. | The sender may well have shared them. The dialog says others need access. |
| S4 | **Files only on the sender's PC start unticked**, with advice to move them to a shared library. | No one else can reach them. Leaving them out silently would confuse people, so they are shown. |
| S5 | **A session still holds only local paths.** A file the recipient can't find can't be saved in the session until it is found (Check again, Locate, Check network files). An online-only file can be opened in the browser. | The session model, the launcher and the Library are unchanged. |
| S6 | **No server named in a shared file is contacted until the user asks** (Check network files). | Looking up `\\server\share` can send the user's Windows sign-in (NTLM) to that server. A `.qpsession` comes from someone else, so a hostile file must not be able to cause this just by being opened. Local paths and synced-library paths are checked without asking: they are on the recipient's own disk. |
| S7 | **Shared files are treated as untrusted input**: https-only addresses with no user name or password; no `..`, `\`, `/` or `:` hidden in an address segment (so an address can't map outside the synced folder); a size cap of 4 MB; at most 1,000 files; names and tags cleaned by the store's rules. | It comes from outside. |
| S8 | **The sender's history isn't shared**: no saved, updated or reopened times, only when the file was made. | Privacy. The dialog says the paths can show the sender's Windows user name. |
| S9 | **One Locate finds the rest.** Pointing at one file works out a folder swap (`C:\Users\alice` → `D:\Work`) and tries it on every file not yet found. | A copy of a whole job folder is the common case for files not in a library. |

## 3. The `.qpsession` file

```json
{
  "format": "quickerplaces-session",
  "schemaVersion": 1,
  "sharedAt": "2026-10-05T01:02:03+00:00",
  "name": "Tower B",
  "tags": [ "markups" ],
  "files": [
    { "path": "C:\\Users\\alice\\Contoso\\Tower B - Documents\\A-101.pdf",
      "location": "cloudLibrary",
      "url": "https://contoso.sharepoint.com/sites/TowerB/Shared%20Documents/A-101.pdf" },
    { "path": "Z:\\Tower B\\Spec.docx", "location": "network", "networkPath": "\\\\files\\projects\\Tower B\\Spec.docx" },
    { "path": "C:\\Users\\alice\\OneDrive - Contoso\\Notes.docx", "location": "personalCloud",
      "url": "https://contoso-my.sharepoint.com/personal/alice_contoso_com/Documents/Notes.docx" }
  ]
}
```

`location` is one of `thisPc`, `network`, `cloudLibrary` or `personalCloud`. `SharedSessionFormat` writes and reads it (`Services/Sessions/SharedSessionFormat.cs`, model in `Models/Sessions/SharedSessionDocument.cs`). Reading refuses another format, a newer version, or a file with no usable files. Otherwise it drops what it can't use and keeps the rest.

## 4. OneDrive and SharePoint paths

`CloudPaths` (pure, tested) maps a path inside a synced folder to its web address and back. It uses the longest match, so a shortcut added with "Add shortcut to My files" wins over the OneDrive folder around it. Addresses are compared in one spelling: https, lower-case host, each segment escaped the same way, no trailing slash or query.

`WindowsCloudSyncRoots` (app-only) gets the synced folders from the registry, under `HKEY_CURRENT_USER`:

- `Software\SyncEngines\Providers\OneDrive\*`: `MountPoint`, `UrlNamespace` and `LibraryType` (`teamsite`, `mysite` or `personal`).
- `Software\Microsoft\OneDrive\Accounts\*`: the fallback for each account's own OneDrive folder. For Business accounts it uses `UserFolder` plus `ServiceEndpointUri` minus `/_api`, plus `/Documents`. For a personal account it uses `UserFolder` plus `cid`.

**Neither key is a documented API.** Both come from how the OneDrive client is known to behave, and this has not been checked on a real PC (§9 items 1–3). In particular, check:

- whether `UrlNamespace` for a library synced from a *subfolder* points at the library or at that folder;
- the exact `LibraryType` values.

If either is off, the only effect is that the recipient sees "Library not synced here" and uses Locate or Open online. Nothing wrong is saved, because every mapped path is checked on disk first.

## 5. Sharing (sender)

**Share…** is on the Sessions panel's buttons and on the card menu. It opens `ShareSessionDialog` over `ShareSessionViewModel`. Each file shows where it lives (SharePoint or Teams / Personal OneDrive / Network share / This PC only) and has a tick. The dialog gives advice for files only on this PC and for personal files, and says what the file reveals. **Save shared file…** writes the file through a temp file. Mapped drives are resolved to UNC paths with the existing `NetworkDriveResolver`.

## 6. Opening a shared session (recipient)

**Open shared…** sits beside **Save files** (both move into the frame's header in the workspace). Dropping a `.qpsession` on the session list also works. It opens `ImportSharedSessionDialog` over `ImportSharedSessionViewModel`. `SessionSharing.Resolve` looks for each file in this order:

1. The same path, on a local or mapped drive.
2. This PC's synced copy of the library.
3. The network path, only after **Check network files**. That check runs off the UI thread.
4. Otherwise the file is *online only* (it has an address) or *missing*.

Rows that were found are ticked. Right-click offers **Open online** and **Open folder online (to Sync it)**. **Check again** reads the registry again and retries. **Locate…** applies S9. **Save session** calls `SessionStore.TryCreate`. If the name is taken, the dialog suggests "Name (shared)", then "(shared 2)", and so on.

## 7. Files

| File | |
|---|---|
| `Models/Sessions/SharedSessionDocument.cs` | The file's model |
| `Services/Sessions/CloudPaths.cs` | Path ↔ address, `ICloudSyncRoots`, `CloudSyncRoot` |
| `Services/Sessions/SharedSessionFormat.cs` | Writing and checked reading |
| `Services/Sessions/SessionSharing.cs` | Describing (sender), resolving (recipient), folder swap |
| `Services/Sessions/WindowsCloudSyncRoots.cs` | Registry reader (app-only) |
| `ViewModels/ShareSessionViewModel.cs`, `ViewModels/ImportSharedSessionViewModel.cs` | The dialogs' logic |
| `Views/ShareSessionDialog.xaml`, `Views/ImportSharedSessionDialog.xaml` | The dialogs |
| `Views/Panels/SessionsPanel.xaml(.cs)` | Share…, Open shared…, dropping a `.qpsession` |
| Tests | `CloudPathsTests`, `SharedSessionFormatTests`, `SessionSharingTests`, `SharedSessionViewModelTests` (91 tests) |

No store schema changed. `sessions.json` stays at version 1.

## 8. Not done (possible follow-ups)

- **Double-clicking a `.qpsession` in Explorer.** This needs a file-type registration and a command-line argument for the app.
- **`qp session export/import`** in the CLI.
- **Share from the File viewer's Sessions tab.** Share is on the panel and the card menu only.
- **Remembered path rules.** For example, "Alice's `C:\Users\alice\Jobs` is my `J:\`", kept for the next shared session from the same person.
- **Starting OneDrive sync directly** (`odopen://`). That needs site, web and list ids the address doesn't carry, so the user clicks Sync in SharePoint instead.

## 9. Windows checklist

1. On a PC that syncs a Teams library, share a session with a file in it. The row says **SharePoint or Teams**, and its tooltip shows a `https://…sharepoint.com/…` address that opens the file in a browser.
2. On a second PC (or user) that syncs the same library to a **different folder**, open the file. The row says **Found in your synced library** with that PC's path, and **Save session** then **Open all** opens it.
3. Repeat 1 with a file in a **shortcut** ("Add shortcut to My files") and in a library synced from a **subfolder**. Record what `HKCU\Software\SyncEngines\Providers\OneDrive\*` holds for each (MountPoint, UrlNamespace, LibraryType).
4. On a PC that **doesn't sync** the library: **Library not synced here**. **Open online** and **Open folder online** open the browser. After syncing, **Check again** finds the file.
5. A file in the sender's **personal OneDrive**: the row says **Personal OneDrive**. On the recipient's PC, without access: **Open online** shows SharePoint's access page. With a shortcut to the shared folder: found.
6. A file on a **mapped drive**: the shared file carries the UNC path. The recipient sees **not checked yet** until **Check network files**, and the dialog stays responsive while a server that doesn't answer is checked.
7. **Locate** on one file of a copied job folder finds the others.
8. Drag a `.qpsession` onto the session list: it opens. Drag a `.pdf`: nothing happens.
9. In the workspace, **Open shared…** and **Save files** both sit in the frame's header and fit.
10. Keyboard only through both dialogs. Alt+H opens a shared session, Alt+A shares the selected one, and Space ticks a row.

Record each item as passed, failed or untested in `BUILD_SUMMARY.md`.

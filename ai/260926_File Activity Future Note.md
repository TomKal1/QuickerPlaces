# Future option: recent document activity

The current tracker observes the foreground File Explorer window and credits its folder path. It cannot infer which PDF, Word document, or Excel workbook is open inside another application. File modification time is also not an open event.

## Practical first version

Add a separate, opt-in **Recent files** view based on Windows Recent Items. Windows and applications can register opened documents through `SHAddToRecentDocs`; the Shell also does this for some Explorer and common file-dialog opens. Resolve Recent Items shortcuts to paths, filter `.pdf`, `.doc`, `.docx`, `.xls`, `.xlsx`, and `.xlsm`, and show the file name, folder, type, and observed recency. Keep file history separate from folder dwell time. Deduplicate by canonical path, respect the existing root allowlist, and let users disable or clear file history.

This source is best effort: an application can omit a file, suppress recent-document registration, or expose a cloud document without a local path. It should be labeled **recently observed**, not a complete open history or time spent in a file.

## If accuracy needs to improve

Investigate app-specific adapters for Word and Excel open/activation events, plus a PDF-reader-specific approach. Those adapters would need separate lifecycle, privacy, and performance testing. Do not infer file opens from `FileSystemWatcher` changes, because viewing a file need not change it.

Microsoft references: [SHAddToRecentDocs](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shaddtorecentdocs), [Word application events](https://learn.microsoft.com/en-us/office/vba/api/word.application), [Excel WorkbookOpen event](https://learn.microsoft.com/en-us/office/vba/api/excel.application.workbookopen).

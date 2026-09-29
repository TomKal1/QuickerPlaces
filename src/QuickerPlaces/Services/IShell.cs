namespace QuickerPlaces.Services;

/// <summary>
/// The shell operations a launch needs, behind a seam so PlaceLauncher can
/// be tested without starting real processes (Phase 3 D23). WindowsShell is
/// the production implementation. Project sessions added FileExists, which
/// Phase 4's file places will use too;
/// Phase 5 adds opening a file with a chosen application.
///
/// UI-free and linked into the test project.
/// </summary>
public interface IShell
{
    bool DirectoryExists(string path);

    /// <summary>True when <paramref name="path"/> is an existing file. Project sessions check each PDF before opening it.</summary>
    bool FileExists(string path);

    /// <summary>Asks Windows to open <paramref name="target"/> with its default handler. Throws if Windows refuses it.</summary>
    void Open(string target);
}

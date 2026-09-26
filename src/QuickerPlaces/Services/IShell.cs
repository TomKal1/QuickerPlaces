namespace QuickerPlaces.Services;

/// <summary>
/// The shell operations a launch needs, behind a seam so PlaceLauncher can
/// be tested without starting real processes (Phase 3 D23). WindowsShell is
/// the production implementation. Phase 4 adds FileExists for file places;
/// Phase 5 adds opening a file with a chosen application.
///
/// UI-free and linked into the test project.
/// </summary>
public interface IShell
{
    bool DirectoryExists(string path);

    /// <summary>Asks Windows to open <paramref name="target"/> with its default handler. Throws if Windows refuses it.</summary>
    void Open(string target);
}

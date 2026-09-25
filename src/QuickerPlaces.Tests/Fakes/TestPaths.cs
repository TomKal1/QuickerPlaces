using System.IO;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>
/// Folder paths that PlacesService accepts on whichever OS runs the tests.
/// A literal like @"C:\Docs" is fully qualified on Windows but not on
/// Linux, where PlacesService's Path.IsPathFullyQualified check would
/// reject it — and the suite is meant to run on both (see the csproj).
/// The folder is never created: validation doesn't require it to exist.
/// </summary>
public static class TestPaths
{
    public static string Folder(string name) => Path.Combine(Path.GetTempPath(), "QuickerPlacesTests", name);
}

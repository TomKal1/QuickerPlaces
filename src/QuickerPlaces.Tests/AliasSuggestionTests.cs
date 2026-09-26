using QuickerPlaces.Services;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The alias the Add Folder dialog suggests from a folder path: its deepest
/// folder name. Windows paths are written out literally, since the suite also
/// runs on Linux, where a backslash is not a path separator.
/// </summary>
public sealed class AliasSuggestionTests
{
    [Theory]
    [InlineData(@"C:\Users\Thomas\Downloads\UFGS_M", "UFGS_M")]
    [InlineData(@"C:\Users\Thomas\Pictures\", "Pictures")]
    [InlineData(@"C:\Projects\Acme\\", "Acme")]
    [InlineData(@"\\server\share\Jobs\2026", "2026")]
    [InlineData(@"\\server\share", "share")]
    [InlineData(@"D:/Mixed/Separators", "Separators")]
    [InlineData(@"  C:\Padded\Name  ", "Name")]
    [InlineData("\"C:\\Quoted From Explorer\\Copy as path\"", "Copy as path")]
    [InlineData(@"C:\Folder With.Dots", "Folder With.Dots")]
    [InlineData(@"C:\", "C:")]
    [InlineData(@"relative", "relative")]
    public void SuggestsTheDeepestFolderName(string path, string expected)
        => Assert.Equal(expected, AliasSuggestion.FromFolderPath(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"\\")]
    [InlineData("\"\"")]
    public void SuggestsNothing_WhenThereIsNoName(string? path)
        => Assert.Null(AliasSuggestion.FromFolderPath(path));
}

using System;
using System.IO;
using System.Runtime.CompilerServices;
using QuickerPlaces.Services;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>
/// Points DiagnosticLog at a throwaway folder for the whole test run,
/// before any test starts. PlacesService, FilePlacesStorage and
/// SingleInstance all log as they work, so without this every test run
/// appended to the developer's real %LocalAppData% log — breaking the
/// "tests never touch real AppData" rule. Tests that need their own log
/// folder (DiagnosticLogTests) switch to it and then back to
/// <see cref="Path"/>, never to the real location.
/// </summary>
public static class TestLogDirectory
{
    public static string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "QuickerPlacesTests-log-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void RedirectLog() => DiagnosticLog.UseDirectoryForTests(Path);
}

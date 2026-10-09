namespace QuickerPlaces.Services.Revit;

/// <summary>
/// How a Revit file's release is shown in a list: a short label for the
/// Type column ("Revit 2025") and a sentence for its tooltip. UI-free and
/// linked into the test project.
/// </summary>
public static class RevitLabels
{
    /// <summary>"Revit 2025"; "Revit 2021 (old)" for an unsupported release; "Revit ?" when it couldn't be read; plain "Revit" until it has been.</summary>
    public static string Kind(RevitFileInfo? info) => info switch
    {
        null => "Revit",
        { Problem: RevitFileProblem.None, Release: { } release } => $"Revit {release}",
        { Problem: RevitFileProblem.TooOld, Release: { } release } => $"Revit {release} (old)",
        { Problem: RevitFileProblem.TooOld } => "Revit (old)",
        { Problem: RevitFileProblem.NotFound } => "Revit",
        _ => "Revit ?",
    };

    /// <summary>The release and worksharing state in words, or why they aren't known; "" until the file has been read.</summary>
    public static string ToolTip(RevitFileInfo? info)
    {
        if (info is null)
            return "";

        return info.Problem switch
        {
            RevitFileProblem.None => $"Saved in Revit {info.Release}. {Worksharing(info)}",
            RevitFileProblem.TooOld => info.Release is { } release
                ? $"Saved in Revit {release}. QuickerPlaces supports Revit {RevitFileInfoReader.OldestSupportedRelease} and later."
                : $"Saved before Revit 2019. QuickerPlaces supports Revit {RevitFileInfoReader.OldestSupportedRelease} and later.",
            RevitFileProblem.NotFound => "The file isn't there.",
            RevitFileProblem.InUse => "Another program has the file locked, so its Revit release can't be read.",
            RevitFileProblem.Unreadable => "The file couldn't be read, so its Revit release isn't known.",
            RevitFileProblem.TimedOut => "Reading the file took too long (a slow or offline network share?). It will be tried again.",
            RevitFileProblem.NotRevitFile => "This doesn't look like a Revit file.",
            _ => "The file doesn't say which Revit release saved it.",
        };
    }

    private static string Worksharing(RevitFileInfo info) => info.Worksharing switch
    {
        RevitWorksharing.Central => "Central model.",
        RevitWorksharing.Local => info.CentralModelPath is { Length: > 0 } central ? $"Local copy of {central}" : "Local copy.",
        RevitWorksharing.NotWorkshared => "Not workshared.",
        _ => "Workshared.",
    };
}

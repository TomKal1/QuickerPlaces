using QuickerPlaces.Services.Revit;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>What the Type column and its tooltip say for a Revit file.</summary>
public sealed class RevitLabelsTests
{
    private static RevitFileInfo Info(int? release, RevitFileProblem problem, RevitWorksharing worksharing = RevitWorksharing.NotWorkshared, string? central = null) =>
        new(release, "b", worksharing, central, null, 14, problem);

    [Fact]
    public void NotReadYet_IsPlainRevit_WithNoTooltip()
    {
        Assert.Equal("Revit", RevitLabels.Kind(null));
        Assert.Equal("", RevitLabels.ToolTip(null));
    }

    [Fact]
    public void Supported_ShowsTheRelease()
    {
        Assert.Equal("Revit 2025", RevitLabels.Kind(Info(2025, RevitFileProblem.None)));
        Assert.Equal("Saved in Revit 2025. Not workshared.", RevitLabels.ToolTip(Info(2025, RevitFileProblem.None)));
    }

    [Fact]
    public void Tooltip_NamesCentralsAndLocals()
    {
        Assert.Equal("Saved in Revit 2024. Central model.", RevitLabels.ToolTip(Info(2024, RevitFileProblem.None, RevitWorksharing.Central, @"\\s\p\T.rvt")));
        Assert.Equal(@"Saved in Revit 2024. Local copy of \\s\p\T.rvt", RevitLabels.ToolTip(Info(2024, RevitFileProblem.None, RevitWorksharing.Local, @"\\s\p\T.rvt")));
    }

    [Fact]
    public void TooOld_IsMarkedOld()
    {
        Assert.Equal("Revit 2021 (old)", RevitLabels.Kind(Info(2021, RevitFileProblem.TooOld)));
        Assert.Contains("supports Revit 2022 and later", RevitLabels.ToolTip(Info(2021, RevitFileProblem.TooOld)));
        Assert.Equal("Revit (old)", RevitLabels.Kind(Info(null, RevitFileProblem.TooOld)));
        Assert.StartsWith("Saved before Revit 2019.", RevitLabels.ToolTip(Info(null, RevitFileProblem.TooOld)));
    }

    [Theory]
    [InlineData(RevitFileProblem.InUse)]
    [InlineData(RevitFileProblem.Unreadable)]
    [InlineData(RevitFileProblem.TimedOut)]
    [InlineData(RevitFileProblem.NotRevitFile)]
    [InlineData(RevitFileProblem.UnrecognisedLayout)]
    [InlineData(RevitFileProblem.ReleaseNotRecorded)]
    public void Unknown_IsAQuestionMark_WithAReason(RevitFileProblem problem)
    {
        Assert.Equal("Revit ?", RevitLabels.Kind(RevitFileInfo.Failed(problem)));
        Assert.NotEqual("", RevitLabels.ToolTip(RevitFileInfo.Failed(problem)));
    }

    [Fact]
    public void Missing_IsPlainRevit()
    {
        Assert.Equal("Revit", RevitLabels.Kind(RevitFileInfo.Failed(RevitFileProblem.NotFound)));
    }
}

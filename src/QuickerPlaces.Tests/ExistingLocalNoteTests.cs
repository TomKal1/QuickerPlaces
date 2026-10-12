using System;
using System.Collections.Generic;
using System.IO;
using QuickerPlaces.Services.Revit.Handlers;
using QuickerPlaces.Services.Revit.Opening;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The line shown with the progress while a central opens: an existing local
/// will be replaced (or kept with its date), and where that is changed.
/// </summary>
public sealed class ExistingLocalNoteTests
{
    private const string Folder = @"C:\REVIT_LOCAL2025";

    private static RevitOpenPlan Plan(string? existingLocal) => new()
    {
        Kind = RevitOpenKind.HandlerRequest, FilePath = @"\\server\p\Tower_Central.rvt", Release = "2025",
        LocalFolder = Folder, ExistingLocal = existingLocal,
    };

    private static Func<string, IEnumerable<string>> Files(params string[] names) =>
        folder => Array.ConvertAll(names, n => Path.Combine(folder, n));

    [Fact]
    public void Overwrite_WithALocalThere_SaysItWillBeReplaced_AndWhereToChangeThat()
    {
        var note = ExistingLocalNote.For(Plan(HandlerProtocol.ExistingLocal.Overwrite), Files("Tower_Central_me.rvt", "Other_me.rvt"));

        Assert.Equal("Your existing local Tower_Central_me.rvt will be replaced with a new copy. To keep old locals instead, change \"When a local already exists\" in Settings › Revit.", note);
    }

    [Fact]
    public void AppendTimestamp_WithALocalThere_SaysItWillBeKept()
    {
        var note = ExistingLocalNote.For(Plan(HandlerProtocol.ExistingLocal.AppendTimestamp), Files("Tower_Central_me.rvt"));

        Assert.Equal("Your existing local Tower_Central_me.rvt will be kept, renamed with its date. To replace it instead, change \"When a local already exists\" in Settings › Revit.", note);
    }

    [Fact]
    public void OnlyDatedOldLocals_OrOtherModels_SayNothing()
    {
        var files = Files("Tower_Central_me_11Oct2026_233201.rvt", "Tower_Central_me_20261011-171534.rvt", "Tower_Central_me_20261011-171534-2.rvt", "Site_me.rvt");

        Assert.Null(ExistingLocalNote.For(Plan(HandlerProtocol.ExistingLocal.Overwrite), files));
    }

    [Fact]
    public void NoChoiceSent_OrNotAHandlerOpen_SaysNothing()
    {
        Assert.Null(ExistingLocalNote.For(Plan(null), Files("Tower_Central_me.rvt")));
        Assert.Null(ExistingLocalNote.For(Plan(HandlerProtocol.ExistingLocal.Overwrite) with { Kind = RevitOpenKind.DirectOpen }, Files("Tower_Central_me.rvt")));
    }

    [Fact]
    public void AFolderThatCantBeListed_SaysNothing()
    {
        Assert.Null(ExistingLocalNote.For(Plan(HandlerProtocol.ExistingLocal.Overwrite), _ => throw new DirectoryNotFoundException()));
    }
}

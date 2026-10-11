using System;
using System.IO;
using System.Linq;
using QuickerPlaces.Models.Sessions;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Session sharing plan §3: the .qpsession file. It comes from someone else,
/// so reading checks everything and drops what can't be used.
/// </summary>
public sealed class SharedSessionFormatTests
{
    private static SharedSessionDocument Sample() => new()
    {
        SharedAt = new DateTimeOffset(2026, 10, 5, 1, 2, 3, TimeSpan.Zero),
        Name = "Tower B",
        Tags = { "markups", "RFI 12" },
        Files =
        {
            new SharedSessionFile
            {
                Path = @"C:\Users\alice\Contoso\Tower B - Documents\A-101.pdf",
                Location = SharedFileLocation.CloudLibrary,
                Url = "https://contoso.sharepoint.com/sites/TowerB/Shared%20Documents/A-101.pdf",
            },
            new SharedSessionFile
            {
                Path = @"Z:\Tower B\Spec.docx",
                Location = SharedFileLocation.Network,
                NetworkPath = @"\\files\projects\Tower B\Spec.docx",
            },
            new SharedSessionFile { Path = @"C:\Users\alice\Desktop\Notes.xlsx", Location = SharedFileLocation.ThisPc },
        },
    };

    [Fact]
    public void RoundTrip_KeepsEverything()
    {
        var json = SharedSessionFormat.Serialize(Sample());
        var read = SharedSessionFormat.Parse(json);

        Assert.Null(read.ErrorMessage);
        var document = read.Document!;
        Assert.Equal("Tower B", document.Name);
        Assert.Equal(new[] { "markups", "RFI 12" }, document.Tags);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 1, 2, 3, TimeSpan.Zero), document.SharedAt);
        Assert.Equal(3, document.Files.Count);
        Assert.Equal(SharedFileLocation.CloudLibrary, document.Files[0].Location);
        Assert.Equal("https://contoso.sharepoint.com/sites/TowerB/Shared%20Documents/A-101.pdf", document.Files[0].Url);
        Assert.Equal(@"\\files\projects\Tower B\Spec.docx", document.Files[1].NetworkPath);
        Assert.Equal(SharedFileLocation.ThisPc, document.Files[2].Location);
    }

    [Fact]
    public void Serialize_WritesTheFormatNameVersionAndReadableNames()
    {
        var json = SharedSessionFormat.Serialize(Sample());

        Assert.Contains("\"format\": \"quickerplaces-session\"", json);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"location\": \"cloudLibrary\"", json);
        Assert.DoesNotContain("\"url\": null", json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"format\":\"something-else\",\"schemaVersion\":1,\"files\":[{\"path\":\"C:\\\\a.pdf\"}]}")]
    [InlineData("{\"format\":\"quickerplaces-session\",\"schemaVersion\":0,\"files\":[{\"path\":\"C:\\\\a.pdf\"}]}")]
    public void Parse_RefusesWhatIsNotASharedSession(string json)
        => Assert.Equal(SharedSessionFormat.NotASharedSessionMessage, SharedSessionFormat.Parse(json).ErrorMessage);

    [Fact]
    public void Parse_RefusesANewerVersion()
    {
        var read = SharedSessionFormat.Parse("{\"format\":\"quickerplaces-session\",\"schemaVersion\":2,\"files\":[]}");

        Assert.Null(read.Document);
        Assert.Contains("newer version", read.ErrorMessage);
    }

    [Fact]
    public void Parse_DropsFilesItCannotUse_AndRefusesWhenNoneAreLeft()
    {
        const string json = """
            {"format":"quickerplaces-session","schemaVersion":1,"name":"X","files":[
              {"path":"relative\\a.pdf"},
              {"path":"C:\\Jobs\\notes.zip"},
              {"path":"","url":"http://insecure.example.com/a.pdf"},
              null
            ]}
            """;

        var read = SharedSessionFormat.Parse(json);

        Assert.Null(read.Document);
        Assert.Contains("lists no PDF, Office, text, Revit or AutoCAD files", read.ErrorMessage);
    }

    [Fact]
    public void Parse_KeepsOnlyHttpsAddressesAndShareNetworkPaths()
    {
        const string json = """
            {"format":"quickerplaces-session","schemaVersion":1,"name":"X","files":[
              {"path":"C:\\Jobs\\a.pdf","url":"javascript:alert(1)","networkPath":"C:\\Jobs\\a.pdf"},
              {"path":"C:\\Jobs\\b.pdf","url":"https://Contoso.sharepoint.com/b.pdf","networkPath":"\\\\files\\jobs\\b.pdf"}
            ]}
            """;

        var files = SharedSessionFormat.Parse(json).Document!.Files;

        Assert.Null(files[0].Url);
        Assert.Null(files[0].NetworkPath);
        Assert.Equal("https://contoso.sharepoint.com/b.pdf", files[1].Url);
        Assert.Equal(@"\\files\jobs\b.pdf", files[1].NetworkPath);
    }

    [Fact]
    public void Parse_KeepsAFileKnownOnlyByItsAddress()
    {
        const string json = """
            {"format":"quickerplaces-session","schemaVersion":1,"name":"X","files":[
              {"path":"/Users/alice/a.pdf","url":"https://contoso.sharepoint.com/sites/A/Shared%20Documents/A%20101.pdf"}
            ]}
            """;

        var file = Assert.Single(SharedSessionFormat.Parse(json).Document!.Files);

        Assert.Equal("", file.Path);
        Assert.Equal("https://contoso.sharepoint.com/sites/A/Shared%20Documents/A%20101.pdf", file.Url);
    }

    [Fact]
    public void Parse_CleansTheNameTagsAndRepeats()
    {
        var longName = new string('n', SessionStore.MaxNameLength + 20);
        var json = $$"""
            {"format":"quickerplaces-session","schemaVersion":1,"name":"  {{longName}}  ",
             "tags":["  #markups ","MARKUPS","{{new string('t', SessionStore.MaxTagLength + 1)}}","RFI 12"],
             "files":[{"path":"C:\\Jobs\\a.pdf"},{"path":"c:\\jobs\\A.PDF"},{"path":"C:\\Jobs\\.\\b.pdf"}]}
            """;

        var document = SharedSessionFormat.Parse(json).Document!;

        Assert.Equal(SessionStore.MaxNameLength, document.Name.Length);
        Assert.Equal(new[] { "markups", "RFI 12" }, document.Tags);
        Assert.Equal(new[] { @"C:\Jobs\a.pdf", @"C:\Jobs\b.pdf" }, document.Files.Select(f => f.Path));
    }

    [Fact]
    public void Parse_NamesAnUnnamedSession()
    {
        var document = SharedSessionFormat.Parse("""{"format":"quickerplaces-session","schemaVersion":1,"files":[{"path":"C:\\a\\b.pdf"}]}""").Document!;

        Assert.Equal(SharedSessionFormat.UnnamedSession, document.Name);
        Assert.Empty(document.Tags);
    }

    [Fact]
    public void Parse_KeepsAtMostMaxFiles()
    {
        var files = string.Join(",", Enumerable.Range(0, SharedSessionFormat.MaxFiles + 5).Select(i => $$"""{"path":"C:\\Jobs\\{{i}}.pdf"}"""));
        var document = SharedSessionFormat.Parse($$"""{"format":"quickerplaces-session","schemaVersion":1,"name":"X","files":[{{files}}]}""").Document!;

        Assert.Equal(SharedSessionFormat.MaxFiles, document.Files.Count);
    }

    [Fact]
    public void WriteAndRead_GoThroughARealFile()
    {
        using var temp = new TempDirectory();
        var path = temp.File("Tower B.qpsession");

        Assert.Null(SharedSessionFormat.Write(path, Sample()));
        var read = SharedSessionFormat.Read(path);

        Assert.Null(read.ErrorMessage);
        Assert.Equal("Tower B", read.Document!.Name);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Read_RefusesAnOversizedFileWithoutParsingIt()
    {
        using var temp = new TempDirectory();
        var path = temp.File("big.qpsession");
        File.WriteAllText(path, new string(' ', (int)SharedSessionFormat.MaxFileBytes + 1));

        Assert.Equal(SharedSessionFormat.NotASharedSessionMessage, SharedSessionFormat.Read(path).ErrorMessage);
    }

    [Fact]
    public void Read_SaysWhenTheFileIsGone()
    {
        using var temp = new TempDirectory();

        Assert.Equal("That file no longer exists.", SharedSessionFormat.Read(temp.File("gone.qpsession")).ErrorMessage);
    }

    [Theory]
    [InlineData("Tower B", "Tower B.qpsession")]
    [InlineData("RFI 12: A/B?", "RFI 12- A-B-.qpsession")]
    [InlineData("  ...  ", "Session.qpsession")]
    public void SuggestedFileName_ReplacesWhatWindowsRefuses(string name, string expected)
        => Assert.Equal(expected, SharedSessionFormat.SuggestedFileName(name));
}

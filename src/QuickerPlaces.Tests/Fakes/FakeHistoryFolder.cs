using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuickerPlaces.Services.History;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>An in-memory history folder: its files, how many writes, and failures to order.</summary>
public sealed class FakeHistoryFolder : IHistoryFolder
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string FolderPath { get; set; } = @"C:\Users\me\Documents\QuickerPlaces\History";

    public int WriteCount { get; private set; }

    /// <summary>Makes every Write throw, until turned off.</summary>
    public bool FailWrites { get; set; }

    public IReadOnlyList<string> List() => Files.Keys.Where(k => k.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();

    public string? Read(string name) => Files.TryGetValue(name, out var text) ? text : null;

    public void Write(string name, string text)
    {
        if (FailWrites)
            throw new IOException("The disk is full.");
        Files[name] = text;
        WriteCount++;
    }

    public string SetAside(string name, DateTimeOffset now)
    {
        var kept = $"{Path.GetFileNameWithoutExtension(name)}.unreadable.txt";
        Files[kept] = Files[name];
        Files.Remove(name);
        return kept;
    }
}

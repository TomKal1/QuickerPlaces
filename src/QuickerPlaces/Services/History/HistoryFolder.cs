using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuickerPlaces.Services.History;

/// <summary>The history folder's files, behind a seam so ActivityHistory's tests never touch a disk.</summary>
public interface IHistoryFolder
{
    /// <summary>The folder, for messages.</summary>
    string FolderPath { get; }

    /// <summary>The names of the .json files in the folder, or none when it doesn't exist yet.</summary>
    IReadOnlyList<string> List();

    /// <summary>A file's text, or null when it doesn't exist. Throws when it exists but can't be read.</summary>
    string? Read(string name);

    /// <summary>Replaces a file with <paramref name="text"/> in one step, creating the folder first if needed. Throws on failure.</summary>
    void Write(string name, string text);

    /// <summary>Renames a file that couldn't be read as history, so it is kept but never written over. Returns the new name.</summary>
    string SetAside(string name, DateTimeOffset now);
}

/// <summary>
/// The real history folder (history plan §3). The folder is created only
/// when the first file is written, so a user who never tracks anything gets
/// no folder in Documents. Writes go to a temp file that then replaces the
/// file, so a crash never leaves half a month.
/// </summary>
public sealed class HistoryFolder : IHistoryFolder
{
    public HistoryFolder(string folderPath) => FolderPath = folderPath;

    public string FolderPath { get; }

    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(FolderPath))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(FolderPath, "*.json").Select(Path.GetFileName).OfType<string>().ToList();
    }

    public string? Read(string name)
    {
        var path = Path.Combine(FolderPath, name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void Write(string name, string text)
    {
        Directory.CreateDirectory(FolderPath);
        var path = Path.Combine(FolderPath, name);
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, text);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception)
            {
                // Best effort: the write already failed, and that is what's reported.
            }

            throw;
        }
    }

    public string SetAside(string name, DateTimeOffset now)
    {
        var kept = $"{Path.GetFileNameWithoutExtension(name)}.unreadable-{now:yyyyMMdd-HHmmss}.txt";
        File.Move(Path.Combine(FolderPath, name), Path.Combine(FolderPath, kept));
        return kept;
    }
}

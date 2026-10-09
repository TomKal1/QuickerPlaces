using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickerPlaces.Services.Documents;

/// <summary>
/// The document types project sessions and Recent Files handle (documents
/// plan D1): PDFs, Word documents, Excel workbooks, PowerPoint presentations,
/// text files, Revit models and AutoCAD drawings. Decided by the file's
/// extension only; QuickerPlaces never opens a document to look inside it.
/// Saved by name, so new kinds go anywhere; listed in display order.
/// </summary>
public enum DocumentKind
{
    Pdf,
    Word,
    Excel,
    PowerPoint,
    Text,
    Revit,
    AutoCad,
}

public static class DocumentKinds
{
    /// <summary>Every kind, in the order they are shown.</summary>
    public static IReadOnlyList<DocumentKind> All { get; } = new[]
    {
        DocumentKind.Pdf, DocumentKind.Word, DocumentKind.Excel, DocumentKind.PowerPoint,
        DocumentKind.Text, DocumentKind.Revit, DocumentKind.AutoCad,
    };

    /// <summary>The kinds Recent Files records until the user chooses: all but text files, which logs and settings make noisy.</summary>
    public static IReadOnlyList<DocumentKind> Defaults { get; } = All.Where(k => k != DocumentKind.Text).ToArray();

    private static readonly IReadOnlyDictionary<string, DocumentKind> ByExtension = new Dictionary<string, DocumentKind>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = DocumentKind.Pdf,
        [".docx"] = DocumentKind.Word,
        [".docm"] = DocumentKind.Word,
        [".doc"] = DocumentKind.Word,
        [".dotx"] = DocumentKind.Word,
        [".rtf"] = DocumentKind.Word,
        [".xlsx"] = DocumentKind.Excel,
        [".xlsm"] = DocumentKind.Excel,
        [".xlsb"] = DocumentKind.Excel,
        [".xls"] = DocumentKind.Excel,
        [".xltx"] = DocumentKind.Excel,
        [".pptx"] = DocumentKind.PowerPoint,
        [".pptm"] = DocumentKind.PowerPoint,
        [".ppt"] = DocumentKind.PowerPoint,
        [".potx"] = DocumentKind.PowerPoint,
        [".ppsx"] = DocumentKind.PowerPoint,
        [".txt"] = DocumentKind.Text,
        [".rvt"] = DocumentKind.Revit,
        [".rfa"] = DocumentKind.Revit,
        [".dwg"] = DocumentKind.AutoCad,
        [".dxf"] = DocumentKind.AutoCad,
    };

    /// <summary>Every extension recognised, with its dot, longest first so ".docx" is tried before ".doc".</summary>
    public static IReadOnlyList<string> Extensions { get; } = ByExtension.Keys.OrderByDescending(e => e.Length).ToArray();

    /// <summary>The kind a path's extension names, or null for any other file.</summary>
    public static DocumentKind? FromPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var name = DocumentPaths.FileName(path);
        var dot = name.LastIndexOf('.');
        return dot > 0 && ByExtension.TryGetValue(name[dot..], out var kind) ? kind : null;
    }

    /// <summary>"PDF", "Word", "Excel", "PowerPoint", "Text", "Revit" or "AutoCAD".</summary>
    public static string Label(this DocumentKind kind) => kind switch
    {
        DocumentKind.Pdf => "PDF",
        DocumentKind.Word => "Word",
        DocumentKind.Excel => "Excel",
        DocumentKind.PowerPoint => "PowerPoint",
        DocumentKind.Text => "Text",
        DocumentKind.Revit => "Revit",
        _ => "AutoCAD",
    };

    /// <summary>"PDFs", "Word documents", "Revit models" and so on, for filters, counts and the file dialog.</summary>
    public static string PluralLabel(this DocumentKind kind) => kind switch
    {
        DocumentKind.Pdf => "PDFs",
        DocumentKind.Word => "Word documents",
        DocumentKind.Excel => "Excel workbooks",
        DocumentKind.PowerPoint => "PowerPoint presentations",
        DocumentKind.Text => "Text files",
        DocumentKind.Revit => "Revit models",
        _ => "AutoCAD drawings",
    };

    /// <summary>The lower-case name the qp CLI takes and prints for a kind: "pdf", "powerpoint", "autocad".</summary>
    public static string CliName(this DocumentKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>The Windows open-file dialog filter for every kind, then each kind alone.</summary>
    public static string FileDialogFilter { get; } = BuildFileDialogFilter();

    private static string BuildFileDialogFilter()
    {
        string Patterns(DocumentKind? kind) => string.Join(";",
            ByExtension.Where(e => kind is null || e.Value == kind).OrderBy(e => All.ToList().IndexOf(e.Value)).Select(e => "*" + e.Key));

        return "Supported files|" + Patterns(null) +
            string.Concat(All.Select(k => $"|{k.PluralLabel()}|{Patterns(k)}"));
    }
}

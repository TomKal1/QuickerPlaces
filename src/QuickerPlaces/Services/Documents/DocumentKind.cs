using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickerPlaces.Services.Documents;

/// <summary>
/// The document types project sessions and Recent Files handle (documents
/// plan D1): PDFs, Word documents and Excel workbooks. Decided by the file's
/// extension only; QuickerPlaces never opens a document to look inside it.
/// </summary>
public enum DocumentKind
{
    Pdf,
    Word,
    Excel,
}

public static class DocumentKinds
{
    /// <summary>Every kind, in the order they are shown.</summary>
    public static IReadOnlyList<DocumentKind> All { get; } = new[] { DocumentKind.Pdf, DocumentKind.Word, DocumentKind.Excel };

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

    /// <summary>"PDF", "Word" or "Excel".</summary>
    public static string Label(this DocumentKind kind) => kind switch
    {
        DocumentKind.Pdf => "PDF",
        DocumentKind.Word => "Word",
        _ => "Excel",
    };

    /// <summary>"PDFs", "Word documents" or "Excel workbooks", for filters and counts.</summary>
    public static string PluralLabel(this DocumentKind kind) => kind switch
    {
        DocumentKind.Pdf => "PDFs",
        DocumentKind.Word => "Word documents",
        _ => "Excel workbooks",
    };

    /// <summary>The name its program shows in a title bar ("Word", "Excel"), or null for a PDF, which has no one program.</summary>
    public static string? OfficeAppName(this DocumentKind kind) => kind switch
    {
        DocumentKind.Word => "Word",
        DocumentKind.Excel => "Excel",
        _ => null,
    };

    /// <summary>The Windows open-file dialog filter for every kind, then each kind alone.</summary>
    public const string FileDialogFilter =
        "PDF, Word and Excel files|*.pdf;*.docx;*.docm;*.doc;*.dotx;*.rtf;*.xlsx;*.xlsm;*.xlsb;*.xls;*.xltx" +
        "|PDF files (*.pdf)|*.pdf" +
        "|Word documents|*.docx;*.docm;*.doc;*.dotx;*.rtf" +
        "|Excel workbooks|*.xlsx;*.xlsm;*.xlsb;*.xls;*.xltx";
}

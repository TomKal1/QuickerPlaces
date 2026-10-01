using System;
using System.Text.Json.Nodes;

namespace QuickerPlaces.Cli;

/// <summary>
/// A failure the caller should act on, reported as the envelope's
/// <c>error</c> with a stable <see cref="Code"/> and process exit code.
/// Agents branch on the code, never on the message.
/// </summary>
public sealed class CliError : Exception
{
    public CliError(string code, string message, JsonObject? details = null) : base(message)
    {
        Code = code;
        Details = details;
    }

    public string Code { get; }

    public JsonObject? Details { get; }

    public int ExitCode => ExitCodes.For(Code);

    public static CliError Usage(string message) => new(ErrorCodes.Usage, message);
}

/// <summary>Every error code the CLI emits. Part of the contract (apiVersion 1): never renamed, only added to.</summary>
public static class ErrorCodes
{
    public const string Usage = "usage";
    public const string NotFound = "not_found";
    public const string Ambiguous = "ambiguous";
    public const string Invalid = "invalid";
    public const string AppRunning = "app_running";
    public const string StoreUnavailable = "store_unavailable";
    public const string SaveFailed = "save_failed";
    public const string OpenFailed = "open_failed";
    public const string Internal = "internal";
}

/// <summary>Process exit codes. Part of the contract (apiVersion 1).</summary>
public static class ExitCodes
{
    public const int Ok = 0;
    public const int Internal = 1;
    public const int Usage = 2;
    public const int NotFound = 3;
    public const int Invalid = 4;
    public const int AppRunning = 5;
    public const int StoreUnavailable = 6;
    public const int OpenFailed = 7;

    public static int For(string code) => code switch
    {
        ErrorCodes.Usage => Usage,
        ErrorCodes.NotFound => NotFound,
        ErrorCodes.Ambiguous or ErrorCodes.Invalid => Invalid,
        ErrorCodes.AppRunning => AppRunning,
        ErrorCodes.StoreUnavailable or ErrorCodes.SaveFailed => StoreUnavailable,
        ErrorCodes.OpenFailed => OpenFailed,
        _ => Internal
    };
}

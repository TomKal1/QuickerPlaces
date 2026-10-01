using System;
using System.Collections.Generic;

namespace QuickerPlaces.Cli;

/// <summary>One option of a command, as <c>qp describe</c> shows it.</summary>
public sealed record OptionSpec(string Name, string Description, bool IsFlag = false, string? ValueName = null, bool Repeatable = false);

/// <summary>
/// One command: its name ("places list"), what it does, its arguments, and
/// whether it changes anything. The catalogue (<see cref="Commands"/>) is the
/// single source for dispatch, parsing and <c>qp describe</c>.
/// </summary>
public sealed record CommandSpec(
    string Name,
    string Description,
    string Usage,
    int MinPositionals,
    int? MaxPositionals,
    IReadOnlyList<OptionSpec> Options,
    bool Writes,
    Func<CliContext, CliArgs, object> Run,
    IReadOnlyList<string> Examples);

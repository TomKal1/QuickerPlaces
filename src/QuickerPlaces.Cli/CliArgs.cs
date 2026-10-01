using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace QuickerPlaces.Cli;

/// <summary>
/// One command's parsed arguments: positionals in order, and --options.
/// An option is a flag when the command declares it so; otherwise it takes
/// the next token or the text after '='. A repeated valued option keeps
/// every value (<see cref="Values"/>). Anything the command did not declare
/// is a usage error, so a typo never silently does the wrong thing.
/// </summary>
public sealed class CliArgs
{
    private readonly Dictionary<string, List<string>> _options = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Positionals { get; }

    private CliArgs(List<string> positionals) => Positionals = positionals;

    public static CliArgs Parse(IReadOnlyList<string> tokens, CommandSpec spec)
    {
        var positionals = new List<string>();
        var args = new CliArgs(positionals);
        var onlyPositionals = false;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (onlyPositionals || !token.StartsWith("--", StringComparison.Ordinal) || token == "-")
            {
                positionals.Add(token);
                continue;
            }

            if (token == "--")
            {
                onlyPositionals = true;
                continue;
            }

            var name = token[2..];
            string? inline = null;
            var eq = name.IndexOf('=');
            if (eq >= 0)
            {
                inline = name[(eq + 1)..];
                name = name[..eq];
            }

            var option = spec.Options.FirstOrDefault(o => o.Name == name)
                ?? throw CliError.Usage($"'{spec.Name}' has no option --{name}. Run 'qp describe' for the options of every command.");

            if (option.IsFlag)
            {
                if (inline is not null)
                    throw CliError.Usage($"--{name} is a flag and takes no value.");
                args._flags.Add(name);
                continue;
            }

            var value = inline ?? (i + 1 < tokens.Count ? tokens[++i] : throw CliError.Usage($"--{name} needs a value."));
            if (!args._options.TryGetValue(name, out var list))
                args._options[name] = list = new List<string>();
            list.Add(value);
        }

        if (positionals.Count < spec.MinPositionals)
            throw CliError.Usage($"'{spec.Name}' needs {spec.Usage}.");
        if (spec.MaxPositionals is { } max && positionals.Count > max)
            throw CliError.Usage($"'{spec.Name}' takes {spec.Usage}; got {positionals.Count} values. Quote a value that contains spaces.");

        return args;
    }

    public bool Flag(string name) => _flags.Contains(name);

    public string? Value(string name) => _options.TryGetValue(name, out var list) ? list[^1] : null;

    public IReadOnlyList<string> Values(string name) => _options.TryGetValue(name, out var list) ? list : Array.Empty<string>();

    /// <summary>Every value of <paramref name="name"/>, each split on commas, trimmed, blanks dropped.</summary>
    public IReadOnlyList<string> List(string name)
        => Values(name).SelectMany(v => v.Split(',')).Select(v => v.Trim()).Where(v => v.Length > 0).ToList();

    public int? Int(string name, int min, int max)
    {
        if (Value(name) is not { } text)
            return null;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
            throw CliError.Usage($"--{name} must be a whole number from {min} to {max}.");
        return value;
    }
}

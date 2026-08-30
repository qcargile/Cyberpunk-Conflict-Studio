using System.Text.RegularExpressions;

namespace ConflictStudio.Core;

internal static class RedScriptConditionalSourceFilter
{
    private static readonly Regex Module = new("^\\s*module\\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex Term = new("^\\s*(?<not>!)?\\s*ModuleExists\\s*\\(\\s*[\"'](?<name>[^\"']+)[\"']\\s*\\)\\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Annotation = new("^\\s*@", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static RedScriptSource[] Filter(IReadOnlyList<RedScriptSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        HashSet<string> modules = sources.SelectMany(source => Module.Matches(SourceTextMask.RedScript(source.Text, false)).Select(match => match.Groups["name"].Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return sources.Select(source => source with { Text = Filter(source.Text, modules) }).ToArray();
    }

    public static SourceAnalysisFailure[] Failures(IReadOnlyList<RedScriptSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        HashSet<string> modules = sources.SelectMany(source => Module.Matches(SourceTextMask.RedScript(source.Text, false)).Select(match => match.Groups["name"].Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return sources.Where(source => SourceTextMask.RedScript(source.Text, false).Split('\n').Any(line => TryCondition(line, modules, out bool? active, out _) && active is null))
            .Select(source => new SourceAnalysisFailure(source.Provider, source.FilePath, "RedScript condition", "At least one RedScript @if condition could not be evaluated, so guarded declarations in this file were excluded from conflict claims."))
            .ToArray();
    }

    private static string Filter(string text, HashSet<string> modules)
    {
        string[] lines = text.Split('\n');
        string[] maskedLines = SourceTextMask.RedScript(text, false).Split('\n');
        bool hasPending = false;
        bool? pendingActive = null;
        for (int index = 0; index < lines.Length; index++)
        {
            if (TryCondition(maskedLines[index], modules, out bool? active, out int conditionEnd))
            {
                string rest = maskedLines[index][conditionEnd..];
                if (Annotation.IsMatch(rest)) lines[index] = active == true ? new string(' ', conditionEnd) + rest : new string(' ', lines[index].Length);
                else if (string.IsNullOrWhiteSpace(rest))
                {
                    hasPending = true;
                    pendingActive = active;
                }
                continue;
            }
            if (!hasPending || string.IsNullOrWhiteSpace(maskedLines[index])) continue;
            if (Annotation.IsMatch(maskedLines[index]) && pendingActive != true) lines[index] = new string(' ', lines[index].Length);
            hasPending = false;
            pendingActive = null;
        }
        return string.Join('\n', lines);
    }

    private static bool TryCondition(string line, HashSet<string> modules, out bool? active, out int end)
    {
        active = null;
        end = 0;
        int marker = line.IndexOf("@if", StringComparison.Ordinal);
        if (marker < 0 || !string.IsNullOrWhiteSpace(line[..marker])) return false;
        bool negateAll = line.AsSpan(marker).StartsWith("@ifNot", StringComparison.Ordinal);
        int opening = line.IndexOf('(', marker);
        if (opening < 0) return false;
        int depth = 0;
        char quote = '\0';
        int closing = -1;
        for (int index = opening; index < line.Length; index++)
        {
            char value = line[index];
            if (quote != '\0')
            {
                if (value == quote && line[index - 1] != '\\') quote = '\0';
                continue;
            }
            if (value is '\'' or '"') quote = value;
            else if (value == '(') depth++;
            else if (value == ')' && --depth == 0)
            {
                closing = index;
                break;
            }
        }
        if (closing < 0) return false;
        string expression = line[(opening + 1)..closing];
        string separator = expression.Contains("||", StringComparison.Ordinal) ? "||" : "&&";
        string[] terms = expression.Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        bool?[] evaluated = terms.Select(term => EvaluateTerm(term, modules)).ToArray();
        if (evaluated.All(value => value.HasValue))
        {
            bool[] values = evaluated.Select(value => value!.Value).ToArray();
            active = separator == "||" ? values.Any(value => value) : values.All(value => value);
        }
        if (negateAll && active.HasValue) active = !active.Value;
        end = closing + 1;
        return true;
    }

    private static bool? EvaluateTerm(string expression, HashSet<string> modules)
    {
        Match match = Term.Match(expression);
        if (!match.Success) return null;
        bool exists = modules.Contains(match.Groups["name"].Value);
        return match.Groups["not"].Success ? !exists : exists;
    }
}

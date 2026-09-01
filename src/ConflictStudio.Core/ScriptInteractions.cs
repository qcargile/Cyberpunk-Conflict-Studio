using System.Text.RegularExpressions;

namespace ConflictStudio.Core;

public sealed record RedScriptSource(string Provider, string FilePath, string Text);

public enum RedScriptHookKind { Wrap, Replace, AddMethod, AddField }

public enum RedScriptOverlapKind { ExclusiveReplacement, CompositionReview, AddedMemberCollision, AddedMemberInteraction, RedundantReplacement }

public sealed record RedScriptHook(string Provider, string FilePath, RedScriptHookKind Kind, string Target, string? BodySha256 = null);

public sealed record RedScriptOverlap(string Target, RedScriptOverlapKind Kind, RedScriptHook[] Hooks);

public static class RedScriptInteractionAnalyzer
{
    private static readonly Regex Annotation = new("^[ \\t]*@(?<kind>wrapMethod|replaceMethod|addMethod|addField)\\((?<class>[^)]+)\\)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex Method = new("\\bfunc\\s+(?<method>[A-Za-z_][A-Za-z0-9_]*)\\s*\\((?<parameters>[^)]*)\\)", RegexOptions.Compiled);
    private static readonly Regex Field = new("\\blet\\s+(?<field>[A-Za-z_][A-Za-z0-9_]*)\\s*:\\s*(?<type>[^;=]+)", RegexOptions.Compiled);

    public static RedScriptOverlap[] Analyze(IReadOnlyList<RedScriptSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        Dictionary<(string Provider, string FilePath, string Target), Queue<string?>> bodies = RedScriptFlowEvidenceAnalyzer.Analyze(sources)
            .GroupBy(value => (value.Provider, value.FilePath, value.Target))
            .ToDictionary(group => group.Key, group => new Queue<string?>(group.OrderBy(value => value.Line).Select(value => value.BodySha256)));
        List<RedScriptHook> hooks = [];
        foreach (RedScriptSource source in RedScriptConditionalSourceFilter.Filter(sources))
        {
            string code = SourceTextMask.RedScript(source.Text, false);
            foreach (Match annotation in Annotation.Matches(code))
            {
                RedScriptHookKind kind = annotation.Groups["kind"].Value switch
                {
                    "wrapMethod" => RedScriptHookKind.Wrap,
                    "replaceMethod" => RedScriptHookKind.Replace,
                    "addMethod" => RedScriptHookKind.AddMethod,
                    _ => RedScriptHookKind.AddField
                };
                Match declaration = kind == RedScriptHookKind.AddField ? Field.Match(code, annotation.Index + annotation.Length) : Method.Match(code, annotation.Index + annotation.Length);
                Match nextAnnotation = annotation.NextMatch();
                if (!declaration.Success || nextAnnotation.Success && nextAnnotation.Index < declaration.Index) continue;
                string className = annotation.Groups["class"].Value.Trim();
                string target = kind == RedScriptHookKind.AddField
                    ? className + "." + declaration.Groups["field"].Value
                    : RedScriptTarget.Normalize(className, declaration.Groups["method"].Value, declaration.Groups["parameters"].Value);
                string? bodySha256 = bodies.TryGetValue((source.Provider, source.FilePath, target), out Queue<string?>? bodyQueue) && bodyQueue.Count > 0 ? bodyQueue.Dequeue() : null;
                hooks.Add(new RedScriptHook(source.Provider, source.FilePath, kind, target, bodySha256));
            }
        }

        return hooks.GroupBy(value => value.Target, StringComparer.Ordinal)
            .Where(group => group.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group => new RedScriptOverlap(group.Key, Classify(group), group.ToArray()))
            .OrderBy(value => value.Target, StringComparer.Ordinal)
            .ToArray();
    }

    private static RedScriptOverlapKind Classify(IEnumerable<RedScriptHook> hooks)
    {
        RedScriptHook[] values = hooks.ToArray();
        if (values.All(value => value.Kind is RedScriptHookKind.AddMethod or RedScriptHookKind.AddField)) return RedScriptOverlapKind.AddedMemberCollision;
        if (values.Any(value => value.Kind is RedScriptHookKind.AddMethod or RedScriptHookKind.AddField)) return RedScriptOverlapKind.AddedMemberInteraction;
        RedScriptHook[] replacements = values.Where(value => value.Kind == RedScriptHookKind.Replace).ToArray();
        if (replacements.Length <= 1) return RedScriptOverlapKind.CompositionReview;
        bool identicalReplacements = replacements.All(value => value.BodySha256 is not null) && replacements.Select(value => value.BodySha256).Distinct(StringComparer.Ordinal).Count() == 1;
        if (!identicalReplacements) return RedScriptOverlapKind.ExclusiveReplacement;
        return replacements.Length == values.Length ? RedScriptOverlapKind.RedundantReplacement : RedScriptOverlapKind.CompositionReview;
    }
}

internal static class RedScriptTarget
{
    public static string Normalize(string className, string methodName, string parameters)
    {
        string[] types = Split(parameters)
            .Select(ParameterType)
            .Where(value => value.Length > 0)
            .ToArray();
        return className.Trim() + "." + methodName + "(" + string.Join(", ", types) + ")";
    }

    private static IEnumerable<string> Split(string parameters)
    {
        int start = 0;
        int depth = 0;
        for (int index = 0; index < parameters.Length; index++)
        {
            char value = parameters[index];
            if (value is '<' or '[' or '(') depth++;
            else if (value is '>' or ']' or ')') depth--;
            else if (value == ',' && depth == 0)
            {
                yield return parameters[start..index];
                start = index + 1;
            }
        }
        if (start < parameters.Length) yield return parameters[start..];
    }

    private static string ParameterType(string parameter)
    {
        int colon = parameter.IndexOf(':');
        string type = colon < 0 ? parameter.Trim() : parameter[(colon + 1)..].Trim();
        int assignment = type.IndexOf('=');
        if (assignment >= 0) type = type[..assignment].Trim();
        type = Regex.Replace(type, "\\s+", string.Empty);
        string previous;
        do
        {
            previous = type;
            type = Regex.Replace(type, "\\[(?<inner>[^\\[\\]]+)\\]", "array<${inner}>");
        }
        while (type != previous);
        return type;
    }
}

public sealed record LuaSource(string Provider, string FilePath, string Text);

public enum LuaHookKind { Observe, Override }

public enum LuaOverlapKind { ObserverComposition, OverrideWithObservers, OverrideReview }

public sealed record LuaHook(string Provider, string FilePath, LuaHookKind Kind, string Target);

public sealed record LuaOverlap(string Target, LuaOverlapKind Kind, LuaHook[] Hooks);

public static class LuaInteractionAnalyzer
{
    public static LuaOverlap[] Analyze(IReadOnlyList<LuaSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        List<LuaHook> hooks = [];
        foreach (LuaSource source in sources)
        {
            foreach (LuaHookRegistration registration in LuaHookRegistrationAnalyzer.Analyze(source.Text).Where(value => value.Confidence == EvidenceConfidence.Literal))
            {
                LuaHookKind kind = registration.Kind == LuaCallbackEvidenceKind.Override ? LuaHookKind.Override : LuaHookKind.Observe;
                hooks.Add(new LuaHook(source.Provider, source.FilePath, kind, registration.Target));
            }
        }

        return hooks.GroupBy(value => value.Target, StringComparer.Ordinal)
            .Where(group => group.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group =>
            {
                int overrides = group.Count(value => value.Kind == LuaHookKind.Override);
                LuaOverlapKind kind = overrides > 1 ? LuaOverlapKind.OverrideReview : overrides == 1 ? LuaOverlapKind.OverrideWithObservers : LuaOverlapKind.ObserverComposition;
                return new LuaOverlap(group.Key, kind, group.ToArray());
            })
            .OrderBy(value => value.Target, StringComparer.Ordinal)
            .ToArray();
    }
}

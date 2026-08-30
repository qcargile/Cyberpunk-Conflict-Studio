using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ConflictStudio.Core;

public enum EvidenceConfidence { ExactToken, Literal, Dynamic }

public enum EvidenceImpact { None, Review }

public enum RedScriptFlowKind { Wrap, Replace, Add }

public enum RedScriptContinuationEvidence { Continues, EarlyReturnBeforeContinuation, Missing, NotApplicable }

public sealed record RedScriptFlowEvidence(
    string Provider,
    string FilePath,
    string Target,
    RedScriptFlowKind Kind,
    RedScriptContinuationEvidence Continuation,
    EvidenceConfidence Confidence,
    EvidenceImpact Impact,
    int Line,
    string SourceHash);

public static class RedScriptFlowEvidenceAnalyzer
{
    private static readonly Regex Annotation = new("@(?<kind>wrapMethod|replaceMethod|addMethod)\\s*\\(\\s*(?<class>[^)]+?)\\s*\\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Method = new("\\bfunc\\s+(?<method>[A-Za-z_][A-Za-z0-9_]*)\\s*\\((?<parameters>[^)]*)\\)[^{]*\\{", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Continuation = new("\\bwrappedMethod\\s*\\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Return = new("\\breturn\\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static RedScriptFlowEvidence[] Analyze(IReadOnlyList<RedScriptSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        List<RedScriptFlowEvidence> evidence = [];
        foreach (RedScriptSource source in RedScriptConditionalSourceFilter.Filter(sources))
        {
            string code = SourceTextMask.RedScript(source.Text, true);
            foreach (Match annotation in Annotation.Matches(code))
            {
                Match method = Method.Match(code, annotation.Index + annotation.Length);
                if (!method.Success) continue;
                int openingBrace = method.Index + method.Length - 1;
                int closingBrace = ClosingBrace(code, openingBrace);
                if (closingBrace < 0) continue;
                string body = code[(openingBrace + 1)..closingBrace];
                RedScriptFlowKind kind = ParseKind(annotation.Groups["kind"].Value);
                RedScriptContinuationEvidence continuation = ContinuationFor(kind, body);
                evidence.Add(new RedScriptFlowEvidence(
                    source.Provider,
                    source.FilePath,
                    RedScriptTarget.Normalize(annotation.Groups["class"].Value, method.Groups["method"].Value, method.Groups["parameters"].Value),
                    kind,
                    continuation,
                    EvidenceConfidence.ExactToken,
                    ImpactFor(kind, continuation),
                    LineAt(source.Text, method.Index),
                    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source.Text)))));
            }
        }

        return evidence.OrderBy(value => value.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Line)
            .ToArray();
    }

    private static int ClosingBrace(string text, int openingBrace)
    {
        int depth = 0;
        for (int index = openingBrace; index < text.Length; index++)
        {
            if (text[index] == '{') depth++;
            if (text[index] == '}' && --depth == 0) return index;
        }

        return -1;
    }

    private static RedScriptFlowKind ParseKind(string value) => value switch
    {
        "wrapMethod" => RedScriptFlowKind.Wrap,
        "replaceMethod" => RedScriptFlowKind.Replace,
        _ => RedScriptFlowKind.Add
    };

    private static RedScriptContinuationEvidence ContinuationFor(RedScriptFlowKind kind, string body)
    {
        if (kind != RedScriptFlowKind.Wrap) return RedScriptContinuationEvidence.NotApplicable;
        Match[] continuations = Continuation.Matches(body).Cast<Match>().ToArray();
        if (continuations.Length == 0) return RedScriptContinuationEvidence.Missing;
        Match? topLevelContinuation = continuations.FirstOrDefault(value => BraceDepthAt(body, value.Index) == 0);
        if (topLevelContinuation is null) return EveryBranchContinues(body) ? RedScriptContinuationEvidence.Continues : RedScriptContinuationEvidence.EarlyReturnBeforeContinuation;
        foreach (Match returnToken in Return.Matches(body).Cast<Match>().Where(value => value.Index < topLevelContinuation.Index))
        {
            int statementEnd = body.IndexOf(';', returnToken.Index);
            string statement = statementEnd < 0 ? body[returnToken.Index..] : body[returnToken.Index..(statementEnd + 1)];
            if (!Continuation.IsMatch(statement)) return RedScriptContinuationEvidence.EarlyReturnBeforeContinuation;
        }
        return RedScriptContinuationEvidence.Continues;
    }

    private static bool EveryBranchContinues(string body)
    {
        Match branch = Regex.Match(body, "\\bif\\b", RegexOptions.CultureInvariant);
        while (branch.Success)
        {
            if (BraceDepthAt(body, branch.Index) == 0)
            {
                if (HasSuppressingReturn(body[..branch.Index])) return false;
                int thenOpen = body.IndexOf('{', branch.Index);
                if (thenOpen >= 0)
                {
                    int thenClose = ClosingBrace(body, thenOpen);
                    if (thenClose >= 0)
                    {
                        Match elseToken = Regex.Match(body[(thenClose + 1)..], "^\\s*;?\\s*else\\s*\\{", RegexOptions.CultureInvariant);
                        if (elseToken.Success)
                        {
                            int elseOpen = body.IndexOf('{', thenClose + 1 + elseToken.Index);
                            int elseClose = ClosingBrace(body, elseOpen);
                            if (elseClose >= 0
                                && BranchContinues(body[(thenOpen + 1)..thenClose])
                                && BranchContinues(body[(elseOpen + 1)..elseClose])) return true;
                        }
                    }
                }
            }
            branch = branch.NextMatch();
        }
        return false;
    }

    private static bool HasSuppressingReturn(string body)
    {
        foreach (Match returnToken in Return.Matches(body))
        {
            int statementEnd = body.IndexOf(';', returnToken.Index);
            string statement = statementEnd < 0 ? body[returnToken.Index..] : body[returnToken.Index..(statementEnd + 1)];
            if (Continuation.IsMatch(statement)) continue;
            int pathStart = EnclosingBlockStart(body, returnToken.Index);
            if (!Continuation.IsMatch(body[pathStart..returnToken.Index])) return true;
        }
        return false;
    }

    private static int EnclosingBlockStart(string body, int position)
    {
        Stack<int> blocks = [];
        for (int index = 0; index < position; index++)
        {
            if (body[index] == '{') blocks.Push(index + 1);
            else if (body[index] == '}' && blocks.Count > 0) blocks.Pop();
        }
        return blocks.Count == 0 ? 0 : blocks.Peek();
    }

    private static bool BranchContinues(string body)
    {
        Match? topLevelContinuation = Continuation.Matches(body).Cast<Match>().FirstOrDefault(value => BraceDepthAt(body, value.Index) == 0);
        if (topLevelContinuation is null) return EveryBranchContinues(body);
        foreach (Match returnToken in Return.Matches(body).Cast<Match>().Where(value => value.Index < topLevelContinuation.Index))
        {
            int statementEnd = body.IndexOf(';', returnToken.Index);
            string statement = statementEnd < 0 ? body[returnToken.Index..] : body[returnToken.Index..(statementEnd + 1)];
            if (!Continuation.IsMatch(statement)) return false;
        }
        return true;
    }

    private static int BraceDepthAt(string text, int position)
    {
        int depth = 0;
        for (int index = 0; index < position; index++)
        {
            if (text[index] == '{') depth++;
            else if (text[index] == '}') depth--;
        }
        return depth;
    }

    private static EvidenceImpact ImpactFor(RedScriptFlowKind kind, RedScriptContinuationEvidence continuation)
        => kind == RedScriptFlowKind.Replace || continuation is RedScriptContinuationEvidence.EarlyReturnBeforeContinuation or RedScriptContinuationEvidence.Missing
            ? EvidenceImpact.Review
            : EvidenceImpact.None;

    internal static int LineAt(string text, int index)
    {
        int line = 1;
        for (int position = 0; position < index; position++)
        {
            if (text[position] == '\n') line++;
        }

        return line;
    }
}

public enum SharedStateSurface { TweakDb, Blackboard, StatusEffect, StatPool, Persistence }

public sealed record SharedStateWrite(string Provider, string FilePath, SharedStateSurface Surface, string Target, int Line, string Operation = "", string Evidence = "", string SourceHash = "");

public sealed record SharedStateWriteFinding(
    SharedStateSurface Surface,
    string Target,
    EvidenceConfidence Confidence,
    EvidenceImpact Impact,
    SharedStateWrite[] Writes);

public static class SharedStateWriteAnalyzer
{
    private static readonly Regex TweakDb = new("\\b(?:TweakDBInterface|TweakDB)(?:\\.|:)(?:SetFlat|SetFlatNoUpdate)\\s*\\(\\s*t?[\"'](?<target>[^\"']+)[\"']", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Blackboard = new("\\b[A-Za-z_][A-Za-z0-9_]*(?:\\.|:)(?:SetBool|SetInt|SetFloat|SetName|SetString|SetVariant|SetEntity|SetVector|SetUint|SetUInt)\\s*\\(\\s*(?<target>GetAllBlackboardDefs\\(\\)\\.[A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StatusEffect = new("\\b(?:StatusEffectHelper|[A-Za-z_][A-Za-z0-9_]*)(?:\\.|:)(?:ApplyStatusEffect|RemoveStatusEffect)\\s*\\(\\s*[^,\\r\\n]+,\\s*t?[\"'](?<target>[^\"']+)[\"']", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StatPool = new("\\b[A-Za-z_][A-Za-z0-9_]*(?:\\.|:)(?:RequestChangingStatPoolValue|RequestSettingStatPoolValue)\\s*\\(\\s*[^,\\r\\n]+,\\s*gamedataStatPoolType\\.(?<target>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Persistence = new("\\b[A-Za-z_][A-Za-z0-9_]*(?:\\.|:)(?:SetFactStr|SetFactValue|SetQuestFact)\\s*\\(\\s*n?[\"'](?<target>[^\"']+)[\"']", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static SharedStateWriteFinding[] Analyze(IReadOnlyList<RedScriptSource> redScripts, IReadOnlyList<LuaSource> luaSources)
    {
        ArgumentNullException.ThrowIfNull(redScripts);
        ArgumentNullException.ThrowIfNull(luaSources);
        List<SharedStateWrite> writes = [];
        foreach (RedScriptSource source in redScripts) Extract(source.Provider, source.FilePath, SourceTextMask.RedScript(source.Text, false), SourceHash(source.Text), writes);
        foreach (LuaSource source in luaSources) Extract(source.Provider, source.FilePath, SourceTextMask.Lua(source.Text), SourceHash(source.Text), writes);
        return writes.GroupBy(value => (value.Surface, value.Target))
            .Where(group => group.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group => new SharedStateWriteFinding(group.Key.Surface, group.Key.Target, EvidenceConfidence.Literal, EvidenceImpact.Review, group.ToArray()))
            .OrderBy(value => value.Surface)
            .ThenBy(value => value.Target, StringComparer.Ordinal)
            .ToArray();
    }

    private static void Extract(string provider, string filePath, string text, string sourceHash, List<SharedStateWrite> writes)
    {
        Extract(TweakDb, SharedStateSurface.TweakDb, provider, filePath, text, sourceHash, writes);
        Extract(Blackboard, SharedStateSurface.Blackboard, provider, filePath, text, sourceHash, writes);
        Extract(StatusEffect, SharedStateSurface.StatusEffect, provider, filePath, text, sourceHash, writes);
        Extract(StatPool, SharedStateSurface.StatPool, provider, filePath, text, sourceHash, writes);
        Extract(Persistence, SharedStateSurface.Persistence, provider, filePath, text, sourceHash, writes);
    }

    private static void Extract(Regex pattern, SharedStateSurface surface, string provider, string filePath, string text, string sourceHash, List<SharedStateWrite> writes)
    {
        foreach (Match match in pattern.Matches(text))
        {
            string operation = Regex.Match(match.Value, "(?:\\.|:)(?<operation>[A-Za-z_][A-Za-z0-9_]*)\\s*\\(").Groups["operation"].Value;
            writes.Add(new SharedStateWrite(provider, filePath, surface, match.Groups["target"].Value, RedScriptFlowEvidenceAnalyzer.LineAt(text, match.Index), operation, Regex.Replace(match.Value, "\\s+", " ").Trim(), sourceHash));
        }
    }

    private static string SourceHash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

public enum LuaCallbackEvidenceKind { Observe, ObserveBefore, ObserveAfter, Override, Lifecycle }

public enum LuaContinuationEvidence { Continues, Missing, NotApplicable, Unknown }

public sealed record LuaSourceCopy(string Provider, string FilePath);

public sealed record LuaCallbackEvidence(
    LuaCallbackEvidenceKind Kind,
    string Target,
    EvidenceConfidence Confidence,
    EvidenceImpact Impact,
    LuaContinuationEvidence Continuation,
    int Line,
    string SourceHash,
    LuaSourceCopy[] Copies);

public static class LuaCallbackEvidenceAnalyzer
{
    private static readonly Regex Lifecycle = new("\\bregisterForEvent\\s*\\(\\s*(?<event>[^,\\r\\n]+)\\s*,", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Literal = new("^[\"'](?<value>[^\"']+)[\"']$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static LuaCallbackEvidence[] Analyze(IReadOnlyList<LuaSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        List<LuaCallbackEvidence> evidence = [];
        foreach (IGrouping<string, LuaSource> group in sources.GroupBy(SourceHash, StringComparer.Ordinal))
        {
            LuaSource source = group.First();
            string code = SourceTextMask.Lua(source.Text);
            LuaSourceCopy[] copies = group.Select(value => new LuaSourceCopy(value.Provider, value.FilePath)).ToArray();
            foreach (Match match in Lifecycle.Matches(code))
            {
                (string target, bool literal) = Target(match.Groups["event"].Value);
                evidence.Add(new LuaCallbackEvidence(LuaCallbackEvidenceKind.Lifecycle, target, literal ? EvidenceConfidence.Literal : EvidenceConfidence.Dynamic, EvidenceImpact.None, LuaContinuationEvidence.NotApplicable, RedScriptFlowEvidenceAnalyzer.LineAt(source.Text, match.Index), group.Key, copies));
            }

            foreach (LuaHookRegistration registration in LuaHookRegistrationAnalyzer.Analyze(source.Text))
            {
                evidence.Add(new LuaCallbackEvidence(registration.Kind, registration.Target, registration.Confidence, registration.Kind == LuaCallbackEvidenceKind.Override ? EvidenceImpact.Review : EvidenceImpact.None, registration.Continuation, registration.Line, group.Key, copies));
            }
        }

        return evidence.OrderBy(value => value.Target, StringComparer.Ordinal)
            .ThenBy(value => value.Kind)
            .ToArray();
    }

    private static (string Target, bool Literal) Target(string expression)
    {
        string trimmed = expression.Trim();
        Match literal = Literal.Match(trimmed);
        return literal.Success ? (literal.Groups["value"].Value, true) : (Regex.Replace(trimmed, "\\s+", string.Empty), false);
    }

    private static string SourceHash(LuaSource source)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.Text)));
}

internal static class SourceTextMask
{
    public static string RedScript(string text, bool maskStrings) => Mask(text, false, maskStrings);

    public static string Lua(string text) => Mask(text, true, false);

    public static string Lua(string text, bool maskStrings) => Mask(text, true, maskStrings);

    private static string Mask(string text, bool lua, bool maskStrings)
    {
        char[] result = text.ToCharArray();
        char quote = '\0';
        bool lineComment = false;
        bool blockComment = false;
        for (int index = 0; index < result.Length; index++)
        {
            char current = text[index];
            char next = index + 1 < text.Length ? text[index + 1] : '\0';
            if (lineComment)
            {
                if (current == '\n') lineComment = false;
                else result[index] = ' ';
                continue;
            }

            if (blockComment)
            {
                bool closes = lua ? current == ']' && next == ']' : current == '*' && next == '/';
                if (closes)
                {
                    result[index] = ' ';
                    result[index + 1] = ' ';
                    index++;
                    blockComment = false;
                }
                else if (current != '\n') result[index] = ' ';
                continue;
            }

            if (quote != '\0')
            {
                if (maskStrings && current != '\n') result[index] = ' ';
                if (current == '\\')
                {
                    if (maskStrings && index + 1 < result.Length && text[index + 1] != '\n') result[index + 1] = ' ';
                    index++;
                }
                else if (current == quote) quote = '\0';
                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
                if (maskStrings) result[index] = ' ';
                continue;
            }

            bool startsLine = lua ? current == '-' && next == '-' : current == '/' && next == '/';
            bool startsBlock = lua
                ? startsLine && index + 3 < text.Length && text[index + 2] == '[' && text[index + 3] == '['
                : current == '/' && next == '*';
            if (startsBlock)
            {
                int width = lua ? 4 : 2;
                for (int offset = 0; offset < width; offset++) result[index + offset] = ' ';
                index += width - 1;
                blockComment = true;
            }
            else if (startsLine)
            {
                result[index] = ' ';
                result[index + 1] = ' ';
                index++;
                lineComment = true;
            }
        }

        return new string(result);
    }
}

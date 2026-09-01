using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ConflictStudio.Core;

public sealed record TweakSource(string Provider, string FilePath, string Text);

public enum TweakOverlapKind { Redundant, ScalarOverwrite, ComposableMutation, AssignmentThenMutation, MixedArrayOperations, DuplicateMutation, RecordDefinitionCollision, SourceArrayDependency }

public enum TweakOperationKind
{
    TypeDeclaration,
    BaseDeclaration,
    ScalarAssignment,
    ArrayReplacement,
    ArrayAppend,
    ArrayAppendOnce,
    ArrayPrepend,
    ArrayPrependOnce,
    ArrayRemove,
    InlineRecord,
    ArrayAppendFrom,
    ArrayPrependFrom
}

public sealed record TweakOperation(
    string Provider,
    string FilePath,
    string Target,
    string Value,
    bool IsMutation,
    TweakOperationKind Kind = TweakOperationKind.ScalarAssignment,
    int LineNumber = 0);

public sealed record TweakOverlap(string Target, TweakOverlapKind Kind, TweakOperation[] Operations);

public sealed record TweakAnalysisResult(TweakOverlap[] Overlaps, SourceAnalysisFailure[] Failures);

public static class TweakInteractionAnalyzer
{
    public static TweakOverlap[] Analyze(IReadOnlyList<TweakSource> sources)
        => AnalyzeDetailed(sources).Overlaps;

    public static TweakAnalysisResult AnalyzeDetailed(IReadOnlyList<TweakSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        List<TweakOperation> operations = [];
        List<SourceAnalysisFailure> failures = [];
        foreach (TweakSource source in sources)
        {
            TryRead(source, operations, failures);
        }

        List<TweakOverlap> overlaps = operations.GroupBy(value => value.Target, StringComparer.Ordinal)
            .Where(group => group.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group =>
            {
                TweakOverlapKind kind = Classify(group);
                return new TweakOverlap(group.Key, kind, RelevantOperations(group, kind));
            })
            .ToList();
        overlaps.AddRange(SourceDependencies(operations));
        return new TweakAnalysisResult(overlaps.OrderBy(value => value.Target, StringComparer.Ordinal).ToArray(), failures.ToArray());
    }

    private static void TryRead(TweakSource source, List<TweakOperation> operations, List<SourceAnalysisFailure> failures)
    {
        List<TweakOperation> parsed = [];
        try
        {
            ReadDocuments(source, source.Text, parsed);
            operations.AddRange(parsed);
        }
        catch (Exception exception) when (exception is YamlException or InvalidOperationException)
        {
            parsed.Clear();
            try
            {
                ReadRepeatedDeclarations(source, parsed);
                operations.AddRange(parsed);
            }
            catch (Exception fallbackException) when (fallbackException is YamlException or InvalidOperationException)
            {
                failures.Add(new SourceAnalysisFailure(source.Provider, source.FilePath, "TweakXL", $"TweakXL source could not be represented completely: {exception.Message}"));
            }
        }
    }

    private static void ReadDocuments(TweakSource source, string text, List<TweakOperation> operations)
    {
        YamlStream stream = [];
        stream.Load(new StringReader(text));
        foreach (YamlDocument document in stream.Documents)
        {
            if (document.RootNode is not YamlMappingNode root) throw new YamlException("TweakXL document root must be a mapping.");
            ReadRoot(source, root, operations);
        }
    }

    private static void ReadRepeatedDeclarations(TweakSource source, List<TweakOperation> operations)
    {
        foreach (LooseYamlNode document in LooseYaml.ParseAll(source.Text))
        {
            if (document is not LooseYamlMapping root) throw new YamlException("TweakXL document root must be a mapping.");
            foreach ((LooseYamlNode keyNode, LooseYamlNode valueNode) in root.Children)
            {
                if (keyNode is not LooseYamlScalar key || string.IsNullOrWhiteSpace(key.Value)) continue;
                string target = key.Value;
                if (valueNode is LooseYamlMapping record)
                {
                    if (TryGetLoose(record, "$instances", out LooseYamlNode? instancesNode) && instancesNode is LooseYamlSequence instances)
                    {
                        foreach (LooseYamlNode instanceNode in instances.Children)
                        {
                            if (instanceNode is not LooseYamlMapping instance) continue;
                            Dictionary<string, string> variables = Variables(instance);
                            foreach ((LooseYamlNode propertyKeyNode, LooseYamlNode propertyValue) in record.Children)
                            {
                                if (propertyKeyNode is not LooseYamlScalar propertyKey || string.IsNullOrWhiteSpace(propertyKey.Value) || propertyKey.Value == "$instances") continue;
                                List<TweakOperation> expanded = [];
                                AddLooseOperations(source, target + "." + propertyKey.Value, propertyKey.Value, propertyValue, expanded);
                                operations.AddRange(expanded.Select(value => value with { Target = Substitute(value.Target, variables), Value = Substitute(value.Value, variables) }));
                            }
                        }
                        continue;
                    }
                    foreach ((LooseYamlNode propertyKeyNode, LooseYamlNode propertyValue) in record.Children)
                    {
                        if (propertyKeyNode is LooseYamlScalar propertyKey && !string.IsNullOrWhiteSpace(propertyKey.Value)) AddLooseOperations(source, target + "." + propertyKey.Value, propertyKey.Value, propertyValue, operations);
                    }
                }
                else AddLooseOperations(source, target, target.Split('.').Last(), valueNode, operations);
            }
        }
    }

    private static void AddLooseOperations(TweakSource source, string target, string property, LooseYamlNode value, List<TweakOperation> operations)
    {
        TweakOperationKind? mutation = LooseMutationKind(value);
        if (mutation is not null)
        {
            if (value is LooseYamlSequence tagged) foreach (LooseYamlNode item in tagged.Children) operations.Add(new TweakOperation(source.Provider, source.FilePath, target, NormalizeLoose(item), true, mutation.Value, value.Line));
            else operations.Add(new TweakOperation(source.Provider, source.FilePath, target, NormalizeLoose(value), true, mutation.Value, value.Line));
            return;
        }
        if (value is LooseYamlSequence sequence)
        {
            LooseYamlNode[] replacements = sequence.Children.Where(item => LooseMutationKind(item) is null).ToArray();
            if (replacements.Length > 0 || sequence.Children.Length == 0) operations.Add(new TweakOperation(source.Provider, source.FilePath, target, "[" + string.Join(",", replacements.Select(NormalizeLoose)) + "]", false, TweakOperationKind.ArrayReplacement, value.Line));
            foreach (LooseYamlNode item in sequence.Children)
            {
                TweakOperationKind? itemMutation = LooseMutationKind(item);
                if (itemMutation is not null) operations.Add(new TweakOperation(source.Provider, source.FilePath, target, NormalizeLoose(item), true, itemMutation.Value, item.Line));
            }
            return;
        }
        if (value is LooseYamlMapping nested && (TryGetLoose(nested, "$type", out _) || TryGetLoose(nested, "$base", out _)))
        {
            foreach ((LooseYamlNode childKeyNode, LooseYamlNode childValue) in nested.Children)
            {
                if (childKeyNode is LooseYamlScalar childKey && !string.IsNullOrWhiteSpace(childKey.Value)) AddLooseOperations(source, target + "." + childKey.Value, childKey.Value, childValue, operations);
            }
            return;
        }
        TweakOperationKind kind = property switch { "$type" => TweakOperationKind.TypeDeclaration, "$base" => TweakOperationKind.BaseDeclaration, _ when value is LooseYamlMapping => TweakOperationKind.InlineRecord, _ => TweakOperationKind.ScalarAssignment };
        operations.Add(new TweakOperation(source.Provider, source.FilePath, DefinitionTarget(target, kind), NormalizeLoose(value), false, kind, value.Line));
    }

    private static TweakOperationKind? LooseMutationKind(LooseYamlNode node) => node.Tag.ToLowerInvariant() switch { "!append" => TweakOperationKind.ArrayAppend, "!append-once" => TweakOperationKind.ArrayAppendOnce, "!append-from" => TweakOperationKind.ArrayAppendFrom, "!prepend" => TweakOperationKind.ArrayPrepend, "!prepend-once" => TweakOperationKind.ArrayPrependOnce, "!prepend-from" => TweakOperationKind.ArrayPrependFrom, "!remove" => TweakOperationKind.ArrayRemove, _ => null };

    private static bool TryGetLoose(LooseYamlMapping mapping, string name, out LooseYamlNode? value)
    {
        foreach ((LooseYamlNode key, LooseYamlNode child) in mapping.Children)
        {
            if (key is LooseYamlScalar scalar && scalar.Value == name)
            {
                value = child;
                return true;
            }
        }
        value = null;
        return false;
    }

    private static Dictionary<string, string> Variables(LooseYamlMapping mapping)
    {
        Dictionary<string, string> variables = new(StringComparer.Ordinal);
        foreach ((LooseYamlNode key, LooseYamlNode value) in mapping.Children)
        {
            if (key is not LooseYamlScalar scalar) continue;
            if (!variables.TryAdd(scalar.Value, NormalizeLoose(value))) throw new YamlException($"Duplicate TweakXL instance variable '{scalar.Value}'.");
        }
        return variables;
    }

    private static string NormalizeLoose(LooseYamlNode node)
        => node switch
        {
            LooseYamlScalar scalar => scalar.Value,
            LooseYamlSequence sequence => "[" + string.Join(",", sequence.Children.Select(NormalizeLoose)) + "]",
            LooseYamlMapping mapping => "{" + string.Join(",", mapping.Children.Select(value => NormalizeLoose(value.Key) + ":" + NormalizeLoose(value.Value))) + "}",
            _ => string.Empty
        };

    private static void ReadRoot(TweakSource source, YamlMappingNode root, List<TweakOperation> operations)
    {
        foreach ((YamlNode keyNode, YamlNode valueNode) in root.Children)
        {
            if (keyNode is not YamlScalarNode key || string.IsNullOrWhiteSpace(key.Value))
            {
                continue;
            }

            string target = key.Value;
            if (valueNode is YamlMappingNode record)
            {
                if (TryGet(record, "$instances", out YamlNode? instancesNode) && instancesNode is YamlSequenceNode instances)
                {
                    foreach (YamlNode instanceNode in instances.Children)
                    {
                        if (instanceNode is not YamlMappingNode instance) continue;
                        Dictionary<string, string> variables = Variables(instance);
                        foreach ((YamlNode propertyKeyNode, YamlNode propertyValue) in record.Children)
                        {
                            if (propertyKeyNode is not YamlScalarNode propertyKey || string.IsNullOrWhiteSpace(propertyKey.Value) || propertyKey.Value == "$instances") continue;
                            List<TweakOperation> expanded = [];
                            AddOperations(source, target + "." + propertyKey.Value, propertyKey.Value, propertyValue, expanded);
                            operations.AddRange(expanded.Select(value => value with { Target = Substitute(value.Target, variables), Value = Substitute(value.Value, variables) }));
                        }
                    }
                    continue;
                }
                foreach ((YamlNode propertyKeyNode, YamlNode propertyValue) in record.Children)
                {
                    if (propertyKeyNode is YamlScalarNode propertyKey && !string.IsNullOrWhiteSpace(propertyKey.Value))
                    {
                        AddOperations(source, target + "." + propertyKey.Value, propertyKey.Value, propertyValue, operations);
                    }
                }
            }
            else
            {
                AddOperations(source, target, target.Split('.').Last(), valueNode, operations);
            }
        }
    }

    private static bool TryGet(YamlMappingNode mapping, string name, out YamlNode? value)
    {
        foreach ((YamlNode key, YamlNode child) in mapping.Children)
        {
            if (key is YamlScalarNode scalar && scalar.Value == name)
            {
                value = child;
                return true;
            }
        }
        value = null;
        return false;
    }

    private static Dictionary<string, string> Variables(YamlMappingNode mapping)
    {
        Dictionary<string, string> variables = new(StringComparer.Ordinal);
        foreach ((YamlNode key, YamlNode value) in mapping.Children)
        {
            if (key is not YamlScalarNode scalar) continue;
            string name = scalar.Value ?? string.Empty;
            if (!variables.TryAdd(name, Normalize(value))) throw new YamlException($"Duplicate TweakXL instance variable '{name}'.");
        }
        return variables;
    }

    private static string Substitute(string value, IReadOnlyDictionary<string, string> variables)
    {
        string result = value;
        foreach ((string name, string replacement) in variables) result = result.Replace("$(" + name + ")", replacement, StringComparison.Ordinal);
        return result;
    }

    private static void AddOperations(TweakSource source, string target, string property, YamlNode value, List<TweakOperation> operations)
    {
        TweakOperationKind? taggedKind = MutationKind(value);
        if (taggedKind is not null)
        {
            if (value is YamlSequenceNode taggedSequence)
            {
                foreach (YamlNode item in taggedSequence.Children)
                {
                    operations.Add(Create(source, target, item, taggedKind.Value, true));
                }
            }
            else
            {
                operations.Add(Create(source, target, value, taggedKind.Value, true));
            }
            return;
        }

        if (value is YamlSequenceNode sequence)
        {
            YamlNode[] replacements = sequence.Children.Where(item => MutationKind(item) is null).ToArray();
            if (replacements.Length > 0 || sequence.Children.Count == 0)
            {
                operations.Add(Create(source, target, new YamlSequenceNode(replacements), TweakOperationKind.ArrayReplacement, false, checked((int)value.Start.Line)));
            }

            foreach (YamlNode item in sequence.Children)
            {
                TweakOperationKind? itemKind = MutationKind(item);
                if (itemKind is not null)
                {
                    operations.Add(Create(source, target, item, itemKind.Value, true));
                }
            }

            return;
        }

        if (value is YamlMappingNode nested && (TryGet(nested, "$type", out _) || TryGet(nested, "$base", out _)))
        {
            foreach ((YamlNode childKeyNode, YamlNode childValue) in nested.Children)
            {
                if (childKeyNode is YamlScalarNode childKey && !string.IsNullOrWhiteSpace(childKey.Value)) AddOperations(source, target + "." + childKey.Value, childKey.Value, childValue, operations);
            }
            return;
        }

        TweakOperationKind kind = property switch
        {
            "$type" => TweakOperationKind.TypeDeclaration,
            "$base" => TweakOperationKind.BaseDeclaration,
            _ when value is YamlMappingNode => TweakOperationKind.InlineRecord,
            _ => TweakOperationKind.ScalarAssignment
        };
        operations.Add(Create(source, DefinitionTarget(target, kind), value, kind, false));
    }

    private static TweakOperation Create(TweakSource source, string target, YamlNode value, TweakOperationKind kind, bool mutation, int? line = null)
        => new(source.Provider, source.FilePath, target, mutation ? NormalizeWithoutMutationTag(value) : Normalize(value), mutation, kind, line ?? checked((int)value.Start.Line));

    private static string DefinitionTarget(string target, TweakOperationKind kind)
    {
        if (kind is not (TweakOperationKind.TypeDeclaration or TweakOperationKind.BaseDeclaration)) return target;
        int separator = target.LastIndexOf('.');
        return separator < 0 ? target + ".$definition" : target[..separator] + ".$definition";
    }

    private static TweakOperationKind? MutationKind(YamlNode node)
    {
        string tag = node.Tag.ToString();
        if (tag.Equals("!append", StringComparison.OrdinalIgnoreCase))
        {
            return TweakOperationKind.ArrayAppend;
        }
        if (tag.Equals("!append-once", StringComparison.OrdinalIgnoreCase)) return TweakOperationKind.ArrayAppendOnce;
        if (tag.Equals("!append-from", StringComparison.OrdinalIgnoreCase)) return TweakOperationKind.ArrayAppendFrom;

        if (tag.Equals("!prepend", StringComparison.OrdinalIgnoreCase))
        {
            return TweakOperationKind.ArrayPrepend;
        }
        if (tag.Equals("!prepend-once", StringComparison.OrdinalIgnoreCase)) return TweakOperationKind.ArrayPrependOnce;
        if (tag.Equals("!prepend-from", StringComparison.OrdinalIgnoreCase)) return TweakOperationKind.ArrayPrependFrom;

        return tag.Equals("!remove", StringComparison.OrdinalIgnoreCase) ? TweakOperationKind.ArrayRemove : null;
    }

    private static string Normalize(YamlNode node)
    {
        string rawTag = node.Tag.ToString();
        string tag = rawTag is "" or "?" ? string.Empty : rawTag + " ";
        return node switch
        {
            YamlScalarNode scalar => tag + (scalar.Value ?? "null"),
            YamlSequenceNode sequence => tag + "[" + string.Join(",", sequence.Children.Select(NormalizeWithoutMutationTag)) + "]",
            YamlMappingNode mapping => tag + "{" + string.Join(",", mapping.Children
                .Select(pair => (Key: NormalizeWithoutMutationTag(pair.Key), Value: NormalizeWithoutMutationTag(pair.Value)))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + ":" + pair.Value)) + "}",
            _ => string.Empty
        };
    }

    private static string NormalizeWithoutMutationTag(YamlNode node)
    {
        string normalized = Normalize(node);
        int separator = normalized.IndexOf(' ');
        return MutationKind(node) is null || separator < 0 ? normalized : normalized[(separator + 1)..];
    }

    private static TweakOverlapKind Classify(IGrouping<string, TweakOperation> group)
    {
        TweakOperation[] operations = group.ToArray();
        if (operations.All(value => !value.IsMutation) && operations.Select(OperationIdentity).Distinct(StringComparer.Ordinal).Count() == 1) return TweakOverlapKind.Redundant;
        if (operations.Select(OperationIdentity).Distinct(StringComparer.Ordinal).Count() == 1)
        {
            return operations[0].Kind is TweakOperationKind.ArrayAppend or TweakOperationKind.ArrayPrepend ? TweakOverlapKind.DuplicateMutation : TweakOverlapKind.Redundant;
        }
        bool equalByProvider = operations
            .GroupBy(value => value.Provider, StringComparer.OrdinalIgnoreCase)
            .Select(provider => string.Join("\n", provider.Select(OperationIdentity).OrderBy(value => value, StringComparer.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Count() == 1;
        if (equalByProvider)
        {
            if (operations.Any(value => value.Kind is TweakOperationKind.ArrayAppend or TweakOperationKind.ArrayPrepend)) return TweakOverlapKind.DuplicateMutation;
            return TweakOverlapKind.Redundant;
        }
        if (operations.Any(value => value.Kind is TweakOperationKind.TypeDeclaration or TweakOperationKind.BaseDeclaration)) return TweakOverlapKind.RecordDefinitionCollision;

        if (operations.All(value => value.Kind == TweakOperationKind.ArrayReplacement)) return TweakOverlapKind.MixedArrayOperations;

        bool replacementAndMutation = operations.Any(value => !value.IsMutation && operations.Any(other => other.IsMutation && !string.Equals(value.Provider, other.Provider, StringComparison.OrdinalIgnoreCase)));
        if (replacementAndMutation)
        {
            TweakOperation[] assignments = operations.Where(value => !value.IsMutation).ToArray();
            return assignments.Select(OperationIdentity).Distinct(StringComparer.Ordinal).Count() > 1
                ? TweakOverlapKind.MixedArrayOperations
                : TweakOverlapKind.AssignmentThenMutation;
        }
        bool opposing = operations.GroupBy(value => value.Value, StringComparer.Ordinal).Any(ValueIsOrderSensitive);
        if (opposing) return TweakOverlapKind.MixedArrayOperations;
        if (operations.GroupBy(value => value.Value, StringComparer.Ordinal).Any(ValueHasDuplicatePlainAdds)) return TweakOverlapKind.DuplicateMutation;

        return operations.All(value => value.IsMutation)
            ? TweakOverlapKind.ComposableMutation
            : operations.Any(value => value.IsMutation)
                ? TweakOverlapKind.MixedArrayOperations
                : TweakOverlapKind.ScalarOverwrite;
    }

    private static TweakOperation[] RelevantOperations(IGrouping<string, TweakOperation> group, TweakOverlapKind kind)
    {
        TweakOperation[] operations = group.ToArray();
        if (kind is not (TweakOverlapKind.MixedArrayOperations or TweakOverlapKind.DuplicateMutation)) return operations;
        if (operations.All(value => value.Kind == TweakOperationKind.ArrayReplacement)) return operations;
        bool replacementAndMutation = operations.Any(value => !value.IsMutation && operations.Any(other => other.IsMutation && !string.Equals(value.Provider, other.Provider, StringComparison.OrdinalIgnoreCase)));
        if (replacementAndMutation) return operations;
        HashSet<string> contestedValues = operations.GroupBy(value => value.Value, StringComparer.Ordinal)
            .Where(kind == TweakOverlapKind.MixedArrayOperations ? ValueIsOrderSensitive : ValueHasDuplicatePlainAdds)
            .Select(values => values.Key)
            .ToHashSet(StringComparer.Ordinal);
        return operations.Where(value => contestedValues.Contains(value.Value)).ToArray();
    }

    private static bool ValueIsOrderSensitive(IGrouping<string, TweakOperation> values)
    {
        if (values.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2) return false;
        bool hasPlainAdd = values.Any(value => value.Kind is TweakOperationKind.ArrayAppend or TweakOperationKind.ArrayPrepend);
        bool hasUniqueAdd = values.Any(value => value.Kind is TweakOperationKind.ArrayAppendOnce or TweakOperationKind.ArrayPrependOnce);
        return hasPlainAdd && hasUniqueAdd;
    }

    private static bool ValueHasDuplicatePlainAdds(IGrouping<string, TweakOperation> values)
        => values.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1
            && values.Count(value => value.Kind is TweakOperationKind.ArrayAppend or TweakOperationKind.ArrayPrepend) > 1
            && values.All(value => IsAddition(value.Kind));

    private static bool IsAddition(TweakOperationKind kind)
        => kind is TweakOperationKind.ArrayAppend or TweakOperationKind.ArrayAppendOnce or TweakOperationKind.ArrayAppendFrom or TweakOperationKind.ArrayPrepend or TweakOperationKind.ArrayPrependOnce or TweakOperationKind.ArrayPrependFrom;

    private static bool IsArrayCopy(TweakOperationKind kind)
        => kind is TweakOperationKind.ArrayAppendFrom or TweakOperationKind.ArrayPrependFrom;

    private static IEnumerable<TweakOverlap> SourceDependencies(IReadOnlyList<TweakOperation> operations)
    {
        foreach (IGrouping<(string Target, string Source), TweakOperation> copies in operations.Where(value => IsArrayCopy(value.Kind)).GroupBy(value => (value.Target, value.Value)))
        {
            TweakOperation[] sourceWrites = operations.Where(value => value.Target == copies.Key.Source && copies.Any(copy => !string.Equals(value.Provider, copy.Provider, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (sourceWrites.Length == 0) continue;
            yield return new TweakOverlap(copies.Key.Target + " <- " + copies.Key.Source, TweakOverlapKind.SourceArrayDependency, [.. copies, .. sourceWrites]);
        }
    }

    private static string OperationIdentity(TweakOperation operation)
        => operation.Kind + ":" + operation.Value;
}

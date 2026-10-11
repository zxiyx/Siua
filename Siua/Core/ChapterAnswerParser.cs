using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Siua.Core;

/// <summary>Index 是截图中从上到下的顺序，不依赖可能重复的页面题号。</summary>
public sealed record ChapterQuestionSpec(
    int Index, string Number, int BlankCount, bool Multiple, IReadOnlyList<string> Options,
    bool IsTextAnswer = false);

internal static class ChapterAnswerParser
{
    public static bool TryParse(string? text, IReadOnlyList<ChapterQuestionSpec> questions,
        out IReadOnlyDictionary<int, IReadOnlyList<string>> answers)
    {
        answers = new Dictionary<int, IReadOnlyList<string>>();
        if (!AnswerJson.TryUnwrap(text, out var json) || questions.Count == 0 ||
            questions.Select(question => question.Index).Distinct().Count() != questions.Count)
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() != questions.Count)
                return false;

            var specs = questions.ToDictionary(question => question.Index);
            var result = new Dictionary<int, IReadOnlyList<string>>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("index", out var indexValue) || !indexValue.TryGetInt32(out var index) ||
                    !specs.TryGetValue(index, out var spec) || result.ContainsKey(index) ||
                    !item.TryGetProperty("answers", out var values) || values.ValueKind != JsonValueKind.Array)
                    return false;

                var entries = new List<string>();
                foreach (var value in values.EnumerateArray())
                {
                    if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                        return false;
                    entries.Add(value.GetString()!.Trim());
                }

                if (spec.IsTextAnswer)
                {
                    if (entries.Count != 1)
                        return false;
                }
                else if (spec.BlankCount > 0)
                {
                    if (entries.Count != spec.BlankCount)
                        return false;
                }
                else
                {
                    if (entries.Count == 0 || (!spec.Multiple && entries.Count != 1) ||
                        entries.Distinct(StringComparer.Ordinal).Count() != entries.Count ||
                        entries.Any(entry => !spec.Options.Contains(entry, StringComparer.Ordinal)))
                        return false;
                }
                result.Add(index, entries);
            }
            answers = result;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }
}

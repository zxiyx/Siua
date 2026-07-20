using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Siua.Core;

/// <summary>提供学习平台共用的 AI 答案匹配规则。</summary>
internal static class AnswerMatcher
{
    private static readonly Regex MarkerPattern = new(
        @"^\s*(?:(?:(?:正确)?答案|选项)\s*(?:是|为)?\s*[:：]?\s*)?" +
        @"(?<markers>[A-H](?:\s*[,，、/和及与]?\s*[A-H])*)\s*[。.］\]]?\s*$",
        RegexOptions.Compiled);

    public static bool SelectsOption(
        string answer,
        string marker,
        string optionText)
    {
        var answerMarkers = ExtractMarkers(answer);
        var markerCharacter = marker
            .Trim()
            .ToUpperInvariant()
            .FirstOrDefault(character => character is >= 'A' and <= 'H');
        if (answerMarkers.Count > 0)
            return markerCharacter != default && answerMarkers.Contains(markerCharacter);

        return !string.IsNullOrWhiteSpace(optionText) &&
               answer.Contains(optionText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlySet<char> ExtractMarkers(string answer)
    {
        var normalized = answer.Replace("**", string.Empty).Replace("`", string.Empty);
        var match = MarkerPattern.Match(normalized);
        return match.Success
            ? match.Groups["markers"].Value
                .Where(character => character is >= 'A' and <= 'H')
                .ToHashSet()
            : new HashSet<char>();
    }
}

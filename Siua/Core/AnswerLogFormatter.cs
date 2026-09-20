using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Siua.Core;

internal static class AnswerLogFormatter
{
    public static string Format(string number, int index, string answer, int blankCount = 0)
    {
        if (blankCount > 0 && FillInBlankAnswerParser.TryParse(answer, blankCount, out var blanks))
            return Format(number, index, blanks, true);

        return $"第 {QuestionNumber(number, index)} 题答案为：{SingleLine(answer)}";
    }

    public static string Format(string number, int index, IReadOnlyList<string> answers, bool isFillInBlank = false)
    {
        var text = isFillInBlank
            ? string.Join("；", answers.Select((answer, i) => $"第 {i + 1} 空：{SingleLine(answer)}"))
            : string.Join("、", answers.Select(SingleLine));
        return $"第 {QuestionNumber(number, index)} 题答案为：{text}";
    }

    private static string QuestionNumber(string number, int index)
    {
        var match = Regex.Match(number, @"^\s*(?:第\s*)?(\d+)");
        return match.Success ? match.Groups[1].Value : index.ToString();
    }

    private static string SingleLine(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}

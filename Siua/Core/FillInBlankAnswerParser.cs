using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Siua.Core;

/// <summary>按空格顺序解析 AI 答案，避免用标点分割而破坏公式或句子。</summary>
internal static class FillInBlankAnswerParser
{
    public static bool TryParse(string answer, int blankCount, out IReadOnlyList<string> blanks)
    {
        blanks = Array.Empty<string>();
        if (blankCount <= 0 || string.IsNullOrWhiteSpace(answer))
            return false;

        var json = answer.Trim();
        // 兼容模型在 JSON 外包裹代码块，但不从解释文字中猜测答案。
        if (json.StartsWith("```", StringComparison.Ordinal) &&
            json.EndsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = json.IndexOf('\n');
            if (firstLineEnd < 0 || firstLineEnd >= json.Length - 3)
                return false;

            var language = json[3..firstLineEnd].Trim();
            if (language.Length > 0 && !language.Equals("json", StringComparison.OrdinalIgnoreCase))
                return false;

            json = json[(firstLineEnd + 1)..^3].Trim();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() != blankCount)
                return false;

            var values = new List<string>(blankCount);
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                    return false;

                var value = element.GetString();
                if (string.IsNullOrWhiteSpace(value))
                    return false;

                values.Add(value.Trim());
            }

            blanks = values;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

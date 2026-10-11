using System;

namespace Siua.Core;

/// <summary>只接受原始 JSON 或完整的 JSON 代码块，不从解释文字中猜测答案。</summary>
internal static class AnswerJson
{
    public static bool TryUnwrap(string? text, out string json)
    {
        json = text?.Trim() ?? string.Empty;
        if (json.Length == 0)
            return false;

        if (json.StartsWith("```", StringComparison.Ordinal) &&
            json.EndsWith("```", StringComparison.Ordinal))
        {
            var newline = json.IndexOf('\n');
            if (newline < 0 || newline >= json.Length - 3)
                return false;

            var language = json[3..newline].Trim();
            if (language.Length > 0 && !language.Equals("json", StringComparison.OrdinalIgnoreCase))
                return false;

            json = json[(newline + 1)..^3].Trim();
        }

        return true;
    }
}

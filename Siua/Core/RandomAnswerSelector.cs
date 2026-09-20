using System;
using System.Collections.Generic;
using System.Linq;

namespace Siua.Core;

/// <summary>为单选题或多选题随机生成至少一个答案索引。</summary>
internal static class RandomAnswerSelector
{
    public static IReadOnlyList<string> FillBlanks(int blankCount)
    {
        if (blankCount <= 0)
            return [];

        const string characters = "天地日月山水春夏秋冬东西南北学文书人花草风云";
        var answers = new string[blankCount];
        for (var index = 0; index < blankCount; index++)
        {
            if (Random.Shared.Next(2) == 0)
            {
                answers[index] = Random.Shared.Next(10000).ToString(System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }

            var text = new char[Random.Shared.Next(1, 5)];
            for (var character = 0; character < text.Length; character++)
                text[character] = characters[Random.Shared.Next(characters.Length)];
            answers[index] = new string(text);
        }
        return answers;
    }

    public static IReadOnlyList<int> Select(int optionCount, bool allowsMultipleAnswers)
    {
        if (optionCount <= 0)
            return [];

        var indexes = Enumerable.Range(0, optionCount).ToArray();
        for (var index = indexes.Length - 1; index > 0; index--)
        {
            var swapIndex = Random.Shared.Next(index + 1);
            (indexes[index], indexes[swapIndex]) = (indexes[swapIndex], indexes[index]);
        }

        var selectedCount = allowsMultipleAnswers
            ? Random.Shared.Next(1, optionCount + 1)
            : 1;
        return indexes[..selectedCount];
    }
}

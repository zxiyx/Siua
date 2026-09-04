using System;
using System.Collections.Generic;
using System.Linq;

namespace Siua.Core;

/// <summary>为单选题或多选题随机生成至少一个答案索引。</summary>
internal static class RandomAnswerSelector
{
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

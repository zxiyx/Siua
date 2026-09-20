using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;
using Siua.Common;
using Siua.Core;
using System.Text.Json;
using Siua.Interfaces;

namespace Siua.Services;

/// <summary>负责调用 AI 模型完成 OCR 与答案分析。</summary>
public class AiControlService
{
    private readonly GlobalSettings _globalSettings;
    private readonly ILogService _logService;

    public AiControlService(GlobalSettings globalSettings, ILogService logService)
    {
        _globalSettings = globalSettings;
        _logService = logService;
    }

    public Task<string?> GetAnswer(string question) => GetAnswerAsync(
        question,
        "你是一个专业答题助手，无论是单选题还是多选题，只告诉我答案即可，比如 ABC、A 等，不要解释");

    public Task<string?> GetFillInBlankAnswer(string question, int blankCount)
    {
        if (blankCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(blankCount));

        return GetAnswerAsync(
            question,
            $"你是一个专业答题助手。当前是填空题，共 {blankCount} 个空。" +
            "请按题目中空格的先后顺序，仅返回 JSON 字符串数组，每个元素对应一个空的答案。" +
            $"数组必须恰好包含 {blankCount} 个非空字符串。" +
            "例如两个空返回 [\"第一个空的答案\",\"第二个空的答案\"]。" +
            "答案中的标点、公式和换行须保留在对应字符串内，不要拆成多个元素。" +
            "返回每个空的实际答案，不要套用选择题格式，不要添加题号、解释或 Markdown 代码块。");
    }

    public Task<string?> GetChapterAnswers(string text, IReadOnlyList<ChapterQuestionSpec> questions) =>
        GetAnswerAsync(text, ChapterPrompt(questions));

    public Task<string?> GetChapterAnswers(byte[] image, IReadOnlyList<ChapterQuestionSpec> questions) =>
        GetAnswerAsync("请回答这张完整章节测试截图中的全部题目。", ChapterPrompt(questions), image);

    private static string ChapterPrompt(IReadOnlyList<ChapterQuestionSpec> questions) =>
        "你是一个专业答题助手。请回答整份章节测试，题目按截图或识别文本从上到下排列。" +
        "仅返回 JSON 数组，每题一个对象，例如 [{\"index\":1,\"answers\":[\"A\"]},{\"index\":2,\"answers\":[\"甲\",\"乙\"]}]。" +
        "index 必须使用下面清单中的 Index（全卷顺序），不能把可能重复的页面题号 Number 当作 index。" +
        "BlankCount>0 表示填空题，每空对应 answers 中一个非空字符串，按空的顺序填写实际答案。" +
        "否则为选择题，answers 只能使用 Options 列出的标号，Multiple=false 时恰好选择一个。" +
        "不得漏题、重复题号或添加说明；答案中的逗号、公式、换行保留在同一个字符串内。题目清单：" +
        JsonSerializer.Serialize(questions);

    private async Task<string?> GetAnswerAsync(string question, string systemPrompt, byte[]? image = null)
    {
        using var client = CreateClient();
        if (client is null)
        {
            return null;
        }

        try
        {
            var messages = new List<Message>
            {
                new(Role.System, systemPrompt),
                image is null
                    ? new Message(Role.User, question)
                    : new Message(Role.User, new List<Content>
                    {
                        question,
                        new ImageUrl($"image/png;base64,{Convert.ToBase64String(image)}")
                    })
            };
            var request = new ChatRequest(messages, _globalSettings.CurrentAi.ModelName, temperature: 0.1, frequencyPenalty: 0);
            var response = await client.ChatEndpoint.GetCompletionAsync(request);
            return response.FirstChoice.Message.Content.ToString();
        }
        catch (Exception exception)
        {
            _logService.AddLog(LogLevel.Error, "AI", $"获取答案失败：{exception.Message}");
            return null;
        }
    }

    public async Task<string?> GetTextFromImage(byte[] imageBytes)
    {
        if (imageBytes is not { Length: > 0 })
        {
            _logService.AddLog(LogLevel.Error, "AI", "图像识别失败：截图数据为空");
            return null;
        }

        using var client = CreateClient();
        if (client is null)
        {
            return null;
        }

        try
        {
            var dataUri = $"image/png;base64,{Convert.ToBase64String(imageBytes)}";
            var content = new List<Content>
            {
                "请识别图片中的文字，包括数学符号等，仅返回识别结果，不要添加其他说明",
                new ImageUrl(dataUri)
            };
            var messages = new List<Message>
            {
                new(Role.System, "你是一个专业的 OCR 文字识别助手"),
                new(Role.User, content)
            };
            var request = new ChatRequest(messages, _globalSettings.CurrentAi.ModelName, 0.3);
            var response = await client.ChatEndpoint.GetCompletionAsync(request);
            return response.FirstChoice.Message.Content.ToString();
        }
        catch (Exception exception)
        {
            _logService.AddLog(LogLevel.Error, "AI", $"图像识别失败：{exception.Message}");
            return null;
        }
    }

    private OpenAIClient? CreateClient()
    {
        var ai = _globalSettings.CurrentAi;
        if (string.IsNullOrWhiteSpace(ai.Domain) ||
            string.IsNullOrWhiteSpace(ai.ApiKey) ||
            string.IsNullOrWhiteSpace(ai.ModelName))
        {
            return null;
        }

        var authentication = new OpenAIAuthentication(ai.ApiKey);
        var settings = new OpenAISettings(ai.Domain.Trim());
        return new OpenAIClient(authentication, settings);
    }
}

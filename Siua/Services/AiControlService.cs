using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;
using Siua.Common;
using Siua.Interfaces;

namespace Siua.Services;

public class AiControlService
{
    private readonly GlobalSettings _globalSettings;
    private readonly ILogService _logService;

    public AiControlService(GlobalSettings globalSettings, ILogService logService)
    {
        _globalSettings = globalSettings;
        _logService = logService;
    }

    public async Task<string?> GetAnswer(string question)
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
                new(Role.System, "你是一个专业答题助手，无论是单选题还是多选题，只告诉我答案即可，比如 ABC、A 等，不要解释"),
                new(Role.User, question)
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

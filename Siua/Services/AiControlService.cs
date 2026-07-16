using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;

namespace Siua.Services;

public class AiControlService
{
    private readonly GlobalSettings _globalSettings;
    private readonly OpenAIClient? _client;

    public AiControlService(GlobalSettings globalSettings)
    {
        _globalSettings = globalSettings;
        if (globalSettings.CurrentAi.Domain is null)
        {
            return;
        }

        var authentication = new OpenAIAuthentication(globalSettings.CurrentAi.ApiKey);
        var settings = new OpenAISettings(globalSettings.CurrentAi.Domain);
        _client = new OpenAIClient(authentication, settings);
    }

    public async Task<string?> GetAnswer(string question)
    {
        var client = _client;
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
        catch
        {
            return null;
        }
    }

    public async Task<string?> GetTextFromImage(string imagePath)
    {
        var client = _client;
        if (client is null)
        {
            return null;
        }

        try
        {
            var imageBytes = await File.ReadAllBytesAsync(imagePath);
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
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
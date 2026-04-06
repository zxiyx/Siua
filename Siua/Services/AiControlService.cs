using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;

namespace Siua.Services;

public class AiControlService
{
    private OpenAIAuthentication _auth;
    private OpenAISettings _settings;
    private OpenAIClient _client;
    private readonly GlobalSettings _globalSettings;
    private bool _hasAi = false;
    public AiControlService(GlobalSettings  globalSettings)
    {
        _globalSettings = globalSettings;
        if (_globalSettings.CurrentAi.Domain!=null) 
        {
            _auth = new OpenAIAuthentication(_globalSettings.CurrentAi.ApiKey);
            _settings = new OpenAISettings(_globalSettings.CurrentAi.Domain); 
            _client = new OpenAIClient(_auth, _settings);
            _hasAi = true;
        }
    }

    public async Task<string?> GetAnswer(string m)
    {
        if (!_hasAi) return null;
        try
        {
            var messages = new List<Message>
            {
                new (Role.System,"你是一个专业答题助手,无论是单选题还是多选题只告诉我答案即可，比如ABC、A等,不要解释"),
                new (Role.User, m)
            };
            var chatRequest = new ChatRequest(messages, _globalSettings.CurrentAi.ModelName, temperature:0.1,frequencyPenalty:0);
            var response = await _client.ChatEndpoint.GetCompletionAsync(chatRequest);
            return response.FirstChoice.Message.Content.ToString();
        }
        catch (Exception e)
        {
            return null;
        }
    }
    public async Task<string?> GetTextFromImage(string m)
    {
        if (!_hasAi) return null;
        try
        {
            string imageBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(m));
            string dataUri = $"image/png;base64,{imageBase64}";
            var contentParts = new List<Content>
            {
                "请识别图片中的文字，包括数学符号等，仅返回识别结果，不要添加其他说明",
                new ImageUrl(dataUri)
            };
            var messages = new List<Message>
            {
                new Message(Role.System, "你是一个专业的 OCR 文字识别助手"),
                new Message(Role.User, contentParts)
            };
            var chatRequest = new ChatRequest(messages, _globalSettings.CurrentAi.ModelName,
                0.3);
            var response = await _client.ChatEndpoint.GetCompletionAsync(chatRequest);
            return response.FirstChoice.Message.Content.ToString();
        }
        catch
        {
            return null;
        }
        
    }
    
    public void Dispose()
    {
        _client.Dispose();
    }
}
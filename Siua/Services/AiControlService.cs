using System;
using System.Collections.Generic;
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
        if (_globalSettings.Ais.Count > 0)
        {
            _auth = new OpenAIAuthentication(_globalSettings.Ais[0].ApiKey);
            _settings = new OpenAISettings(_globalSettings.Ais[0].Host); 
            _client = new OpenAIClient(_auth, _settings);
            _hasAi = true;
        }
    }

    public async Task<string?> GetAnswer(string m)
    {
        if (!_hasAi) return null;
        var messages = new List<Message>
        {
            new Message(Role.System,"无论是单选题还是多选题，只告诉我答案即可，比如ABC、A等"),
            new Message(Role.User, m)
        };
        var chatRequest = new ChatRequest(messages, _globalSettings.Ais[0].ModelName, 0.3);
        var response = await _client.ChatEndpoint.GetCompletionAsync(chatRequest);
        return response.FirstChoice.Message.Content.ToString();
    }
    
    public void Dispose()
    {
        _client.Dispose();
    }
}
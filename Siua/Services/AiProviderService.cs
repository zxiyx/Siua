using System;
using System.Linq;

namespace Siua.Services;

public record AiProvider(string Provider, string Domain);
public static class AiProviderService
{
    public static readonly AiProvider[] Providers = new[]
    {
        new AiProvider("DeepSeek", "api.deepseek.com"),
        new AiProvider("阿里Qwen", "dashscope.aliyuncs.com/compatible-mode"),
        new AiProvider("腾讯混元", "api.hunyuan.cloud.tencent.com"),
        new AiProvider("Kimi", "api.moonshot.cn"),
        new AiProvider("豆包", "ark.cn-beijing.volces.com"),
    };

    /*
    public static AiProvider Get(string providerName) =>
        Providers.FirstOrDefault(p => p.Provider.Equals(providerName, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"未找到提供商: {providerName}");
    */
    
    
}
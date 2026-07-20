using System;
using System.Linq;

namespace Siua.Services;

/// <summary>描述一个 AI 服务商及其默认接口地址。</summary>
public record AiProvider(string Provider, string Domain);

/// <summary>提供内置 AI 服务商配置。</summary>
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

}

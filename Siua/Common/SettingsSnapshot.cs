using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Siua.Common;

/// <summary>保存应用设置的持久化快照。</summary>
internal sealed class SettingsSnapshot
{
    public string CurrentPlatform { get; set; } = "学习通";
    public string Pix2TextExecutablePath { get; set; } = Path.Combine("Pix2TextRuntime", "Scripts", "p2t.exe");
    public string Pix2TextHost { get; set; } = "127.0.0.1";
    public int Pix2TextPort { get; set; } = 8503;
    public string BrowserCannel { get; set; } = "系统默认";
    public bool JumpCompleted { get; set; } = true;
    public int ChapterJumpInterval { get; set; } = 2000;
    public int AiAnsweringInterval { get; set; } = 2000;
    public bool TryFinishVideo { get; set; }
    public bool IsMuted { get; set; } = true;
    public int PopupTimeout { get; set; } = 1500;
    public double VideoPlayRate { get; set; } = 1.0;
    public bool UsedAiToOcr { get; set; }
    public bool AutoTest { get; set; }
    public Dictionary<string, string[]> CoursesByPlatform { get; set; } = [];

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string[]? Courses { get; set; }
    public AiSettingsSnapshot CurrentAi { get; set; } = new();
}

/// <summary>保存 AI 服务连接配置的持久化快照。</summary>
internal sealed class AiSettingsSnapshot
{
    public string? AiProvider { get; set; }
    public string? Domain { get; set; }
    public string? ModelName { get; set; }
    public string? ApiKey { get; set; }
}

/// <summary>表示设置文件的读取结果。</summary>
internal sealed record SettingsLoadResult(SettingsSnapshot? Snapshot, bool ShouldRewrite);

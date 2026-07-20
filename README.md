# Siua

> 基于 Avalonia、SukiUI 与 Playwright 构建的桌面端在线课程学习辅助工具。

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-12.1-8B44AC?style=flat-square)](https://avaloniaui.net/)
[![Playwright](https://img.shields.io/badge/Playwright-1.61-2EAD33?style=flat-square&logo=playwright)](https://playwright.dev/dotnet/)
[![License](https://img.shields.io/github/license/zxiyx/Siua?style=flat-square)](LICENSE)

Siua 将课程地址管理、浏览器自动化、视频学习、章节测试、OCR 和 AI 答题整合到一个 SukiUI 风格的桌面应用中。项目目前重点适配学习通和智慧树，并为不同平台提供独立的课程列表与执行流程。

> [!IMPORTANT]
> 本项目仅供技术研究与学习交流。使用前请确认相关平台规则、课程要求及所在地法律法规，并对自己的账号和操作负责。自动化结果会受平台页面更新、网络状况和模型输出影响，请勿将其视为无人值守或结果保证。

## 平台支持

| 平台 | 当前状态 | 已实现能力 |
| --- | --- | --- |
| 学习通 | 可用 | 课程遍历、视频播放、文档任务、章节测试、OCR/AI 答题 |
| 智慧树 | 可用 | 目录展开、视频进度判断与播放、章节测试新页面处理、OCR/AI 答题 |
| 中国大学 MOOC | 尚未适配 | 界面入口预留 |
| U 校园 | 尚未适配 | 界面入口预留 |

平台页面结构可能随时变化。如果自动化流程失效，请先保留日志和页面截图，再通过 Issue 反馈。

## 主要功能

- 按平台分别管理多个课程地址，启动时可选择本次需要执行的课程。
- 使用 `XxtRunner` 与 `ZhsRunner` 隔离学习通、智慧树的执行逻辑。
- 自动遍历章节和小节，支持跳过已完成任务、章节切换间隔和弹窗等待时间。
- 支持视频静音、播放倍速和尝试快速完成视频等策略。
- 学习通支持视频、文档和章节测试任务。
- 智慧树支持目录折叠项展开、SVG 视频进度识别和独立章节测试页面。
- 章节测试可截图后交给本地 Pix2Text 或已配置的 AI 模型识别，再由 AI 分析答案并匹配选项。
- Pix2Text 支持从应用内安装、启动、停止，并可配置监听 IP 和端口。
- 支持 DeepSeek、阿里 Qwen、Kimi、腾讯混元、豆包及自定义 OpenAI 兼容接口。
- 设置自动保存；日志按系统、浏览器、学习通、智慧树和 Pix2Text 等来源分类。

## 运行环境

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Edge 或系统中可用的 Chromium 内核浏览器
- 可正常访问课程平台的网络环境
- 可选：AI 服务的 API Key，用于章节测试分析和 AI 图像识别
- 可选：[uv](https://docs.astral.sh/uv/)，用于从应用内安装 Pix2Text

安装 Pix2Text 时，程序会优先复用系统中可用的 Python；未检测到 Python 时，才会通过 uv 准备 Python 3.11。独立运行环境默认放在软件目录下的 `Pix2TextRuntime`。

## 快速开始

### 1. 获取源码

```powershell
git clone https://github.com/zxiyx/Siua.git
cd Siua
```

### 2. 还原依赖并运行

```powershell
dotnet restore
dotnet run --project .\Siua\Siua.csproj
```

### 3. 完成首次配置

1. 在“设置”中选择学习平台，并为该平台添加一个或多个课程地址。
2. 返回“开始”页，选择本次要执行的课程。
3. 根据需要设置浏览器、视频策略、流程间隔和弹窗等待时间。
4. 如需自动处理章节测试，配置 AI 模型，并选择 Pix2Text 或 AI 图像识别。
5. 使用 Pix2Text 时，先点击“安装”，安装完成后点击“启动”。
6. 点击“执行课程”打开课程页面，按页面提示完成登录，然后点击“启动学习”。

课程地址必须与当前平台匹配：

- 学习通：`chaoxing.com` 或 `xuexitong.com`
- 智慧树：`zhihuishu.com` 或 `zhidao.com`

## Pix2Text

Pix2Text 用于识别题目截图中的普通文字和数学公式。默认服务地址为：

```text
http://127.0.0.1:8503/
```

可以在“设置”页修改监听 IP 与端口。开始页提供以下操作：

- **安装**：在软件目录创建独立的 Pix2Text 运行环境并安装 HTTP 服务依赖。
- **启动**：启动本地识别服务，并等待模型加载完成。
- **停止**：结束由应用启动的服务，同时尝试释放对应监听端口。

首次安装和首次加载模型耗时通常较长。出现识别失败时，请在日志页筛选“Pix2Text”来源查看服务输出。

## AI 模型配置

在“设置 → AI 模型设置”中选择服务商并填写 API Key 与模型名称。选择“自定义”时，可以填写 OpenAI 兼容接口地址。

API Key 会保存在本机设置文件中，不会提交到仓库。请勿在 Issue、日志截图或提交记录中公开密钥。

## 常用策略

| 设置项 | 作用 |
| --- | --- |
| 跳过已完成 | 遍历任务时忽略已经完成的内容 |
| 尝试完成视频 | 尝试直接推进当前视频；失败时继续正常播放 |
| 静音播放 | 控制课程视频的静音状态 |
| 视频倍速 | 设置视频播放速率 |
| 弹窗等待 | 等待平台确认框等弹窗出现的时间 |
| 章节间隔 | 完成当前内容后进入下一章节前的等待时间 |
| 答题请求间隔 | 控制连续 AI 请求之间的等待时间 |
| AI 识图 | 使用 AI 模型代替本地 Pix2Text 识别题目截图 |
| 自动答题 | 启用章节测试识别、答案分析与选项匹配流程 |

## 项目结构

```text
Siua/
├─ Siua/
│  ├─ Core/
│  │  ├─ xxt/          # 学习通页面解析与 XxtRunner
│  │  └─ zhs/          # 智慧树页面解析与 ZhsRunner
│  ├─ Services/        # 浏览器、AI、Pix2Text、日志与设置服务
│  ├─ ViewModels/      # MVVM 视图模型
│  └─ Views/           # Avalonia / SukiUI 界面
├─ Test/               # 平台页面与 Pix2Text 的实验性控制台项目
├─ Siua.sln
└─ README.md
```

## 构建

```powershell
dotnet build .\Siua\Siua.csproj
```

发布 Windows 版本：

```powershell
dotnet publish .\Siua\Siua.csproj -c Release
```

主项目使用 .NET 10、Avalonia 12、SukiUI、CommunityToolkit.Mvvm、Microsoft.Playwright 和 OpenAI-DotNet。

## 参与贡献

欢迎提交 Issue 或 Pull Request。反馈平台适配问题时，建议附上：

- 平台与课程页面类型
- 可复现的操作步骤
- Siua 日志（请先移除账号、课程和 API Key 等敏感信息）
- 相关页面结构变化或脱敏后的截图

## 许可证

本项目基于 [MIT License](LICENSE) 开源。

---

Made by [zxiyx](https://github.com/zxiyx)

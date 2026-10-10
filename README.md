<p align="center">
  <img src="./Siua/Assets/siua.png" width="128" height="128" alt="Siua Logo">
</p>

<h1 align="center">Siua</h1>

<p align="center">
  <strong>简洁、可配置的在线课程学习辅助工具</strong>
  <br>
  使用 Avalonia、SukiUI 与 Playwright 构建
</p>

<p align="center">
  <a href="https://github.com/zxiyx/Siua/releases"><img src="https://img.shields.io/github/v/release/zxiyx/Siua?style=flat-square&color=ff7a1a" alt="Latest Release"></a>
  <a href="https://github.com/zxiyx/Siua/stargazers"><img src="https://img.shields.io/github/stars/zxiyx/Siua?style=flat-square" alt="GitHub Stars"></a>
  <a href="./LICENSE"><img src="https://img.shields.io/github/license/zxiyx/Siua?style=flat-square" alt="License"></a>
  <img src="https://img.shields.io/badge/.NET-8%20%2F%2010-512BD4?style=flat-square&logo=dotnet" alt=".NET 8 / 10">
  <img src="https://img.shields.io/badge/Platform-Windows%20x64-0078D4?style=flat-square" alt="Windows x64">
</p>

<p align="center">
  <a href="#功能特性">功能特性</a> ·
  <a href="#平台支持">平台支持</a> ·
  <a href="#快速开始">快速开始</a> ·
  <a href="#配置说明">配置说明</a> ·
  <a href="#日志与常见问题">日志与常见问题</a> ·
  <a href="#本地开发与发布">本地开发与发布</a>
</p>

---

Siua 是一款面向 Windows 的桌面端课程学习辅助工具，将课程管理、账号管理、视频策略、章节测试、本地 OCR 与 AI 答题集中在统一界面中。目前适配学习通和智慧树，两个平台使用独立的任务执行流程。

本文按当前源码说明功能，已发布安装包的功能及系统要求请以 [Releases](https://github.com/zxiyx/Siua/releases) 中对应版本的说明为准。

> 本项目用于技术研究与学习交流。使用时请遵守课程要求、平台条款与相关法律法规。平台页面变化、网络状态和模型输出可能影响执行结果。

## 功能特性

<table>
  <tr>
    <td width="50%" valign="top">
      <h3>🎓 多平台课程管理</h3>
      <p>学习通、智慧树的课程地址独立保存。支持添加多个课程，并在开始页选择本次执行的课程。</p>
    </td>
    <td width="50%" valign="top">
      <h3>🔐 账号与自动登录</h3>
      <p>按平台管理账号、密码和备注。学习通支持选择账号自动登录，密码通过当前 Windows 用户加密保存。</p>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>▶️ 视频与章节流程</h3>
      <p>支持静音、倍速、跳过已完成任务点、章节间隔和尝试快速完成视频；学习通可处理视频、文档及章节测试。</p>
    </td>
    <td width="50%" valign="top">
      <h3>🤖 本地 OCR 与 AI 答题</h3>
      <p>Pix2Text 识别截图中的文字与数学公式，再由已配置的 AI 模型分析答案。支持逐题截图和整份章节测试区域截图。</p>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>🧩 多种答题方式</h3>
      <p>支持 AI 答题与随机答题。学习通适配选择题、判断题、填空题及已适配文本控件的“其他题”。</p>
    </td>
    <td width="50%" valign="top">
      <h3>🛠️ 安装与排错</h3>
      <p>软件内可安装 Pix2Text 并自动准备 uv、Python。界面显示简洁日志，详细错误写入运行目录的 Log/Log.txt。</p>
    </td>
  </tr>
</table>

## 平台支持

| 功能 | 学习通 | 智慧树 |
| :--- | :--- | :--- |
| 课程管理与视频任务 | 支持 | 支持 |
| 文档任务 | 支持 | 暂未适配 |
| 章节测试 | 支持 | 支持 |
| AI 逐题答题 / 区域截图答题 | 支持 | 支持已适配的选择类题目 |
| 填空题、“其他题”文本填写 | 支持已适配控件 | 暂未适配 |
| 随机答题 | 随机选项；填空和文本题随机填入汉字或数字 | 随机选择已识别的选项 |
| 账号管理 | 支持 | 支持保存账号 |
| 账号密码自动登录 | 支持 | 暂需手动登录 |

中国大学 MOOC、U 校园等平台暂未适配。实际支持范围取决于课程页面和题目控件结构，不能保证所有课程模板均可使用。

## 快速开始

### 下载与环境

前往 [Releases](https://github.com/zxiyx/Siua/releases) 下载适合系统的 Windows x64 安装包。项目提供 .NET 10 和 .NET 8 构建目标，请结合发布页说明选择版本。

- 使用安装包或自包含发布版本时，不需要自行安装 .NET SDK；SDK 仅用于源码构建。
- 建议安装 Microsoft Edge，也可选择已安装的 Chromium 内核浏览器作为系统默认浏览器。
- 使用 AI 答题时，需要可用的模型接口、API Key，以及 Pix2Text 本地服务。
- 无需预先手动安装 uv 或 Python，软件内的 Pix2Text 安装按钮会按需准备环境。
- Pix2Text依赖MSVC++环境，如安装过程提示 msvc++14.0 or greater is required则需自行下载Microsoft C++ Build Tool
- 安装包中的程序、DLL、配置文件和 `.playwright` 目录应保持完整，不要只移动 `Siua.exe`。

### 首次使用

1. **选择平台**：在“开始”页选择学习通或智慧树。
2. **添加课程**：进入“设置 → 课程与浏览器 → 管理课程”，保存进入课程后的完整地址。
3. **配置登录**：需要学习通自动登录时，在“管理账号”中保存账号；返回开始页，勾选“自动登录”并选择账号。不启用时，在浏览器中手动登录。
4. **选择执行课程**：在开始页的“执行课程”下拉框中选择课程，并按需要调整视频和流程策略。
5. **选择答题方式**：需要 AI 答题时，先配置模型，在 Pix2Text 一栏依次点击“安装”和“启动”，再启用“自动答题”。随机答题无需 AI 或 OCR。
6. **启动任务**：点击“启动学习”，软件打开浏览器并跳转到日志页；完成登录后继续执行任务。需要结束时，返回开始页点击“停止任务”。

### 课程地址

请复制进入课程后的学习页面地址，不要填写平台首页、扫码页或登录页。

| 平台 | 填写建议 |
| :--- | :--- |
| 学习通 | 使用 `mooc1.chaoxing.com` 下实际课程学习页的完整地址，保留页面自带参数 |
| 智慧树 | 使用实际课程学习地址；地址校验接受 `zhihuishu.com`、`zhidao.com` 及其子域名 |

地址通过域名校验不代表该页面就是可执行任务的课程页面。

## 配置说明

### 账号管理与自动登录

入口：**设置 → 课程与浏览器 → 管理账号**。

- 支持添加、编辑和删除账号，账号备注可不填。
- 编辑已有账号时，密码留空表示保留原密码。
- 自动登录开关和所选账号按平台保存；删除当前选中的账号会关闭对应平台的自动登录。
- 学习通自动登录会填写账号密码并提交一次。验证码、滑块或二次验证需要在浏览器中手动完成，软件不会反复提交登录。
- 密码使用 Windows 用户级加密保存，复制设置到其他电脑或其他 Windows 用户后，可能需要重新填写密码。

### 视频与流程

| 设置项 | 说明 |
| :--- | :--- |
| 静音播放 | 控制视频静音状态 |
| 尝试快速完成 | 尝试推进视频进度，未成功时继续正常播放；结果取决于平台限制 |
| 播放倍速 | 设置视频播放速率，建议使用平台允许的范围 |
| 跳过已完成任务点 | 跳过已完成的视频、文档等任务点 |
| 弹窗等待 | 调整处理提示弹窗的等待时间，单位为毫秒 |
| 章节间隔 | 调整章节切换间隔，单位为毫秒 |
| 答题请求间隔 | 在“设置 → AI 与答题”中调整 AI 答题间隔，单位为毫秒 |

学习通页面手动切换小节时，软件会中止旧小节的处理并重新解析当前小节。平台弹出必须作答的验证题时，应在浏览器中完成后再继续。

### 答题与区域截图

“自动答题”和“随机答题”互斥，启用其中一项会关闭另一项。

| 方式 | 处理流程 |
| :--- | :--- |
| AI 逐题答题 | 逐题截图 → Pix2Text 识别 → AI 分析 → 填写答案 |
| AI 区域截图答题 | 截取整份章节测试区域 → Pix2Text 识别 → AI 批量分析 → 按题目填写 |
| 随机答题 | 不调用 OCR 或 AI，直接随机选择选项或填写已支持的输入框 |

“区域截图”位于 **设置 → AI 与答题**，默认关闭，仅影响 AI 答题。学习通的填空题和已适配的“其他题”文本控件，在逐题模式与区域模式下均可处理。

区域截图适合整份测试能完整加载的页面，可减少逐题识别与请求次数，但也可能受页面长度、渲染状态及模型上下文限制影响。题目识别、截图、答案解析或填写失败时，请查看日志处理，不要将失败视作任务已经完成。

AI 返回的答案会按题号记录到软件日志，例如“第 3 题答案为：A”。当前版本使用 **Pix2Text 做截图识别、AI 做文本答案分析**，不再提供独立的 AI 识图开关。

### Pix2Text 本地服务

在开始页的“答题与文字识别”卡片中操作 Pix2Text：

| 操作 | 说明 |
| :--- | :--- |
| 安装 | 检查已有环境；需要时自动下载 uv、Python 3.11 和 Pix2Text，安装后验证服务依赖 |
| 启动 | 使用运行环境中的 Python 启动服务，等待模型加载并检查接口是否就绪 |
| 停止 | 停止由当前软件启动的服务进程，不强制结束其他程序占用的端口 |

安装程序优先尝试国内镜像：uv / Python 使用中科大镜像；Python 包依次尝试清华、中科大和官方 PyPI。失败时会重试并切换来源，下载缓存会保留以便再次安装。

常用位置：

| 内容 | 默认位置 |
| :--- | :--- |
| Pix2Text 独立环境 | 软件运行目录下的 `Pix2TextRuntime` |
| 自动下载的 uv / Python | `%LOCALAPPDATA%\Siua\Tools` |
| 安装日志 | 软件运行目录下的 `Log\Pix2Text-install-*.log` |

脚本会配置当前用户的 PATH。软件启动 Pix2Text 使用明确的 Python 路径，不依赖重新启动电脑来刷新 PATH。

默认服务地址为 `http://127.0.0.1:8503/`，监听 IP 和端口可在“设置 → Pix2Text 本地服务”中修改。首次安装和首次加载模型需要联网，下载时间取决于网络及模型文件大小。

若加载超时但进程仍在运行，再次点击“启动”会继续等待已有进程，不重复启动。失败详情会写入 `Log\Log.txt`；服务就绪后不记录 OCR 识别正文。

### AI 模型与连通测速

入口：**设置 → AI 与答题 → 配置模型**。

支持 DeepSeek、阿里 Qwen、Kimi、腾讯混元、豆包，以及自定义 OpenAI 兼容接口。填写接口地址、API Key 和模型名称后即可使用。

“API 连通测速”会在页面顶部显示“连通正常 / 错误（延迟 ms）”。该功能检查接口地址的 HTTP 可达性，**不验证 API Key、模型权限或余额**；收到 HTTP 错误响应也可能显示连通正常。

设置默认保存在 `%LOCALAPPDATA%\Siua\settings.json`。账号密码为加密数据，API Key 不应视为同等加密保护；不要直接公开设置文件。

## 日志与常见问题

软件内显示简洁的运行信息；出现错误时，到 **软件运行目录 → Log → Log.txt** 查看详细记录。这里的运行目录是 `Siua.exe` 所在目录，不是默认设置文件目录。

| 现象 | 排查方向 |
| :--- | :--- |
| Pix2Text 安装失败 | 查看 `Pix2Text-install-*.log` 中的下载、依赖或文件权限错误；重试可复用缓存 |
| Pix2Text 启动退出码为 `-1` | 查看 `Log.txt` 中的 Python 路径、退出码和启动输出；仅凭退出码无法判断原因 |
| Pix2Text 长时间加载 | 首次运行可能仍在下载模型；若提示进程保留，可再次点击启动继续等待 |
| 端口被占用 | 修改监听端口，或在启动该服务的程序中停止它 |
| 账号保存失败 | 查看 `[Account]` 日志，区分密码加密、设置写入和界面更新等阶段 |
| 安装版无法保存密码，Debug 正常 | 检查发布和安装文件是否完整，尤其是 `System.Security.Cryptography.ProtectedData.dll` |
| 连通测速正常，但 AI 答题失败 | 继续核对 API Key、模型名称、余额和接口兼容性 |
| 截图或章节处理失败 | 保留出错题号、日志和页面结构，检查页面是否完整加载或已经切换 |

Pix2Text 启动输出保留末尾最多 200 行、65536 字符，用于排查加载异常。账号保存日志会过滤输入的账号、密码和备注，但课程日志可能包含题目答案及页面信息；反馈前请自行检查并脱敏。

## 本地开发与发布

### 从源码运行

安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。项目包含 `net10.0` 和 `net8.0` 两个目标，可使用 .NET 10 SDK 构建，运行时需指定框架：

```powershell
git clone https://github.com/zxiyx/Siua.git
cd Siua
dotnet restore .\Siua\Siua.csproj
dotnet run --project .\Siua\Siua.csproj -f net10.0
```

使用 .NET 8 目标时，将 `-f net10.0` 改为 `-f net8.0`。构建命令：

```powershell
dotnet build .\Siua\Siua.csproj -c Release -f net10.0
```

### 发布 Windows x64

以下示例生成自包含的目录式发布结果，便于核对安装包文件：

```powershell
dotnet publish .\Siua\Siua.csproj -c Release -f net10.0 -r win-x64 --self-contained true -p:PublishSingleFile=false -o .\artifacts\win-x64\net10.0

# .NET 8 构建
dotnet publish .\Siua\Siua.csproj -c Release -f net8.0 -r win-x64 --self-contained true -p:PublishSingleFile=false -o .\artifacts\win-x64\net8.0
```

制作安装包时，应以同一次发布生成的**完整输出目录**作为文件来源，包含 DLL、运行时配置及 `.playwright` 等子目录，不要混用不同构建的文件或只打包 EXE。

目录式发布中，`System.Security.Cryptography.ProtectedData.dll` 是账号密码加密所需依赖，必须保留。`Avalonia.Diagnostics.dll` 属于调试依赖，Release 中不包含它通常是正常的。

`Siua/Scripts/InstallPix2Text.ps1` 会作为资源嵌入程序，由软件内的安装按钮释放并调用；用户无需另外寻找脚本。也可将该脚本放到软件目录中单独运行，或指定安装目录：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Siua\Scripts\InstallPix2Text.ps1 -InstallDirectory "D:\Siua"
```

独立运行脚本前请退出 Siua。安装目标应与实际使用的软件目录一致。

### 技术栈与结构

| 组件 | 用途 |
| :--- | :--- |
| .NET 8 / 10、C# | 应用运行时与主要开发语言 |
| Avalonia 12、SukiUI 7 | 桌面界面、主题与控件 |
| CommunityToolkit.Mvvm | MVVM、命令与属性通知 |
| Microsoft Playwright | 浏览器自动化、截图与页面交互 |
| Pix2Text | 本地文字与数学公式识别 |
| OpenAI-DotNet | OpenAI 兼容模型接口调用 |

```text
Siua/
├─ Siua/
│  ├─ Common/         # 账号、设置快照与共用模型
│  ├─ Core/
│  │  ├─ xxt/        # 学习通页面解析与任务流程
│  │  └─ zhs/        # 智慧树页面解析与任务流程
│  ├─ Services/      # 浏览器、登录、AI、OCR、日志与持久化
│  ├─ Scripts/       # 内置 Pix2Text 安装脚本
│  ├─ ViewModels/    # 页面状态与命令
│  ├─ Views/         # Avalonia / SukiUI 界面
│  └─ Assets/        # 图标与应用资源
├─ Test/             # 平台页面与功能实验
├─ Siua.sln
└─ README.md
```

## 参与贡献

欢迎通过 [Issues](https://github.com/zxiyx/Siua/issues) 反馈问题，或提交 Pull Request。反馈时建议附上：

- 软件版本、Windows 版本，以及使用的 .NET 10 / .NET 8 安装包。
- 平台名称、课程页面类型和可复现步骤。
- 脱敏后的 `Log.txt`；安装问题请同时提供 Pix2Text 安装日志。
- 涉及控件识别时，提供相关页面 HTML、内层 iframe HTML 或截图。

## 许可证

本项目基于 [MIT License](./LICENSE) 开源。

<p align="center">
  Made with 🧡 by <a href="https://github.com/zxiyx">zxiyx</a>
</p>

# Siua 🎓

> 基于 Playwright + Avalonia的自动化刷课/答题软件

![C#](https://img.shields.io/badge/C%23-.NET%208-512BD4?style=flat-square&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-.NET%208-512BD4?style=flat-square&logo=dotnet)
![Playwright](https://img.shields.io/badge/Playwright-Automation-2EAD33?style=flat-square)
![License](https://img.shields.io/github/license/zxiyx/Siua?style=flat-square)

## 📋 项目简介

**Siua** 是一款基于 Playwright 开发的自动化刷课工具，旨在帮助用户自动完成在线课程的视频播放、章节测验等重复性操作，提升学习效率。

> ⚠️ **免责声明**：本项目仅供学习交流使用，请遵守相关平台的使用条款和法律法规。作者不对任何滥用行为承担责任。

## ✨ 功能特性
### 🎬 自动化课程学习
- 自动识别课程章节并顺序播放
- 等待视频加载与播放完成
- 可配置的等待时间与操作策略
### 🧠 AI 智能答题
- 支持配置多个 AI 模型接入（OpenAI / Kimi / 自定义接口）
- 支持下载离线OCR模型用于题目文字识别
- 自动提取题目文本，调用大模型生成答案
- 暂仅支持单选题/多选题
- 数学题需要自行配置可识别图片的AI模型

### 📅 计划中
- [ ] 适配知到智慧树
- [ ] 多账号管理
- [ ] 答题得分显示
- [ ] 移动端
## 🛠️ 技术栈

| 技术 | 说明 |
|------|------|
| **Avalonia** | C#跨平台框架 |
| **.NET 8** | 运行时框架,提供高性能与长期支持 |
| **MVVM架构** | 界面与逻辑分离，支持数据绑定与命令模式 |
| **DI 依赖注入** | 使用 `Microsoft.Extensions.DependencyInjection` 管理服务生命周期 |
| **微服务设计** | 答题服务、浏览器控制、配置管理等模块解耦，支持独立部署 |
| **配置热更新** | 运行时修改参数，无需重启 |
| **Playwright for .NET** | 浏览器自动化控制框架 |
| **Rider / Visual Studio** | 推荐开发环境 |

## 📦 环境要求

- .NET 8 SDK 或更高版本
- Windows 10/11
- 稳定的网络连接（用于加载课程页面）
- （可选）AI 模型 API Key（用于智能答题）

## 🚀 快速开始

### 克隆项目
```bash
git clone https://github.com/zxiyx/Siua.git
cd Siua
```



## 💬 反馈与支持
| 渠道 | 链接/地址 | 用途 |
|------|-----------|------|
| **GitHub Issues** | [提交 Issue](https://github.com/zxiyx/Siua/issues) | Bug 报告、功能请求 |
| **电子邮件** | [2230861629@qq.com](mailto:2230861629@qq.com) | 合作、私人咨询 |


> 🌟 **如果本项目对你有帮助，欢迎 Star ⭐ 支持！**
> 
> 你的鼓励是我持续更新的最大动力～
> 
> ---
> 
> Made by [zxiyx](https://github.com/zxiyx)
> 
> [![GitHub](https://img.shields.io/badge/GitHub-zxiyx-181717?style=for-the-badge&logo=github)](https://github.com/zxiyx)

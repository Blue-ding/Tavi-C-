# Tavi

Tavi 正在从 Unity C# 代码包迁移为独立的 .NET 后端。当前入口是命令行程序，核心代码不依赖具体前端。

## 目录

```text
src/
  Tavi.Domain/                    世界图等领域模型
  Tavi.Application/               应用服务与 LLM 契约
  Tavi.Infrastructure.OpenAI/     OpenAI SDK 适配
  Tavi.Infrastructure.Persistence/  本地持久化适配
  Tavi.Cli/                       命令行入口
tests/
  Tavi.Domain.Tests/
  Tavi.Application.Tests/
legacy/
  Tavi.Unity/                     原 Unity 代码和元数据，仅供迁移比对
```

## 开发环境

- .NET 8 SDK
- JetBrains Rider

在 Rider 中打开根目录的 `Tavi.sln`。命令行构建方式：

```powershell
dotnet restore Tavi.sln
dotnet build Tavi.sln
dotnet test Tavi.sln
```

本项目的活动代码不依赖 Newtonsoft.Json；持久化模块使用 `System.Text.Json`。

## 语言模型配置

Application 的模型运行策略保存在本地 `Tavi/Settings/language-model.json`，其中不包含供应商密钥。
CLI 通过环境变量显式组装 OpenAI 适配器：

```text
TAVI_OPENAI_API_KEY       必需，OpenAI 或兼容服务密钥
TAVI_OPENAI_MODEL         必需，模型名称
TAVI_OPENAI_ENDPOINT      可选，默认为 https://api.openai.com/v1
TAVI_OPENAI_CLIENT_TYPE   可选，chat（默认）或 responses
```

未设置前两个变量时，CLI 仍可运行，但不会创建语言模型服务。

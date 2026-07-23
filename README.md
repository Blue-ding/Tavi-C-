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

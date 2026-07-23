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
  Tavi.Host/                      本地 HTTP/SSE 宿主与 ViewModel 展示层
  Tavi.Web/                       React 世界图工作台
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

## 世界图工作台

首次运行前端时安装依赖并构建静态资源：

```powershell
cd src/Tavi.Web
npm install
npm run build
cd ../..
dotnet run --project src/Tavi.Host
```

随后访问 `http://127.0.0.1:5178`。Host 默认加载本地 `Tavi/Saves/default` 存档；可使用 `TAVI_SAVE_DIRECTORY` 覆盖存档目录。前端支持 Anchor、Relation 和 Character 子世界的图形化观察与编辑，以及撤销、重做、手动保存和自动保存状态反馈。配置语言模型后，还可以从顶部工具栏打开 Guidance，通过对话生成、逐项审阅并原子提交世界提案。

## 语言模型配置

Application 的模型运行策略保存在本地 `Tavi/Settings/language-model.json`，其中不包含供应商密钥。OpenAI 兼容服务配置保存在同目录的敏感本地文件 `openai.json`：

```json
{
  "version": 1,
  "endpoint": "https://兼容服务地址",
  "model": "模型名称",
  "api_key": "本地密钥",
  "client_type": "chat",
  "supports_required_tool_choice": false,
  "enable_thinking": false
}
```

Windows 默认目录为 `%LOCALAPPDATA%\Tavi\Settings`，可使用 `TAVI_SETTINGS_DIRECTORY` 覆盖整个设置目录，也可使用 `TAVI_OPENAI_CONFIGURATION_PATH` 单独覆盖 OpenAI 配置文件。`openai.json` 包含 API Key，不应提交、同步或输出到日志。该文件缺失时 CLI 和 Host 仍可运行，但不会创建语言模型服务；世界图编辑功能不受影响。

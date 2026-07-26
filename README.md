# Tavi

Module 与 Plugin 的声明格式、能力接口和推荐实践见 [Module 接入规范](docs/modules.md)。

Tavi 正在从 Unity C# 代码包迁移为独立的 .NET 后端。当前入口是命令行程序，核心代码不依赖具体前端。

## 目录

```text
src/
  Tavi.Domain/                    EARS、World、Scenario 等领域模型
  Tavi.Utilities/                 乐观并发与版本化工作区等通用机制
  Tavi.Application/               应用服务与 LLM 契约
  Tavi.Infrastructure.OpenAI/     OpenAI SDK 适配
  Tavi.Infrastructure.Persistence/  本地持久化适配
  Tavi.Cli/                       命令行入口
  Tavi.Host/                      本地 HTTP/SSE 宿主与 ViewModel 展示层
  Tavi.Web/                       React 世界图工作台
  Tavi.App/                       .NET MAUI 桌面外壳
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

随后访问 `http://127.0.0.1:5178`。Host 默认加载本地 `Tavi/Saves/default` 存档；可使用 `TAVI_SAVE_DIRECTORY` 覆盖存档目录。前端支持 Element、Scope、Aspect 和 Relation 插件式断言图的观察与编辑，以及撤销、重做、手动保存和自动保存状态反馈。规则化 Scope、Aspect 与 Relation 的语义由 Module Type Definition 提供，Quantity 必须是整数；自由 Name/Description 只用于不会进入 Scenario 或 Evolution 的 World Local 语义。配置语言模型后，还可以从顶部工具栏打开 Guidance，通过对话生成、逐项审阅并原子提交世界提案。

## Windows 桌面应用

Windows 外壳使用 .NET MAUI WebView 承载同一套 React 前端，并在应用进程内启动
`Tavi.Host`。Host 只监听随机的 `127.0.0.1` 回环端口；窗口关闭时会一并停止，
不会额外打开控制台窗口，也不会占用固定端口。

首次构建前安装 Windows MAUI workload：

```powershell
dotnet workload install maui-windows
dotnet build src/Tavi.App/Tavi.App.csproj
dotnet run --project src/Tavi.App/Tavi.App.csproj
```

构建桌面项目时会自动执行 `Tavi.Web` 的前端构建，并将静态资源复制到桌面应用
输出目录。当前项目只启用 Windows TFM；未来开始 Android 或 macOS 适配时，再在
`Tavi.App.csproj` 中追加对应目标框架和 workload。

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

## Host 日志

Host 会同时向控制台和本地滚动文件输出结构化日志。Windows 默认日志目录为 `%LOCALAPPDATA%\Tavi\Logs`，可使用配置键 `Tavi:Logging:Directory` 或环境变量 `TAVI_LOG_DIRECTORY` 覆盖，其中配置键优先。文件采用每行一个 JSON 事件的 `.jsonl` 格式，按日期和 20 MB 大小滚动，并在启动时清理最后修改时间超过 14 天的 `tavi-*.jsonl` 文件。

持久化日志初始化失败不会阻止 Host 启动；此时应急诊断和后续日志仍会输出到标准错误与控制台。日志不得包含 API Key、Authorization 请求头、完整用户提示词、世界正文或存档正文。

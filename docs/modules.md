# Tavi Module、Scenario 与 Extension 接入规范

## 责任边界

Module 是带稳定身份、版本、依赖和可选行为参数的纵向语义包。Plugin 是由受信宿主显式构造的代码载体。`Tavi.Extensibility` 只公开稳定 SDK；`Tavi.Application.Extension` 管理启停、参数和冻结快照；`Tavi.Application.Scenario` 管理静态、玩家显式驱动的 Scenario 与 Scene。

当前基础设施不包含自动演化、时间周期或自动选择。未来的 Evolution 应建立在 `IScenarioWorkspace` 之上，而不是把调度能力焊入 Module 或 ScenarioSession。

依赖方向固定如下：

```text
Tavi.Abstractions
  ↑
Tavi.Domain       Tavi.Extensibility
        ↑          ↑
        Tavi.Application
```

`Tavi.Domain.Scenario` 不加载 Module、不解析 JSON，也不调用 Plugin。Application 在隔离候选状态上执行 Module 语义校验后才提交真实 Scenario。

## Module 包

```text
Modules/MyModule/
  module.json
  semantics.json
  scenes.json
  writing.json
  README.md
```

`module.json` 必需，其他声明文件可省略。Schema 位于 `docs/schemas`。Module ID、版本、语义键、依赖和跨 Module AspectGroup 扩展规则保持严格校验。

纯声明式 Module 不设置 `entrypoint`。Loader 只读取入口文本，不根据 Manifest 自动执行程序集；受信宿主必须显式构造 `ITaviPlugin` 并交给 ExtensionSession。

## Extension 与参数

`ExtensionService` 加载 Module 包和 `IExtensionSettingsStore`，`ExtensionSession` 提供启用、禁用、依赖检查和参数修改。`Freeze()` 产生供新 ScenarioSession 使用的 `FrozenExtensionSnapshot`。

冻结后修改设置只会令 `RestartRequired` 变为 true，既有快照和 ScenarioSession 不改变。宿主应警告玩家重启并重新加载存档。

Module 参数只允许影响 SceneDefinition 可用性、规则选择、展示或其他运行行为。参数不得改变：

- JSON 存档结构；
- 稳定类型或语义键身份；
- 既有 EARS 数据的解释；
- Module 版本兼容性；
- 全局语义约束是否能解释既有存档。

上述变化必须通过 Module 版本和显式迁移完成，不能作为玩家参数。参数是 boolean、integer、number 或 string 标量，默认值和覆盖值使用规范字符串保存。

## SceneDefinition 权限

`ISceneDefinitionProvider` 可以读取完整、隔离的 `IScenarioView`，据此判断当前可以发生什么并返回动态 SceneDefinition。它不能修改 Scenario、创建 Scene 或保存上下文引用。

静态 Definition 来自 `scenes.json`。ScenarioSession 在玩家显式命令下实例化 Scene，并把 Definition 的槽位要求冻结到 Scene 存档中。因此后续绑定和处理不依赖调用方再次提交可伪造或过期的 Definition。

## Scene 生命周期

```text
Binding → Processing → Settled
```

- `Binding`：允许不完整绑定并持久化；可以绑定、解绑、重新绑定或删除 Scene。
- `Processing`：槽位已经完整并被冻结；不能修改绑定、删除 Scene或从外部修改其局部图。
- `Settled`：局部结果已经原子提交；活动 Element 归属释放，但 Scene 保留冻结绑定作为历史。

同一 Element 同时只能出现在一个未结算 Scene 的一个槽位中。该不变量由 Domain 强制，前端可以据此把已占用 Element 从桌面可用区域移入 Scene。

删除 Binding Scene 会释放其绑定。Processing Scene 不能删除。Settled Scene 可单独删除，也可由 `ClearSettledScenes` 批量清理。

通用 `IScenarioWorkspace.Commands.Apply` 只接受 EARS 操作。Scene 创建、绑定、状态转换、结算和清理必须使用专用命令，避免调用方绕过生命周期与局部权限 seam。

旧 V1 存档中的 `Preparing`/`Ready` 会迁移为 Binding，`AwaitingWriting` 会迁移为 Processing。旧存档没有保存完整槽位定义，因此旧 Binding Scene 不能重新进入 Processing，应删除并从当前 Definition 重建；旧 `Cancelled` Scene 没有无歧义迁移路径并会产生明确读取错误。

## 局部结算

Module 的全局读取能力止于 Definition 阶段。`ISceneRuleSettler` 只收到 `SceneContextView`，其中包含处理开始时冻结的 Element，以及完全位于局部边界内的 Scope、Aspect 和 Relation。

结算提案使用 `SceneSettlementProposal` 和 `SceneOperationIntent`：

- 可以更新或删除绑定的内部 Element；
- 可以修改内部 Element 所拥有的局部 EARS 数据；
- 可以创建新 Element，并在同一提案中为它创建 Scope、Aspect 和 Relation；
- 新 Relation 的两个端点和所属 Scope 必须位于当前局部边界；
- 不能根据 Definition 阶段看到的全局 ID 修改未绑定既有 Element。

Application 会逐项模拟局部 ID 集合，再把提案转换为 Domain 操作。随后先在隔离 Scenario 投影上执行完整结构和 Module 语义校验，最后把 Scene 结算与结果作为一个原子提交写入真实 Scenario。

删除内部 Element 引发的结构级联属于该内部 Element 的结算结果。Settled Scene 的历史绑定允许引用已经从活动 Scenario 删除的 Element ID。

## World 边界

`ScenarioWorldBridge.Import` 从确定的 WorldSnapshot 复制规则化 EARS 身份和数据，并记录来源 WorldStateId。World 中没有 Type 的 `LocalAspect` 与 `LocalRelation` 不会进入 Scenario，也不会从 World 中删除；Module 不参与导入。

`ScenarioWorldBridge.CreateProposal` 比较来源 World 的规则化 EARS 和 Scenario，产生 `ScenarioWorldProposal`。该方法不会提交 World；提案通过 `ScenarioWorldCoordinator` 进入 `WorldBuildSession` 的统一构筑日志，与玩家、Guidance 和 Module Authoring 修改共同接受冲突检测及玩家审批。Scenario 未包含 Local 语义不构成删除差异。

Scope、Aspect 与 Relation 实例只携带 Type 和整数 Quantity；它们的展示名称与语义说明来自相应 Type Definition。Local 变体只携带自由 Name、Description 和整数 Quantity，不对 Module 或 Evolution 暴露。

## Writing 扩展点

Module 可以用两种方式提供 Beat 正文：

- Plugin 在 `BeatResolutionProposal.Paragraphs` 中直接返回最终文本；
- `writing.json` 声明 Paragraph 模板、确定性 Binding 和需要语言模型填充的字段。

声明式正文使用 `beatNarrations` 将 BeatDefinition 映射到一个或多个 Paragraph。Binding 可以读取 Beat Slot、Aspect、Module Setting 或 `BeatResolutionProposal.Values` 中的结构化结果；`fills` 只描述确实需要模型生成的字段、提示和上下文。没有 `model` Fill 时不会调用语言模型。Schema 见 `docs/schemas/tavi-writing.schema.json`。

```json
{
  "schemaVersion": 1,
  "beatNarrations": [
    {
      "beatDefinition": "magic:encounter",
      "paragraphs": [
        {
          "key": "arrival",
          "template": "{{subject.name}}抵达{{place}}，{{detail}}",
          "bindings": {
            "subject": {
              "source": "beatSlot",
              "slot": "subject"
            },
            "place": {
              "source": "resolution",
              "key": "place"
            }
          },
          "fills": {
            "detail": {
              "kind": "model",
              "instruction": "用一句话描写{{subject.name}}眼前的景象。",
              "context": [
                "subject.description",
                "place",
                "interaction"
              ],
              "minimumLength": 1,
              "maximumLength": 80
            }
          }
        }
      ]
    }
  ]
}
```

Application 负责解析声明、构造受约束模型请求并生成最终 `BeatParagraph`；Runtime 只持有可热替换的语言模型 Adapter 和 Session 生命周期。Beat 创建时会冻结当时的 Writing Profile 并随存档保存，避免 Module 文件更新改变尚未解决的 Beat。Extension Settings 的 Profile、HTTP Interface 和前端状态模型与 Writing Profile 相互独立。

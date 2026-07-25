# Tavi Module 与 Plugin 接入规范

## 目标与边界

Module 是具有稳定身份、版本和依赖关系的纵向语义包。Plugin 是可选代码载体，用于实现声明式格式不适合表达的动态行为。一个 Module 可以完全由 JSON 构成；Plugin 可以为一个 Module 注册多项相互独立的能力。

依赖方向固定如下：

```text
Tavi.Abstractions
  ↑
Tavi.Extensibility

Tavi.Abstractions
  ↑
Tavi.Domain

Tavi.Domain + Tavi.Extensibility
  ↑
Tavi.Application
```

`Tavi.Domain.Scenario` 不加载 Module、不解析 JSON，也不调用 Plugin。`Tavi.Application.Evolution` 负责把声明式定义和 Plugin 提案适配为 Scenario 操作，并在完整候选状态上统一校验。

## 标准目录

```text
Modules/MyModule/
  module.json
  semantics.json
  scenes.json
  README.md
```

`module.json` 必需。`semantics.json` 和 `scenes.json` 可省略，省略时视为空定义。JSON Schema 位于 `docs/schemas`。

## 身份和版本

- Module ID 必须以小写 ASCII 字母开头，只能包含小写字母、数字、`-` 和 `_`。
- Module 版本使用严格的 `major.minor.patch`，首版不支持预发布或构建后缀。
- 语义键使用大小写敏感的 `module-id:local-name`。
- ElementType、ScopeType、AspectGroup、AspectType、RelationType、Constraint 和 SceneDefinition 必须由声明它们的 Module 命名空间拥有。
- Module 可以向其他 Module 的 AspectGroup 添加成员，但目标 Group 必须声明 `extensible: true`。

## Manifest

```json
{
  "id": "alchemy",
  "version": "1.0.0",
  "schemaVersion": 1,
  "name": "Alchemy",
  "description": "炼金语义与能力。",
  "dependencies": [
    {
      "id": "character",
      "minimumVersion": "1.0.0"
    }
  ],
  "entrypoint": "Alchemy.Plugin.AlchemyPlugin, Alchemy.Plugin"
}
```

纯声明式 Module 不设置 `entrypoint`。当前 Loader 读取并验证入口声明，但不会自动加载任意程序集；代码 Plugin 由受信宿主构造后调用 `EvolutionModuleCatalog.RegisterPlugin` 注册。这样避免仅凭存档或 JSON 执行不受信代码。

## 声明式语义

首版支持：

- Element、Scope、Aspect 和 Relation 类型；
- AspectGroup；
- 类型标签；
- Scope Owner、Aspect Subject、Relation Source/Target 类型限制；
- Aspect/Relation Quantity 范围；
- Owned Scope 基数约束；
- 指定 Scope 中 AspectGroup 的基数约束。

约束只在整个 `ScenarioChangeSet` 已经作用于隔离投影后执行。Module 不得依赖单项 Operation 的中间状态，否则无法原子创建需要多个 EARS 对象的语义实体。

自然语言字段仅供玩家、作者、诊断或语言模型使用。程序不得解析 `name`、`description` 或 `message` 执行规则。

## SceneDefinition

SceneDefinition 描述一个可填入 Element 并产生演绎的功能模板。静态定义放在 `scenes.json`；依赖当前 Scenario 的动态定义由 `ISceneDefinitionProvider` 返回。

槽位可以声明：

- 最小和最大 Element 数；
- 允许的 ElementType；
- Element 必须具有的 AspectGroup。

`settlement` 可以包含：

- `rules`：由 `ISceneRuleSettler` 产生确定性的 `ScenarioChangeProposal`；
- `writing`：允许进入独立 Writing 演绎路径。

规则结算与 Writing 必须使用不同入口。Writing 结果不会调用规则结算器，也不要求满足原 SceneDefinition 的预期后置目标；它只需要满足 Scenario 结构不变量和当前激活 Module 的全局语义约束。

## Plugin 能力

Plugin 实现 `ITaviPlugin` 并通过 `IPluginRegistrar` 注册细粒度能力：

- `ISceneDefinitionProvider`
- `ISceneRuleSettler`
- `IWritingContextContributor`
- `IWritingInteractionPolicy`
- `IWrittenSceneOutcomeContributor`

Writing 相关接口目前只是稳定扩展契约，Evolution 尚未连接 `Tavi.Application.Writing`。

Plugin 只能接收 `IScenarioView` 和不可变上下文，并返回定义、选项或 `ScenarioChangeProposal`。禁止：

- 保存上下文对象引用；
- 获取真实 Scenario；
- 直接修改 EARS；
- 自行提交事务；
- 持有 EvolutionSession 或 WritingSession；
- 通过解析自然语言偷偷产生领域变化。

## Module 缺失与存档

Scenario 存档记录创建和解释当前状态所需的 Module ID 与版本。加载时如果 Catalog 缺少完全兼容版本，EvolutionSession 拒绝进入可写状态。底层 Scenario JSON Store 仍能无语义猜测地读取结构化数据，便于诊断和未来迁移。

未知类型不会被静默删除。`core:none` 是唯一无需 Module 注册的开放类型。

## 声明式还是代码

优先使用声明式定义处理稳定类型、局部约束、基数和简单槽位。以下需求应使用代码 Plugin：

- 依赖非局部图查询动态生成 SceneDefinition；
- 复杂推理、搜索或可复现随机策略；
- 无法由标准 ScenarioOperationIntent 表达的选择过程；
- Writing 上下文、玩家互动规则或结果解释。

不要把 JSON 扩展成包含循环、任意表达式、反射或脚本执行的通用编程语言。需要程序行为时，应使用受版本控制的 Plugin 接口。

## 推荐拆分

一个 Module 包可以注册多个小型能力类，但不推荐一个类同时承担全部行为。例如：

```text
CharacterSceneProvider
CharacterRuleSettler
CharacterWritingContextContributor
CharacterInteractionPolicy
CharacterWrittenOutcomeContributor
```

没有相关能力时不注册对应接口。基础 `Modules/Character` 因为只声明角色语义，所以完全不包含 C# Plugin。

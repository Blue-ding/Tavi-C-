# Domain.World 根本性重构计划

## 1. 文档目的

本文档是 `Domain.World` 从 Anchor/SubWorld 模型迁移到 Element/Aspect/Relation/Scope 断言图模型的执行基线。它必须在任何生产代码改动之前存在，以便在长周期、多上下文的重构中持续保存已经确认的语义、实施顺序、兼容策略、风险和完成标准。

执行期间如发现本文档中的领域决定无法成立，应先更新“决策记录”和受影响阶段，再修改代码。不得让实现悄悄成为新的规格。

## 2. 已确认的目标

1. `Anchor` 重命名为 `Element`。
2. 新增 `Aspect`，表示对一个 Element 的一元断言。
3. 保留 `Relation`，将其定义为两个 Element 之间的二元断言。
4. 新增 `Scope`，表示 Aspect 和 Relation 成立的断言域；Scope 的具体语义由 Module 决定。
5. 删除 Character、Item、事实世界、语义世界和 SubWorld 等 World Kernel 内置语义。
6. 删除 `SubWorldSnapshot`；World 不再按主世界和子世界分别存储 Relation。
7. 每个 Aspect 和 Relation 只属于一个 Scope，使用单个必填 `ScopeId`。
8. 同一命题需要在多个 Scope 中成立时，创建多个独立 Aspect 或 Relation；每个副本具有自己的 ID、Quantity 和生命周期。
9. `ElementType`、`AspectType`、`RelationType`、`ScopeType` 使用名义上互不兼容的强类型开放键，不使用封闭枚举。
10. 四种开放类型均内置 `core:none`，Module 可以在自己的命名空间中提供其他键。
11. `Quantity` 的具体语义由解释对应类型的 Module 决定；World Kernel 只保证它是有限 `double`。
12. Relation 与 Aspect 一样拥有 `Quantity`，使一元和二元断言都能表达局部强度。
13. Aspect 必须显式持有目标 `ElementId`。
14. Scope 必须显式持有 `OwnerElementId`；抽象所有者也建模为 Element，不使用空 Owner。
15. Scope 删除时级联删除该 Scope 中的全部 Aspect 和 Relation。
16. Element 删除时级联删除直接依赖它的 Aspect、Relation、其持有的 Scope，以及这些 Scope 中的全部断言。
17. World Kernel 不自动解释任何 Scope 为“客观事实”“全局事实”或“角色认知”。
18. World Kernel 不自动创建默认 Element 或默认 Scope；需要断言的调用方必须显式提供 Scope。旧存档迁移器可以为了保存旧语义而生成兼容 Element 和 Scope。

## 3. 核心术语与身份语义

### 3.1 Element

Element 是可被一元或二元断言引用的世界实体。Element 本身不因类型而获得任何 Kernel 特权。

建议的公开形态：

```csharp
public sealed record Element
{
    public Element(Guid id, string name, string description, ElementType type);
    public Guid Id { get; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public ElementType Type { get; private set; }
}
```

约定：

- `Id` 非空，在 Elements 集合内唯一。
- `Name` 非 null、非空白，但不要求全局唯一。
- `Description` 非 null，可以为空字符串。
- `Type` 必须是已初始化且格式合法的开放键。
- Element 类型只用于 Module 解释和查询，不触发 Kernel 特殊行为。

### 3.2 Aspect

Aspect 是某个 Scope 中针对一个 Element 的一元断言。

建议的公开形态：

```csharp
public sealed record Aspect
{
    public Aspect(Guid id, string name, string description, double quantity, AspectType type, Guid elementId, Guid scopeId);
    public Guid Id { get; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public double Quantity { get; private set; }
    public AspectType Type { get; private set; }
    public Guid ElementId { get; }
    public Guid ScopeId { get; }
}
```

约定：

- `ElementId` 和 `ScopeId` 是结构性字段；需要改变时删除后重建，不提供原地迁移操作。
- `Quantity` 必须有限，可以为负数、零或大于一；范围由 Module 进一步约束。
- 同一 Scope 内允许存在结构相同但 ID 不同的 Aspect；Kernel 不擅自去重或合并。
- 跨 Scope 的 Aspect 即使描述同一命题也必须使用不同 ID。

### 3.3 Relation

Relation 是某个 Scope 中从 Source Element 指向 Target Element 的有向二元断言。

建议的公开形态：

```csharp
public sealed record Relation
{
    public Relation(Guid id, string name, string description, double quantity, RelationType type, Guid sourceElementId, Guid targetElementId, Guid scopeId);
    public Guid Id { get; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public double Quantity { get; private set; }
    public RelationType Type { get; private set; }
    public Guid SourceElementId { get; }
    public Guid TargetElementId { get; }
    public Guid ScopeId { get; }
}
```

约定：

- Relation 有方向；反向语义由 Module 决定，不由 Kernel 自动推导。
- Source、Target 和 Scope 是结构性字段；需要改变时删除后重建。
- 是否允许自环由 Module 决定；Kernel 允许 `SourceElementId == TargetElementId`。
- 同一 Scope 内允许结构相同但 ID 不同的 Relation。
- 跨 Scope 比较不能依赖 Relation ID，应按 Module 选择的结构签名比较。

### 3.4 Scope

Scope 是 Aspect 和 Relation 成立的断言域。

建议的公开形态：

```csharp
public sealed record Scope
{
    public Scope(Guid id, string name, string description, double quantity, ScopeType type, Guid ownerElementId);
    public Guid Id { get; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public double Quantity { get; private set; }
    public ScopeType Type { get; private set; }
    public Guid OwnerElementId { get; }
}
```

约定：

- Owner 是结构性字段；需要改变时删除后重建。
- Scope 的 Quantity 不等同于 Scope 内断言的 Quantity。
- Scope 不被实现为 Element 的内部容器，而是 World 中独立、可索引的实体。
- 当前版本不实现 Scope 嵌套，但数据模型不得把 Scope 固化为只能表达角色认知。
- Scope 不包含自己的 Element 集合；所有断言引用同一个 World 的 Elements。

### 3.5 断言实例

Aspect 和 Relation 的 ID 标识“某个 Scope 中的一次断言实例”，不标识跨 Scope 共享的抽象命题。

复制到另一个 Scope 时：

- 分配新的断言 ID。
- 保留或修改 Name、Description、Type、Quantity，由调用方决定。
- 使用新的 ScopeId。
- 不在 Kernel 中自动保存 `OriginId`；需要来源追踪的 Module 使用自己的证据或特征模型。

## 4. 开放类型键设计

### 4.1 类型

新增四个公开 `readonly record struct`：

- `ElementType`
- `AspectType`
- `RelationType`
- `ScopeType`

它们可以复用内部校验器，但必须是不同的 CLR 类型，禁止互相隐式转换。

### 4.2 格式和比较

- 文本格式采用大小写敏感的 `namespace:name`。
- namespace 和 name 都不得为空。
- 禁止前后空白；不自动 Trim，避免持久化后身份变化。
- `default(T)` 表示未初始化值，必须在进入有效 World 前被拒绝。
- `ToString()` 返回稳定文本；默认值返回空字符串，仅用于诊断，不代表 `core:none`。
- 相等性和哈希使用大小写敏感的序数语义。
- 每种类型提供公开静态 `None`，值为 `core:none`。
- 不提供从 `string` 的隐式转换，避免绕过显式建模。

### 4.3 Module 与未知键

- World Kernel 只校验键格式，不要求对应 Module 当前已加载。
- 未知键必须能从存档加载、创建快照、保存和经 Host API 往返。
- Module 注册表及 Module 专属 Quantity 规则不属于本次 World Kernel 第一阶段；未来可以在 Application 启动期叠加。
- 多个 Module 声明同一键时的冲突应由未来注册设施在启动期报告，不能由 World 持久化层擅自改名。

## 5. WorldSnapshot V2

建议结构：

```csharp
public sealed record WorldSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Dictionary<Guid, Element> Elements { get; set; } = new();
    public Dictionary<Guid, Aspect> Aspects { get; set; } = new();
    public Dictionary<Guid, Relation> Relations { get; set; } = new();
    public Dictionary<Guid, Scope> Scopes { get; set; } = new();
}
```

重要约定：

- `Id` 继续作为不透明 World StateId，不表达顺序。
- 四类实体分别在自己的字典中保持 ID 唯一；不同实体类别之间允许相同 Guid，因为所有引用字段和操作目标都有明确类别。
- World 构造时深复制并完整校验输入。
- World 对外返回的实体、集合和 Snapshot 都是独立副本。
- Snapshot 使用可变字典是为了序列化和组装；运行时隔离依靠深复制保证。
- 不保留 `Anchors`、`SubWorlds`、主世界 Relations 或 DomainId 兼容属性。

## 6. World 不变量

World 创建和每项操作必须维护以下不变量：

1. World StateId 非空。
2. 所有字典非 null。
3. 字典中不存在 null 实体。
4. 每个字典键非空并与实体 ID 相同。
5. 每个实体 ID 非空。
6. 所有 Name 非 null、非空白。
7. 所有 Description 非 null。
8. 所有开放类型键已初始化且格式合法。
9. Aspect、Relation、Scope 的 Quantity 都是有限 double。
10. 每个 Aspect.ElementId 指向存在的 Element。
11. 每个 Aspect.ScopeId 指向存在的 Scope。
12. 每个 Relation.SourceElementId 和 TargetElementId 指向存在的 Element。
13. 每个 Relation.ScopeId 指向存在的 Scope。
14. 每个 Scope.OwnerElementId 指向存在的 Element。
15. 不对名称、类型与端点组合施加唯一约束。
16. 不对 Relation 自环施加禁止约束。
17. 不对 Quantity 范围施加 Module 语义约束。

完整快照校验继续汇总所有可发现错误后一次抛出 `WorldException`，而不是只报告第一项。

## 7. 删除和级联规则

### 7.1 删除 Aspect 或 Relation

只删除目标断言。

### 7.2 删除 Scope

原子删除：

1. 该 Scope 中全部 Aspect；
2. 该 Scope 中全部 Relation；
3. Scope 本身。

逆操作必须按依赖顺序恢复 Scope，再恢复 Aspect 和 Relation。

### 7.3 删除 Element

原子删除以下闭包，且每个对象只删除一次：

1. OwnerElementId 等于目标 Element 的全部 Scope；
2. 上述 Scope 中的全部 Aspect 和 Relation；
3. 其他 Scope 中 ElementId 等于目标 Element 的全部 Aspect；
4. 其他 Scope 中 Source 或 Target 等于目标 Element 的全部 Relation；
5. Element 本身。

逆操作顺序：

1. 恢复 Element；
2. 恢复被其持有的 Scope；
3. 恢复所有被删除 Aspect；
4. 恢复所有被删除 Relation。

事务回滚仍使用精确内部恢复动作；撤销/重做使用公开 WorldOperation 组成的正反 ChangeSet。

## 8. WorldOperation V2

保留抽象 `WorldOperation`、`WorldChangeSet`、`AppliedWorldChangeSet` 和事务语义，替换具体操作。

### 8.1 Element 操作

- `AddElementOperation`
- `RemoveElementOperation`
- `UpdateElementNameOperation`
- `UpdateElementDescriptionOperation`
- `UpdateElementTypeOperation`

### 8.2 Aspect 操作

- `AddAspectOperation`
- `RemoveAspectOperation`
- `UpdateAspectNameOperation`
- `UpdateAspectDescriptionOperation`
- `UpdateAspectQuantityOperation`
- `UpdateAspectTypeOperation`

### 8.3 Relation 操作

- `AddRelationOperation`
- `RemoveRelationOperation`
- `UpdateRelationNameOperation`
- `UpdateRelationDescriptionOperation`
- `UpdateRelationQuantityOperation`
- `UpdateRelationTypeOperation`

`AddRelationOperation` 移除可空 `DomainId`，增加必填 `ScopeId`、`Quantity` 和 `RelationType`。

### 8.4 Scope 操作

- `AddScopeOperation`
- `RemoveScopeOperation`
- `UpdateScopeNameOperation`
- `UpdateScopeDescriptionOperation`
- `UpdateScopeQuantityOperation`
- `UpdateScopeTypeOperation`

### 8.5 结构字段

第一版不提供以下更新操作：

- Aspect.ElementId
- Aspect.ScopeId
- Relation.SourceElementId
- Relation.TargetElementId
- Relation.ScopeId
- Scope.OwnerElementId

改变这些字段使用删除后重建，以保持逆操作、暂存依赖和身份语义明确。

### 8.6 工厂

`WorldOperations` 提供为新增实体分配 ID 的工厂，以及 `Single`、`Combine`。工厂不自动创建依赖的 Element 或 Scope。

## 9. World 运行时 API

目标 API 至少包含：

- `StateId`
- `CreateSnapshot()`
- `GetElement(Guid)`
- `GetAspect(Guid)`
- `GetRelation(Guid)`
- `GetScope(Guid)`
- `GetElements()`
- `GetAspects()`
- `GetRelations()`
- `GetScopes()`
- `GetAspectsForElement(Guid)`
- `GetAspectsInScope(Guid)`
- `GetRelationsForElement(Guid)`
- `GetIncomingRelations(Guid)`
- `GetOutgoingRelations(Guid)`
- `GetRelationsInScope(Guid)`
- `GetScopesOwnedByElement(Guid)`

所有返回值均为副本。是否额外提供组合过滤由 Application `WorldQueries` 决定，Domain API 只提供安全的基础查询。

删除：

- `GetAnchor`
- `GetAnchors`
- `GetCharacters`
- `GetWorldRelations`
- `GetSubWorld`
- `GetSubWorlds`
- `GetSubWorldRelations`

## 10. Application 查询模型

### 10.1 基础查询

`WorldQueries` 重建为围绕四类实体和 Scope 的查询器：

- 按名称、类型和字符串线索查询 Element。
- 按 Element、Scope、类型、名称和线索查询 Aspect。
- 按 Source、Target、方向、Scope、类型、名称和线索查询 Relation。
- 按 Owner、类型、名称和线索查询 Scope。
- 提供 RequireSingle 系列，零结果或歧义时明确失败。

### 10.2 解析结果

删除 `ScopedRelation` 和 `WorldRelationComparison`。

建议新增：

- `ResolvedAspect(Aspect Aspect, Element Element, Scope Scope, Element ScopeOwner)`
- `ResolvedRelation(Relation Relation, Element Source, Element Target, Scope Scope, Element ScopeOwner)`
- 如有需要，`ResolvedScope(Scope Scope, Element Owner)`

这些是独立查询副本，不持有运行时 World 引用。

### 10.3 跨 Scope 比较

Kernel/Application 不定义唯一的跨 Scope 等价规则。基础查询提供候选数据；Module 可按 Type、端点、名称或自己的签名比较。

如果保留通用比较帮助函数，它只能按明确传入的结构条件工作，不能把不同 ID 默认视为不同命题。

## 11. WorldSession 与暂存区

`WorldSession` 的并发、StateId、保存、自动保存、撤销和重做框架保留。

### 11.1 暂存目标

`TargetKey` 扩展为：

- Element + ElementId
- Aspect + AspectId
- Relation + RelationId
- Scope + ScopeId

对同一实体的多个待提交修改继续判为冲突，除非它们在同一个原子 WorldChangeSet 中。

### 11.2 暂存依赖

投影和选中提交必须理解：

- Add Scope 依赖 Owner Element。
- Add Aspect 依赖 Element 和 Scope。
- Add Relation 依赖 Source Element、Target Element 和 Scope。
- 删除 Element 隐式级联，不要求调用方单独选中依赖对象删除项。
- 删除 Scope隐式级联。
- 如果选中的新增断言依赖另一个未选中的暂存新增实体，应拒绝选中提交并给出依赖说明。
- 如果选中的删除与未选中的更新存在级联冲突，应将相关项标为 Conflict 或 Invalid，规则需由测试固定。

### 11.3 投影

继续通过 `RuntimeWorld.Create(realSnapshot)` 后顺序应用有效项生成 `ProjectedWorld`。任何无效项不得污染后续投影。

## 12. Guidance 重构

Guidance 必须从 Anchor/SubWorld 语义迁移到四实体模型，不能只做名称替换。

### 12.1 临时标识和引用

建议：

- `ProposalElementId`
- `ProposalScopeId`
- `ProposalElementReference.Existing/Proposed`
- `ProposalScopeReference.Existing/Proposed`

### 12.2 提案类型

至少支持：

- `ProposeAddElement`
- `ProposeAddScope`
- `ProposeAddAspect`
- `ProposeAddRelation`

Aspect 可以引用现有或同一提案中新建的 Element 与 Scope。Relation 的 Source、Target 和 Scope也可以引用现有或提案对象。

### 12.3 编译顺序

`WorldProposalCompiler` 必须：

1. 校验接受的 Change ID；
2. 为所有接受的新 Element 和 Scope预分配真实 ID；
3. 解析引用；
4. 先编译 Element；
5. 再编译 Scope；
6. 再编译 Aspect 和 Relation；
7. 形成保持依赖顺序的暂存操作；
8. 返回临时 ID 到真实 ID 的映射。

如果只接受依赖项的一部分，应返回结构化 GuidanceIssue，而不是依赖 Domain 异常作为正常控制流。

### 12.4 Guidance 工具

删除 Character、Item、World/SubWorld 工具语义。新增或改造为：

- 查询/新增/修改/删除 Element；
- 查询/新增/修改/删除 Scope；
- 查询/新增/修改/删除 Aspect；
- 查询/新增/修改/删除 Relation；
- 参数中的开放类型使用稳定字符串，不使用封闭 JSON 枚举；
- Quantity 参数使用有限 double 校验；
- Scope 通过 ID 或可唯一解析的选择器指定。

工具描述必须明确 Module 语义不由 World Kernel解释。

## 13. Narrative 适配

Narrative 模块架构保持不变，但 World 引用术语必须更新：

- `NarrativeWorldReferenceNode.WorldAnchorId` 重命名为 `WorldElementId`。
- XML 注释中的 Anchor 改为 Element。
- `NarrativeBeatNode.EvidenceRelationIds` 保留。
- 为一元断言证据新增 `EvidenceAspectIds`，使 Narrative 能追踪 Aspect 来源。
- `SourceWorldStateId` 保留。

NarrativeGraph 的开放类型键暂不与四种 World 类型合并；二者属于不同名义域，避免错误混用。可以复用相同的 `namespace:name` 约定。

## 14. 持久化 V2 与 V1 迁移

### 14.1 文件版本

- 保留 V1 DTO 只读兼容。
- 新增 V2 DTO，保存时只写 V2。
- Reader 根据根 `version` 分派。
- 不原地改变 `SaveDocumentV1` 的字段语义。
- V1 加载后得到 V2 `WorldSnapshot`；下一次显式保存或脏状态刷新时写为 V2。

### 14.2 V2 DTO

V2 包含：

- World StateId
- Elements
- Aspects
- Relations
- Scopes
- 四种开放类型字符串
- 三种 Quantity 数值
- 所有引用 ID

JSON 命名继续遵循当前存储策略，读取时拒绝缺失必需集合、null 项、无效键和非有限数值。

### 14.3 V1 Element 迁移

- Anchor → Element，保留 ID、Name、Description。
- Character → `legacy:character`。
- Item → `legacy:item`。
- 不把旧类型全部映射为 `core:none`，避免静默丢失信息。

### 14.4 V1 Scope 迁移

每个旧 SubWorld：

- 生成一个 Scope。
- 可优先复用旧 SubWorld ID 作为 Scope ID，因为不同实体类别允许 Guid 重复。
- OwnerElementId = 旧 DomainId。
- Name 使用稳定、可诊断的兼容名称。
- Description 说明其来自 V1 Character SubWorld。
- Quantity = 1。
- ScopeType = `legacy:character-subworld`。

全部旧主世界 Relation 需要一个兼容基础 Scope：

- 生成一个合成 World Element 作为 Owner。
- ElementType = `legacy:world`。
- 生成一个由它持有的基础 Scope。
- ScopeType = `legacy:world`。
- Quantity = 1。
- 旧主世界 Relation 全部指向该 Scope。

如果 V1 不含主世界 Relation，则不必生成合成 World Element 和基础 Scope。

### 14.5 确定性 ID

V1 文件没有合成 World Element 与基础 Scope 的 ID。迁移必须从旧 World StateId、固定命名空间和实体类别确定性派生 Guid，并在极端碰撞时使用固定递增 salt 重试。禁止每次加载随机生成，否则同一 V1 存档在未另存前会产生不稳定引用。

确定性 Guid 实现必须：

- 输入编码固定为 UTF-8；
- 哈希算法和字节序写入注释及测试；
- 设置合法 UUID version/variant 位，或明确它只是 Guid 容器；
- 对相同输入始终输出相同结果；
- 对 Element 与 Scope 使用不同类别标签。

### 14.6 V1 Relation 迁移

- 保留 Relation ID、Name、Description、Source 和 Target。
- Quantity = 1。
- RelationType = `core:none`，因为 V1 没有独立 RelationType。
- 主世界 Relation.ScopeId = 兼容基础 Scope。
- SubWorld Relation.ScopeId = 对应迁移 Scope。

V1 不产生 Aspect。

### 14.7 StateId

迁移保持旧 World StateId，不因为内存格式转换生成新 StateId。只有后续真实领域写入才生成新 StateId。

## 15. Host API 与 ViewModel

### 15.1 World 图响应

`WorldGraphViewModel` 调整为：

- Elements
- Aspects
- Relations
- Scopes
- 暂存状态、健康状态和历史状态保留

删除：

- AnchorViewModel
- SubWorldViewModel
- Relation 的 Scope 字符串和 DomainCharacterId

新增：

- `ElementViewModel`
- `AspectViewModel`
- 新版 `RelationViewModel`
- `ScopeViewModel`

所有开放类型在 HTTP 契约中使用字符串。

### 15.2 请求

新增四类实体的 Add/Update 请求。结构字段不提供 PATCH 式更新；复制或移动断言由客户端执行新增和删除，必要时以后增加原子 Copy Endpoint。

所有依赖当前状态的写请求继续携带 `ExpectedStateId`。实际编辑仍先进入暂存区，提交时执行乐观并发检查。

### 15.3 路由

建议稳定路由：

- `/api/world/elements`
- `/api/world/aspects`
- `/api/world/relations`
- `/api/world/scopes`

删除旧 anchor/sub-world 路由，不保留行为含混的兼容别名。若外部兼容后来成为要求，应另开适配层，不污染新领域 API。

### 15.4 事件

World 事件继续发布 StateId、Dirty、CommitId 和操作摘要。操作摘要更新为 Element/Aspect/Relation/Scope 术语。

## 16. Web 前端

`src/Tavi.Web` 在本次重构范围内，必须与 Host 同步完成，不能留下仅后端可构建但产品不可用的状态。

工作项：

1. 更新 TypeScript API 类型。
2. Anchor 节点术语改为 Element。
3. 删除 Character/Item 枚举选择。
4. 类型输入改为开放键文本，默认 `core:none`。
5. 删除 World/SubWorld 图层和 Character 子世界 UI。
6. 新增 Scope 列表、Owner Element、ScopeType、Quantity 编辑。
7. 新增 Aspect 列表或节点属性展示与编辑。
8. Relation 编辑新增 Scope、RelationType、Quantity。
9. 图边显示可以按 Scope 过滤，不能把不同 Scope 的重复 Relation 错误折叠。
10. 暂存项描述支持四种实体。
11. Guidance 面板支持新的提案 ViewModel 和创建结果映射。
12. 更新可访问文本、空状态、错误信息和 README 描述。
13. 运行 TypeScript 编译和 Web build。

UI 的视觉重设计不是本次目标；优先保证新模型完整可观察、可编辑和可提交。

## 17. CLI 与 App

- CLI 初始化和状态输出通常只需跟随 Application API 编译修复。
- 检查 CLI 是否展示旧 Anchor/SubWorld 术语并更新。
- MAUI App 不直接理解 World 模型，但其构建会触发 Web 资产构建，最终验证必须覆盖。
- 遵循仓库偏好：Unity/C# IDE 集成使用 Rider；本次不添加其他 IDE 配置。

## 18. XML 注释与代码格式

### 18.1 XML 注释

本次新增或修改的所有公开 API 必须有显式中文 XML 注释，包括：

- public class、record、struct、enum、interface；
- public 构造函数；
- public 方法；
- public 属性；
- public 事件；
- public enum 成员；
- public 常量和静态属性；
- public record 主构造参数需要通过 `<param>` 解释。

关键 internal 类型、事务规则、迁移算法、非显然不变量也必须注释。

注释应说明语义和约束，不复述成员名称。尤其要明确：

- Quantity 只要求有限，语义由 Module 决定；
- ScopeId 是单一断言域；
- 跨 Scope 通过复制断言表达；
- 结构字段需删除后重建；
- 未知开放类型键允许往返；
- 删除级联和 StateId 规则。

### 18.2 换行

遵循用户要求“非必要不换行”：

- 简短构造器、record 声明、表达式体和对象初始化尽量保持一行。
- 只有超长签名、复杂布尔条件、提高依赖顺序可读性或符合现有格式器要求时换行。
- 不为了视觉对齐拆散简单参数。
- 不进行与重构无关的全仓格式化。

### 18.3 命名

- 代码标识符统一使用 Element，不保留 Anchor 别名。
- 代码标识符统一使用 Scope，不保留 SubWorld 或 DomainId。
- `SourceId`/`TargetId` 优先明确为 `SourceElementId`/`TargetElementId`。
- 用户可见文案同步使用 Element、Aspect、Relation、Scope。

## 19. 分阶段实施顺序

### 阶段 0：基线

- 确认工作区干净。
- 保存本文档。
- 运行现有测试获得基线。
- 记录任何既有失败，不把它们误归因于重构。

完成门槛：计划已落盘，基线结果已知。

基线记录（2026-07-25，生产代码改动前）：

- `git status --short`：干净。
- `dotnet build Tavi.sln --no-restore`：成功，0 warning、0 error。
- `dotnet test Tavi.sln --no-restore`：38 passed、0 failed、2 skipped；跳过项均为需要外部服务的 OpenAI 集成测试。
- `npm run build`（`src/Tavi.Web`）：成功。

### 阶段 1：开放类型和实体

- 新增四种开放类型。
- Anchor 文件替换为 Element。
- 新增 Aspect 和 Scope。
- 扩展 Relation。
- 为所有公开成员补 XML 注释。
- 添加开放键和实体值语义测试。

完成门槛：Domain 项目可以单独编译；新实体基础测试通过。

### 阶段 2：Snapshot、World 和 Operation

- 重建 WorldSnapshot。
- 重建 WorldOperation 派生类型。
- 重写 World 创建校验、深复制、基础查询、Apply 和事务逆操作。
- 删除全部 Character/SubWorld 分支。
- 添加快照完整性、级联、原子失败、StateId、不变操作和撤销所需逆集测试。

完成门槛：Domain 测试完整覆盖新 Kernel，Domain 项目无旧术语。

### 阶段 3：Application Session、Queries、Staging

- 改造 WorldQueries。
- 更新 WorldSession 调用。
- 重写暂存冲突与依赖规则。
- 更新 WorldCommitResult 和事件文案中受影响部分。
- 改造 Application 测试。

完成门槛：Application 项目编译，World Session/暂存测试通过。

### 阶段 4：Persistence V2

- 冻结 V1 DTO。
- 新增 V2 DTO 和映射。
- 实现版本分派。
- 实现确定性 V1 迁移。
- 保存统一写 V2。
- 添加 V2 往返、损坏数据、未知类型键、非有限 Quantity、V1 迁移和确定性测试。

完成门槛：Persistence 及其测试通过；旧测试存档语义被显式迁移。

### 阶段 5：Guidance 与 Narrative

- 改造 WorldProposal、Draft、Compiler 和 Tool。
- 改造 Guidance 契约、映射和测试。
- Narrative World 引用改为 Element，添加 Aspect 证据。
- 更新相关 XML 注释。

完成门槛：Guidance 和 Narrative 测试通过，不残留 Character/SubWorld 规则。

### 阶段 6：Host API

- 改造 ViewModel、Mapper、Endpoint 和事件描述。
- 更新 Host 集成测试。
- 检查错误码和响应细节。

完成门槛：Host 项目与测试通过，新 API 可以完成四类实体的暂存、提交、撤销、重做和保存流程。

### 阶段 7：Web、CLI、App 与文档

- 更新 Web 类型、API、组件和样式。
- 更新 CLI 编译与术语。
- 更新根 README 和 Host README。
- 检查 App 构建链。

完成门槛：Web build、Host、CLI 编译通过；README 不再描述旧模型。

### 阶段 8：清理与全量验证

- 全仓搜索旧术语。
- 删除无用文件、别名、DTO、工具和测试夹具。
- 检查所有新增/修改公开 API 的 XML 注释。
- 运行格式检查、build、test、Web build。
- 检查 git diff，确认没有无关修改。

完成门槛：满足第 23 节全部验收标准。

## 20. 测试矩阵

### 20.1 开放类型

- `core:none` 可用。
- 合法 Module 键可用。
- 空值、默认值、缺少冒号、空 namespace、空 name、前后空白被拒绝。
- 大小写敏感。
- 四种类型不能在编译期互换。
- 未知但格式合法的键可以持久化往返。

### 20.2 Snapshot 校验

- 空 World 合法。
- 空 StateId 非法。
- null 集合和 null 实体非法。
- 字典键与实体 ID 不一致非法。
- 所有悬空 Element/Scope 引用非法。
- 非有限 Quantity 非法。
- 重复结构但不同 ID 合法。
- Relation 自环合法。
- 同 Guid 跨不同实体类别合法。

### 20.3 查询隔离

- 构造 World 后修改输入 Snapshot 不影响 World。
- 修改查询返回实体或集合不影响 World。
- 修改 CreateSnapshot 返回值不影响 World。
- 按 Scope、Owner、Element、方向和类型查询准确。

### 20.4 操作与 StateId

- 每种 Add/Remove/Update 的有效路径。
- no-op 更新不改变 StateId。
- 空 ChangeSet 不改变 StateId。
- 原子组任一操作失败时全部回滚且 StateId 不变。
- 回滚失败进入既有事务故障路径。
- 删除 Scope 正确级联。
- 删除 Element 正确计算级联闭包。
- 正向/反向 ChangeSet 能精确恢复。

### 20.5 Session

- 乐观并发冲突。
- Apply、Undo、Redo。
- Dirty、Save、AutoSave。
- 事件只在原子提交后发布。
- Faulted 后拒绝继续读写。

### 20.6 Staging

- 四类实体追加、删除和投影。
- 同目标冲突。
- 新增依赖的选择提交。
- 删除级联与未选更新冲突。
- Invalid 清理。
- Revision 变化。
- 提交消费正确项。

### 20.7 Persistence

- V2 空 World 和完整 World 往返。
- 所有类型键和 Quantity 精确保留。
- V1 Anchor 类型迁移。
- V1 主世界 Relation 迁移。
- V1 SubWorld Relation 迁移。
- V1 合成 ID 多次加载确定一致。
- V1 StateId 保留。
- 损坏 JSON、未知版本、null 集合和重复 ID 稳定失败。
- 原子保存和取消语义不回退。

### 20.8 Guidance

- 提案内新 Element → 新 Scope → 新 Aspect/Relation 依赖链。
- 现有和临时引用混用。
- 部分接受导致缺失依赖时报告 issue。
- 提交冲突。
- 临时 ID 到真实 ID 映射。
- 开放类型键和 Quantity 参数校验。

### 20.9 Host/Web

- 四类实体 Endpoint。
- 暂存、提交、撤销、重做、保存。
- 错误映射和 HTTP 409。
- World Graph ViewModel 完整。
- Guidance ViewModel 完整。
- TypeScript 编译。
- Web production build。

## 21. 风险与控制措施

### 21.1 语义漂移

风险：实现过程中重新引入“默认事实 Scope”或 Character 特权。

控制：World Domain 禁止引用 Character、Item、SubWorld、事实世界等术语；兼容语义只允许出现在 V1 迁移命名中。

### 21.2 数据丢失

风险：V1 类型和 SubWorld 语义被映射为 none。

控制：使用 `legacy:*` 键和显式迁移 Scope；添加逐字段迁移测试。

### 21.3 重复断言误合并

风险：Mapper、查询或 UI 按端点折叠不同 Scope 的 Relation。

控制：ID 始终表示断言实例；UI key 使用 ID；跨 Scope 比较显式执行。

### 21.4 级联逆操作不完整

风险：删除 Element/Scope 后 Undo 丢失部分断言。

控制：Domain 级联测试同时比较删除前与 Undo 后完整 Snapshot，不只比较计数。

### 21.5 暂存依赖错误

风险：有效投影与选中提交对依赖的判断不同。

控制：共享依赖分析帮助函数，并对投影、PrepareCommit 和冲突报告做成对测试。

### 21.6 长重构中间态无法编译

风险：跨层破坏导致难以定位。

控制：严格按阶段推进；每阶段优先使最低层项目编译并测试，再进入上层。允许短暂的全解决方案不编译，但不得同时开始多个未闭合层。

### 21.7 XML 注释遗漏

风险：大量新 API 缺少约定说明。

控制：阶段末使用公开声明搜索人工核对；最终检查 diff 中所有 public 声明。暂不全仓启用 CS1591，以免把无关模块纳入本次重构。

## 22. 非目标

本次不负责：

- 实现 Module 动态加载器或注册中心。
- 定义具体 ElementType/AspectType/RelationType/ScopeType 的故事语义。
- 实现 Scope 嵌套、继承或集合逻辑。
- 实现断言来源、多前提证明图或 OriginId。
- 实现完整时间数据库或历史 Snapshot 存储。
- 实现三元关系；高元事件通过事件 Element 和多条 Relation 重化。
- 重新设计 Narrative Kernel 的评分与选择算法。
- 对 Web 做无关的视觉重设计。
- 添加与本次重构无关的 IDE 配置。

## 23. 最终验收标准

全部条件必须满足：

1. `dotnet build Tavi.sln` 成功。
2. `dotnet test Tavi.sln` 全部通过。
3. `Tavi.Web` production build 成功。
4. Domain World 中不存在 Anchor、AnchorType、Character 特权、SubWorld、DomainId、World/SubWorld Relation 分区。
5. WorldSnapshot 只包含 Elements、Aspects、Relations、Scopes 和 StateId。
6. 每个 Aspect/Relation 具有单一必填 ScopeId。
7. 每个 Scope 具有必填 OwnerElementId。
8. 三种 Quantity 均校验为有限 double。
9. 四种开放类型支持 `core:none` 和未知合法 Module 键往返。
10. Scope 和 Element 删除级联及 Undo/Redo 由测试覆盖。
11. V1 存档可确定性迁移，所有新保存写 V2。
12. Guidance、Host 和 Web 能完整观察和编辑新模型。
13. Narrative 的 World 引用已使用 Element 术语，并能追踪 Aspect 与 Relation 证据。
14. 所有本次新增或修改的公开 API 有明确中文 XML 注释。
15. README 和用户可见文案不再宣称 Character/SubWorld 是 World Kernel 概念。
16. 全仓不存在为了兼容而保留的含混旧 API；V1 兼容仅存在于持久化迁移层。
17. git diff 不包含无关格式化、用户文件或 IDE 配置修改。

## 24. 执行清单

- [x] 重构计划在代码改动前落盘。
- [x] 记录现有 build/test/Web build 基线。
- [x] 阶段 1：开放类型和实体。
- [x] 阶段 2：Snapshot、World 和 Operation。
- [x] 阶段 3：Application Session、Queries、Staging。
- [x] 阶段 4：Persistence V2 与 V1 迁移。
- [x] 阶段 5：Guidance 与 Narrative。
- [x] 阶段 6：Host API。
- [x] 阶段 7：Web、CLI、App 与文档。
- [x] 阶段 8：清理与全量验证。

最终验证结果：

- `dotnet build Tavi.sln --no-restore`：0 警告、0 错误，包含 CLI、Host 与 Windows App 构建链。
- `dotnet test Tavi.sln --no-restore --no-build`：54 项通过，2 项依赖外部 OpenAI 配置的集成测试按预期跳过。
- `npm run build`：TypeScript 检查与生产资源构建通过。
- `git diff --check`：通过；仅报告工作区既有的 LF/CRLF 转换提示。
- 旧领域术语仅保留在 V1 DTO、V1 迁移器及其兼容语义中。

## 25. 决策记录

### D-001：Scope 是单值而不是集合

状态：已确认。

决定：Aspect 和 Relation 使用单个必填 ScopeId。跨 Scope 使用复制断言表达。

原因：让断言身份、Quantity、删除、修改和 Module 解释保持局部且无歧义。

### D-002：Relation 拥有 Quantity

状态：已确认。

决定：Relation 与 Aspect 一样包含有限 double Quantity。

原因：一元与二元断言均需要可由 Module 解释的强度，避免模型不对称。

### D-003：Scope Owner 必填

状态：已确认。

决定：OwnerElementId 非空且必须指向现有 Element；抽象 Owner 也显式建模为 Element。

原因：避免可空 Owner 分支并保持所有断言域具有明确持有者。

### D-004：开放类型只做格式校验

状态：已确认。

决定：World 不要求 Module 已加载，未知合法键可以读取和保存。

原因：插件缺失或卸载不能让已有 World 数据不可恢复。

### D-005：结构字段删除后重建

状态：已确认。

决定：第一版不原地修改断言端点、Scope 或 Scope Owner。

原因：这些字段参与身份和依赖关系，删除后重建能保持操作、暂存、撤销和审计语义明确。

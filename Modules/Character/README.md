# Character Module

Character 是纯声明式 Module，不包含 C# Plugin，也不依赖 `Tavi.Domain`。

它注册以下 Scenario 语义：

- `character:character` Element 类型；
- 每个角色必须恰好持有一个 `character:identity` Scope；
- `character:gender` 是允许其他 Module 扩充的 Aspect Group；
- 每个角色的身份 Scope 必须恰好包含一个指向 Owner 的性别 Aspect。

创建角色时，Element、Identity Scope 和 Gender Aspect 必须放入同一个 `ScenarioChangeSet`。Evolution 只在完整候选状态上执行 Module 约束，因此不会拒绝合法操作组的中间状态。

该 Module 暂不声明 SceneDefinition。角色行为、Writing 上下文或复杂互动规则应由独立能力 Plugin 按需提供，而不是加入基础语义包。

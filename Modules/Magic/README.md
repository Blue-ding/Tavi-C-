# Magic Module

Magic 是一个声明式语义包与受信规则 Plugin 组成的小型纵向 Module。

它注册法术、角色魔法状态、当前法力和基础施法消耗，并提供 `magic:cast-spell` SceneDefinition。Scene 进入 Processing 后，Plugin 只读取冻结的局部 `SceneContextView`，按 `mana-cost-multiplier` 计算消耗并原子更新施法者的法力 Aspect。

Magic 依赖 Character 1.0.0。普通角色不强制拥有魔法状态；只有具有 `magic:mana` Aspect 的角色才能绑定到施法者槽位。

Plugin 还通过现有 Guidance seam 提供 Magic 结构约束说明，并暴露两个原子 World Authoring Action：

- `magic:create-spell`：创建法术、法术定义 Scope 和基础法力消耗；
- `magic:awaken-character`：为既有角色添加魔法状态和当前法力。

Guidance 不会通过 World 工具模拟施法；`magic:cast-spell` 仍只能由 Scenario 的规则结算入口执行。

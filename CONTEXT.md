# Tavi

Tavi 以结构化事实描述世界，并在演绎状态中通过场景推动这些事实发生变化。

## Language

**EARS**:
由 Element、Aspect、Relation 与 Scope 组成的规则化领域语言；它不从属于 World 或 Scenario。

**Element**:
可以被一元或二元断言引用的实体。

**Scope**:
由一个 Element 持有、容纳规则化事实的断言域。

**Aspect**:
在一个 Scope 中针对单个 Element 的规则化一元断言。

**Relation**:
在一个 Scope 中从来源 Element 指向目标 Element 的规则化有向二元断言。

**World**:
玩家维护的世界事实状态，包含规则化 EARS 与不参与演绎的 Local 事实。

**Scenario**:
从确定 World 状态产生、可由 Scene 演绎改变的当前事实状态。

**Scene**:
约束一组 Element 并产生局部演绎结果的情境。

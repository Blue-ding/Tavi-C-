# Tavi.Runtime 运行期约定

`Tavi.Runtime` 持有一个 Tavi 进程中的活动 Session、持久化 Adapter、并发访问和生命周期，
并协调 World、Scenario、Guidance、Writing 与 Extension。

## 依赖约定

- Runtime 可以依赖 Application、Domain、Extensibility 和具体 Infrastructure。
- Runtime 不依赖 Host、HTTP、SSE 或任何展示 ViewModel。
- Runtime 事件只携带 Application 类型、Domain 类型或 `RuntimeContracts` 中定义的协议无关结果。
- Host 是组合根，负责注册 Runtime、选择具体 Infrastructure 和 Plugin。

## 接口约定

- `WorldCoordinator` 仅通过 `IWorldBuildSessionLifecycle` 管理 `WorldBuildSession`，
  并按 `IWorldBuildView`、`IWorldBuildContributor`、`IWorldBuildController`
  三种权限协调读取、提案和玩家审批。
- Guidance 与 Scenario 只通过各自的 World Coordinator 向统一构筑日志提交提案；
  同一 World 实体上的竞争写入由 `WorldBuildSession` 标记为冲突。
- `PerformanceRuntime` 与 `WorldCoordinator` 一样分别持有 Workspace 和 Lifecycle；进程内
  至多存在一个 Performance Session，但已结束 Performance 可以保留为历史记录。
- `ScenarioRuntime` 与 `WritingRuntime` 同样仅通过各自的 `I*SessionLifecycle`
  管理 Session，并通过 `I*Workspace` 向上层 Adapter 提供业务能力。
- `WritingRuntime` 另向 `PerformanceRuntime` 暴露最小的 `IBeatPublisher` 能力，
  不授予活动手稿编辑或归档库管理权限。
- `ScenarioPerformanceRuntime` 统一采用 Scenario 后 Performance 的锁顺序，从
  Performance-capable Processing Scene 启动唯一 Performance，并把全部已发布 Beat
  产生的结算提案回写来源 Scene 后结束 Performance。
- 不为 Runtime 创建与现有 `I*Service` 一一对应的转发接口。
- 后续如果拆分 Application 接口，应按稳定能力和权限拆分，而不是按 Runtime、HTTP 等调用方拆分。
- Runtime 的对外 interface 应逐步隐藏 Session 生命周期、同步锁和具体持久化 Adapter。

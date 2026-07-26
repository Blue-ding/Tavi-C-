# Tavi.Runtime 运行期约定

`Tavi.Runtime` 持有一个 Tavi 进程中的活动 Session、持久化 Adapter、并发访问和生命周期，
并协调 World、Scenario、Guidance、Writing 与 Extension。

## 依赖约定

- Runtime 可以依赖 Application、Domain、Extensibility 和具体 Infrastructure。
- Runtime 不依赖 Host、HTTP、SSE 或任何展示 ViewModel。
- Runtime 事件只携带 Application 类型、Domain 类型或 `RuntimeContracts` 中定义的协议无关结果。
- Host 是组合根，负责注册 Runtime、选择具体 Infrastructure 和 Plugin。

## 接口约定

- 当前 `ExecuteAsync` seam 暂时接受 `IWorldService`、`IScenarioService` 和 `IWritingService`
  操作，以保持迁移行为不变。
- 不为 Runtime 创建与现有 `I*Service` 一一对应的转发接口。
- 后续如果拆分 Application 接口，应按稳定能力和权限拆分，而不是按 Runtime、HTTP 等调用方拆分。
- Runtime 的对外 interface 应逐步隐藏 Session 生命周期、同步锁和具体持久化 Adapter。

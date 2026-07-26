# Tavi.Host 展示层约定

`Tavi.Host` 是本地前端的组合根和 HTTP/SSE 展示适配层，不实现世界业务规则，
也不持有 Tavi 的运行期 Session。

## 依赖约定

- Endpoint 只能通过 `Tavi.Runtime` 访问活动 Application Session。
- ViewModel 是稳定的 HTTP/SSE 契约，不直接暴露运行时 `World`。
- Runtime 事件在 Host 中映射为 HTTP/SSE ViewModel，`Tavi.Runtime` 不依赖 Host。
- Domain 和 Application 决定操作是否合法，Host 只完成协议解析与结果映射。
- 当前世界会话由 Runtime 单例持有，避免丢失状态标识、撤销历史和自动保存状态。
- World、Scenario、Performance 与 Writing 分别提供 `/events` SSE；事件字段同构，
  调用方收到事件后重新读取对应完整工作区，不依赖事件重建领域状态。
- Performance HTTP seam 覆盖从 Processing Scene 启动、Beat 完整生命周期、
  Scenario 结算、放弃、历史、保存与归档。

## 修改约定

- 每一个提交请求都携带 `expectedStateId`；状态标识只判断提案是否仍基于当前完整状态，不表达提交顺序。
- 状态冲突统一返回 HTTP 409；通用乐观并发冲突在 `details` 中提供
  `expectedStateId` 与 `actualStateId`，调用方随后重新读取完整工作区。
- 多属性更新被组合成一个 `WorldChangeSet`，因此只产生一次提交和一次撤销记录。
- Aspect 的 Element 与 Scope、Relation 的端点与 Scope、Scope 的 Owner 均不可直接更新；结构变化通过删除后重建完成。
- `ElementType`、`AspectType`、`RelationType` 和 `ScopeType` 是大小写敏感的开放键，Host 不要求对应 Module 已加载。
- Aspect 与 Relation 各自只属于一个 Scope；同一命题进入多个 Scope 时由调用方创建具有独立 ID 和生命周期的副本。

## 异常约定

- `TaviException` 按稳定分类转换为 HTTP 状态和 `ErrorViewModel`。
- 参数及状态异常同样经过全局异常处理器。
- 未知异常仅记录在 Host 日志中，响应不得包含调用栈、文件路径或内部类型信息。
- Endpoint 不编写重复的 `try/catch`，避免不同接口产生不一致的错误契约。

## 前端资源约定

- 后端不要求仓库包含 `Tavi.Web`；存在其 `package.json` 时，发布与桌面构建才执行
  前端构建并使用 `Tavi.Host/wwwroot` 产物。
- Host 默认只监听 `127.0.0.1:5178`，除非启动环境显式提供 `ASPNETCORE_URLS`。
- `Tavi.App` 在同一进程中组合 Host，并使用动态回环端口和桌面输出目录中的
  `wwwroot`；Host 的组合入口由 `TaviHost.Build` 统一提供。

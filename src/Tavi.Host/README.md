# Tavi.Host 展示层约定

`Tavi.Host` 是本地前端的组合根和展示适配层，不实现世界业务规则。

## 依赖约定

- Endpoint 只能通过 `WorldRuntime` 访问 `WorldSession`。
- ViewModel 是稳定的 HTTP/SSE 契约，不直接暴露运行时 `World`。
- Domain 和 Application 决定操作是否合法，Host 只完成协议解析与结果映射。
- 当前世界会话由 Host 单例持有，避免丢失 revision、撤销历史和自动保存状态。

## 修改约定

- 每一个修改请求都携带 `expectedRevision`。
- revision 冲突统一返回 HTTP 409，前端随后重新读取完整世界快照。
- 多属性更新被组合成一个 `WorldChangeSet`，因此只产生一次提交和一次撤销记录。
- Relation 的端点和所属范围不可直接更新；结构变化通过删除后重建完成。

## 异常约定

- `TaviException` 按稳定分类转换为 HTTP 状态和 `ErrorViewModel`。
- 参数及状态异常同样经过全局异常处理器。
- 未知异常仅记录在 Host 日志中，响应不得包含调用栈、文件路径或内部类型信息。
- Endpoint 不编写重复的 `try/catch`，避免不同接口产生不一致的错误契约。

## 前端资源约定

- `Tavi.Web` 构建产物写入 `Tavi.Host/wwwroot`，该目录不提交 Git。
- 发布 Host 时会依据 `package-lock.json` 重新还原并构建前端。
- Host 默认只监听 `127.0.0.1:5178`，除非启动环境显式提供 `ASPNETCORE_URLS`。

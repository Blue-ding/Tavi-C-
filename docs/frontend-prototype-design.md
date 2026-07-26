# Tavi 完整前端原型设计与实施规格

> 状态：可实施
>
> 目标读者：接手实现前端的工程师或后续 Codex 会话
>
> 目标平台：Windows 桌面应用内嵌 WebView，同时兼容本地浏览器
>
> 对应后端：`Tavi.Host` 的 `/api/v1/*` HTTP/SSE interface
>
> 计划前端目录：`src/Tavi.Web`
> 文档范围：完整可交互原型；不在本阶段重新设计 Domain、Application 或 Runtime

---

## 1. 文档用途

本文不是概念稿，而是实现规格。接手者应能够仅凭本文和仓库现有代码完成：

1. 创建 `src/Tavi.Web` 前端工程。
2. 实现 World、Scenario、Performance、Writing、Guidance、Extension、Settings 全部页面。
3. 对接当前 `Tavi.Host` HTTP/SSE interface。
4. 将构建产物输出到 `src/Tavi.Host/wwwroot`，供浏览器和 MAUI WebView 共用。
5. 完成单元测试、HTTP adapter 测试、关键交互测试和端到端验收。

本文明确区分：

- **当前必须实现**：当前后端 interface 已支持，原型必须完成。
- **降级策略**：后端存在已知限制，前端必须安全处理。
- **后续 interface 改进**：不阻塞当前原型，但不得在前端伪造为已存在能力。

除非用户另行指定，不增加 VS Code、WebStorm 或其他 IDE 专用配置文件。

---

## 2. 产品定位与范围

### 2.1 产品定位

Tavi 是一个本地优先的叙事世界构筑与演绎工作台。玩家依次或交叉完成：

1. 在 **World** 中构筑世界事实。
2. 使用 **Guidance** 从自然语言生成 World 修改提案。
3. 在 **Scenario** 中从已提交 World 创建 Scene 并演绎局部变化。
4. 从支持 Performance 的 Processing Scene 进入 **Performance**。
5. 将已解决 Beat 发布到 **Writing** 活动手稿。
6. 把 Performance 结算回 Scenario，再把 Scenario 结果暂存回 World。
7. 在 **Extensions** 和 **Settings** 中控制 Module 与模型运行方式。

### 2.2 当前原型假设

- 单机、单用户、可信本地环境。
- Host 仅监听回环地址，不实现账户、权限或远程协作。
- 当前只有一个活动 World 槽位和一个活动 Scenario 槽位。
- Writing 至多有一篇活动手稿，但可以有多篇归档手稿。
- Performance 至多有一个当前 Session，可以浏览多个归档 Performance。
- Guidance 使用一个当前运行时 Session。
- 前端不直接读写本地文件。
- 前端不依赖网络服务，除非用户主动使用 Guidance 或需要模型生成的 Performance 内容。

### 2.3 明确不做

- 不实现 World/Scenario 存档槽选择器；后端尚未暴露对应 interface。
- 不实现用户登录、云同步、多人协作。
- 不实现 Module 安装、卸载或在线市场；当前只编辑已发现 Module 的启用状态和设置。
- 不实现归档 Performance/Manuscript 分页；当前数据量按本地原型处理。
- 不在前端复制 Domain 校验规则；以后端错误为权威。
- 不把 SSE 当作完整事件日志或状态重放来源。
- 不持久化图节点坐标到后端。
- 不在前端显示 API Key 明文或从后端读取密钥正文。

---

## 3. 成功标准

完整原型必须满足以下结果：

1. 首次启动进入 World 工作台，无白屏、无必须手工配置的前端环境变量。
2. 没有模型配置时，World、Scenario、Writing、Extension 页面仍可使用。
3. World 六类实体均可创建、编辑、删除、审阅暂存、提交、撤销、重做和保存。
4. Guidance 可开始、流式显示、继续、失败重试、取消、刷新、选择提案项并提交。
5. Scenario 可查看 World 连接状态、从 World 重建、创建 Scene、绑定、Processing、规则结算、清理和回写 World。
6. Performance 可从 Scene 启动、创建与绑定 Beat、处理、发布到手稿、完成或放弃、归档及浏览历史。
7. Writing 可创建、编辑、保存、撤销、重做、归档、改名和删除归档手稿。
8. Settings 可配置 OpenAI 与模型策略；Extensions 可编辑完整候选配置并明确展示“需重启”。
9. 每个修改操作都使用最新 `expectedStateId`，HTTP 409 时不丢失用户输入。
10. SSE 断线不会冻结页面；重连后重新读取权威工作区。
11. 键盘可完成主要操作；焦点、对比度和动态提示满足基本无障碍要求。
12. `npm run build`、`npm run test`、`npm run test:e2e` 均有明确结果。

---

## 4. 设计原则

### 4.1 前端 module 必须有深度

页面不直接散落 `fetch`、JSON 解析、错误映射、Query Key 和 SSE 重连逻辑。每个业务区形成一个前端 module：

```text
Screen
  └─ feature hook
       └─ domain-oriented client interface
            └─ HTTP/SSE adapter
```

例如 World 页面只学习：

```ts
worldWorkspace.load()
worldWorkspace.stageElement(input)
worldWorkspace.commit(changeIds)
worldWorkspace.subscribe(onHint)
```

它不应学习：

- URL 拼接。
- Problem Details 的嵌套结构。
- 何时解析 JSON。
- 409 如何刷新。
- EventSource 如何重连。
- 每个请求如何设置请求头。

HTTP adapter 是 transport seam。Mock Service Worker 是测试 adapter。不要再为只有一个实现的纯函数引入空转 interface。

### 4.2 后端状态是权威状态

- TanStack Query 保存后端权威快照。
- 表单草稿、选中项、抽屉开关、图视口属于本地 UI 状态。
- SSE 只发出“对应工作区可能变化”的提示；收到后 invalidate/refetch。
- mutation 成功响应若包含完整工作区，直接写入缓存，避免额外闪烁。
- 不通过前端事件增量重建 World、Scenario、Performance 或 Writing。

### 4.3 所有修改都显式乐观并发

- mutation 从当前 Query Cache 读取 `stateId`，不要把初次 render 的值永久闭包捕获。
- mutation 进行期间禁用同一资源的冲突操作。
- HTTP 409 后先重新读取权威状态，再让用户选择放弃草稿或基于新状态重试。
- 不自动重试非幂等 POST/PATCH/DELETE。

### 4.4 不把后端内部术语原样倾倒给玩家

保留业务名词 World、Scenario、Scene、Performance、Beat、Guidance、Module；其他状态用中文解释：

- `Binding` → “待绑定”
- `Processing` → “处理中”
- `Settled` → “已结算”
- `Editing` → “编辑中”
- `Archived` → “已归档”
- `IsDirty` → “有未保存修改”
- `RestartRequired` → “重启后生效”

错误详情页可以同时显示稳定错误码和 Trace ID，主提示只显示可执行的中文说明。

---

## 5. 技术方案

### 5.1 技术栈

创建 `src/Tavi.Web`，采用：

- React + TypeScript，开启 `strict`。
- Vite，单页应用。
- React Router，管理 URL 和页面级错误。
- TanStack Query，管理后端权威状态、mutation 和失效。
- Zod，运行时解析后端 DTO 和表单结构。
- React Hook Form，复杂表单和动态 JSON Schema 表单外围状态。
- `@xyflow/react`，World 关系图。
- `@dagrejs/dagre`，仅用于自动布局；布局结果不提交后端。
- `lucide-react`，统一图标。
- CSS Modules + 全局 design tokens；不引入重量级 UI 框架。
- Vitest + Testing Library + MSW，单元和交互测试。
- Playwright，端到端测试。
- ESLint + Prettier，统一质量门槛。

包版本选择实施时最新的相互兼容稳定版本，并提交 `package-lock.json`。禁止使用浮动的 CDN 脚本。

### 5.2 为什么不使用 Electron

仓库已有 MAUI WebView 外壳和本地 Host。前端只需生成静态资源。另加 Electron 会重复窗口、打包和生命周期实现。

### 5.3 构建输出

`vite.config.ts`：

- `build.outDir = "../Tavi.Host/wwwroot"`
- `build.emptyOutDir = true`
- `base = "/"`
- 开发服务器将 `/api` 代理到 `http://127.0.0.1:5178`
- 开发服务器不代理静态页面 fallback；由 Vite 自己处理。

Host 发布已在 `Tavi.Host.csproj` 中调用 `npm ci` 和 `npm run build`。MAUI 构建已在 `Tavi.App.csproj` 中调用前端构建并复制 `wwwroot`。前端实现不得另建一套复制脚本。

### 5.4 建议脚本

```json
{
  "scripts": {
    "dev": "vite",
    "build": "tsc -b && vite build",
    "preview": "vite preview",
    "lint": "eslint . --max-warnings 0",
    "format": "prettier --write .",
    "format:check": "prettier --check .",
    "test": "vitest run",
    "test:watch": "vitest",
    "test:e2e": "playwright test",
    "check": "npm run lint && npm run format:check && npm run test && npm run build"
  }
}
```

---

## 6. 目录和 module 结构

```text
src/Tavi.Web/
  package.json
  package-lock.json
  tsconfig.json
  tsconfig.app.json
  tsconfig.node.json
  vite.config.ts
  eslint.config.js
  playwright.config.ts
  index.html
  src/
    main.tsx
    app/
      App.tsx
      router.tsx
      providers.tsx
      AppShell.tsx
      AppShell.module.css
      routes.ts
    styles/
      reset.css
      tokens.css
      global.css
    shared/
      http/
        httpClient.ts
        problemDetails.ts
        schemas.ts
      events/
        sseSubscription.ts
        useWorkspaceEvents.ts
      query/
        queryClient.ts
        queryKeys.ts
      ui/
        Button/
        Dialog/
        Drawer/
        EmptyState/
        ErrorNotice/
        Field/
        IconButton/
        InlineStatus/
        LoadingSkeleton/
        Menu/
        Select/
        SplitPane/
        Tabs/
        Toast/
        Tooltip/
      hooks/
        useConfirm.ts
        useDebouncedValue.ts
        useDocumentTitle.ts
        useHotkeys.ts
        useOnlineHostStatus.ts
      utils/
        ids.ts
        randomSeed.ts
        text.ts
        time.ts
    modules/
      world/
        client/worldClient.ts
        client/worldSchemas.ts
        model/worldTypes.ts
        model/worldGraph.ts
        model/stagingSelection.ts
        hooks/useWorldWorkspace.ts
        screens/WorldScreen.tsx
        components/...
      guidance/
        client/guidanceClient.ts
        client/guidanceSchemas.ts
        model/proposalDependencies.ts
        hooks/useGuidanceSession.ts
        components/GuidanceDrawer.tsx
      scenario/
        client/scenarioClient.ts
        model/scenarioTypes.ts
        hooks/useScenarioWorkspace.ts
        screens/ScenarioScreen.tsx
        components/...
      performance/
        client/performanceClient.ts
        model/performanceTypes.ts
        hooks/usePerformanceWorkspace.ts
        screens/PerformanceScreen.tsx
        components/...
      writing/
        client/writingClient.ts
        model/editorDraft.ts
        hooks/useWritingWorkspace.ts
        screens/WritingScreen.tsx
        components/...
      extensions/
        client/extensionsClient.ts
        model/jsonSchemaForm.ts
        screens/ExtensionsScreen.tsx
        components/...
      settings/
        client/settingsClient.ts
        screens/ModelSettingsScreen.tsx
        components/...
    test/
      setup.ts
      server.ts
      handlers/
      fixtures/
  e2e/
    world.spec.ts
    guidance.spec.ts
    scenario-performance.spec.ts
    writing.spec.ts
    settings.spec.ts
```

规则：

- `shared` 不得 import `modules`。
- 一个业务 module 可以 import `shared`，不能直接 import 另一个业务 module 的内部文件。
- 跨 module 流程通过公开的 `index.ts` 或 app orchestration hook 协调。
- DTO schema 只存在于各 module 的 `client`。
- React component 不直接 import `httpClient`。
- Domain 派生计算放在 `model`，不放在 JSX 中。

---

## 7. 全局信息架构

### 7.1 路由

| 路由 | 页面 | 默认行为 |
|---|---|---|
| `/` | 重定向 | 重定向到 `/world` |
| `/world` | World 工作台 | 恢复上次视图模式 |
| `/scenario` | Scenario 工作台 | 显示 World 连接状态 |
| `/performance` | Performance 工作台 | 无 Session 时显示启动说明 |
| `/writing` | Writing 工作台 | 选中活动手稿，否则选中最近归档 |
| `/settings/extensions` | Extensions | 展示活动与期望配置 |
| `/settings/model` | 模型设置 | OpenAI 与运行策略 |
| `*` | Not Found | 提供返回 World 按钮 |

Guidance 是全局抽屉，不占独立主路由。刷新页面后通过 `/api/v1/guidance/session` 恢复。

### 7.2 App Shell

桌面宽度大于等于 1180 px：

```text
┌─────────────────────────────────────────────────────────────────────┐
│ Tavi  [保存状态] [Guidance]                         [设置] [连接状态]│
├─────────────┬───────────────────────────────────────────────────────┤
│ World       │                                                       │
│ Scenario    │                    当前页面                           │
│ Performance │                                                       │
│ Writing     │                                                       │
│             │                                                       │
│ Extensions  │                                                       │
│ Settings    │                                                       │
└─────────────┴───────────────────────────────────────────────────────┘
```

窄于 1180 px：

- 左侧导航收成 56 px 图标栏。
- Inspector 和 Guidance 改为覆盖式 Drawer。
- 不支持小于 760 px 的完整图编辑；显示可用但紧凑的列表模式。

### 7.3 顶栏状态

从当前页面工作区派生：

- 绿色圆点：“已保存”
- 琥珀色圆点：“有未保存修改”
- 红色圆点：“自动保存失败”
- 灰色圆点：“当前无活动 Session”

顶栏 Guidance 按钮：

- 未配置：禁用并显示可用性说明；点击可跳模型设置。
- Idle：普通状态。
- Generating：显示旋转指示和“生成中”。
- Faulted：红色小点。

Host 连接状态：

- 正常时不持续占据视觉注意力。
- 连续两个权威查询失败后显示“本地服务连接中断”横幅。
- 恢复后显示 3 秒成功 Toast 并刷新当前页面所有 Query。

### 7.4 全局快捷键

| 快捷键 | 动作 |
|---|---|
| `Ctrl+1` | World |
| `Ctrl+2` | Scenario |
| `Ctrl+3` | Performance |
| `Ctrl+4` | Writing |
| `Ctrl+G` | 打开/关闭 Guidance |
| `Ctrl+S` | 保存当前活动工作区；表单输入内同样拦截浏览器默认行为 |
| `Ctrl+Z` | 当前页面 Undo，输入框正在编辑时保留文本撤销 |
| `Ctrl+Shift+Z` | 当前页面 Redo |
| `Escape` | 关闭最上层 Dialog/Drawer/Menu |
| `/` | 非输入状态下聚焦当前页面搜索框 |

所有快捷键必须在按钮 Tooltip 中显示；禁用动作不得触发请求。

---

## 8. 视觉系统

### 8.1 视觉方向

“深色档案馆 + 活体世界图”，克制、安静、信息密度高。避免游戏 HUD 式霓虹，也避免通用后台模板的纯表格感。

### 8.2 Design Tokens

```css
:root {
  --color-bg: #0f1412;
  --color-surface-1: #151c19;
  --color-surface-2: #1b2521;
  --color-surface-3: #24302b;
  --color-border: #31413a;
  --color-border-strong: #496055;
  --color-text: #f1f5f3;
  --color-text-muted: #aebbb5;
  --color-text-subtle: #7f9189;
  --color-accent: #87c9a5;
  --color-accent-hover: #a2d8ba;
  --color-accent-ink: #101713;
  --color-info: #78aee8;
  --color-warning: #d8b66c;
  --color-danger: #df8078;
  --color-success: #77c69a;
  --focus-ring: 0 0 0 3px rgb(135 201 165 / 35%);
  --radius-sm: 6px;
  --radius-md: 10px;
  --radius-lg: 14px;
  --shadow-popover: 0 18px 55px rgb(0 0 0 / 38%);
  --space-1: 4px;
  --space-2: 8px;
  --space-3: 12px;
  --space-4: 16px;
  --space-5: 24px;
  --space-6: 32px;
}
```

### 8.3 字体

- 中文正文：`"Microsoft YaHei UI", "PingFang SC", system-ui, sans-serif`
- 英文和数字不强制另用字体，保持 WebView 字体加载零依赖。
- 正文 14 px / 1.55。
- 辅助文本 12 px。
- 页面标题 24 px / 600。
- 卡片标题 15–16 px / 600。
- UUID 默认不完整展示；Tooltip 或详情区提供复制。

### 8.4 状态颜色不得单独传达语义

Valid、Conflict、Invalid 除颜色外必须有图标和文字。图节点选中态使用描边与背景双重变化。所有图表提供等价列表视图。

---

## 9. HTTP adapter 通用规格

### 9.1 基础请求

`httpClient.ts` 只暴露：

```ts
type RequestOptions<T> = {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  body?: unknown;
  schema: z.ZodType<T>;
  signal?: AbortSignal;
};

request<T>(path: string, options: RequestOptions<T>): Promise<T>;
requestVoid(path: string, options: Omit<RequestOptions<void>, "schema">): Promise<void>;
```

行为：

1. 相对当前 origin 请求，禁止硬编码 `5178`。
2. JSON body 设置 `Content-Type: application/json`。
3. 204 返回走 `requestVoid`。
4. 2xx 响应必须通过 Zod；解析失败抛 `ContractError`，包含路径和 Trace ID（若有），不得静默使用部分数据。
5. 非 2xx 解析 `application/problem+json`：

```ts
type TaviProblem = {
  status: number;
  title: string;
  type?: string;
  error: {
    code: string;
    message: string;
    category: string;
    operation?: string | null;
    isTransient: boolean;
    details: Record<string, string>;
    traceId: string;
  };
};
```

6. 网络错误映射为 `HostUnavailableError`。
7. AbortError 不显示 Toast。
8. GET 可由 TanStack Query重试两次，退避 300 ms、1000 ms。
9. mutation 默认不自动重试；只有明确幂等且错误 `isTransient` 时由用户点击重试。

### 9.2 时间、枚举、GUID

- 后端 JSON 使用 camelCase。
- GUID 在前端作为不透明 string，不做大小写转换。
- `DateTimeOffset` 解析为 ISO string，渲染时才转本地时间。
- 开放类型字符串不得在前端做 enum 限制。
- 后端 enum 字符串应保留未知值的展示兜底：`未知状态（rawValue）`。

### 9.3 Query Key

```ts
queryKeys = {
  world: {
    workspace: ["world", "workspace"],
    types: ["world", "types"],
    moduleActions: ["world", "module-actions"]
  },
  scenario: {
    workspace: (seed: number) => ["scenario", "workspace", seed],
    worldLink: ["scenario", "world-link"]
  },
  performance: {
    workspace: (seed: number) => ["performance", "workspace", seed],
    archives: ["performance", "archives"],
    archive: (id: string) => ["performance", "archives", id]
  },
  writing: {
    workspace: ["writing", "workspace"],
    manuscript: (id: string) => ["writing", "manuscript", id]
  },
  guidance: {
    availability: ["guidance", "availability"],
    session: ["guidance", "session"]
  },
  extensions: {
    workspace: ["extensions", "workspace"]
  },
  settings: {
    languageModel: ["settings", "language-model"],
    openAI: ["settings", "openai"]
  }
};
```

随机种子的具体规则见第 18 节，禁止组件随 render 生成新种子。

---

## 10. SSE 同步规格

### 10.1 普通工作区

订阅：

- `/api/v1/world/events`
- `/api/v1/scenario/events`
- `/api/v1/performance/events`
- `/api/v1/writing/events`

处理算法：

1. 页面对应 module 首次 mount 后建立一个 EventSource。
2. 收到任何事件，记录最近事件元数据，仅 debounce 100 ms 后 invalidate 对应 workspace。
3. `error` 事件不直接覆盖权威快照；显示警告 Toast，再 refetch。
4. EventSource 原生重连期间，页面保留旧数据并显示“正在重新同步”。
5. 重连成功后的第一个事件触发全量 refetch。
6. 页面 unmount 后可关闭该页面的 EventSource；App Shell 可选择常驻订阅 Writing/Performance，以更新全局状态，但每个 URL 全局只能有一个连接。
7. 若 30 秒没有任何事件，不判定断线；SSE 没有心跳约定。

实现 `SseSubscriptionRegistry`，以 URL 为 key 复用连接和引用计数，避免 React Strict Mode 创建重复订阅。

### 10.2 Guidance 流

订阅 `/api/v1/guidance/sessions/{sessionId}/events`。

事件：

- `guidance.operation.started`
- `guidance.text.delta`
- `guidance.operation.completed`
- `guidance.operation.failed`
- `guidance.operation.cancelled`
- `guidance.session.changed`
- `guidance.cancelled`
- `guidance.refreshed`

当前 Broker 不重放订阅前的增量。原型必须采用以下降级策略：

1. POST 返回 `sessionId` 后立即订阅。
2. `text.delta` 只作为临时流式草稿，不直接加入持久消息数组。
3. completed/failed/cancelled 事件携带 snapshot 时，以 snapshot 完整替换 Query Cache，并清空临时增量。
4. EventSource 建立后若 800 ms 内没有事件而操作仍显示 Created/Running，开始每 1 秒 GET session。
5. 页面失焦、SSE error、重新打开 Drawer 时均 GET session。
6. 临时增量标题明确标注“正在生成”；不得把不完整增量当成最终 Guidance 消息。

后续后端增加 sequence/replay 后，可以只替换 Guidance client，不改页面。

---

## 11. World 工作台

### 11.1 页面目标

World 是默认首页，服务于“看懂世界—定位实体—提出修改—审阅暂存—提交真实 World”的闭环。

### 11.2 页面布局

```text
┌─────────────────────────────────────────────────────────────────────┐
│ World  [图谱|列表]  [搜索…]  [+ 新建] [Module 操作] [撤销][重做][保存]│
├──────────────────┬─────────────────────────────┬────────────────────┤
│ 类型/实体导航    │       图谱或列表主区域       │ Inspector          │
│ Elements         │                             │ 当前实体详情/编辑  │
│ Scopes           │                             │                    │
│ Aspects          │                             │                    │
│ Relations        │                             │                    │
│ Local            │                             │                    │
├──────────────────┴─────────────────────────────┴────────────────────┤
│ 暂存栏：N 项 [有效 N] [冲突 N] [无效 N]             [展开审阅]       │
└─────────────────────────────────────────────────────────────────────┘
```

宽度建议：

- 左导航：240 px，可折叠。
- Inspector：340 px，可折叠。
- 暂存 Drawer：底部最大 55 vh。

### 11.3 数据加载

并行请求：

- `GET /api/v1/world/`
- `GET /api/v1/world/types`
- `GET /api/v1/world/module-actions`

Workspace skeleton 显示三栏轮廓。类型或 Module Action 请求失败不能阻止只读 World；相应入口显示局部错误与重试。

### 11.4 图谱视图

#### 节点

- Element 是主节点。
- Scope 是附着在 Owner Element 下方的小型容器节点。
- Aspect 不单独成为大节点，以目标 Element 上的 badge/属性行呈现。
- Relation 是 Source → Target 有向边。
- LocalAspect 使用虚线 badge。
- LocalRelation 使用虚线边。

#### Scope 表达

边和 Aspect 必须标明所属 Scope。默认显示 Scope 类型短标签；Hover 展示 Owner、Quantity 和完整 ID。

#### 交互

- 单击节点/边：选中并打开 Inspector。
- 双击空白：打开“新建 Element”。
- 双击节点：进入 Element 编辑。
- `Delete`：打开确认框，不直接删除。
- 框选只用于视觉选择，不支持批量删除。
- 搜索结果使非匹配项降至 20% 透明度，并自动 fitView。
- 布局仅由前端计算；切换页面期间可保存在 `sessionStorage`。

#### 大图降级

- 超过 250 个 Element 时默认进入列表视图，并提示“图谱较大，已切换为高性能列表”。
- 用户仍可手工开启图谱。
- 图谱只渲染当前过滤结果和一跳邻居。

### 11.5 列表视图

Tabs：

- Elements
- Scopes
- Aspects
- Relations
- Local Aspects
- Local Relations

每个表格：

- 可按名称/类型/数量排序。
- 本地内存搜索。
- 行点击打开 Inspector。
- 空状态提供对应新建按钮。
- UUID 仅在详情中展示。

### 11.6 新建和编辑

统一使用右侧 Inspector 表单，不跳页面。

#### Element

- Name：必填，trim 后提交。
- Description：多行。
- Type：可搜索 Select，来源 `world/types`；允许“自定义键”并明确大小写敏感。

#### Scope

- Quantity：整数输入。
- Type：目录 + 自定义键。
- Owner：Element 搜索 Select。
- 编辑时 Owner 只读；结构变化提示“请删除后重建”。

#### Aspect

- Quantity、Type、Element、Scope。
- 编辑时 Element 和 Scope 只读。
- Scope 选择项显示 Owner 和 Type。

#### Relation

- Quantity、Type、Source、Target、Scope。
- 编辑时 Source、Target、Scope 只读。

#### LocalAspect

- Name、Description、Quantity、Element、Scope。
- 编辑时 Element、Scope 只读。

#### LocalRelation

- Name、Description、Quantity、Source、Target、Scope。
- 编辑时 Source、Target、Scope 只读。

所有修改请求使用当前 `world.stateId`。响应的 `WorldStagingResultViewModel.world` 直接写入 workspace cache。

### 11.7 暂存审阅

底栏永远可见。展开后每项显示：

- Checkbox。
- Source：玩家 / Guidance / Scenario。
- Status：有效 / 冲突 / 无效。
- Operation 可读文本。
- Issue。
- 冲突项跳转链接。
- 删除按钮。

选择规则：

- 默认选中全部 Valid，Conflict/Invalid 不可选。
- “只选玩家”“只选 Guidance”快捷筛选。
- 点击冲突 ID 高亮双方。
- `Delete invalid` 需要确认并调用 `DELETE /staging/invalid`。
- Commit 按钮显示“提交 N 项”。
- Commit 请求携带当前真实 `stateId`，不是 `stagingRevision`。
- 提交成功后立即 invalidate World；关闭 Drawer 前展示成功摘要。

### 11.8 Undo/Redo/Save

- `canUndo/canRedo` 控制按钮。
- 若暂存区非空，Undo/Redo 仍按后端允许状态执行，但按钮旁提示“仅影响已提交历史，不处理暂存项”。
- Save 只保存已提交 World；暂存项不是已提交内容。保存按钮 Tooltip 明示此点。
- 自动保存错误显示可展开 Banner，包含重试“立即保存”。

### 11.9 Module Actions

- 列表展示 Name、Description、Module。
- `parameterSchema` 解析为 JSON Schema。
- 原型至少支持：string、number/integer、boolean、enum、array、object、required、description、default。
- 不支持的 schema keyword 原样展示 JSON 编辑器，并在本地通过基本 JSON 解析后提交。
- 调用后返回 World staging result，自动打开暂存 Drawer 并高亮 affected IDs。

### 11.10 World HTTP interface

| Method | Path | 用途 |
|---|---|---|
| GET | `/api/v1/world/` | 完整工作区 |
| GET | `/api/v1/world/types` | 类型目录 |
| POST/PATCH/DELETE | `/api/v1/world/elements...` | Element 暂存修改 |
| POST/PATCH/DELETE | `/api/v1/world/scopes...` | Scope 暂存修改 |
| POST/PATCH/DELETE | `/api/v1/world/aspects...` | Aspect 暂存修改 |
| POST/PATCH/DELETE | `/api/v1/world/relations...` | Relation 暂存修改 |
| POST/PATCH/DELETE | `/api/v1/world/local-aspects...` | LocalAspect 暂存修改 |
| POST/PATCH/DELETE | `/api/v1/world/local-relations...` | LocalRelation 暂存修改 |
| POST | `/api/v1/world/staging/commit` | 提交选择项 |
| DELETE | `/api/v1/world/staging/{changeId}` | 删除一项 |
| DELETE | `/api/v1/world/staging/invalid` | 删除全部无效项 |
| POST | `/api/v1/world/undo` | 撤销 |
| POST | `/api/v1/world/redo` | 重做 |
| POST | `/api/v1/world/save` | 保存 |
| GET | `/api/v1/world/module-actions` | 可用 Module Action |
| POST | `/api/v1/world/module-actions/{module}/{action}` | 调用 Action |
| GET | `/api/v1/world/events` | SSE 提示流 |

---

## 12. Guidance 全局抽屉

### 12.1 入口和布局

从顶栏 `Guidance` 打开 460–560 px 右侧 Drawer：

```text
┌────────────────────────────────────┐
│ Guidance      Provider: OpenAI  [×]│
├────────────────────────────────────┤
│ 对话历史                           │
│ 玩家消息 / Guidance 消息           │
│ 流式草稿                           │
│                                    │
│ 提案审阅卡片（若存在）             │
├────────────────────────────────────┤
│ [输入框.........................]  │
│ [发送] [取消/重试]                 │
└────────────────────────────────────┘
```

### 12.2 未配置状态

`GET /api/v1/guidance/` 返回 `available=false`：

- 不请求 current session。
- 展示 message。
- 主按钮“配置模型”，跳 `/settings/model`。
- World 其他功能不受影响。

### 12.3 开始会话

空会话展示 Narrative Potential 输入：

- 最少 1 个非空字符。
- 建议提示：“描述你希望世界中出现的叙事可能性……”
- POST `/sessions` 后保存 operation 和 snapshot，立即订阅 SSE。
- 发送后输入框清空，但保留一份本地草稿直到 POST 成功。

### 12.4 继续、取消、重试、刷新、遗忘

- Idle：允许继续消息。
- Generating：输入禁用，显示取消。
- Faulted：显示失败码、说明、是否可重试；预填 `retryMessage`，用户编辑后 Retry。
- Refresh：二次确认“将清空 Guidance 对话和运行缓存，不删除已进入 World 暂存区的提案”。
- Forget：仅在非活跃状态展示于更多菜单；成功后清空 Query Cache。

### 12.5 提案选择依赖

后端没有显式 dependency 数组，但引用可推导依赖：

- AddScope 依赖 Proposed Owner Element。
- AddAspect 依赖 Proposed Element 和 Proposed Scope。
- AddRelation 依赖 Proposed Source、Target 和 Scope。
- LocalAspect/LocalRelation 同理。

`proposalDependencies.ts` 构建有向图：

```ts
type ProposalDependencyGraph = {
  dependenciesByChangeId: Map<string, Set<string>>;
  dependentsByChangeId: Map<string, Set<string>>;
};
```

选择行为：

- 默认全选。
- 选择一项时递归选中其依赖。
- 取消一项时递归取消所有依赖它的项。
- UI 在自动变化项上短暂高亮并解释原因。
- 无法解析的 Proposed 引用使对应项禁用并显示本地契约错误；不得提交猜测结果。

### 12.6 Commit 结果

- `Committed`：显示成功，关闭提案区，invalidate World，并提供“打开 World 暂存区”。
- `InvalidSelection`：保持选择，逐项显示 issues。
- `WorldConflict`：提示 World 已变化，显示“刷新 Guidance”而不是自动重试旧提案。
- `SessionNotReady`：刷新 snapshot。

注意：Guidance commit 当前最终进入 World 构筑日志/暂存流程，页面必须以返回 snapshot 和随后 World refetch 为准，不自行假定已保存。

### 12.7 Guidance HTTP interface

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/api/v1/guidance/` | 无 | Availability |
| GET | `/api/v1/guidance/session` | 无 | 当前 Snapshot |
| POST | `/api/v1/guidance/sessions` | `{ potential }` | 202 + Operation |
| GET | `/api/v1/guidance/sessions/{sessionId}` | 无 | Snapshot |
| POST | `/api/v1/guidance/sessions/{sessionId}/messages` | `{ message }` | 202 + Operation |
| POST | `/api/v1/guidance/sessions/{sessionId}/retry` | `{ message }` | 202 + Operation |
| POST | `/api/v1/guidance/sessions/{sessionId}/refresh` | 无 | Snapshot |
| POST | `/api/v1/guidance/sessions/{sessionId}/commit` | `{ acceptedChangeIds }` | Commit Result |
| POST | `/api/v1/guidance/sessions/{sessionId}/cancel` | 无 | Snapshot |
| DELETE | `/api/v1/guidance/sessions/{sessionId}` | 无 | 204 |
| GET | `/api/v1/guidance/sessions/{sessionId}/events` | SSE | Guidance Event |

---

## 13. Scenario 工作台

### 13.1 页面布局

```text
┌─────────────────────────────────────────────────────────────────────┐
│ Scenario [World连接状态] [从World重建] [结果暂存回World] [撤销][保存]│
├────────────────────┬────────────────────────────────────────────────┤
│ Scene Definitions  │  待绑定       处理中          已结算           │
│ 搜索/Module过滤    │  ┌──────┐     ┌──────┐       ┌──────┐         │
│ [+ 创建]           │  │Scene │     │Scene │       │Scene │         │
│                    │  └──────┘     └──────┘       └──────┘         │
└────────────────────┴────────────────────────────────────────────────┘
```

### 13.2 数据加载

并行：

- `GET /api/v1/scenario/?randomSeed={definitionSeed}`
- `GET /api/v1/scenario/world-link`

页面 Session 固定 `definitionSeed`。刷新浏览器时从 `sessionStorage` 恢复。

### 13.3 World 连接状态

显示：

- 当前 World State。
- Scenario 来源 World State。
- Scenario State。
- Binding/Processing/Settled Scene 数量。
- Link state 的中文解释。

“从 World 重建”：

- 仅当没有 Scene 且 World 暂存区为空时后端允许。
- Dialog 明确这会替换当前空 Scenario。
- 请求需要 `expectedWorldStateId` 与 `expectedScenarioStateId`。
- 成功后更新 Scenario 和 World Link。

“结果暂存回 World”：

- 只在所有流程满足后端条件时可尝试。
- 成功返回 changeId 后显示“已加入 World 暂存区”，提供跳转。
- `changed=false` 显示“没有需要回写的变化”。

### 13.4 Scene Definition

左栏卡片：

- Name、Module、Description。
- Settlement badges：Rules / Performance 等原始能力映射。
- Slot 数量和必填数量。
- “创建 Scene”按钮。

创建请求携带当前 `definitionSeed` 和 Scenario stateId。

### 13.5 Scene 状态列

#### Binding

- Slot 显示 min/max、允许 Element Types、Required Aspect Groups。
- 点击 Slot 打开 Element Picker。
- Picker 只优先展示本地可判断匹配项，但不得把本地筛选当最终校验。
- 支持搜索、查看 Element 已占用提示。
- 保存绑定调用 PUT；清空调用 DELETE。
- 满足本地可见 minimum 后启用“开始处理”，最终合法性以后端为准。

#### Processing

- 显示冻结绑定。
- 若 Settlement 包含 Rules：显示“按规则结算”。
- 若包含 Performance：显示“开始 Performance”并跳转 Performance。
- Processing Scene 不显示删除按钮。

#### Settled

- 只读显示结算状态和绑定。
- 可删除单项。
- 顶部有“清理全部已结算”。

### 13.6 状态变化

任何 Scene mutation 成功后使用返回的完整 workspace 更新缓存，并重新请求 world-link。409 保留当前 Picker 或 Dialog 的选择。

### 13.7 Scenario HTTP interface

| Method | Path | 用途 |
|---|---|---|
| GET | `/api/v1/scenario/?randomSeed=` | Workspace 和 Definitions |
| GET | `/api/v1/scenario/world-link` | World 连接状态 |
| POST | `/api/v1/scenario/start-from-world` | 从 World 重建 |
| POST | `/api/v1/scenario/stage-outcome` | 结果暂存回 World |
| POST | `/api/v1/scenario/scenes` | 创建 Scene |
| PUT/DELETE | `/api/v1/scenario/scenes/{sceneId}/bindings/{slotId}` | 绑定 |
| POST | `/api/v1/scenario/scenes/{sceneId}/processing` | 开始处理 |
| POST | `/api/v1/scenario/scenes/{sceneId}/settle-rules` | 规则结算 |
| DELETE | `/api/v1/scenario/scenes/{sceneId}` | 删除 |
| POST | `/api/v1/scenario/scenes/clear-settled` | 清理已结算 |
| POST | `/api/v1/scenario/undo` | 撤销 |
| POST | `/api/v1/scenario/redo` | 重做 |
| POST | `/api/v1/scenario/save` | 保存 |
| GET | `/api/v1/scenario/events` | SSE |

---

## 14. Performance 工作台

### 14.1 三种顶级状态

1. **无 Session**：说明如何从 Scenario Processing Scene 启动，提供“前往 Scenario”。
2. **有活动 Performance**：完整 Beat 工作台。
3. **已结束但未归档**：只读总结，提供归档；Completed/Abandoned 都不能继续创建 Beat。

页面另有“历史”Tab，浏览归档。

### 14.2 活动布局

```text
┌─────────────────────────────────────────────────────────────────────┐
│ Performance [来源Scene] [状态] [撤销][重做][保存] [完成/放弃]       │
├───────────────────┬─────────────────────────────────────────────────┤
│ Beat Definitions  │  Beats                                          │
│ 搜索               │  待绑定 → 处理中 → 已解决 → 已发布             │
│ [+ 创建]           │                                                 │
└───────────────────┴─────────────────────────────────────────────────┘
```

### 14.3 从 Scenario 启动

启动动作位于 Scenario Processing Scene 卡片：

- 生成并固定 `performanceSeed`。
- POST `/api/v1/performance/start`。
- 成功后导航 `/performance`，同一 seed 存入 sessionStorage。
- 若已有 Performance，409 后跳转现有 Performance，不提供覆盖。

### 14.4 Beat Definition 与 Beat

Beat Definition 卡显示 Name、Module、Description、Slots。创建使用相同 `performanceSeed`。

Beat 状态：

- Binding：设置槽位绑定，开始 Processing。
- Processing：输入 interaction，自然语言多行；Resolve 期间卡片局部 loading。
- Resolved：展示 paragraphs，允许发布到 Writing。
- Published：展示 Manuscript ID，禁止重复按钮或显示“已发布”。

状态原始值若不同，进入只读未知状态并显示 raw value。

### 14.5 发布到 Writing

发布前必须读取 Writing workspace：

- 无活动手稿：Dialog 提示先创建手稿，提供跳转。
- 有活动手稿：请求携带 `expectedManuscriptStateId` 和 Performance stateId。
- 成功后同时更新 Performance cache，并 invalidate Writing。
- Manuscript 冲突时重新读取 Writing，确认后重试；不得自动重复发布。后端发布幂等，但前端仍需明确反馈。

### 14.6 Complete、Abandon、Archive

- Complete 触发 Scenario + Performance 跨 module 结算，需要两个 expected state IDs。
- 成功后更新 Performance，并 invalidate Scenario。
- Abandon 二次确认，说明不会把 Performance 结果结算回 Scenario。
- Completed 或 Abandoned 才显示 Archive。
- Archive 后活动工作区为空，invalidate archive list。

### 14.7 历史

- `GET /archives` 显示摘要表。
- 点击一项请求完整 snapshot，右侧 Drawer 只读展示 Source Scene、Beat 和 paragraphs。
- 不提供删除归档，因为后端没有 interface。

### 14.8 Performance HTTP interface

| Method | Path | 用途 |
|---|---|---|
| GET | `/api/v1/performance/?randomSeed=` | 当前工作区 |
| POST | `/api/v1/performance/start` | 从 Scene 启动 |
| GET | `/api/v1/performance/archives` | 历史摘要 |
| GET | `/api/v1/performance/archives/{id}` | 历史详情 |
| POST | `/api/v1/performance/beats` | 创建 Beat |
| PUT/DELETE | `/api/v1/performance/beats/{id}/bindings/{slotId}` | 绑定 |
| POST | `/api/v1/performance/beats/{id}/processing` | 开始处理 |
| POST | `/api/v1/performance/beats/{id}/resolve` | 解决 Beat |
| POST | `/api/v1/performance/beats/{id}/publish` | 发布到 Writing |
| POST | `/api/v1/performance/complete` | 结算回 Scenario |
| POST | `/api/v1/performance/abandon` | 放弃 |
| POST | `/api/v1/performance/undo` | 撤销 |
| POST | `/api/v1/performance/redo` | 重做 |
| POST | `/api/v1/performance/save` | 保存 |
| POST | `/api/v1/performance/archive` | 归档结束项 |
| GET | `/api/v1/performance/events` | SSE |

---

## 15. Writing 工作台

### 15.1 页面布局

```text
┌─────────────────────────────────────────────────────────────────────┐
│ Writing [新建手稿]                                  [撤销][重做][保存]│
├────────────────────┬────────────────────────────────────────────────┤
│ 手稿库             │ 活动编辑器 / 归档阅读器                         │
│ 编辑中             │ Title                                          │
│ 已归档             │ Paragraph 1                                    │
│                    │ Paragraph 2                                    │
│                    │ [+ 段落]                                       │
└────────────────────┴────────────────────────────────────────────────┘
```

### 15.2 手稿库

- 活动手稿固定置顶并标 “编辑中”。
- 归档按 updatedAtUtc 降序。
- 每项显示 title、preview、段落数、更新时间。
- 搜索在本地匹配 title 和 preview。
- 无活动手稿时突出“新建手稿”。

### 15.3 活动编辑器

#### 草稿策略

当前后端每次 PATCH 替换完整段落，并要求最新 stateId。为避免每个按键都产生提交：

- 输入仅更新本地 draft。
- 失焦、`Ctrl+Enter` 或切换段落时提交。
- 同一时间只允许一个段落 mutation；后续提交进入本地串行队列。
- 队列每次发送前读取最新 Query Cache stateId。
- 成功响应完整替换 Writing session cache。
- 提交中显示小型 spinner。
- 本地 dirty draft 使用蓝点；后端 `isDirty` 使用琥珀保存状态，两者不得混为一谈。
- 离开页面时若有本地 draft，使用应用内确认 Dialog；浏览器刷新使用 `beforeunload`。

#### 段落操作

- 新建：在当前段落后插入；无选择时末尾。
- 更新：失焦提交。
- 删除：确认后提交。
- 暂不实现拖拽排序，因为后端没有 Move Operation 的 HTTP 映射。
- 空段落允许存在。

#### 标题

- 点击标题进入 inline edit。
- 失焦提交。
- 活动标题请求必须携带 expectedStateId。

#### Undo/Redo

- 存在本地未提交 draft 时禁用，并提示“先保存或放弃当前文本草稿”。
- 后端 Undo/Redo 成功后清空对应段落 draft。

### 15.4 Save 与 Archive

- Save 保存后端已提交内容；先 flush 本地 draft queue。
- Archive 先 flush 所有 draft，再二次确认并发送 expectedStateId。
- 成功后活动编辑器变为空，归档项自动选中。

### 15.5 归档手稿

- 只读正文。
- 可修改标题；无需 expectedStateId。
- 可永久删除；必须输入或确认手稿标题。
- 不提供恢复为活动手稿，因为后端没有 interface。

### 15.6 Writing staging 限制

Application 支持 Writing staging，但 Host 当前只返回 `stagedChangeCount`，没有详情和操作 endpoint。本原型：

- 顶栏仅显示“有 N 项内部暂存修改”。
- 不提供虚假的审阅 UI。
- 若数量大于 0，Archive 前说明后端会提交全部暂存修改。

### 15.7 Writing HTTP interface

| Method | Path | 用途 |
|---|---|---|
| GET | `/api/v1/writing/` | 手稿库和活动 Session |
| POST | `/api/v1/writing/manuscripts` | 新建 |
| GET | `/api/v1/writing/manuscripts/{id}` | 详情 |
| PATCH | `/api/v1/writing/manuscripts/{id}/title` | 归档改名 |
| DELETE | `/api/v1/writing/manuscripts/{id}` | 删除归档 |
| PATCH | `/api/v1/writing/session/title` | 活动改名 |
| POST | `/api/v1/writing/session/paragraphs` | 插入段落 |
| PATCH | `/api/v1/writing/session/paragraphs/{id}` | 更新段落 |
| DELETE | `/api/v1/writing/session/paragraphs/{id}?expectedStateId=` | 删除段落 |
| POST | `/api/v1/writing/session/undo` | 撤销 |
| POST | `/api/v1/writing/session/redo` | 重做 |
| POST | `/api/v1/writing/session/save` | 保存 |
| POST | `/api/v1/writing/session/archive` | 归档 |
| GET | `/api/v1/writing/events` | SSE |

---

## 16. Extensions 页面

### 16.1 页面结构

- 顶部显示 revision、全局 message。
- `restartRequired=true` 时固定显示琥珀 Banner。
- Module 列表支持按 Name/ID 搜索和 Enabled 筛选。
- 每个 Module 卡展示：
  - Name、ID、Version、Description。
  - Active Enabled 与 Desired Enabled。
  - Dependencies。
  - Active Settings 与 Desired Settings 差异。
  - 设置表单。

### 16.2 编辑模型

页面载入后复制完整 `modules` 为 draft。保存时必须发送全部 Module，而非只发送变化项：

```ts
{
  expectedRevision: workspace.revision,
  modules: draft.map(({ id, desiredEnabled, desiredSettings }) => ({
    id,
    enabled: desiredEnabled,
    settings: desiredSettings
  }))
}
```

### 16.3 依赖提示

- 前端可根据 Dependencies 提示风险，但不自行决定最终合法性。
- 关闭被其他启用 Module 依赖的项时显示确认。
- 后端拒绝时保留 draft 并定位相关 Module。

### 16.4 JSON Schema 表单

支持集与 World Module Actions 相同。额外要求：

- `additionalProperties` 不明确时不任意删除未知设置。
- 原始 JSON 模式始终可切换。
- 切换表单/JSON 不丢字段。
- 本地校验只提升体验，保存必须以后端候选校验为准。

### 16.5 并发冲突

revision conflict：

- GET 最新 workspace。
- 对比本地 draft 与旧 snapshot。
- Dialog 提供“放弃本地修改”和“基于最新配置重新应用”。
- 重新应用按 Module ID 合并用户实际编辑字段，不覆盖用户未触碰字段。

### 16.6 Extensions HTTP interface

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/api/v1/extensions/` | 无 | Extension Workspace |
| PUT | `/api/v1/extensions/settings` | `{ expectedRevision, modules }` | Extension Workspace |

---

## 17. 模型设置页面

### 17.1 分区

#### OpenAI 连接

- Endpoint：URL 输入。
- Model：必填。
- API Key：password；后端只返回 `hasApiKey`。
- 空 Key 的语义是保留现值。
- Client Type：来自后端已知值；当前至少 Chat。
- Supports Required Tool Choice：开关。
- Enable Thinking：三态，默认/启用/禁用。

显示：

- 已保存 Key：只显示“已配置”，不伪造星号长度。
- “清除 Key”当前不支持；不要提供无效按钮。

#### 运行策略

- Max Tool Rounds。
- Max Output Repair Attempts。
- Overall Timeout Seconds。
- Tool Calls。
- Native JSON Output。
- Streaming。

枚举字段使用 Select，但保留未知值显示。

### 17.2 保存

优先使用 `PUT /api/v1/settings/` 一次提交两组设置并取得统一成功响应。当前 Host 内部仍按 OpenAI、Language Model 的顺序保存，并非事务；第二步失败时第一步可能已经持久化。因此失败后必须重新 GET 两组设置，不得继续显示提交前缓存。

成功响应：

- 更新两个 Query Cache。
- invalidate Guidance availability。
- Toast 显示 message。

当前 Settings 没有 revision。页面载入后若另一个窗口修改，保存会覆盖；在页面底部明确说明“设置以最后一次保存为准”。

### 17.3 密钥安全

- 不写 localStorage/sessionStorage。
- 不写日志、Toast、错误详情。
- React state 在成功提交后立即置空 API Key 输入。
- 测试 fixture 只使用明显虚构值。

### 17.4 Settings HTTP interface

| Method | Path | Request | Response |
|---|---|---|---|
| PUT | `/api/v1/settings/` | `{ languageModel, openAI }` | Settings Save Result |
| GET | `/api/v1/settings/language-model` | 无 | Language Model Settings |
| PUT | `/api/v1/settings/language-model` | Language Model Update | Language Model Settings |
| GET | `/api/v1/settings/openai` | 无 | OpenAI Configuration |
| PUT | `/api/v1/settings/openai` | OpenAI Update | OpenAI Configuration |

页面正常保存使用聚合 PUT；拆分 PUT 保留给局部重试和 adapter 测试，不在 UI 中放两个相互竞争的“保存”按钮。

---

## 18. 随机种子管理

当前 Scenario/Performance interface 要求前端重复提供随机种子。建立唯一的 `RandomSeedRegistry`：

```ts
type SeedScope =
  | "scenario-definitions"
  | `scenario-settlement:${sceneId}`
  | `performance:${performanceIdOrSceneId}`
  | `performance-resolve:${beatId}`;
```

规则：

1. 使用 `crypto.getRandomValues(new BigInt64Array(1))` 生成有符号 64 位整数。
2. 传 JSON 时 JavaScript number 无法安全表达全部 Int64，因此只生成 `Number.MIN_SAFE_INTEGER` 到 `Number.MAX_SAFE_INTEGER` 范围内整数。
3. 每个 scope 存 `sessionStorage`。
4. 同一 Definition 列表查询和 Create 请求必须复用相同 seed。
5. 同一 Resolve 用户点击重试时复用 seed；用户明确“重新生成”时才换 seed。
6. 页面普通 refetch 不换 seed。
7. Session/Performance ID 变化后清理旧 scope。

在后端改为 DefinitionSet token 前，这个 module 是唯一允许生成和管理种子的地方。

---

## 19. 错误、冲突和恢复

### 19.1 展示层级

| 错误范围 | 展示 |
|---|---|
| 字段输入错误 | 字段下方 |
| 单个 mutation 失败 | 表单内 Alert + Toast |
| 局部列表加载失败 | 局部 Error State |
| 页面权威状态加载失败 | 页面 Error State |
| Host 不可用 | 全局顶部 Banner |
| 未知前端错误 | Route Error Page |

### 19.2 409 冲突标准流程

```text
mutation → 409
  → 保留表单草稿
  → invalidate/refetch workspace
  → 比较操作目标是否仍存在
      ├─ 不存在：说明已被删除，只允许复制草稿/关闭
      └─ 存在：展示新值与草稿，允许基于新 stateId 重试
```

不得：

- 静默用新 stateId 自动重复删除。
- 在不知道语义是否仍成立时自动重发 Add/Commit。
- 用旧 mutation 响应覆盖更晚的 SSE refetch。

### 19.3 保存失败

快照中的 `autoSaveError`：

- 页面固定 Banner。
- 提供“立即保存”。
- 保存成功后等待权威快照清除错误；前端不自行隐藏。

普通 World/Scenario 事件只有字符串 error。展示时标注为运行时状态提示，不当作可编程错误码。

### 19.4 Trace ID

错误详情折叠区：

- Error Code。
- Operation。
- Trace ID + 复制按钮。
- Details key/value。
- `isTransient` 为 true 时显示重试建议。

不显示 stack trace。

---

## 20. Loading、空状态和操作反馈

### 20.1 Loading

- 首次页面加载使用结构化 Skeleton。
- 后台 refetch 不清空现有内容，只在标题旁显示同步 spinner。
- mutation 只锁定相关对象，不锁整页。
- 规则结算、模型生成等长操作显示经过时间，但不伪造进度百分比。

### 20.2 空状态文案

- World 无 Element：“从一个角色、地点或概念开始构筑世界。”
- Scenario 无 Scene：“从左侧选择一个 Scene Definition。”
- Performance 无 Session：“在 Scenario 中让支持 Performance 的 Scene 进入处理中。”
- Writing 无活动手稿：“创建手稿，已发布的 Beat 会追加到这里。”
- Guidance 未配置：“配置模型后，Guidance 可以把叙事想法整理成 World 提案。”
- Extensions 无 Module：“当前部署没有发现可配置 Module。”

### 20.3 Toast

- 成功 Toast 3 秒。
- 警告 6 秒。
- 错误不自动消失，除非页面已有等价 inline error。
- 相同 SSE 错误 10 秒内去重。

---

## 21. 可访问性

必须完成：

- 所有 icon-only button 有 `aria-label` 和 Tooltip。
- Dialog 打开后 focus trap，关闭后焦点回触发器。
- Drawer 有标题关联。
- 表单 label 与控件关联，错误使用 `aria-describedby`。
- Toast 容器使用合适的 live region；普通成功 `polite`，阻塞错误 `assertive`。
- 图谱所有实体可从等价列表访问。
- 键盘 Tab 顺序与视觉顺序一致。
- `prefers-reduced-motion` 下关闭 Drawer 大幅动画、图节点弹跳和 shimmer。
- 正文和背景至少 WCAG AA 对比度。
- 不依赖 hover 才能发现关键动作。
- Loading spinner 同时提供可读文本。

---

## 22. 性能与资源管理

- Route 级 code splitting。
- World 图相关库只在 `/world` 图谱模式加载。
- JSON Schema 表单编辑器按需加载。
- Query 默认 `staleTime`：
  - 活动工作区 10 秒。
  - 类型和 Module Actions 5 分钟。
  - 归档列表 30 秒。
  - Settings/Extensions 30 秒。
- 不对 mutation 响应做无限历史缓存。
- EventSource 在最后一个订阅者退出后关闭。
- 搜索输入 150 ms debounce。
- Writing 文本 draft 不进入 TanStack Query cache。
- 大型手稿只渲染可见段落的优化推迟到超过 500 段后；原型先保持普通 DOM。

---

## 23. 前端类型与 Schema 最低要求

每个后端 ViewModel 必须有：

1. TypeScript 类型。
2. Zod schema。
3. 至少一个成功 fixture。
4. 对关键多态结构的解析测试。

特别测试：

- Guidance `ProposalChangeViewModel` 的 `kind` 判别联合。
- Nullable `performance`、`failure`、`proposal`、`publication`。
- Extension `settingsSchema/settings` 任意 JSON。
- 未知 enum 字符串。
- Problem Details 中缺失可选字段。

禁止直接使用 `as SomeDto` 绕过解析。

---

## 24. 测试策略

### 24.1 model 单元测试

- Guidance 提案依赖选择和级联取消。
- World 图节点/边投影。
- Scope/Element 查找索引。
- RandomSeedRegistry 稳定性和范围。
- Writing draft queue 串行化。
- 409 merge decision。
- 状态中文映射的未知值兜底。

### 24.2 HTTP adapter 测试

MSW 覆盖：

- 每个 GET 正常解析。
- 每类 mutation body 包含正确 expectedStateId。
- 204。
- Problem Details。
- 非 JSON 500。
- DTO 契约破坏。
- Abort。
- Host unavailable。

### 24.3 component 测试

- World 创建 Element 后显示 staging。
- 暂存冲突不可选。
- Guidance 自动选择依赖。
- Scene Slot 最小/最大数量提示。
- Performance 无手稿时阻止发布并给出跳转。
- Writing 失焦提交且串行使用新 stateId。
- Extension revision conflict 保留 draft。
- API Key 成功保存后清空输入。

### 24.4 SSE 测试

用可注入 EventSource adapter：

- 普通事件触发 debounce refetch。
- Strict Mode 不产生两个连接。
- error 后保留旧数据。
- Guidance delta 临时追加。
- completed snapshot 替换并清空 delta。
- 重连触发全量 refetch。

### 24.5 Playwright 端到端

至少五条：

1. **World 基础闭环**：创建 Element → staging → commit → save → undo → redo。
2. **Guidance**：未配置状态；使用 mock Host 时完成流式生成、选择依赖、commit。
3. **Scenario 到 Performance**：创建 Scene → 绑定 → Processing → 启动 Performance → Beat → Complete。
4. **Writing**：创建 → 编辑两段 → Save → Archive → Rename → Delete。
5. **Settings/Extensions**：保存设置、编辑 Module、显示 restart required、处理 revision conflict。

真实后端 E2E 不应调用真实 OpenAI；Guidance 使用可控 mock server 或后端测试 adapter。

---

## 25. 分阶段实施计划

### Phase 0：工程骨架

- 创建 Vite React TypeScript 工程。
- 配置 build 输出、proxy、lint、format、Vitest、Playwright。
- 实现 tokens、App Shell、Router、QueryClient、Error Boundary。
- 实现 HTTP client、Problem Details、基础 UI。
- 验证 Host 和 MAUI 能加载 `index.html`。

验收：空壳页面能从 `dotnet run --project src/Tavi.Host` 与 MAUI WebView 打开。

### Phase 1：World

- DTO schemas 和 adapter。
- 图/列表双视图。
- 六类实体 Inspector。
- staging、commit、undo/redo/save。
- SSE。
- Module Actions。

验收：完整 World E2E 通过。

### Phase 2：Guidance

- Availability、Drawer、对话、流式草稿。
- proposal 多态卡片和依赖选择。
- commit/refresh/retry/cancel/forget。
- 丢事件降级轮询。

验收：mock 流式 E2E 通过，SSE 断开可恢复。

### Phase 3：Scenario

- World Link。
- Definition 列表、Scene lanes、Slot Picker。
- Rules settlement、start-from-world、stage-outcome。
- seed registry。

验收：Scenario E2E 通过。

### Phase 4：Performance

- 空/活动/结束状态。
- Definition、Beat 生命周期。
- Writing publish 协调。
- Complete/Abandon/Archive/history。

验收：Scenario → Performance → Writing 跨 module E2E 通过。

### Phase 5：Writing

- Library。
- draft editor 和串行 mutation queue。
- Save/Undo/Redo/Archive。
- 归档查看、改名、删除。

验收：刷新/冲突时不丢本地 draft。

### Phase 6：Settings 与 Extensions

- 两类设置表单。
- JSON Schema 表单。
- revision conflict。
- restart required。

验收：设置和 Module E2E 通过，密钥不进入存储和日志。

### Phase 7：加固

- 全部空状态、错误状态、Skeleton。
- 键盘和无障碍审查。
- 大 World 性能测试。
- Host/MAUI 发布验证。
- `npm run check` 和 `.NET` 现有测试。

---

## 26. 实施会话的工作规则

接手者应按以下顺序工作：

1. 阅读本文。
2. 阅读：
   - `src/Tavi.Host/README.md`
   - `src/Tavi.Host/Endpoints/*.cs`
   - `src/Tavi.Host/ViewModels/*.cs`
   - `src/Tavi.App/LocalWebHost.cs`
   - `src/Tavi.App/Tavi.App.csproj`
3. 检查工作树，保护用户已有修改。
4. 先搭通真实 Host 的一个 GET，再批量写 schema。
5. 每完成一个业务 module 就补齐该 module 的测试，不把测试全部留到最后。
6. 不修改后端业务规则来迎合 UI；遇到 interface 缺口，使用本文降级策略并记录。
7. 不引入另一套全局状态容器保存后端快照。
8. 不添加 IDE 专用配置。
9. 不提交真实 API Key、存档正文或用户提示词 fixture。
10. 最终同时验证浏览器 Host 和 MAUI 构建路径。

---

## 27. 完成定义

只有同时满足以下条件，才能称为“完整前端原型”：

- [ ] `src/Tavi.Web` 存在并有锁文件。
- [ ] 所有主路由可直接刷新。
- [ ] 所有当前 Host endpoint 都有明确 UI 使用点，或在本文“不做”范围内有说明。
- [ ] World 六类实体全部可编辑。
- [ ] Guidance 完整生命周期可操作。
- [ ] Scenario 与 World 协调可操作。
- [ ] Performance 与 Scenario/Writing 协调可操作。
- [ ] Writing 活动与归档生命周期可操作。
- [ ] Extensions 与 Settings 可操作。
- [ ] 409 不会静默丢弃表单输入。
- [ ] SSE 断线可恢复。
- [ ] API Key 不落地到前端持久化。
- [ ] `npm run check` 通过。
- [ ] Playwright 关键流程通过。
- [ ] `npm run build` 输出到 `src/Tavi.Host/wwwroot`。
- [ ] `dotnet build src/Tavi.Host/Tavi.Host.csproj` 通过。
- [ ] Windows 环境具备 workload 时，`dotnet build src/Tavi.App/Tavi.App.csproj` 通过。

---

## 28. 已知后端 interface 改进清单

这些不是当前前端原型的先决条件，但后续应优先处理：

1. Guidance 事件增加 `sequence`、有限重放和按 operation 查询。
2. 普通 Runtime 事件增加 revision 或 `resync-required`。
3. Scenario/Performance Definition 改为 `definitionSetToken`，不让前端重复管理 random seed。
4. World/Scenario 增加存档 catalog 与切换生命周期。
5. Runtime 增加显式用例级方法，逐步隐藏 `ExecuteAsync(Func<I*Workspace,...>)`。
6. Runtime 事件错误改为稳定结构，不只提供字符串。
7. Settings 增加 revision 乐观并发。
8. Writing Host 暴露 staging 详情与提交/删除 interface。
9. 归档列表增加分页、排序和过滤。
10. Host 增加一次性 bootstrap/status snapshot，减少首屏跨工作区竞态。

前端 module 的 interface 已按这些变化可局部替换 adapter 的方式设计；未来改进不应迫使所有 Screen 重写。

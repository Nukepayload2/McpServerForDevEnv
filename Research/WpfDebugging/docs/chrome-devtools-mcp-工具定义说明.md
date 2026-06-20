# chrome-devtools-mcp 的工具定义机制

本文讲 chrome-devtools-mcp 里一个工具是怎么定义出来、怎么汇总、最后怎么变成模型能调用的 MCP 工具的。想直接看代码的话，核心就三个文件：定义框架在 `G:\Projects\chrome-devtools-mcp\src\tools\ToolDefinition.ts`，分类在 `G:\Projects\chrome-devtools-mcp\src\tools\categories.ts`，把定义跑起来的是 `G:\Projects\chrome-devtools-mcp\src\ToolHandler.ts`。

## 一个工具长什么样

每个工具本质上是一个对象，描述「叫什么、干什么、接受什么参数、怎么执行」。最上面那层字段叫 `BaseToolDefinition`，定义在 `ToolDefinition.ts` 的 `BaseToolDefinition` 接口里，包含这几样：

- **name**：工具名，比如 `take_snapshot`、`click`。最终按名字字母序排（见后面汇总那一节）。
- **description**：给模型看的说明文字，告诉它什么时候该用这个工具。
- **annotations**：一组元信息，包括 `category`（属于哪个分类）、`readOnlyHint`（是不是只读）、可选的 `conditions`（额外要开哪些实验特性）、可选的 `title`。注意 `readOnlyHint` 是「这个工具会不会改环境」的提示，跟它要不要写文件不完全是一回事——比如 `take_snapshot` 本质是读快照，但因为带了个 `filePath` 参数可能落盘，它的 `readOnlyHint` 被标成了 `false`。
- **schema**：参数定义，用 zod 写，是个 `ZodRawShape`（也就是「对象有哪些字段、每个字段什么类型」的形状，不是一整个 `zod.object`）。
- **blockedByDialog**：页面级工具才用得上。设成 `true` 表示执行前先检查页面上有没有没处理掉的浏览器对话框（alert/confirm/prompt），有的话直接抛错，让模型先去调 `handle_dialog`。
- **verifyFilesSchema**：一个 schema 字段名的数组，标出哪些参数是文件路径。被标中的字段在执行 handler 之前会先过一遍 `context.validatePath` 校验。

## 两种工厂：普通工具和页面级工具

定义工具用两个工厂函数，都在 `ToolDefinition.ts` 里。

绝大多数工具是**页面级**的，用 `definePageTool`。它干的事就是在普通定义上多打一个 `pageScoped: true` 标记，并且 handler 拿到的 request 里多一个 `page` 对象——也就是当前要操作的那个浏览器页面。`take_snapshot`、`click`、`fill` 这些全是页面级工具。`snapshot.ts` 里的 `takeSnapshot` 就是一个标准样例，可以对照看。

少数工具不绑定具体页面（比如列出所有标签页、装扩展），用 `defineTool`，handler 没有 `page`。

这两个工厂都支持两种写法：直接传一个定义对象，或者传一个 `(args) => 定义` 的工厂函数。后者用来根据启动参数动态生成工具——比如某些工具只有在开了某个实验特性、或指定了隔离上下文名时才该出现。汇总那一步会把工厂函数真正调用一次。

## handler 的三件套

handler 是工具真正干活的地方，签名是 `async (request, response, context) => Promise<void>`。

**request** 里的 `params` 就是模型传进来的参数，类型由 schema 推断出来。页面级工具还会多一个 `page`（类型是 `ContextPage`），能拿到 puppeteer 的页面对象、按 uid 取元素、取 a11y 节点、等事件、读 DevTools 数据等。

**response** 是一个收集结果的容器，类型是 `Response` 接口（同样在 `ToolDefinition.ts`）。handler 不直接返回文本，而是往 response 里塞东西：`appendResponseLine` 加一行文字，`includeSnapshot` 附带页面快照，`attachImage` 附图，还有一堆 `setIncludeNetworkRequests`、`attachConsoleMessage`、`attachTraceSummary`、heap snapshot 相关的 setter。等 handler 跑完，由 `McpResponse` 统一把这些拼成最终的文本和结构化内容。

**context** 类型是 `Context`，提供跨页面的能力：存文件（`saveFile`）、管标签页（`newPage`/`closePage`/`selectPage`）、模拟环境（`emulate`）、按 id 取页面（`getPageById`）、heap snapshot 的各种查询、扩展管理等等。这两个类型的注释都写明「只放 tools/* 里真正用到的方法」，所以看接口就能知道工具能调什么。

## 参数 schema 的几个要点

参数用 zod 写，但传进去的是 `ZodRawShape`——一组字段定义的原始形状，框架内部再包成 `zod.object(...).passthrough()`（在 `ToolHandler.ts` 的构造函数里，`registeredInputSchema` 那行）。passthrough 意味着模型多传的字段不会直接报错，而是会被 `unknownArgumentNames` 检查出来，返回一句「未知参数，应该是这几个，去掉重试」的提示。

有两个现成的 schema 片段可以复用，都在 `ToolDefinition.ts` 末尾：

- `pageIdSchema`：一个 `pageId` 数字字段，用来按 id 指定目标页面。
- `timeoutSchema`：一个可选的 `timeout` 数字字段，0 或负数会被归一化成 undefined（即用默认超时）。

还有个自动注入的细节：如果一个工具是页面级的，并且服务端开了 `experimentalPageIdRouting`、又不是 slim 模式，框架会自动在它的 schema 最前面塞一个 `pageId` 字段。这样模型就能在调用时指定「这个 click 作用在第 3 个标签页」。这个拼接逻辑在 `ToolHandler.ts` 构造函数里 `inputSchema` 那段。

## 分类和默认开关

工具归到 `ToolCategory` 这个枚举里，定义在 `categories.ts`，一共十类：input、navigation、emulation、performance、network、debugging、extensions、experimentalThirdParty、memory、experimentalWebmcp。每类还有一个给人看的标签（`labels`）。

其中 extensions、experimentalThirdParty、experimentalWebmcp 这三类**默认是关掉的**（列在 `OFF_BY_DEFAULT_CATEGORIES` 里），得用对应的启动参数打开。判断逻辑在 `ToolHandler.ts` 的 `getCategoryStatus`：默认开的类，只有显式传 false 才关；默认关的类，得显式传 true 才开。另外 `annotations.conditions` 里还能挂额外的实验特性 flag，道理类似。

## 工具怎么汇总起来

汇总在 `G:\Projects\chrome-devtools-mcp\src\tools\tools.ts` 的 `createTools(args)` 里。它把 `tools/` 下各个模块（console、emulation、input、network、snapshot 等十几个）的导出值用 `Object.values` 全收集到一起；如果是 slim 模式，就只收 `slim/tools` 那一小套。收集时遇到函数类型的（也就是工厂），就传 `args` 调一次，拿到真正的定义对象。最后全部按 `name` 字母序排好返回。

## 从定义到可执行：ToolHandler

`ToolHandler.ts` 是把「定义」变成「对外注册的 MCP 工具 + 执行入口」的地方。每个工具定义都包成一个 `ToolHandler` 实例。

构造时它先算两件事。一是 `shouldRegister`：工具如果被分类或 condition 关掉了，默认就不注册（对外不可见），除非是 `viaCli` 模式——那种情况下仍注册，但调用时返回一句「这工具在某某分类下，当前禁用，用 `chrome-devtools start --xxx=true` 开启」的提示（提示文案见 `buildDisabledMessage`）。二是把 schema 处理成最终对外暴露的 `inputSchema`（含上面说的自动 pageId 注入）和 `registeredInputSchema`。

真正执行是 `handle(params)` 方法，流程大致是：

1. 如果工具被禁用，直接返回禁用原因。
2. 用 `unknownArgumentNames` 检查多余参数，有的话返回带纠正提示的错误。
3. 抓一把全局互斥锁（`toolMutex`），保证工具串行执行、不会并发打架。
4. 对 `verifyFilesSchema` 里标出的文件路径参数逐个 `validatePath`。
5. 如果是页面级工具：按 `pageId`（开了路由的话）或「当前选中页」解析出 page，`setPage` 绑到 response 上；如果 `blockedByDialog` 为真，先 `page.throwIfDialogOpen()`。
6. 调 handler。handler 里抛的异常会被 `response.setError` 接住，不会让整个调用崩掉。
7. 调 `response.handle(...)` 把收集到的内容拼成 MCP 的 `content` 和 `structuredContent`。如果 response 上有 error，结果带上 `isError: true`。
8. 最后无论成败，记一条调用遥测（工具名、参数、成功与否、耗时分桶），释放锁。

页面级工具的判定靠 `isPageScopedTool`——就是看对象上有没有 `pageScoped === true`。

## 最小的一个例子

`take_snapshot` 几乎是最简单的页面级工具了，可以作为模板。它在 `G:\Projects\chrome-devtools-mcp\src\tools\snapshot.ts` 里，结构就是：用 `definePageTool` 传一个对象，里面有 name、description、annotations（category 是 DEBUGGING、readOnlyHint 因为 filePath 标成 false）、schema（verbose 和 filePath 两个可选字段）、`blockedByDialog: true`（有对话框时先挡住）、`verifyFilesSchema: ['filePath']`（落盘前校验路径），handler 里就一行 `response.includeSnapshot(...)`。想做新工具，照着它改就行。

## 相关源码（绝对路径）

- `G:\Projects\chrome-devtools-mcp\src\tools\ToolDefinition.ts` — 工具定义框架：`BaseToolDefinition`、`ToolDefinition`、`defineTool`/`definePageTool`、`Response`/`Context`/`ContextPage` 接口、`pageIdSchema`/`timeoutSchema`。
- `G:\Projects\chrome-devtools-mcp\src\tools\categories.ts` — `ToolCategory` 枚举、各类标签、默认关闭的分类清单。
- `G:\Projects\chrome-devtools-mcp\src\tools\tools.ts` — `createTools`：汇总各模块导出、处理工厂、排序。
- `G:\Projects\chrome-devtools-mcp\src\ToolHandler.ts` — 把定义注册成 MCP 工具并执行：禁用判定、参数校验、加锁、page 注入、handler 调用、结果拼装。
- `G:\Projects\chrome-devtools-mcp\src\tools\snapshot.ts` — 最简单的页面级工具样例（`take_snapshot`、`wait_for`）。
- `G:\Projects\chrome-devtools-mcp\src\McpResponse.ts` — `Response` 的实现，负责把 handler 收集的内容拼成最终响应。
- `G:\Projects\chrome-devtools-mcp\src\tools\input.ts` — 一组更典型的页面级工具（click/fill/hover 等），看 `includeSnapshot`、uid 参数怎么用。

# chrome-devtools-mcp 的 `take_snapshot` a11y 文本格式

本文梳理 chrome-devtools-mcp 项目里 `take_snapshot` 工具产生的 a11y（无障碍树）文本快照到底是什么格式，供 WpfDebugging 研究参考。所有引用的源码文件都标了绝对路径，方便直接跳过去核对。

## 一句话概括

`take_snapshot` 调用 puppeteer 的无障碍树接口拿到页面 a11y 树，给每个节点编一个稳定的 uid，然后用「缩进 + 属性」的方式逐行打印成一棵树。文本里每个可交互元素对应一行，模型（或人）靠 uid 去引用、点击、填充某个元素。

## 数据是怎么流出来的

链路很短：

工具入口 `take_snapshot` 只是把 `verbose` 和 `filePath` 两个参数记下来，交给响应对象 `McpResponse`。真正干活的是 `McpResponse.handle()`——它先用 `TextSnapshot.create()` 把页面的 a11y 树读进来、给节点编号，再交给 `SnapshotFormatter` 转成文本（`toString`）和结构化对象（`toJSON`）。

对应到代码：
- 工具定义和参数：`G:\Projects\chrome-devtools-mcp\src\tools\snapshot.ts`，看 `takeSnapshot`。
- 读取 a11y 树、编号、识别选中元素：`G:\Projects\chrome-devtools-mcp\src\TextSnapshot.ts`，看 `TextSnapshot.create`。
- 文本和 JSON 的排版：`G:\Projects\chrome-devtools-mcp\src\formatters\SnapshotFormatter.ts`，看 `SnapshotFormatter` 类。
- 拼装进最终响应、决定是贴在回复里还是存成文件：`G:\Projects\chrome-devtools-mcp\src\McpResponse.ts`，看 `handle` 和 `format` 两个方法。

a11y 树本身的读取用的是 `pptrPage.accessibility.snapshot({ includeIframes: true, interestingOnly: !verbose })`——也就是说，iframe 里的内容会一并带上，而 `verbose` 决定要不要包含那些「没意义」的节点（后面细说）。

## 节点长什么样

每个节点是一个 `TextSnapshotNode`，本质是 puppeteer 的 `SerializedAXNode` 加上一个自编的 id。类型在 `G:\Projects\chrome-devtools-mcp\src\types.ts`（`TextSnapshotNode`）。

puppeteer 会把无障碍属性对象拍平到节点顶层，所以一个节点上可能挂着这些字段：`role`、`name`、`value`，以及一堆布尔或标量属性，比如 `disabled`、`expanded`、`focused`、`selected`、`checked`、`busy`、`atomic`、`live`、`relevant`、`errormessage`、`details`、`level`、`readonly`、`required`、`valuemin`、`valuemax`、`valuetext` 等等。

**uid 的编号规则**值得单独说，因为它直接关系到跨快照的元素引用是否稳定。格式是 `<snapshotId>_<自增计数>`，例如 `1_1`、`1_2`、`1_3`。`snapshotId` 是进程内自增的整数，每拍一次快照加一。但同一个 DOM 节点（按 `loaderId_backendNodeId` 唯一标识）在前后两次快照里会尽量复用同一个 uid，这样模型在「拍快照→操作→再拍快照」的循环里，同一个按钮的 uid 不会跳来跳去。另外有个小细节：`option`（下拉项）角色节点的 `value` 会被强制设成它自己的文本名，因为 a11y 树里 option 本身不带 value。

## 文本格式：一行一个节点

整份文本由「可选的一段提示」加上「逐行渲染的节点树」组成。每个节点恰好占一行，结构是：

```
<缩进><属性段><可能的选中标记>
```

逐条说规则：

**缩进。** 每往下一层缩进两个空格，根节点顶格不缩进。子节点的 depth 比父节点大一，所以是 `' '.repeat(depth * 2)`。

**属性段的顺序。** 这是格式里最容易被忽略、又最容易写错的部分，因为它不是「按字段定义顺序」，而是分两段拼出来的：

1. 开头永远是 `uid=<id>`。
2. 接着是 `role`（如果有的话）。有个特例：当 role 是 `none` 时，文本里印的是 `ignored`，不是 `none`。
3. 再接 `name`（如果非空），用双引号包起来，像 `"登录"`。空 name 不输出。
4. 最后是「其余属性」，按字段名的字母升序逐个排。

这里有个反直觉的点：`value` 不在 name 后面，而是混在「其余属性」里按字母序排，所以它通常落在很靠后的位置（v 这个字母靠后）。比如一个 textbox 的输出是 `uid=1_1 textbox "textbox" details="..." errormessage="..." live="..." relevant="..." value="value"`——value 在最后。

**其余属性的渲染方式。** 遍历节点字段（已经按字母序），跳过一组永远不输出的内部字段：`id`、`role`、`name`、`elementHandle`、`children`、`backendNodeId`、`loaderId`。剩下的，布尔值 `true` 就直接把字段名打出来（比如 `checked`、`busy`），字符串或数字就打成 `key="value"`。

**布尔别名。** 有四个布尔属性在为真时，会额外先插一个「能力词」，再输出原词本身：

| 原属性 | 额外插入的能力词 |
|---|---|
| `disabled` | `disableable` |
| `expanded` | `expandable` |
| `focused` | `focusable` |
| `selected` | `selectable` |

所以一个既禁用又聚焦的按钮会印成 `... disableable disabled focusable focused`——先说「它可被禁用/可被聚焦」，再说「它现在确实禁用了/聚焦了」。这个能力词出现在原属性按字母序该出现的位置。

**选中标记。** 如果某个节点的 uid 正好等于快照里记录的「DevTools Elements 面板选中元素」，这行末尾会追加 ` [selected in the DevTools Elements panel]`。

## 顶部那段提示和选中元素

「选中元素」指的是用户在 DevTools 的 Elements 面板里点中的那个节点。它的来源是 `devtoolsData.cdpBackendNodeId`，会被解析成 uid 存进 `selectedElementUid`。

只有一种情况会在快照最前面插一段英文提示：**非 verbose 模式 + 确实有选中元素 + 但这个选中元素没出现在当前这棵裁剪过的树里**。这时会印：

```
Note: there is a selected element in the DevTools Elements panel but it is not included into the current a11y tree snapshot.
Get a verbose snapshot to include all elements if you are interested in the selected element.

```

意思是「面板里选了个东西，但它被默认的精简树过滤掉了，想要就拍 verbose」。verbose 模式下这段提示永不出现。

## verbose 到底改了什么

`verbose` 透传给 puppeteer 的 `interestingOnly`（取反）。默认（false）只返回「有意思」的节点，puppeteer 会把 `none` 这类无语义节点裁掉；`verbose=true` 则返回完整 a11y 树，那些被裁掉的节点会回来，role 为 `none` 的就印成 `ignored`。换句话说：默认快照更短更聚焦，verbose 快照更全但更啰嗦。

## 结构化（JSON）格式

除了给人/模型看的文本，格式化器还会同时产出一份结构化对象，放进响应的 `structuredContent.snapshot`。它的规则是：每个节点先 `structuredClone` 一份属性 map（含 id、role、name 和所有非排除属性，布尔属性同样会带上能力词），如果有子节点就再加一个 `children` 数组。这份数据和文本是同源的，只是形态不同，方便程序消费。

## 响应里怎么包装

最终拼进模型响应时：
- 没指定 `filePath`：文本部分加一行标题 `## Latest page snapshot`，下面紧跟整棵树的文本，行与行之间用 `\n` 连接。
- 指定了 `filePath`：把文本按 UTF-8 存成 `.txt` 文件，响应里只说一句 `Saved snapshot to <路径>.`，路径同时写进 `structuredContent.snapshotFilePath`。

## 几个真实样例

下面这些是从单元测试里直接摘出来的，输入是手造的节点、输出是断言里的期望文本，最权威。

值和标量属性（注意 value 落在最后）：

```
uid=1_1 textbox "textbox" details="details-id" errormessage="error-id" live="polite" relevant="additions" value="value"
  uid=1_2 statictext "text"
```

布尔属性带能力词（`disableable disabled`）：

```
uid=1_1 button "button" atomic busy disableable disabled
  uid=1_2 statictext "text"
```

嵌套 + 多类型 + 两个能力词：

```
uid=1_1 root "root"
  uid=1_2 button "button" disableable disabled focusable focused
  uid=1_3 textbox "textbox" value="value"
```

带选中标记：

```
uid=1_1 checkbox "checkbox" checked [selected in the DevTools Elements panel]
  uid=1_2 statictext "text"
```

## 相关源码（绝对路径）

要核对细节，按这个清单去看：

- `G:\Projects\chrome-devtools-mcp\src\tools\snapshot.ts` — 工具入口、参数定义。
- `G:\Projects\chrome-devtools-mcp\src\TextSnapshot.ts` — 读 a11y 树、给节点编 uid、识别选中元素。
- `G:\Projects\chrome-devtools-mcp\src\formatters\SnapshotFormatter.ts` — 文本和 JSON 的排版规则（缩进、属性顺序、布尔别名、排除字段都在这里）。
- `G:\Projects\chrome-devtools-mcp\src\McpResponse.ts` — 拼装响应、决定贴回复还是存文件。
- `G:\Projects\chrome-devtools-mcp\src\types.ts` — `TextSnapshotNode` 类型定义。
- `G:\Projects\chrome-devtools-mcp\tests\formatters\snapshotFormatter.test.ts` — 输入↔期望输出对照，本文样例的来源。
- `G:\Projects\chrome-devtools-mcp\tests\formatters\snapshotFormatter.test.js.snapshot` — 顶部提示、选中标记的真实文案快照。
- `G:\Projects\chrome-devtools-mcp\src\third_party\index.ts` — puppeteer `SerializedAXNode` 等类型的转出处。

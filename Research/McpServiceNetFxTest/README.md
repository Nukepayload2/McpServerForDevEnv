# MCP HTTP 服务测试

这个测试项目包含了对 MCP HTTP 服务的单元测试和集成测试。

## 测试内容

### McpHttpServiceTests 类
测试 MCP HTTP 服务的 JSON-RPC 请求和响应结构：

- `TestToolsListRequest` - 测试工具列表请求
- `TestBuildSolutionRequest` - 测试构建解决方案请求
- `TestGetErrorListRequest` - 测试获取错误列表请求
- `TestGetSolutionInfoRequest` - 测试获取解决方案信息请求
- `TestBuildProjectRequest` - 测试构建项目请求
- `TestErrorResponseStructure` - 测试错误响应结构
- `TestBuildResultResponseStructure` - 测试构建结果响应结构
- `TestSolutionInfoResponseStructure` - 测试解决方案信息响应结构

### McpExceptionTests 类
测试自定义 MCP 异常类：

- `TestMcpExceptionCreation` - 测试 MCP 异常的创建
- `TestMcpErrorCodeConstants` - 测试 MCP 错误代码常量

## 运行测试

### 方法 1: 使用 Visual Studio
1. 在 Visual Studio 中打开解决方案
2. 右键点击测试项目，选择"运行测试"
3. 或者使用测试资源管理器

### 方法 2: 使用 .NET CLI
```bash
# 进入测试项目目录
cd G:\Projects\McpServerForDevEnv\Research\McpServiceNetFxTest

# 运行所有测试
dotnet test

# 运行特定测试类
dotnet test --filter "TestClass=McpHttpServiceTests"

# 运行特定测试方法
dotnet test --filter "TestMethod=TestToolsListRequest"
```

### 方法 3: 使用 vstest.console
```bash
vstest.console.exe "bin\Debug\net472\McpServiceNetFxTest.dll"
```

## 集成测试说明

注意：大部分测试主要验证 JSON 结构和类型定义的正确性，不需要实际的 MCP 服务运行。

要进行完整的集成测试（包括 HTTP 请求），需要：

1. 首先启动 McpServiceNetFx 应用程序
2. 选择一个 Visual Studio 实例
3. 启动 MCP 服务（默认端口 8080）
4. 取消相关测试代码中的注释以启用 HTTP 请求

## 测试覆盖率

测试覆盖了以下功能：
- JSON-RPC 请求格式验证（4个 MCP 工具）
- JSON-RPC 响应格式验证
- 强类型序列化/反序列化
- 自定义异常类功能
- MCP 工具调用参数验证

**MCP 工具清单（符合 Plan.md 要求）：**
1. `build_solution` - 构建整个解决方案
2. `build_project` - 按项目名称编译指定项目
3. `get_error_list` - 获取错误列表
4. `get_solution_info` - 获取当前解决方案路径和项目列表

## 注意事项

- 测试使用 MSTest 框架
- 目标框架为 .NET Framework 4.7.2，与主项目保持一致
- 包含了对所有 MCP 工具的请求格式测试
- 包含了强类型响应的序列化测试

## 故障排除

如果测试失败：
1. 确保主项目编译成功
2. 检查所有必要的 NuGet 包是否已还原
3. 确保 JSON 字符串格式正确
4. 检查强类型定义是否存在
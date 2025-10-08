# McpServerForDevEnv

**[简体中文](README.zh-CN.md)** | English

Visual Studio MCP Server GUI, providing development environment capabilities integrated with Visual Studio for AI assistants.

## Progress
The project is in Beta stage

- [x] Standalone Service Manager
    - [x] Basic functionality
    - [x] English text resources
- [x] VSIX Embedded Service Manager
- [ ] Research which MCP tools are still needed to cover basic use cases

## Features (Standalone Service Manager)

Expose selected Visual Studio instances to AI programming tools through HTTP-based MCP protocol locally

### Core Features
- **Build**: Support building entire solution or specified projects with Debug/Release configuration options
- **Error Management**: Get current error list in the solution
- **Document Management**: Get current active document information and list of all open documents
- **Trigger Resources and Template Generation**: Execute all custom tools for specified projects
- **Solution Information**: Get current solution location and structure

### Visual Studio Integration
- **Auto Detection**: Automatically detect multiple Visual Studio instances
- **Single Control**: Select desired Visual Studio instance from the list to expose as MCP service
- **Real-time Monitoring**: Monitor Visual Studio runtime status, automatically disconnect on exit
- **Version Compatibility**: Support Visual Studio from 2015 to latest version

### Permission Control
- **Fine-grained Permissions**: Configure independent permission levels for each MCP feature
- **Three Permission Modes**: Allow, Ask, Deny
- **Security Control**: Ensure AI assistants can only perform authorized operations

### User Interface
- **Modern Interface**: Windows 11 style interface based on WPF-UI framework
- **Multi-tab Design**: Separate management for service management, permission configuration, and log viewing
- **Real-time Status Display**: Dynamic updates of service status and connection information

## Requirements
- Windows 11: No dependencies
- Earlier Windows versions: .NET Framework version >= 4.7.2

## Features (VSIX Embedded Service Manager)
- Open main interface from View -> Other Windows -> MCP Service Manager
- Provides the same MCP tools and permission management as the standalone service manager
- Supports dark theme and light theme
- Does not support controlling other Visual Studio instances, only the current Visual Studio instance
- Supports Visual Studio from 2022 to latest version
## Project Structure

Main projects:
- McpServiceNetFx is a standalone WPF UI,
- McpServiceNetFx.Core is the underlying MCP implementation
- McpServiceNetFx.VsixAsync is the Visual Studio plugin

Research projects are in the Research folder

## Usage

1. Launch the application and select target Visual Studio instance
2. Configure service port and start MCP server
3. Add generated configuration to Claude Desktop or other MCP clients
4. Configure permission levels for each feature as needed
5. Monitor service runtime status through logs

## Security

- Permission control ensures AI assistants can only perform authorized operations
- Service binds to local network loopback only, preventing external access

## License

This project is licensed under the Apache License 2.0 - see the LICENSE file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
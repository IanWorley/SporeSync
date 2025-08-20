# SporeSync

A .NET-based file synchronization system using SSH.NET for tracking and syncing files across directories and remote servers.

## Project Overview

SporeSync is designed to track files in local directories and synchronize them with remote servers using SSH/SFTP protocols. The project follows Clean Architecture principles for maintainable and testable code.

## Architecture

```
SporeSync/
├── SporeSync.Domain/          # 🎯 Core business models and interfaces
├── SporeSync.Application/     # 🚀 Use cases and application services  
├── SporeSync.API/            # 🌐 Web API controllers and configuration
└── SporeSync.sln             # Solution file
```

## Features

- 📁 **Directory Tracking** - Monitor local directories for file changes
- 🔄 **File Synchronization** - Sync files to remote servers via SSH/SFTP
- 🔐 **SSH Authentication** - Support for password and private key authentication
- 📊 **File Status Tracking** - Track file states (New, Modified, Synced, etc.)
- 🚀 **RESTful API** - HTTP API for managing sync operations
- 🏗️ **Clean Architecture** - Separation of concerns with dependency inversion

## Prerequisites

- .NET 9.0 SDK
- SSH server access (for file synchronization)

## Getting Started

### 1. Clone and Build
```bash
git clone <repository-url>
cd SporeSync
dotnet build
```

### 2. Run the API
```bash
cd SporeSync.API
dotnet run
```

### 3. Access API Documentation
Navigate to `https://localhost:5001/swagger` (or the configured port) to view the API documentation.

## Project Structure

### SporeSync.Domain
Contains core business models and interfaces:
- **Models**: `FileInfo`, `Directory`, `SshConfiguration`
- **Interfaces**: `ISshService`, `IFileTrackingService`
- **Enums**: `FileStatus`, `AuthenticationType`

### SporeSync.Application
Contains application services and use cases:
- File synchronization logic
- Directory management services
- Business rule orchestration

### SporeSync.API
Contains Web API controllers and configuration:
- RESTful endpoints
- Dependency injection setup
- API models and DTOs

## Key Models

### FileInfo
Tracks individual files with metadata:
- File path and size information
- Hash for change detection
- Sync status and timestamps
- Remote path mapping

### Directory
Manages directory tracking:
- Local and remote path configuration
- File count and total size tracking
- Auto-sync capabilities

### SshConfiguration
SSH connection settings:
- Host, port, and authentication details
- Support for password and private key auth
- Connection timeout and SSL settings

## Dependencies

- **SSH.NET** - SSH/SFTP client library
- **ASP.NET Core** - Web API framework
- **System.ComponentModel.DataAnnotations** - Model validation

## Development

### Adding New Features
1. Define interfaces in `SporeSync.Domain`
2. Implement business logic in `SporeSync.Application`
3. Create API endpoints in `SporeSync.API`
4. Follow Clean Architecture principles

### Service Layer Guidelines
See [SERVICE_ARCHITECTURE.md](SERVICE_ARCHITECTURE.md) for detailed service architecture documentation and best practices.

## Configuration

Configuration can be managed through `appsettings.json` in the API project:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "AllowedHosts": "*"
}
```

## Contributing

1. Follow the established architecture patterns
2. Maintain separation of concerns
3. Write unit tests for business logic
4. Document public APIs and interfaces

## License

[Add your license information here]

## Future Enhancements

- [ ] Infrastructure project for concrete implementations
- [ ] Entity Framework integration for data persistence  
- [ ] Real-time file watching with SignalR
- [ ] Batch synchronization operations
- [ ] Conflict resolution strategies
- [ ] Comprehensive logging and monitoring

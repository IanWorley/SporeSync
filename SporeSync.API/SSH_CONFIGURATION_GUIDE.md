# SSH Configuration Guide

This guide explains how to configure SSH connections for SporeSync.

## Configuration Files

- `ssh-config.json` - Production configuration
- `ssh-config.template.json` - Template for development/testing

## SSH Configuration Options

### SshConfiguration

| Property               | Type   | Default  | Description                                   |
| ---------------------- | ------ | -------- | --------------------------------------------- |
| `Name`                 | string | -        | Descriptive name for the connection           |
| `Host`                 | string | -        | SSH server hostname or IP address             |
| `Port`                 | int    | 22       | SSH server port                               |
| `Username`             | string | -        | SSH username                                  |
| `Password`             | string | ""       | SSH password (leave empty for key-based auth) |
| `PrivateKeyPath`       | string | ""       | Path to private key file                      |
| `PrivateKeyPassphrase` | string | ""       | Passphrase for private key (if encrypted)     |
| `TimeoutSeconds`       | int    | 30       | Connection timeout in seconds                 |
| `AuthType`             | enum   | Password | Authentication method                         |
| `IsActive`             | bool   | true     | Whether this connection is active             |

### Authentication Types

- `Password` - Use password authentication
- `PrivateKey` - Use private key authentication
- `PasswordAndPrivateKey` - Try both methods

### RemotePathOptions

| Property     | Type   | Default | Description                      |
| ------------ | ------ | ------- | -------------------------------- |
| `RemotePath` | string | -       | Path on remote server to monitor |
| `LocalPath`  | string | -       | Local path for syncing files     |

### RemoteMonitor

| Property                 | Type | Default | Description                      |
| ------------------------ | ---- | ------- | -------------------------------- |
| `CheckIntervalSeconds`   | int  | 30      | How often to check for changes   |
| `ErrorRetryDelaySeconds` | int  | 60      | Delay between retry attempts     |
| `MaxRetryAttempts`       | int  | 3       | Maximum number of retry attempts |
| `EnableLogging`          | bool | true    | Enable detailed logging          |

## Example Configurations

### Production Server
```json
{
  "Settings": {
    "SshConfiguration": {
      "Name": "Production Server",
      "Host": "prod.example.com",
      "Port": 22,
      "Username": "sporesync",
      "Password": "",
      "PrivateKeyPath": "/home/user/.ssh/id_rsa",
      "PrivateKeyPassphrase": "",
      "TimeoutSeconds": 30,
      "AuthType": "PrivateKey",
      "IsActive": true
    },
    "RemotePathOptions": {
      "RemotePath": "/var/sporesync/data",
      "LocalPath": "/home/user/SporeSync/data"
    },
    "RemoteMonitor": {
      "CheckIntervalSeconds": 30,
      "ErrorRetryDelaySeconds": 60,
      "MaxRetryAttempts": 3,
      "EnableLogging": true
    }
  }
}
```

### Development Server
```json
{
  "Settings": {
    "SshConfiguration": {
      "Name": "Development Server",
      "Host": "dev.example.com",
      "Port": 22,
      "Username": "developer",
      "Password": "",
      "PrivateKeyPath": "/home/user/.ssh/id_rsa",
      "PrivateKeyPassphrase": "",
      "TimeoutSeconds": 30,
      "AuthType": "PrivateKey",
      "IsActive": true
    },
    "RemotePathOptions": {
      "RemotePath": "/home/developer/sporesync/data",
      "LocalPath": "/home/user/SporeSync/data"
    },
    "RemoteMonitor": {
      "CheckIntervalSeconds": 15,
      "ErrorRetryDelaySeconds": 30,
      "MaxRetryAttempts": 5,
      "EnableLogging": true
    }
  }
}
```

### Password Authentication
```json
{
  "Settings": {
    "SshConfiguration": {
      "Name": "Legacy Server",
      "Host": "legacy.example.com",
      "Port": 22,
      "Username": "admin",
      "Password": "your-secure-password",
      "PrivateKeyPath": "",
      "PrivateKeyPassphrase": "",
      "TimeoutSeconds": 30,
      "AuthType": "Password",
      "IsActive": true
    }
  }
}
```

## Security Best Practices

1. **Use Private Key Authentication**: Prefer SSH keys over passwords
2. **Secure Key Storage**: Store private keys with appropriate permissions (600)
3. **Key Passphrases**: Use encrypted private keys with strong passphrases
4. **Limited User Permissions**: Create dedicated users with minimal required permissions
5. **Network Security**: Use VPN or firewall rules to restrict SSH access
6. **Regular Key Rotation**: Periodically rotate SSH keys
7. **Audit Logging**: Enable SSH audit logging on the server

## Troubleshooting

### Common Issues

1. **Connection Timeout**: Check network connectivity and firewall rules
2. **Authentication Failed**: Verify username, password, or key path
3. **Permission Denied**: Check file permissions on private key (should be 600)
4. **Host Key Verification**: Add server to known_hosts or configure StrictHostKeyChecking

### Testing Connection

Use the SSH command line to test your configuration:

```bash
# Test with private key
ssh -i /path/to/private/key username@hostname

# Test with password
ssh username@hostname
```

### Logs

Check application logs for detailed error messages when SSH connections fail.

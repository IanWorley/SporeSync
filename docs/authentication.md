# Authentication

SporeSync protects its API, dashboard SPA, and SignalR hub with a minimal
single-admin login. There is one configurable administrator account, cookie
sessions, and no user database or role system.

## What is protected

When authentication is enabled:

- All API controllers require an authenticated session and return `401` when
  it is missing or expired. Exceptions: `/api/auth/login`, `/api/auth/logout`,
  and `/api/auth/session` remain anonymous so the SPA can sign in and discover
  session state.
- The SignalR hub at `/hubs/dashboard` requires an authenticated session; the
  negotiation request is rejected with `401` otherwise.
- Static SPA assets (`index.html`, JS, CSS) stay anonymous so the login page
  can load. The SPA redirects unauthenticated visitors to `/login` based on
  `/api/auth/session`, and the server independently rejects all data access.

Login attempts are rate limited to 10 per minute per source IP address.

## Configuration

Settings live in the `Auth` section (`SporeSync.Web/appsettings.json`), or as
environment variables using the `Auth__` prefix:

```json
{
  "Auth": {
    "Enabled": true,
    "Username": "admin",
    "Password": "",
    "PasswordHash": "",
    "SessionHours": 12
  }
}
```

| Setting | Default | Description |
| --- | --- | --- |
| `Auth:Enabled` | `true` (production), `false` (Development) | Requires login for API, SPA data, and SignalR access. |
| `Auth:Username` | `admin` | The single admin username. |
| `Auth:PasswordHash` | empty | PBKDF2-SHA256 hash of the admin password. Preferred credential source; takes precedence over `Auth:Password`. |
| `Auth:Password` | empty | Plaintext admin password. Intended for local development only. |
| `Auth:SessionHours` | `12` | Sliding lifetime of the session cookie, in hours. |

When `Auth:Enabled` is `true`, the application refuses to start until
`Auth:PasswordHash` or `Auth:Password` is set. This makes deployments secure
by default: an unconfigured production instance fails fast with a clear error
message instead of serving data anonymously.

### Generating a password hash

```bash
dotnet run --project SporeSync.Web/SporeSync.Web.csproj -- hash-password
```

The command prompts for a password and prints a value like
`PBKDF2-SHA256.210000.<salt>.<hash>` for `Auth:PasswordHash` (environment
variable `Auth__PasswordHash`). You can also pass the password as an argument
(`-- hash-password 'my-password'`), but the interactive prompt keeps it out of
shell history.

## Login and session flow

1. The SPA calls `GET /api/auth/session` on navigation. The response reports
   `{ authRequired, authenticated, username }`.
2. If authentication is required and there is no session, the SPA redirects to
   `/login` and posts credentials to `POST /api/auth/login`.
3. On success, the server issues an HTTP-only, same-site session cookie
   (`.SporeSync.Auth`) with a sliding expiration of `Auth:SessionHours`.
   Because SignalR uses the same cookie, the dashboard hub connection works
   without extra configuration.
4. `POST /api/auth/logout` clears the session. The "Sign out" button in the
   header appears whenever authentication is enabled.
5. If a session expires, API calls return `401` and the SPA returns to the
   login page.

The cookie is issued with `HttpOnly`, `SameSite=Lax`, and `Secure` when the
request arrives over HTTPS. Terminate TLS in front of the app in production.

## Local development

`appsettings.Development.json` ships with `Auth:Enabled: false`, so the usual
`dotnet run` / Vite workflow needs no login. To exercise the login flow
locally, enable it with a plaintext dev password:

```json
{
  "Auth": {
    "Enabled": true,
    "Username": "admin",
    "Password": "dev-password"
  }
}
```

or via user secrets / environment variables:

```bash
Auth__Enabled=true Auth__Password=dev-password \
  dotnet run --project SporeSync.Web/SporeSync.Web.csproj
```

The Vite dev server proxies `/api` and `/hubs` to the backend, so cookie login
works the same at `http://localhost:5173`.

## Docker Compose deployment

Add the credential to the `web` service environment (see
[`docker-compose-deployment.md`](docker-compose-deployment.md)):

```yaml
environment:
  Auth__Username: admin
  Auth__PasswordHash: "PBKDF2-SHA256.210000.<salt>.<hash>"
```

Quote the hash so YAML does not misinterpret the dots. To run without
authentication on a trusted network, set `Auth__Enabled: "false"` explicitly.

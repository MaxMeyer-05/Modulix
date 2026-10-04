# Modulix

Modulix is an ASP.NET Core service for user and session management and for
managing modular applications in Docker containers. It accepts published module
ZIP archives, scans their controller endpoints, and manages module metadata,
endpoint confirmation, and container lifecycle.

## Documentation

| Guide | Contents |
| --- | --- |
| [Server, Authentication, and Users](docs/readme.server.md) | Setup, configuration, authentication, token lifecycle, current-user and admin APIs, and error handling. |
| [Module Management](docs/readme.module-management.md) | Archive preparation, upload fields and limits, endpoint confirmation, Docker lifecycle, file replacement, and deletion. |
| [Postman testing conventions](postman/documents/testing-conventions.md) | Request order, environment variables, test data, and manual cleanup. |

## Contribute

### Prerequisites

- .NET SDK 10.0
- A reachable Docker daemon with access for the service account when creating,
	confirming, updating, or deleting module containers. On Linux, the service
	uses `/var/run/docker.sock`; on Windows, it uses the Docker named pipe.

Restore dependencies, build the solution, and run all tests from the repository
root:

```sh
dotnet restore
dotnet build
dotnet test
```

Start the service locally:

```sh
dotnet run --project src/Server
```

The development profile listens on `http://localhost:5284`. OpenAPI and Swagger
UI are available only in the Development environment at
`http://localhost:5284/swagger`.

### Configuration

Use `src/Server/appsettings.Template.json` as the starting point for local
configuration. It contains the SQLite connection string, Serilog settings, and
non-secret JWT options.

The `Jwt` configuration section requires the following values:

| Key | Description |
| --- | --- |
| `Jwt:Issuer` | Expected token issuer. |
| `Jwt:Audience` | Expected token audience. |
| `Jwt:AccessTokenLifetimeMinutes` | Positive access-token lifetime in minutes. |
| `Jwt:RefreshTokenLifetimeMinutes` | Positive refresh-token lifetime in minutes. |
| `Jwt:SecretKey` | UTF-8 signing key with at least 32 bytes. |

Do not add `Jwt:SecretKey` to an `appsettings` file. For local development,
store it with .NET User Secrets:

```sh
dotnet user-secrets set "Jwt:SecretKey" "replace-with-a-random-secret-of-at-least-32-bytes" --project src/Server
```

For deployments, provide it through the `Jwt__SecretKey` environment variable:

```sh
export Jwt__SecretKey="replace-with-a-random-secret-of-at-least-32-bytes"
```

Environment variables override configuration values from JSON files.

### Swagger Authentication

Register a user with `POST /api/auth/register`, then use
`POST /api/auth/login` to obtain an access token and refresh token. In Swagger
UI, select **Authorize** and enter the access token. Swagger sends it as an
`Authorization: Bearer <token>` header for secured endpoints.

Use `POST /api/users/token/refresh` to exchange a valid refresh token for a new
token pair. Use `POST /api/auth/logout` to revoke a refresh token.

### Postman

The source-controlled collection in [postman](postman) includes authentication,
user management, module management, and authorization/error checks. Select
`Modulix API v1` and the `Modulix Local` environment in the Postman VS Code view.
Run the prerequisite registration/login requests before authenticated requests;
admin actions additionally require an existing admin account.

Module uploads use multipart form-data with a `ModuleFile` ZIP. Configure the
local archive paths or select files in Postman. Creating a module saves its ID
for subsequent requests. Review any endpoint discrepancy report before
confirming it, and run destructive cleanup requests manually only.

Both upload endpoints limit the entire request body to 50 MiB (52,428,800
bytes), including text fields and multipart overhead. The ZIP must therefore
be smaller than 50 MiB. MVC form binding returns `400` when this limit is
exceeded; a reverse proxy can return `413` or enforce a lower limit. ZIP
validation separately allows at most 1,000 entries and 512 MiB of uncompressed
data.

See [Postman testing conventions](postman/documents/testing-conventions.md) for
environment variables, module preparation, request order, and upload fields.
Keep credentials, tokens, and local file paths in an ignored
`*.private.environment.yaml` file.

## Module Management API

Module routes use `/api/modules-management/modules`. Read operations require
an authenticated user; mutations require the `Admin` role. Creation and file
replacement use multipart form-data; metadata updates and endpoint confirmation
use JSON. Endpoint discrepancies leave new modules in `PendingConfirmation`
until reviewed. Always inspect the returned status, because a successful HTTP
response does not guarantee that a container is running.

File replacement preserves the registered endpoint contract and waits for the
candidate container to be ready before switching the persisted module version.
Pending endpoint confirmation or changed HTTP methods/routes cause `409`.
Upload only trusted binaries: module archives are executed in Docker containers.
These endpoints manage module metadata and containers; they do not expose a
proxy that forwards traffic to module endpoints.

See [Module Management](docs/readme.module-management.md) for the full API
reference, request examples, confirmation semantics, and lifecycle limitations.

## Project Structure

| Path | Responsibility |
| --- | --- |
| `docs` | Feature guides for the server, authentication, users, and module management. |
| `src/Server` | ASP.NET Core host, dependency injection, middleware, and configuration. |
| `src/Server/Controllers` | HTTP endpoints for authentication, user management, and module management. |
| `src/Server/Database` | EF Core context, entities, and SQLite migrations. |
| `src/Server/Infrastructure` | Cross-cutting infrastructure, including exception handling. |
| `src/Server/Mappers` | Mapping between persistence entities and API DTOs. |
| `src/Server/Models` | API DTOs, session data, and role definitions. |
| `src/Server/Security` | JWT configuration and issuance, password hashing, and claims helpers. |
| `src/Server/Services` | Authentication, user management, module scanning, and Docker lifecycle logic. |
| `tests/Server` | Unit tests for security, services, and models. |
| `postman` | API collection, environment templates, and testing conventions. |

## Key Concepts

- **Authentication:** Users register and log in with an email address and
	password. A successful login returns a JWT access token and a refresh token.
- **Authorization:** Secured endpoints validate issuer, audience, signature,
	lifetime, subject, and role claims. Admin-only endpoints require the `Admin`
	role.
- **Sessions:** Refresh tokens are persisted in SQLite. They can be refreshed
	to obtain a new token pair or revoked during logout.
- **User management:** Authenticated users can view, update, and delete their
	own account. Administrators can list users, change roles and scopes, and
	delete user accounts.
- **Module management:** Authenticated users can inspect modules and their
	registered endpoints. Administrators upload module archives, confirm endpoint
	discrepancies, update metadata or binaries, and delete modules and containers.
- **Operations:** EF Core applies pending migrations during application startup.
	Serilog records application logs, while the global exception handler returns
	RFC 7807 problem-details responses.

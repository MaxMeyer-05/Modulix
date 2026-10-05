# Modulix

Modulix is an ASP.NET Core 10 Web API for centrally managing users, sessions,
and modular applications. Administrators upload published applications as ZIP
archives; the server discovers their controller endpoints and manages their
deployment in Docker containers.

## What It Does

- **Authentication and sessions:** Email/password login, JWT access tokens,
  and refresh-token rotation and revocation.
- **User management:** Self-service account management and admin-controlled
  roles and scopes.
- **Module management:** Archive uploads, endpoint discovery and confirmation,
  metadata updates, binary replacement, and container deletion.
- **Persistence and operations:** SQLite storage through EF Core, automatic
  database migrations at startup, and structured logging with Serilog.

Modulix manages module metadata and containers. It does not currently provide
a reverse proxy that forwards requests to module endpoints.

## Quick Start

### Prerequisites

- .NET SDK 10.0.
- Write access to the SQLite database location and configured log destinations.
- A reachable Docker daemon for module container operations. Authentication
  and user management do not require Docker. See the
  [module prerequisites](docs/readme.module-management.md#prerequisites) for
  Docker access and storage requirements.

### Configure and Run

Run these commands from the repository root. Review
[appsettings.Template.json](src/Server/appsettings.Template.json) for the local
SQLite, logging, and non-secret JWT settings. Configuration keys and deployment
guidance are documented in the
[server guide](docs/readme.server.md#configuration).

Store the JWT signing key in .NET User Secrets, not in a committed
configuration file. Replace the placeholder with a random secret of at least
32 UTF-8 bytes before running the server:

```sh
dotnet restore
dotnet user-secrets set "Jwt:SecretKey" "replace-with-a-random-secret-of-at-least-32-bytes" --project src/Server
dotnet run --project src/Server
```

Pending database migrations are applied automatically; no separate database
setup command is required.

The development profile listens on `http://localhost:5284`. The following
documentation endpoints are available only in Development:

| Endpoint | URL |
| --- | --- |
| Swagger UI | `http://localhost:5284/swagger` |
| Swagger JSON | `http://localhost:5284/swagger/v1/swagger.json` |
| OpenAPI document | `http://localhost:5284/openapi/v1.json` |

Authentication endpoints and token behavior are documented in the
[authentication reference](docs/readme.server.md#authentication-api).
For collection-based testing, follow the
[Postman guide](postman/documents/testing-conventions.md).

## Documentation

| Guide | Contents |
| --- | --- |
| [Server, Authentication, and Users](docs/readme.server.md) | Configuration, deployment considerations, authentication, token lifecycle, user APIs, and error handling. |
| [Module Management](docs/readme.module-management.md) | Archive preparation, upload fields and limits, endpoint confirmation, Docker lifecycle, file replacement, and deletion. |
| [Postman testing conventions](postman/documents/testing-conventions.md) | Request order, environment variables, test data, and manual cleanup. |

## Project Structure

| Path | Responsibility |
| --- | --- |
| `src/Server` | API host, controllers, services, security, and database persistence. |
| `tests/Server` | Unit tests for security, services, and models. |
| `docs` | Detailed server and module documentation. |
| `postman` | API collection, environment templates, and testing conventions. |

## Development

Build the solution and run its tests from the repository root:

```sh
dotnet build
dotnet test
```

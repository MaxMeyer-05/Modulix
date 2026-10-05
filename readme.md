# Modulix

Modulix is an ASP.NET Core 10 Web API for centrally managing modular
applications. Administrators upload published applications as ZIP
archives; the server discovers their controller endpoints and manages their
deployment in Docker containers.

## What It Does

- **Authentication:** Validation of Keycloak access tokens. Keycloak owns
  users, passwords, sessions, token issuance, and role assignments.
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
- A configured Keycloak realm and access tokens intended for this API.
- A reachable Docker daemon for module container operations. See the
  [module prerequisites](docs/readme.module-management.md#prerequisites) for
  Docker access and storage requirements.

### Configure and Run

Run these commands from the repository root. Review
[appsettings.Template.json](src/Server/appsettings.Template.json) for the local
SQLite, logging, and Keycloak settings. Configuration keys and deployment
guidance are documented in the
[server guide](docs/readme.server.md#configuration).

Configure `Keycloak:Issuer`, `Keycloak:Audience`, and
`Keycloak:MetadataAddress` through local settings or environment variables.
The API does not need a local JWT signing key.

```sh
dotnet restore
dotnet run --project src/Server
```

Pending database migrations are applied automatically. `InitialCreate` in
`src/Server/Database/Migrations` provisions the module-only schema for a new
database. Existing databases from before the Keycloak migration require
their previous migration history to be reconciled before applying it.

Admin checks use the top-level `roles` claim, for example `["Admin", "User"]`.
Configure Keycloak to include the API client roles in this claim and the API
audience in access tokens; see the
[authorization reference](docs/readme.server.md#authorization).

The development profile listens on `http://localhost:5284`. The following
documentation endpoints are available only in Development:

| Endpoint | URL |
| --- | --- |
| Swagger UI | `http://localhost:5284/swagger` |
| Swagger JSON | `http://localhost:5284/swagger/v1/swagger.json` |
| OpenAPI document | `http://localhost:5284/openapi/v1.json` |

Authentication responsibilities are documented in the
[authentication reference](docs/readme.server.md#authentication).
For collection-based testing, follow the
[Postman guide](postman/documents/testing-conventions.md).

## Documentation

| Guide | Contents |
| --- | --- |
| [Server and Authentication](docs/readme.server.md) | Configuration, Keycloak authentication, deployment considerations, and error handling. |
| [Module Management](docs/readme.module-management.md) | Archive preparation, upload fields and limits, endpoint confirmation, Docker lifecycle, file replacement, and deletion. |
| [Postman testing conventions](postman/documents/testing-conventions.md) | Request order, environment variables, test data, and manual cleanup. |

## Project Structure

| Path | Responsibility |
| --- | --- |
| `src/Server` | API host, module controllers and services, token validation, and database persistence. |
| `tests/Server` | Unit tests for module services, endpoint discovery, and Docker operations. |
| `docs` | Detailed server and module documentation. |
| `postman` | API collection, environment templates, and testing conventions. |

## Development

Build the solution and run its tests from the repository root:

```sh
dotnet build
dotnet test
```

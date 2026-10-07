# Modulix Service

Modulix is an ASP.NET Core service for managing modular .NET applications in Docker containers. It accepts published applications as ZIP archives, discovers their HTTP endpoints, stores module metadata in SQLite, and manages container creation, startup, updates, and deletion.

The repository also includes a standalone endpoint scanner and a .NET HTTP client library. Modulix manages modules and their endpoint metadata; it does not currently expose a reverse proxy for forwarding requests to module endpoints.

## Contribute

### Requirements

- .NET 10 SDK.
- Docker Engine, or Docker Desktop configured to use Linux containers.
- A reachable Keycloak instance with a configured issuer, audience, and OpenID Connect discovery endpoint.
- Write access to the database, module storage, and log directories.

The service connects to the local Docker daemon through the Windows named pipe or `/var/run/docker.sock` on Linux. The daemon must be able to access the module storage directory because scanner containers mount it as a read-only volume. On Docker Desktop, enable file sharing for that directory if required.

### Configuration

Use [src/Modulix/appsettings.Template.json](src/Modulix/appsettings.Template.json) as the reference for [src/Modulix/appsettings.json](src/Modulix/appsettings.json). Replace the Keycloak placeholders with values from your environment. Do not commit credentials or environment-specific secrets.

For local development, configuration values can be overridden with .NET User Secrets:

```powershell
dotnet user-secrets set "Keycloak:Issuer" "http://localhost:8080/realms/modulix" --project src/Modulix
dotnet user-secrets set "Keycloak:Audience" "modulix-api" --project src/Modulix
dotnet user-secrets set "Keycloak:MetadataAddress" "http://localhost:8080/realms/modulix/.well-known/openid-configuration" --project src/Modulix
```

These are example values; they must match your Keycloak realm and client configuration. Tokens must contain the expected audience. Administrative operations require the exact role `Admin` in the token's `roles` claim, so configure a Keycloak protocol mapper if necessary.

| Setting | Template value | Purpose |
| --- | --- | --- |
| `ConnectionStrings:ServerDatabase` | `Data Source=modulix.db` | SQLite connection string. |
| `Modulix:StorageBasePath` | `storage/modules` | Module files; relative paths are resolved against the application's content root. |
| `Modulix:MaximumArchiveEntryCount` | `1000` | Maximum number of entries in an uploaded archive. |
| `Modulix:MaximumArchiveUncompressedBytes` | `536870912` | Maximum total uncompressed archive size, equivalent to 512 MiB. |
| `Modulix:NetworkName` | `modulix-network` | Docker bridge network for module containers. |
| `Modulix:BaseImage` | `mcr.microsoft.com/dotnet/aspnet:10.0` | Runtime image used to build module containers. |
| `Modulix:ScannerImage` | `modulix-scanner:latest` | Locally built image used for endpoint discovery. |
| `Modulix:MaxConcurrentScans` | `4` | Maximum number of concurrent scans. |
| `Keycloak:Issuer` | Placeholder | Expected JWT issuer. |
| `Keycloak:Audience` | Placeholder | Expected JWT audience. |
| `Keycloak:MetadataAddress` | Placeholder | OpenID Connect discovery URL. |

The `Modulix` options are validated at startup. Environment variables can also override configuration, using double underscores for nested keys, for example `Modulix__StorageBasePath` or `Keycloak__Audience`.

Serilog is configured in the `Serilog` section. The template writes warning-level and higher messages to the console and daily log files under `logs/`, retaining 30 files.

### Build and Run

Run the following commands from the repository root:

```powershell
dotnet restore Modulix.slnx
dotnet build Modulix.slnx
docker build -t modulix-scanner:latest -f src/Modulix.Scanner/Dockerfile src/Modulix.Scanner
dotnet run --project src/Modulix --launch-profile http
```

The scanner image tag must match `Modulix:ScannerImage`. The module runtime image must also be available locally or downloadable by Docker. The service creates the configured module network when provisioning a module container.

The `http` launch profile starts the API at `http://localhost:5284` in the Development environment. Swagger UI is available at `http://localhost:5284/swagger`, and the OpenAPI document is available at `http://localhost:5284/openapi/v1.json`. These documentation endpoints are enabled only in Development.

Entity Framework Core applies database migrations automatically at startup. No separate migration command is needed for normal local startup.

### Tests

```powershell
dotnet test Modulix.slnx
```

The test projects cover server-side services and extensions, as well as endpoint discovery in the standalone scanner. Request examples and local/development environments are available in [postman](postman).

## Project Structure

| Location | Responsibility |
| --- | --- |
| [src/Modulix](src/Modulix) | ASP.NET Core management API, authentication, configuration, and service registration. |
| [src/Modulix/Controllers](src/Modulix/Controllers) | HTTP endpoints for module management. |
| [src/Modulix/Services](src/Modulix/Services) | Module lifecycle, Docker operations, and scan scheduling. |
| [src/Modulix/Extensions](src/Modulix/Extensions) | Archive handling, endpoint reconciliation, and port selection. |
| [src/Modulix/Database](src/Modulix/Database) | EF Core context, entities, and SQLite migrations. |
| [src/Modulix/Models](src/Modulix/Models) | DTOs, status enums, and validated configuration options. |
| [src/Modulix/Mappers](src/Modulix/Mappers) | Mapping between database entities and API models. |
| [src/Modulix/Infrastructure](src/Modulix/Infrastructure) | Central exception handling and Problem Details responses. |
| [src/Modulix.Scanner](src/Modulix.Scanner) | Standalone assembly scanner, JSON scan output, and scanner Dockerfile. |
| [src/Modulix.Client](src/Modulix.Client) | HTTP client implementation and dependency injection registration. |
| [src/Modulix.Client.Abstraction](src/Modulix.Client.Abstraction) | Client interfaces, DTOs, and enums for consumers. |
| [tests/Modulix.Tests](tests/Modulix.Tests) | Server-side tests. |
| [tests/Modulix.Scanner.Tests](tests/Modulix.Scanner.Tests) | Standalone scanner tests and test data. |
| [postman](postman) | API request collections and environment definitions. |

## Key Concepts

### Module Artifacts

A module is a published .NET application uploaded as a ZIP archive. Package the publish output, including the entry assembly, runtime configuration, and dependencies, rather than the source project. The application must be compatible with the configured runtime image and Linux containers.

Each module has an ID, a unique base endpoint path, a storage location, a container port, endpoint metadata, and a lifecycle status. Upload requests are limited to 50 MiB; archive entry count and uncompressed size have separate configurable limits.

### Endpoint Discovery and Confirmation

1. An administrator uploads a module, optionally supplying an initial endpoint list.
2. Modulix extracts the archive and scans its assemblies in a temporary Docker container.
3. The scanner returns the entry assembly filename and discovered HTTP endpoints as JSON.
4. Modulix compares the discovered endpoints with the supplied list. Differences produce a discrepancy report and the `PendingConfirmation` status.
5. If no manual endpoint list is supplied, discovered endpoints are accepted automatically. Modules ready for provisioning proceed through `Starting` to `Running`, or become `Failed` if provisioning fails.
6. Administrators resolve pending endpoint differences through the confirmation API before the module is started.

Scanner containers have networking disabled, mount module files read-only, and drop Linux capabilities. These restrictions reduce exposure but are not a guarantee that arbitrary uploaded code is safe. Only accept artifacts from trusted sources.

### Scan Scheduling

`MaxConcurrentScans` limits parallel scanner containers. Additional requests enter an in-memory priority queue and receive the `QueuedForScan` status. Completed background scans update the stored module information and reconcile its endpoints. The queue is not persisted across service restarts.

Clients can poll the module details to observe progress. Cancelling a queued scan through the API also deletes the associated module.

### Container Lifecycle

Modulix generates a Dockerfile for each module using the configured base image and starts the discovered entry assembly with `dotnet`. Module containers join the configured Docker network and use an `UnlessStopped` restart policy. Docker assigns an ephemeral host port bound to `127.0.0.1`; this is distinct from the configured container port.

The API supports metadata updates, replacement ZIP uploads, and module deletion. Replacement artifacts are scanned again, and file updates are rejected while endpoints await confirmation. Deletion removes the module's container, image, database records, and attempts to remove its stored files.

### Management API

All routes use the prefix `/api/modules-management/modules` and require a bearer token. Write operations additionally require the `Admin` role.

| Method | Relative route | Purpose | Access |
| --- | --- | --- | --- |
| `GET` | `/` | List modules. | Authenticated |
| `GET` | `/{moduleId}` | Retrieve module details and status. | Authenticated |
| `GET` | `/{moduleId}/endpoints` | Retrieve module endpoints. | Authenticated |
| `POST` | `/create` | Upload a module using `multipart/form-data`. | Admin |
| `POST` | `/{moduleId}/confirm-endpoints` | Resolve pending endpoint discrepancies. | Admin |
| `PUT` | `/{moduleId}` | Update module metadata. | Admin |
| `PUT` | `/{moduleId}/files` | Upload replacement files using `multipart/form-data`. | Admin |
| `DELETE` | `/{moduleId}/delete` | Delete a module and its resources. | Admin |
| `DELETE` | `/{moduleId}/cancel-scan` | Cancel a queued scan and delete its module. | Admin |

Creation returns `202 Accepted` when a scan is queued or endpoint confirmation is pending; otherwise, it returns `201 Created`. Global exception handling translates service errors into HTTP Problem Details responses. Refer to Swagger for request models and response schemas.
# Modulix

Modulix is a .NET 10 platform for hosting, inspecting, and managing modular .NET applications inside isolated Docker containers. It accepts published .NET applications as ZIP archives, inspects their assemblies in an isolated scanner container to discover HTTP endpoints, persists module and endpoint metadata in SQLite, provisions and manages module containers, and dynamically routes incoming HTTP traffic to running modules through an in-memory YARP reverse proxy.

In addition to the core management service and API gateway, the repository provides an ASP.NET Core Razor Pages web frontend (`Modulix.Web`), a standalone CLI endpoint scanner (`Modulix.Scanner`), and reusable class libraries for Docker orchestration, YARP reverse proxy management, and typed HTTP client integration.

## Contribute

### Requirements

- .NET 10 SDK.
- Docker Engine, or Docker Desktop configured to use Linux containers.
- A reachable OpenID Connect / OAuth 2.0 identity provider (such as Keycloak) for JWT bearer authentication (`Modulix`) and interactive + client-credentials authentication (`Modulix.Web`).
- Write access to the SQLite database, module storage, and log directories.

The server connects to the local Docker daemon via `/var/run/docker.sock` on Linux or the Windows named pipe. The Docker daemon must have access to the configured module storage directory because scanner containers mount extracted module directories as read-only volumes.

### Configuration

Both runnable web applications provide template configuration files that can be copied to `appsettings.json` or overridden via .NET User Secrets and environment variables (using `__` for nested keys, e.g., `Authorization__Authority`). Do not commit credentials or environment-specific secrets.

#### Core Server (`src/Modulix`)

Use [./src/Modulix/appsettings.Template.json](./src/Modulix/appsettings.Template.json) as the reference for `./src/Modulix/appsettings.json`:

| Setting | Template value | Purpose |
| --- | --- | --- |
| `ConnectionStrings:ServerDatabase` | `Data Source=modulix.db` | SQLite database connection string. |
| `Modulix:StorageBasePath` | `storage/modules` | Directory for extracted module artifacts (relative paths resolve against the content root). |
| `Modulix:MaximumArchiveEntryCount` | `1000` | Maximum number of entries allowed in an uploaded ZIP archive. |
| `Modulix:MaximumArchiveUncompressedBytes` | `536870912` | Maximum total uncompressed size of an archive (512 MiB). |
| `Docker:NetworkName` | `modulix-network` | Docker bridge network attached to module containers. |
| `Docker:BaseImage` | `mcr.microsoft.com/dotnet/aspnet:10.0` | Base runtime image used when building module container images. |
| `Docker:ScannerImage` | `modulix-scanner:latest` | Local Docker image executed for assembly endpoint discovery. |
| `Docker:MaxConcurrentScans` | `4` | Maximum number of scanner containers running in parallel. |
| `Authorization:Authority` | `<authority-url>` | OpenID Connect authority / issuer URL (e.g., `http://localhost:8080/realms/modulix`). |
| `Authorization:Audience` | `modulix_api` | Expected JWT audience claim (optional; validated when set). |

Example using .NET User Secrets:

```powershell
dotnet user-secrets set "Authorization:Authority" "http://localhost:8080/realms/modulix" --project src/Modulix
dotnet user-secrets set "Authorization:Audience" "modulix_api" --project src/Modulix
```

#### Web Frontend (`src/Modulix.Web`)

Use [./src/Modulix.Web/appsettings.Template.json](./src/Modulix.Web/appsettings.Template.json) as the reference for `./src/Modulix.Web/appsettings.json`:

| Setting | Template value | Purpose |
| --- | --- | --- |
| `ModulixApi:BaseUrl` | `PLACEHOLDER_FOR_MODULIX_API_BASE_URL` | Base URL of the `Modulix` backend API (e.g., `http://localhost:5284`). |
| `Authorization:Authority` | `<authority-url>` | OpenID Connect authority URL used for user sign-in and token endpoints. |
| `Authorization:Service:ClientId` | `modulix_Service` | OAuth 2.0 client ID for backend-to-backend client credentials requests. |
| `Authorization:Service:ClientSecret` | `<service-account-client-secret>` | Client secret for the service account client. |
| `Authorization:Client:ClientId` | `modulix_Client` | OpenID Connect client ID for interactive user login (Authorization Code + PKCE). |
| `Authorization:Client:ClientSecret` | `<client-secret>` | Client secret for the interactive web client. |

Both `Modulix` and `Modulix.Web` configure Serilog in the `Serilog` section to log warning-level and higher events (and `Information` for application namespaces) to the console and rolling daily log files under `logs/` (retaining 30 files).

### Build, Run, and Test

Run the following commands from the repository root:

```powershell
# 1. Restore and build the solution
dotnet restore Modulix.slnx
dotnet build Modulix.slnx

# 2. Build the isolated endpoint scanner Docker image (must match Docker:ScannerImage)
docker build -t modulix-scanner:latest -f src/Modulix.Scanner/Dockerfile src/Modulix.Scanner

# 3. Start the Modulix server API and reverse proxy (http://localhost:5284)
dotnet run --project src/Modulix --launch-profile http

# 4. Start the Modulix web frontend (http://localhost:5038)
dotnet run --project src/Modulix.Web --launch-profile http
```

In the `Development` environment, the backend exposes Swagger UI at `http://localhost:5284/swagger` and the OpenAPI document at `http://localhost:5284/openapi/v1.json`. Entity Framework Core applies pending SQLite migrations automatically on startup.

To run the unit and integration test suites:

```powershell
dotnet test Modulix.slnx
```

## Project Structure

| Location | Responsibility |
| --- | --- |
| [./src/Modulix](./src/Modulix) | Core ASP.NET Core Web API, module lifecycle orchestration, authentication, and reverse proxy host. |
| [./src/Modulix/Controllers](./src/Modulix/Controllers) | REST API endpoints (`ModulesManagementController`) for uploading, inspecting, confirming, updating, and deleting modules. |
| [./src/Modulix/Services](./src/Modulix/Services) | Business logic for module lifecycle management (`ModuleService`) and bounded-concurrency scan scheduling (`ModuleEndpointScanner`). |
| [./src/Modulix/Extensions](./src/Modulix/Extensions) | ZIP archive extraction and validation, endpoint discrepancy reconciliation, and host port allocation. |
| [./src/Modulix/Database](./src/Modulix/Database) | Entity Framework Core `ServerContext`, database entities, and SQLite migrations. |
| [./src/Modulix/Models](./src/Modulix/Models) | Validated configuration option models (`ModulixOptions`). |
| [./src/Modulix/Mappers](./src/Modulix/Mappers) | Mapping extensions between database entities, DTOs, and reverse proxy route definitions. |
| [./src/Modulix/Infrastructure](./src/Modulix/Infrastructure) | Centralized exception handling (`GlobalExceptionHandler`) producing RFC 7807 Problem Details responses. |
| [./src/Modulix.Web](./src/Modulix.Web) | ASP.NET Core Razor Pages management UI with OIDC authentication, dashboard, module management, endpoint browser, scan queue monitor, and log viewer. |
| [./src/Modulix.Scanner](./src/Modulix.Scanner) | Standalone CLI tool and `Dockerfile` that inspects published .NET assemblies via `MetadataLoadContext` to discover ASP.NET Core controller endpoints. |
| [./src/libraries/Modulix.Docker](./src/libraries/Modulix.Docker) | Reusable library (`Modulix.Docker`) for Docker image generation, container lifecycle management, and isolated scanner execution via `Docker.DotNet`. |
| [./src/libraries/Modulix.ReverseProxy](./src/libraries/Modulix.ReverseProxy) | Reusable library (`Modulix.ReverseProxy`) providing dynamic in-memory YARP route and cluster configuration (`IReverseProxyService`). |
| [./src/libraries/Modulix.Client](./src/libraries/Modulix.Client) | Reusable HTTP client library (`Modulix.Client`) implementing `IModulixApiClient` and DI registration (`AddModulixApi`). |
| [./src/libraries/Modulix.Client.Abstraction](./src/libraries/Modulix.Client.Abstraction) | Shared contracts library (`Modulix.Client.Abstraction`) containing `IModulixApiClient`, DTOs, and module/endpoint status enums. |
| [./tests/Modulix.Tests](./tests/Modulix.Tests) | Unit tests for server services, archive validation, endpoint reconciliation, and reverse proxy mapping. |
| [./tests/Modulix.Scanner.Tests](./tests/Modulix.Scanner.Tests) | Unit tests and sample assembly fixtures for the standalone endpoint scanner. |
| [./postman](./postman) | Postman collections and environment definitions for manual API testing. |

## Key Concepts

### Module Artifacts and Upload Validation

A module is a published .NET web application packaged as a ZIP archive (containing the entry `.dll`, `.runtimeconfig.json`, and dependencies). Each module is assigned a unique `Guid`, a unique `BaseUrlPath` prefix, a local storage directory, an allocated host port, discovered sub-endpoints, and a `ModuleStatus`.

To protect the host during extraction, uploads are capped at 50 MiB and validated against path traversal (ZipSlip), maximum entry counts (`MaximumArchiveEntryCount`), and maximum uncompressed size (`MaximumArchiveUncompressedBytes`).

### Isolated Endpoint Discovery and Confirmation

1. **Upload:** A user uploads a module ZIP archive via the API or `Modulix.Web`, optionally providing an expected list of sub-endpoints and a scan priority.
2. **Isolated Scan:** `Modulix` runs `Modulix.Scanner` inside a short-lived, locked-down Docker container (`NetworkDisabled`, read-only bind mount, dropped Linux capabilities, non-root user) to inspect the assemblies without executing untrusted module code on the host.
3. **Reconciliation:** The scanner outputs the entry assembly name and discovered HTTP routes (`HttpMethod` and `Path`) as JSON. `Modulix` compares the discovered endpoints against any user-supplied endpoints:
   - If no manual endpoints were supplied, or if they match the scan result, all discovered endpoints are marked `Active` and the module proceeds directly to container provisioning (`Starting` -> `Running`).
   - If differences exist (`MissingFromScan` or `UnexpectedInScan`), the module enters `PendingConfirmation` and returns a discrepancy report.
4. **Confirmation:** Administrators review and confirm or reject individual endpoints via `POST /api/modules-management/modules/{moduleId}/confirm-endpoints` (or the Web UI) before the container is built and started.

### Priority Scan Queue

Concurrent scanner containers are bounded by `Docker:MaxConcurrentScans`. When all scan slots are busy, new uploads receive the `QueuedForScan` status and are placed in an in-memory priority queue. As running scans finish, queued modules are dequeued automatically in priority order, scanned, and provisioned. Queued scans can be inspected via `GET /api/modules-management/modules/queue` and cancelled via `DELETE /api/modules-management/modules/{moduleId}/cancel-scan`.

### Container Lifecycle Management

When a module is ready to start, `Modulix.Docker` generates a `Dockerfile` based on `Docker:BaseImage`, builds a dedicated image for the module, attaches the container to `Docker:NetworkName` with an `UnlessStopped` restart policy, and binds the container's HTTP port to an ephemeral host port on `127.0.0.1`.

Modules support in-place metadata updates (`PUT /{moduleId}`), artifact replacement with re-scanning (`PUT /{moduleId}/files`), and full teardown (`DELETE /{moduleId}/delete`), which stops and removes the container, image, database records, reverse proxy routes, and stored files.

### Dynamic Reverse Proxy Routing

`Modulix.ReverseProxy` integrates YARP with an in-memory `IProxyConfigProvider`:

- Only modules with status `Running` and sub-endpoints with status `Active` are exposed through the proxy.
- Incoming requests to `/{BaseUrlPath}/{EndpointPath}` matching the configured HTTP method have their `/{BaseUrlPath}` prefix stripped and are forwarded to `http://127.0.0.1:{ContainerPort}/{EndpointPath}`.
- Reserved system prefixes (`api/modules-management`, `api`, `swagger`, `openapi`) are blocked from being registered by modules.
- Active routes are restored from SQLite on server startup and hot-reloaded without downtime whenever modules are created, confirmed, updated, or deleted.

### Web Management Portal (`Modulix.Web`)

`Modulix.Web` provides an interactive administrative UI built with ASP.NET Core Razor Pages. Users authenticate via OpenID Connect (Authorization Code flow with PKCE), while backend requests to `Modulix` use `Modulix.Client` with automated OAuth 2.0 Client Credentials token management (`Duende.AccessTokenManagement`). The portal offers:

- **Dashboard:** High-level system metrics and module status overview.
- **Modules:** Uploading new ZIP artifacts, inspecting module details, resolving endpoint discrepancies, updating metadata/files, and deleting modules.
- **Endpoints:** Searching and inspecting all active and inactive sub-endpoints across modules.
- **Scan Queue:** Monitoring queued scans and priorities or cancelling pending scans.
- **Logs:** Browsing and filtering structured Serilog log files.

### Management API

All management routes are served under `/api/modules-management/modules` and require a valid JWT bearer token:

| Method | Relative route | Purpose |
| --- | --- | --- |
| `GET` | `/` | List all registered modules. |
| `GET` | `/endpoints` | Retrieve detailed sub-endpoints across all modules. |
| `GET` | `/queue` | Retrieve the current scan queue and priorities. |
| `GET` | `/{moduleId}` | Retrieve module details and lifecycle status. |
| `GET` | `/{moduleId}/endpoints` | Retrieve sub-endpoints for a specific module. |
| `POST` | `/create` | Upload a new module archive (`multipart/form-data`). |
| `POST` | `/{moduleId}/confirm-endpoints` | Confirm or reject pending endpoint discrepancies. |
| `PUT` | `/{moduleId}` | Update module metadata. |
| `PUT` | `/{moduleId}/files` | Upload replacement module files (`multipart/form-data`). |
| `DELETE` | `/{moduleId}/delete` | Delete a module, its container/image, and stored files. |
| `DELETE` | `/{moduleId}/cancel-scan` | Cancel a queued scan and delete the associated module. |
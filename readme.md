# Modulix

Modulix is a .NET 10 platform for hosting, inspecting, and managing modular .NET applications inside isolated Docker containers. It accepts published .NET applications as ZIP archives, inspects their assemblies in an isolated scanner container to discover HTTP endpoints, persists module and endpoint metadata in SQLite, provisions and manages module containers, and dynamically routes incoming HTTP traffic to running modules through an in-memory YARP reverse proxy.

In addition to the core management service and API gateway, the repository provides an ASP.NET Core Razor Pages web frontend (`Modulix.Web`), a standalone CLI endpoint scanner (`Modulix.Scanner`), and reusable class libraries for Docker orchestration, YARP reverse proxy management, and typed HTTP client integration.

## Contribute

### Requirements

- .NET 10 SDK.
- Docker Engine, or Docker Desktop configured to use Linux containers.
- A reachable OpenID Connect / OAuth 2.0 identity provider (such as Keycloak) for JWT bearer authentication (`Modulix`) and interactive + client-credentials authentication (`Modulix.Web`).
- Write access to the SQLite database, module storage, and log directories.

The server connects to the local Docker/Podman daemon via `/var/run/docker.sock` on Linux or the Windows named pipe (`npipe://./pipe/docker_engine`) by default, or via a custom endpoint configured through `Docker:SocketUri` (for example `unix:///run/user/1000/podman/podman.sock` for rootless Podman). The container daemon must have access to the configured module storage directory because scanner containers mount extracted module directories as read-only volumes.

### Configuration

Both runnable web applications provide template configuration files that can be copied to `appsettings.json` or overridden via .NET User Secrets and environment variables (using `__` for nested keys, e.g., `Authorization__Authority`). Do not commit credentials or environment-specific secrets.

#### Core Server (`src/Modulix`)

Use [./src/Modulix/appsettings.Template.json](./src/Modulix/appsettings.Template.json) as the reference for `./src/Modulix/appsettings.json`:

| Setting | Template value | Purpose |
| --- | --- | --- |
| `ConnectionStrings:ServerDatabase` | `Data Source=modulix.db` | SQLite database connection string. |
| `Module:StorageBasePath` | `storage/modules` | Directory for extracted module artifacts (relative paths resolve against the content root). |
| `Module:MaximumArchiveEntryCount` | `1000` | Maximum number of entries allowed in an uploaded ZIP archive. |
| `Module:MaximumArchiveUncompressedBytes` | `536870912` | Maximum total uncompressed size of an archive (512 MiB). |
| `Module:MaxRetainedVersionsPerModule` | `5` | Maximum number of stored versions retained per module (`0` for unlimited); older inactive versions are automatically cleaned up when a new version is activated. |
| `Docker:SocketUri` | `null` | Optional Docker/Podman daemon socket URI (e.g., `unix:///run/user/1000/podman/podman.sock` for rootless Podman). Defaults to `npipe://./pipe/docker_engine` on Windows or `unix:///var/run/docker.sock` on Linux/macOS when unset. |
| `Docker:NetworkName` | `modulix-network` | Docker bridge network attached to module containers. |
| `Docker:BaseImage` | `mcr.microsoft.com/dotnet/aspnet:10.0` | Base runtime image used when building module container images. |
| `Docker:ScannerImage` | `ghcr.io/maxmeyer-05/modulix-scanner:latest` | Docker image executed for assembly endpoint discovery (pulled automatically if not present locally). |
| `Docker:MaxConcurrentScans` | `4` | Maximum number of scanner containers running in parallel. |
| `Authorization:Authority` | `<authority-url>` | OpenID Connect authority / issuer URL (e.g., `http://localhost:8080/realms/modulix`). |
| `Authorization:Audience` | `modulix_api` | Expected JWT audience claim (optional; validated when set). |
| `Https:Enabled` | `false` | Enables HTTPS redirection and (outside `Development`) HSTS. |
| `Https:Port` | `7142` | Optional HTTPS port used for redirecting HTTP requests. |
| `Https:CertificatePath` | `<optional-path-to-certificate.pfx>` | Optional path to a `.pfx` certificate file for Kestrel. |
| `Https:CertificatePassword` | `<optional-certificate-password>` | Optional password for the Kestrel `.pfx` certificate. |

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
| `Https:Enabled` | `false` | Enables HTTPS redirection and (outside `Development`) HSTS. |
| `Https:Port` | `7062` | Optional HTTPS port used for redirecting HTTP requests. |
| `Https:CertificatePath` | `<optional-path-to-certificate.pfx>` | Optional path to a `.pfx` certificate file for Kestrel. |
| `Https:CertificatePassword` | `<optional-certificate-password>` | Optional password for the Kestrel `.pfx` certificate. |

Both `Modulix` and `Modulix.Web` configure Serilog in the `Serilog` section to log warning-level and higher events (and `Information` for application namespaces) to the console and rolling daily log files under `logs/` (retaining 30 files).

### Build, Run, and Test

Run the following commands from the repository root:

```powershell
# 1. Restore and build the solution
dotnet restore Modulix.slnx
dotnet build Modulix.slnx

# 2. (Optional) Pull or build the isolated endpoint scanner Docker image
docker pull ghcr.io/maxmeyer-05/modulix-scanner:latest

# 3. Start the Modulix server API and reverse proxy (http://localhost:5284 or https://localhost:7142)
dotnet run --project src/Modulix --launch-profile http
# or with HTTPS:
dotnet run --project src/Modulix --launch-profile https

# 4. Start the Modulix web frontend (http://localhost:5038 or https://localhost:7062)
dotnet run --project src/Modulix.Web --launch-profile http
# or with HTTPS:
dotnet run --project src/Modulix.Web --launch-profile https
```

In the `Development` environment, the backend exposes Swagger UI at `/swagger` and the OpenAPI document at `/openapi/v1.json` (on `http://localhost:5284` or `https://localhost:7142` depending on the chosen profile). Entity Framework Core applies pending SQLite migrations automatically on startup.

To run the unit and integration test suites:

```powershell
dotnet test Modulix.slnx
```

## Project Structure

| Location | Responsibility |
| --- | --- |
| [./src/Modulix](./src/Modulix) | Core ASP.NET Core Web API, module lifecycle orchestration, authentication, and reverse proxy host. |
| [./src/Modulix/Controllers](./src/Modulix/Controllers) | REST API endpoints (`ModulesManagementController`, `ModuleVersionsManagementController`, `SubEndpointsManagementController`, `ScannerManagementController`) for managing modules, versions, sub-endpoints, and the scan queue. |
| [./src/Modulix/Services](./src/Modulix/Services) | Business logic for module lifecycle (`ModuleService`), versioning and rollbacks (`ModuleVersionService`), sub-endpoints (`SubEndpointService`), and scan queue processing (`ScannerService`). |
| [./src/Modulix/Scanner](./src/Modulix/Scanner) | Bounded-concurrency scan scheduling and isolated scanner container execution (`ModuleEndpointScanner`). |
| [./src/Modulix/Extensions](./src/Modulix/Extensions) | ZIP archive extraction and validation, endpoint discrepancy reconciliation, version retention cleanup, and host port allocation. |
| [./src/Modulix/Database](./src/Modulix/Database) | Entity Framework Core `ServerContext`, database entities (`Module`, `ModuleVersion`, `ModuleEndpoint`), and SQLite migrations. |
| [./src/Modulix/Models](./src/Modulix/Models) | Validated configuration option models (`ModuleSettings`). |
| [./src/Modulix/Mappers](./src/Modulix/Mappers) | Mapping extensions between database entities, DTOs, and reverse proxy route definitions. |
| [./src/Modulix/Infrastructure](./src/Modulix/Infrastructure) | Centralized exception handling (`GlobalExceptionHandler`) producing RFC 7807 Problem Details responses. |
| [./src/Modulix.Web](./src/Modulix.Web) | ASP.NET Core Razor Pages management UI with OIDC authentication, dashboard, module management, endpoint browser, scan queue monitor, and log viewer. |
| [./src/Modulix.Scanner](./src/Modulix.Scanner) | Standalone CLI tool and `Dockerfile` that inspects published .NET assemblies via `MetadataLoadContext` to discover ASP.NET Core controller endpoints. |
| [./src/libraries/Modulix.Docker](./src/libraries/Modulix.Docker) | Reusable library (`Modulix.Docker`) for Docker image generation, container lifecycle management, and isolated scanner execution via `Docker.DotNet`. |
| [./src/libraries/Modulix.ReverseProxy](./src/libraries/Modulix.ReverseProxy) | Reusable library (`Modulix.ReverseProxy`) providing dynamic in-memory YARP route and cluster configuration (`IReverseProxyService`). |
| [./src/libraries/Modulix.Client](./src/libraries/Modulix.Client) | Reusable HTTP client library (`Modulix.Client`) implementing `IModulixApi` and DI registration (`AddModulixApi`). |
| [./src/libraries/Modulix.Client.Abstraction](./src/libraries/Modulix.Client.Abstraction) | Shared contracts library (`Modulix.Client.Abstraction`) containing `IModulixApi`, sub-client interfaces, DTOs, and module/endpoint status enums. |
| [./tests/Modulix.Tests](./tests/Modulix.Tests) | Unit tests for server services, archive validation, endpoint reconciliation, and reverse proxy mapping. |
| [./tests/Modulix.Scanner.Tests](./tests/Modulix.Scanner.Tests) | Unit tests and sample assembly fixtures for the standalone endpoint scanner. |

## Key Concepts

### Module Artifacts, Versioning, and Upload Validation

A module is a published .NET web application packaged as a ZIP archive (containing the entry `.dll`, `.runtimeconfig.json`, and dependencies). Each module is assigned a unique `Guid`, a unique `BaseUrlPath` prefix, an allocated host port, a `CurrentVersion`, discovered sub-endpoints (linked to the `ModuleVersion` in which they were discovered), and a `ModuleStatus`. Version artifacts are stored per version under `<StorageBasePath>/<moduleId>/versions/<version>` so previous versions remain available for instant rollbacks without rescanning (automatically pruning the oldest inactive versions beyond `Module:MaxRetainedVersionsPerModule`), while a version-independent directory `<StorageBasePath>/<moduleId>/data` persists runtime data (such as local SQLite databases or generated files) across version updates and rollbacks.

To protect the host during extraction, uploads are capped at 50 MiB and validated against path traversal (ZipSlip), maximum entry counts (`MaximumArchiveEntryCount`), and maximum uncompressed size (`MaximumArchiveUncompressedBytes`).

### Isolated Endpoint Discovery and Confirmation

1. **Upload:** A user uploads a module ZIP archive (with a `Version` and optional `Changelog`) via the API or `Modulix.Web`, optionally providing an expected list of initial sub-endpoints.
2. **Isolated Scan:** `Modulix` runs `Modulix.Scanner` inside a short-lived, locked-down Docker container (`NetworkDisabled`, read-only bind mount, dropped Linux capabilities, non-root user) to inspect the assemblies without executing untrusted module code on the host.
3. **Reconciliation:** The scanner outputs the entry assembly name and discovered HTTP routes (`HttpMethod` and `EndpointPath`) as JSON. `Modulix` compares the discovered endpoints against any user-supplied or existing endpoints:
   - If no manual endpoints were supplied, or if they match the scan result, all discovered endpoints are marked `Active` and the module proceeds directly to container provisioning (`Starting` -> `Running`).
   - If differences exist (`MissingEndpoints` or `ExtraEndpoints`), the module enters `PendingConfirmation` and returns a discrepancy report.
4. **Confirmation:** Administrators review and confirm or reject individual endpoints via `POST /api/v1/modules/{moduleId}/endpoints/confirm` (or the Web UI) before the container is built and started.

### Priority Scan Queue

Concurrent scanner containers are bounded by `Docker:MaxConcurrentScans`. When all scan slots are busy, new uploads receive the `QueuedForScan` status and are placed in an in-memory priority queue. As running scans finish, queued modules are dequeued automatically in priority order, scanned, and provisioned. Queued scans can be inspected via `GET /api/v1/scanner/queue` and cancelled via `DELETE /api/v1/scanner/queue/{moduleId}`.

### Container Lifecycle and Version Rollback

When a module is ready to start, `Modulix.Docker` generates a `Dockerfile` based on `Docker:BaseImage` (preparing `/app/data` with write permissions for `$APP_UID`), builds a dedicated image for the active `ModuleVersion`, attaches the container to `Docker:NetworkName` with an `UnlessStopped` restart policy, mounts `<StorageBasePath>/<moduleId>/data` into `/app/data`, and binds the container's HTTP port to an ephemeral host port on `127.0.0.1`.

Modules support:
- **Start / Stop & Live Metrics:** Starting and stopping module containers (`POST /api/v1/modules/{moduleId}/start` and `/stop`), as well as inspecting live container CPU %, memory usage/limit, uptime, and exit code (`GET /api/v1/modules/{moduleId}/stats`).
- **Metadata Updates:** Updating module name and description (`PUT /api/v1/modules/{moduleId}`).
- **Version Releases, Rollbacks & Retention:** Deploying new module versions (`POST /api/v1/modules/{moduleId}/versions`), rolling back to an earlier version (`POST /api/v1/modules/{moduleId}/versions/{version}/rollback`), updating or deleting inactive versions, and automatically pruning the oldest inactive versions exceeding `Module:MaxRetainedVersionsPerModule`.
- **Individual Sub-Endpoint Management:** Activating or deactivating individual sub-endpoints on a running module (`PATCH /api/v1/modules/{moduleId}/endpoints/{subEndpointId}/status`) and deleting unneeded sub-endpoints (`DELETE /api/v1/modules/{moduleId}/endpoints/{subEndpointId}`) with immediate YARP routing updates.
- **Full Teardown:** Deleting a module (`DELETE /api/v1/modules/{moduleId}`), which stops and removes the container, image, database records, reverse proxy routes, persistent data directory, and all stored version directories.

### Dynamic Reverse Proxy Routing

`Modulix.ReverseProxy` integrates YARP with an in-memory `IProxyConfigProvider`:

- Only modules with status `Running` and sub-endpoints with status `Active` are exposed through the proxy. Sub-endpoints automatically transition to `Inactive` when a module stops or fails, and are restored to `Active` for the current version when the module is running.
- Incoming requests to `/{BaseUrlPath}/{EndpointPath}` matching the configured HTTP method have their `/{BaseUrlPath}` prefix stripped and are forwarded to `http://127.0.0.1:{ContainerPort}/{EndpointPath}`.
- Reserved system prefixes (`api/v1/modules`, `api/v1/module-versions`, `api/v1/endpoints`, `api/v1/scanner`, `api/v1/dockerfiles`, `swagger`, `openapi`) are blocked from being registered by modules.
- Active routes are restored from SQLite on server startup and hot-reloaded without downtime whenever modules are created, confirmed, started, stopped, rolled back, updated, or deleted.

### Web Management Portal (`Modulix.Web`)

`Modulix.Web` provides an interactive administrative UI built with ASP.NET Core Razor Pages. Users authenticate via OpenID Connect (Authorization Code flow with PKCE), while backend requests to `Modulix` use `Modulix.Client` with automated OAuth 2.0 Client Credentials token management (`Duende.AccessTokenManagement`). The portal offers:

- **Dashboard:** High-level system metrics and module status overview.
- **Modules:** Uploading new ZIP artifacts with initial sub-endpoints, browsing modules with pagination and page-size controls, viewing live container CPU %, memory usage, and uptime, starting/stopping modules, deploying new versions, rolling back to previous versions, activating/deactivating or deleting individual sub-endpoints, resolving endpoint discrepancies, editing metadata, and deleting modules.
- **Endpoints:** Searching, filtering, paginating, and inspecting all active, pending, and inactive sub-endpoints across modules (including `DiscoveredInVersion`).
- **Scan Queue:** Monitoring queued scans and priorities or cancelling pending scans.
- **Logs:** Browsing and filtering structured Serilog log files.

### Management API

All management endpoints require a valid JWT bearer token and are organized by controller:

#### Modules (`ModulesManagementController` — `/api/v1/modules`)

| Method | Relative route | Purpose |
| --- | --- | --- |
| `GET` | `/` | List registered modules (supports `page` and `pageSize` query parameters; returns `PagedResultDto<ModuleDto>`). |
| `GET` | `/{moduleId}` | Retrieve module details, sub-endpoints, versions, and lifecycle status. |
| `GET` | `/{moduleId}/stats` | Retrieve live container resource metrics (CPU %, memory usage/limit, uptime, and exit code) for a module. |
| `POST` | `/` | Upload a new module archive (`multipart/form-data`). |
| `POST` | `/{moduleId}/start` | Start a stopped or created module container. |
| `POST` | `/{moduleId}/stop` | Stop a running module container. |
| `PUT` | `/{moduleId}` | Update module metadata (`ModuleName`, `Description`). |
| `DELETE` | `/{moduleId}` | Delete a module, its container/image, versions, and stored files. |

#### Module Versions (`ModuleVersionsManagementController`)

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/v1/module-versions` | Retrieve module versions across all modules (supports `page` and `pageSize` query parameters; returns `PagedResultDto<ModuleVersionDto>`). |
| `GET` | `/api/v1/modules/{moduleId}/versions` | Retrieve all versions for a specific module. |
| `GET` | `/api/v1/modules/{moduleId}/versions/{version}` | Retrieve details of a specific module version. |
| `POST` | `/api/v1/modules/{moduleId}/versions` | Upload and deploy a new version for a module (`multipart/form-data`). |
| `POST` | `/api/v1/modules/{moduleId}/versions/{version}/rollback` | Roll a module back to a previously released version. |
| `PUT` | `/api/v1/modules/{moduleId}/versions/{version}` | Update version metadata (`Version`, `Changelog`). |
| `DELETE` | `/api/v1/modules/{moduleId}/versions/{version}` | Delete an inactive version of a module and its files. |

#### Sub-Endpoints (`SubEndpointsManagementController`)

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/v1/endpoints` | Retrieve detailed sub-endpoints across all modules (supports `page` and `pageSize` query parameters; returns `PagedResultDto<ModuleSubEndpointsDetailDto>`). |
| `GET` | `/api/v1/modules/{moduleId}/endpoints` | Retrieve sub-endpoints for a specific module (supports `page` and `pageSize` query parameters; returns `PagedResultDto<ModuleSubEndpointDto>`). |
| `POST` | `/api/v1/modules/{moduleId}/endpoints/confirm` | Confirm or reject pending endpoint discrepancies. |
| `PATCH` | `/api/v1/modules/{moduleId}/endpoints/{subEndpointId}/status` | Activate or deactivate an individual sub-endpoint on a running module and synchronize YARP routing. |
| `DELETE` | `/api/v1/modules/{moduleId}/endpoints/{subEndpointId}` | Delete an individual sub-endpoint from a module and synchronize YARP routing. |

#### Scanner (`ScannerManagementController` — `/api/v1/scanner`)

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/queue` | Retrieve the current scan queue and priorities. |
| `DELETE` | `/queue/{moduleId}` | Cancel a queued scan and delete the associated module. |

#### Dockerfiles (`DockerfilesManagementController` — `/api/v1/dockerfiles`)

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/` | Retrieve all Dockerfiles. |
| `GET` | `/{id}` | Retrieve a specific Dockerfile by ID. |
| `POST` | `/` | Create a new Dockerfile. |
| `PUT` | `/{id}` | Update a specific Dockerfile. |
| `DELETE` | `/{id}` | Delete a specific Dockerfile. |
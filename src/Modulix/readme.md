# Modulix

Core server and API for the Modulix framework.

## setup

To run the Modulix server locally:

```powershell
dotnet run --project src/Modulix --launch-profile http
```

## changelog

### 1.3.0
- Refactored and standardized API routes under `/api/v1/` across all controllers, removing redundant URL prefixes and verbs.
- Normalized sub-endpoint and version route hierarchies under `/api/v1/modules/{moduleId}/`.
- Updated `ReservedBasePaths` to protect system endpoints under `/api/v1/` from reverse proxy module conflicts.

### 1.2.0
- Added `GET /api/modules-management/modules/{moduleId}/stats` (`IModuleService.GetModuleStatsAsync`) to expose live container CPU %, memory usage/limit, uptime, and exit code metrics.
- Added `PATCH /api/endpoint-management/sub-endpoints/{moduleId}/{endpointId}/status` and `DELETE /api/endpoint-management/sub-endpoints/{moduleId}/{endpointId}/delete` to activate, deactivate, or delete individual module sub-endpoints with live YARP route synchronization.
- Added `Module:MaxRetainedVersionsPerModule` configuration (default `5`) and `ModuleVersionExtension` to automatically clean up the oldest inactive versions from database and disk storage when a new version is activated.

### 1.1.3
- Added version-independent persistent module data directories (`<StorageBasePath>/<moduleId>/data`) via `DirectoryExtension`, preserved across version updates and rollbacks and removed upon module deletion.
- Synchronized sub-endpoint statuses (`Active` / `Inactive`) with the module runtime lifecycle (`Running`, `Stopped`, `Failed`, `QueuedForScan`).
- Normalized user-supplied sub-endpoint paths without a leading `/` and removed the module container logs endpoint.

### 1.1.2
- Added `GET /api/modules-management/modules/{moduleId}/logs` endpoint (with optional `tail` parameter) to retrieve module container logs.
- Added runtime container state reconciliation to detect crashed `Running` containers, transition them to `Failed`, and remove stale YARP routes.
- Added `Docker:SocketUri` configuration support to `appsettings.Template.json`.

### 1.1.1
- Added pagination support (`page`, `pageSize`) to module, module version, and sub-endpoint listing endpoints.
- Explicitly reserved all management API base paths (`api/modules-management`, `api/version-management`, `api/endpoint-management`, `api/scanner-management`) against module route conflicts.
- Configured EF Core `SplitQuery` behavior for SQLite and cleaned up unused controller dependencies.

### 1.1.0
- Added module version management with backup and rollback support.
- Added endpoints for starting and stopping modules.
- Split scanner and sub-endpoint management into dedicated services and controllers.

### 1.0.1
- Minor bug fixes and https support.

### 1.0.0
- Initial release of the Modulix server and API.

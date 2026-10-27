# Modulix

Core server and API for the Modulix framework.

## setup

To run the Modulix server locally:

```powershell
dotnet run --project src/Modulix --launch-profile http
```

## changelog

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

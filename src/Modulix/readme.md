# Modulix

Core server and API for the Modulix framework.

## setup

To run the Modulix server locally:

```powershell
dotnet run --project src/Modulix --launch-profile http
```

## changelog

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

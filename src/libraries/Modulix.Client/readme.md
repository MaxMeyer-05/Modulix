# Modulix - Client

HTTP client library for the Modulix framework.

## setup

To use the Modulix HTTP client library, add the following package reference to your project:

```xml
<PackageReference Include="Modulix.Client" Version="1.2.0" />
```

## changelog

### 1.2.0
- Added `GetModuleStatsAsync` to `ModulixApi` (`IModuleManagementClient`) for retrieving live container resource statistics.
- Added `UpdateSubEndpointStatusAsync` and `DeleteSubEndpointAsync` to `ModulixApi` (`ISubEndpointManagementClient`) for toggling and deleting individual module sub-endpoints.

### 1.1.3
- Removed `GetModuleContainerLogsAsync` from `ModulixApi`.

### 1.1.2
- Added `GetModuleContainerLogsAsync` (with optional `tail` parameter) to `ModulixApi` for retrieving module container logs.

### 1.1.1
- Added optional `PaginationQueryDto` query parameter support to module, module version, and sub-endpoint listing methods.

### 1.1.0
- Added clients for module version, scanner, and sub-endpoint management.
- Added start, stop, and rollback methods to module management and updated operations to return `HttpResponseMessage`.

### 1.0.0
- Initial release of the Modulix HTTP client library.

# Modulix - Abstraction

Abstraction library for the Modulix HTTP client.

## changelog

### 1.1.2
- Added `GetModuleContainerLogsAsync` (with optional `tail` parameter) to `IModuleManagementClient`.

### 1.1.1
- Added `PagedResultDto<T>` and `PaginationQueryDto` pagination contracts and updated client interfaces for paged list requests.

### 1.1.0
- Added client interfaces and DTOs for module versions, scanner, and sub-endpoint management.
- Added start, stop, and rollback support to `IModuleManagementClient`.

### 1.0.0
- Initial release of the Modulix HTTP client abstraction library.
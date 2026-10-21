# Modulix - Web

Web frontend for the Modulix framework.

## setup

To run the Modulix web frontend locally:

```powershell
dotnet run --project src/Modulix.Web
```

## changelog

### 1.1.1
- Added pagination controls and page-size selection to the Modules, API Endpoints, and Module Detail pages.
- Updated dashboard and management views to consume paged API responses (`PagedResultDto<T>`).

### 1.1.0
- Added module start, stop, and version rollback actions in the UI.
- Added sub-endpoint management and endpoint scan confirmation with improved error handling.

### 1.0.1
- Minor bug fixes and https support.

### 1.0.0
- Initial release of the Modulix web frontend.

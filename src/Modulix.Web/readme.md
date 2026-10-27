# Modulix - Web

Web frontend for the Modulix framework.

## setup

To run the Modulix web frontend locally:

```powershell
dotnet run --project src/Modulix.Web
```

## changelog

### 1.1.3
- Surfaced `SubEndpointDiscrepancyReportDto` details (`Extra`, `Missing`, and `Matched` endpoints) on `/modules/{id}` and in the endpoint confirmation dialog, defaulting `Extra` endpoints to confirm and `Missing` endpoints to remove.
- Hid unavailable/blocked action buttons across module details, dialogs, pagination, and log views instead of rendering them disabled.
- Allowed entering initial sub-endpoint paths without a leading `/` during module creation and removed the module container logs section.

### 1.1.2
- Added container logs viewer section on the module detail page (`/modules/{id}`) with tail line selection, line wrapping, refresh, and `.log` file download.
- Added a prominent failure alert banner on `/modules/{id}` when a module container is in `Failed` status.
- Split `DetailModel` and `Detail.cshtml` into focused partial classes and Razor partial views.

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
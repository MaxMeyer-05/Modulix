# Modulix

Core server and API for the Modulix framework.

## setup

To run the Modulix server locally:

```powershell
dotnet run --project src/Modulix --launch-profile http
```

## changelog

### 1.1.0
- Added module version management with backup and rollback support.
- Added endpoints for starting and stopping modules.
- Split scanner and sub-endpoint management into dedicated services and controllers.

### 1.0.1
- Minor bug fixes and https support.

### 1.0.0
- Initial release of the Modulix server and API.

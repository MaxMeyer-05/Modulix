# Modulix - Docker

Docker container management library for the Modulix framework.

## setup

To use the Modulix Docker library, add the following package reference to your project:

```xml
<PackageReference Include="Modulix.Docker" Version="1.2.0" />
```

## changelog

### 1.2.0
- Added `ContainerStats` and `IDockerService.GetContainerStatsAsync` / `DockerService.GetContainerStatsAsync` to inspect live container CPU %, memory usage/limit (MiB), uptime, and exit code.
- Extracted Docker image cleanup, host data directory setup, and port probing helpers into `DockerExtension` and registered it in `DockerRegistration.AddDockerServices`.

### 1.1.3
- Mounted a persistent, version-independent host data directory (`<hostDataPath>:/app/data`) into module containers in `DockerService.BuildContainerAsync` and configured write permissions for `APP_UID`.
- Removed `GetContainerLogsAsync` from `IDockerService` and `DockerService`.

### 1.1.2
- Added `GetContainerLogsAsync` and `IsContainerRunningAsync` to `IDockerService` / `DockerService`, and enhanced `WaitUntilReadyAsync` with TCP port readiness checks for containers without a `HEALTHCHECK`.
- Added configurable `DockerOptions.SocketUri` support in `DockerRegistration.AddDockerServices`.
- Improved Podman/Docker compatibility by using `Force = true` and handling conflicts during image deletion (`TryDeleteImageAsync`) and rootless network teardown errors in `StopContainerAsync`.

### 1.1.0
- Added `TryRemoveModuleContainerAsync` for safe module container cleanup with timeout handling.

### 1.0.0
- Initial release of the Modulix Docker library.

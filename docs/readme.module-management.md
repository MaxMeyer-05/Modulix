# Module Management

Modulix accepts published ASP.NET Core applications as ZIP archives, scans
their controller endpoints, stores module metadata, and builds and runs Docker
containers. Module management does not provide a reverse proxy or forward
requests to the registered endpoints.

See the [project README](../readme.md) for the repository overview and
[Server, Authentication, and Users](readme.server.md) for setup and token handling.

## Prerequisites

- A running Modulix server with configured SQLite and JWT settings.
- A valid access token. Reads require authentication; mutations require `Admin`.
- A reachable Docker daemon for container creation, confirmation, replacement,
  and deletion. On Linux the service connects to `/var/run/docker.sock`; on
  Windows it uses `npipe://./pipe/docker_engine`.
- Write access for the service account to `storage/modules` below the server's
  content root and to the temporary-file location.

Only upload trusted binaries. The server inspects assemblies and executes the
uploaded application in Docker. Access to the Docker daemon is privileged;
containerization is not a substitute for trusting the uploaded code.

## Prepare a Module Archive

Publish a controller-based ASP.NET Core application for .NET 10. For example,
with a module project outside this repository:

```sh
dotnet publish path/to/ExampleModule.csproj -c Release -o ./module-publish
cd module-publish
zip -r ../example-module.zip .
```

Archive the contents of the publish directory, not the directory itself. The
entry assembly and matching `.runtimeconfig.json` must be at the archive root.
Include the `.deps.json`, dependencies, application settings, and other required
published assets. Avoid unrelated `.runtimeconfig.json` files that could make
entry-assembly selection ambiguous.

Endpoint discovery inspects controller metadata rather than making live HTTP
requests. Do not assume it discovers every endpoint registered dynamically or
through minimal APIs. The Docker service generates its own Dockerfile using
`mcr.microsoft.com/dotnet/aspnet:10.0` and starts the detected entry assembly in
the Production environment. A module must work with those runtime settings.

### Upload Limits

Both creation and file replacement accept `multipart/form-data`:

| Limit | Value |
| --- | --- |
| Entire HTTP request body | 50 MiB (52,428,800 bytes). |
| Individual multipart section | 50 MiB. |
| ZIP entry count | At most 1,000. |
| Total uncompressed ZIP contents | At most 512 MiB. |

The request-body limit includes text fields and multipart boundaries, so leave
room below 50 MiB for the ZIP itself. MVC form binding returns `400` for an
oversized request; a reverse proxy may return `413` or impose a lower limit.
Archive entry and uncompressed-size limit violations return `409`.

## API Overview

All routes in this table are relative to
`/api/modules-management/modules`. IDs are GUIDs.

| Method | Route | Access | Body | Success |
| --- | --- | --- | --- | --- |
| `GET` | `/` | Authenticated | None | `200`, module list. |
| `GET` | `/{moduleId}` | Authenticated | None | `200`, module details and endpoints. |
| `GET` | `/{moduleId}/sub-endpoints` | Authenticated | None | `200`, endpoint list. |
| `POST` | `/create` | Admin | Multipart | `201` or `202`, creation result. |
| `POST` | `/{moduleId}/confirm-endpoints` | Admin | JSON | `200`, module details. |
| `PUT` | `/{moduleId}` | Admin | JSON | `204`. |
| `PUT` | `/{moduleId}/files` | Admin | Multipart | `204`. |
| `DELETE` | `/{moduleId}` | Admin | None | `204`, including an absent module. |

Use `Authorization: Bearer <accessToken>` on every request. Missing or invalid
authentication returns `401`; a non-admin calling a mutation receives `403`.
Missing modules return `404`, except for idempotent deletion.

## Create a Module

| Form field | Required | Description |
| --- | --- | --- |
| `ModuleName` | Yes | Display name, at most 100 characters. |
| `Description` | No | Description, at most 500 characters. |
| `BaseEndpointPath` | Yes | Unique base path, at most 200 characters. |
| `ContainerPort` | No | Port from 1 to 65535; must not already be assigned to another module. |
| `ModuleFile` | Yes | Nonempty ZIP archive supplied as a file field. |
| `InitialEndpoints[i].HttpMethod` | No | Expected endpoint method, at most 10 characters. |
| `InitialEndpoints[i].EndpointPath` | No | Expected endpoint path, at most 200 characters. |

Without `ContainerPort`, the service chooses an available port from its
allocation range. Base paths are trimmed and stripped of leading/trailing
slashes. Existing base paths and conflicting prefixes are rejected with `400`.
The base path is stored metadata, not an automatically exposed proxy route.

```sh
curl -i http://localhost:5284/api/modules-management/modules/create \
  -H "Authorization: Bearer $ADMIN_ACCESS_TOKEN" \
  -F 'ModuleName=Example Module' \
  -F 'Description=Local test module' \
  -F 'BaseEndpointPath=example' \
  -F 'ModuleFile=@./example-module.zip;type=application/zip'
```

Let curl or Postman generate the multipart `Content-Type` and boundary. JSON
does not work for uploads. To compare expected endpoints with scanned endpoints,
add indexed fields, for example:

```sh
-F 'InitialEndpoints[0].HttpMethod=GET' \
-F 'InitialEndpoints[0].EndpointPath=api/example'
```

Use the exact route strings returned by the scanner, without assuming that the
module base path is added to them. Initial creation compares method and path
strings exactly. If no initial endpoints are supplied, discovered endpoints are
registered without a discrepancy-confirmation step.

Both success codes return a `ModuleCreationResultDto` envelope:

```json
{
  "module": {
    "id": "<moduleId>",
    "moduleName": "Example Module",
    "baseEndpointPath": "example",
    "containerPort": 1024,
    "status": 3,
    "endpoints": []
  },
  "discrepancyReport": {
    "hasDiscrepancy": false
  }
}
```

This example shows selected fields. Details also include description, creation
time, storage path, and container ID. Save `module.id` for later operations.
`202` means the module is `PendingConfirmation`; `201` means it is not pending,
but does not guarantee that Docker started successfully. Always inspect
`module.status`, which may be `Failed`.

## Endpoint Discrepancies and Confirmation

When expected and discovered endpoints differ, creation stores the module in
`PendingConfirmation` and does not start its container. The discrepancy report
contains the module ID and these lists:

| Field | Meaning |
| --- | --- |
| `matchedEndpoints` | Expected endpoints that were discovered. |
| `missingEndpoints` | Expected endpoints that were not discovered. |
| `extraEndpoints` | Discovered endpoints that were not expected. |

Review the report before submitting it. Confirmation accepts an array of
reports, not a flat endpoint array:

```json
{
  "confirmedEndpoints": [
    {
      "moduleId": "<moduleId>",
      "hasDiscrepancy": true,
      "missingEndpoints": [
        { "httpMethod": "GET", "endpointPath": "api/expected" }
      ],
      "extraEndpoints": [
        { "httpMethod": "POST", "endpointPath": "api/discovered" }
      ]
    }
  ]
}
```

Send this JSON to
`POST /api/modules-management/modules/{moduleId}/confirm-endpoints`.
The current confirmation behavior is important:

- Pending endpoints listed in `missingEndpoints` are accepted as active metadata.
- Pending endpoints listed in `extraEndpoints` are removed from registered metadata.
- Unaddressed pending endpoints remain pending. An empty report array does not
  complete confirmation.

Confirmation changes metadata, not the application's actual routes. Accepting
a missing endpoint does not create it in the module binary, and removing an
extra endpoint does not disable it inside the container.

Only after every pending endpoint has been resolved does the service attempt
to build and start the container. The response is `200` even when confirmation
remains incomplete or container startup sets the module to `Failed`. Inspect
the returned status and retain the report while it is still pending. Confirming
a module that is not in `PendingConfirmation` returns `409`.

## Read and Update

Listing returns `ModuleDto` records. Details add `containerId`, `storagePath`,
and `endpoints`. Endpoint records contain their ID, HTTP method, path, status,
and creation time.

Metadata updates accept only the optional name and description fields:

```json
{
  "moduleName": "Renamed Module",
  "description": "Updated description"
}
```

Send this JSON to `PUT /api/modules-management/modules/{moduleId}`. This does
not replace files, change the base path or port, or restart the container.

### Replace Module Files

```sh
curl -i -X PUT "http://localhost:5284/api/modules-management/modules/$MODULE_ID/files" \
  -H "Authorization: Bearer $ADMIN_ACCESS_TOKEN" \
  -F 'ModuleFile=@./replacement-module.zip;type=application/zip'
```

The service extracts replacement files into a separate version directory and
scans them before changing the active version. Replacement requires exactly the
registered set of HTTP methods and paths. Methods are compared case-insensitively
for replacement; paths are compared exactly. Pending endpoint confirmation or
a changed endpoint contract returns `409` and preserves the previous version.

A candidate container must start and pass its TCP port health check before the
new container, storage path, entry assembly, and `Running` status are persisted.
Readiness checks have a 60-second deadline; they test an open port, not a
business-level HTTP health endpoint. Creation and confirmation do not perform
this replacement-readiness wait.

On success, the service attempts to remove the old container/image and files.
On failure before activation, it restores the previous persisted version and
attempts to clean up the candidate. Cleanup failures are logged and may leave
resources behind; there is no automatic cleanup retry or traffic-draining
mechanism. This API does not guarantee a zero-downtime traffic switch.

## Status and Deletion

Module statuses are serialized as numbers:

| Value | Name | Meaning |
| --- | --- | --- |
| `0` | `PendingConfirmation` | Endpoint discrepancies still require review. |
| `1` | `Created` | Created but not yet started. |
| `2` | `Starting` | Container startup is in progress. |
| `3` | `Running` | Container start succeeded. |
| `4` | `Stopped` | Stopped status defined by the model. |
| `5` | `Failed` | Container provisioning/startup failed. |

There are no public start, stop, or restart endpoints. Treat the status as
persisted lifecycle information, not continuous live Docker monitoring.

`DELETE /api/modules-management/modules/{moduleId}` removes the container/image
when present, attempts to delete stored files, and deletes module and endpoint
records. It returns `204` even if the module is already absent. Docker removal
errors can abort deletion; file cleanup errors are logged and can leave files
behind. Use deletion deliberately, especially against production modules.

## Testing and Implementation

The [Postman testing conventions](../postman/documents/testing-conventions.md)
describe module preparation, indexed form fields, saved module IDs, confirmation
reports, and manual cleanup. Create a module before running detail requests on
an empty database. Do not confirm discrepancies in an unattended run.

The implementation is split between the
[controller](../src/Server/Controllers/ModulesManagementController.cs),
[module service](../src/Server/Services/ModuleService.cs),
[endpoint scanner](../src/Server/Services/ModuleEndpointScanner.cs), and
[Docker service](../src/Server/Services/DockerService.cs).
Request and response models are defined in
[ModuleDtos.cs](../src/Server/Models/Dtos/ModuleDtos.cs).
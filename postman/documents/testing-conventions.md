# Postman API Testing

## Use in VS Code

The Postman extension reads the source-controlled collection and environment files below `postman/` automatically. Select `Modulix API v1` in the Postman view, then activate the required `Modulix` environment.

1. Set `baseUrl` for non-local environments to the actual deployment URL.
2. Configure `adminEmail` and `adminPassword` only when admin requests are needed.
3. Export from the Postman view when the collection must be used in a separate Postman client.

The local environment targets `http://localhost:5284`, which is the default development profile URL. Start the service before sending requests:

```sh
dotnet run --project src/Server
```

## Collection order

Run `00 - Prerequisites` before authenticated folders. It registers a local test user, logs in, and writes `userAccessToken`, `userRefreshToken`, and `testUserId` to the active environment. The email and test passwords are generated only when their variables are empty.

`10 - Authentication and Session` rotates the user token pair. Profile and password changes in `20 - Current User` also rotate the token pair; their test scripts update the active environment automatically.

`30 - Admin User Management` requires an existing admin account. Set `adminTargetUserId` only to a disposable test account. Do not execute `99 - Manual Cleanup` in a normal Collection Runner run; select a cleanup request deliberately.

## Module management

The module requests target `/api/modules-management/modules`. `40 - Modules` uses the standard user token for list, detail, and sub-endpoint requests. Set `moduleId` to an existing module before the detail requests, or first create a disposable module in `50 - Admin Module Management`. The folder order is not a self-contained module lifecycle: run the creation request before the read requests when starting with an empty database.

`50 - Admin Module Management` uses `adminAccessToken`. A reachable Docker daemon is required to build and run module containers. The service connects to `/var/run/docker.sock` on Linux or `npipe://./pipe/docker_engine` on Windows; the service account needs access. Use only trusted module binaries, because uploads are executed in containers.

1. Publish an ASP.NET Core module for .NET 10 and ZIP its publish output, including its entry assembly, dependencies, `.deps.json`, and `.runtimeconfig.json`.
2. Set `moduleName`, `moduleDescription`, and a unique `moduleBaseEndpointPath`. Configure `moduleZipPath` and `moduleReplacementZipPath` with local ZIP paths, or select the files directly in the requests' `ModuleFile` form-data fields. Local file paths should stay in a private environment.
3. Run **Create module - 201 or 202**. The request stores `moduleId` and a JSON array of discrepancy reports in `moduleEndpointConfirmation`. The response is a `ModuleCreationResultDto` envelope containing `module` and `discrepancyReport`, for both status codes. A `201` does not guarantee that the container is running; inspect `module.status`.
4. For `202`, review the report before running **Confirm module endpoints - 200**. The request sends `{ "confirmedEndpoints": [...] }` and is skipped when `moduleEndpointConfirmation` is empty. A `200` can still leave the module in `PendingConfirmation` (status `0`); the script retains the report in that case. Do not include this step in an unattended run when discrepancies require review.
5. Run the read requests and metadata update as needed. For the file update, select a replacement ZIP with exactly the registered HTTP methods and paths. Pending confirmation or an endpoint contract change results in `409`; successful replacement waits for container readiness before activating the candidate.
6. Delete the disposable module explicitly using **Delete configured module - 204** in `99 - Manual Cleanup`. This request uses the admin token and clears the saved module ID and confirmation report on success.

The creation request omits the optional `ContainerPort` and `InitialEndpoints` fields. The service assigns an available port. To test endpoint discrepancies, add indexed form-data text fields such as `InitialEndpoints[0].HttpMethod` and `InitialEndpoints[0].EndpointPath` with the expected routes. Let Postman generate the multipart `Content-Type` header and boundary; do not set it manually.

Both upload actions explicitly bind `[FromForm]` and accept `multipart/form-data`. Select the ZIP as a file field named `ModuleFile`; creation also requires the text fields `ModuleName` and `BaseEndpointPath`. A missing file fails validation with `400`; JSON is not accepted for these uploads.

Creation and file replacement limit the entire request body to 50 MiB (52,428,800 bytes), including all fields and multipart boundaries. Leave room for this overhead when preparing the ZIP. An oversized request is rejected during MVC form binding with `400`; a reverse proxy may instead return `413` or impose a lower limit. Both endpoints also limit individual multipart sections to 50 MiB. Archive validation separately limits ZIPs to 1,000 entries and 512 MiB of uncompressed data.

## Sensitive values

Committed environment files contain no credentials or tokens. Store real credentials in a local environment named `*.private.environment.yaml`; private environments are ignored by Git. Do not use real production users for destructive requests.

## API response contract

Successful logins return:

```json
{
  "user": { "id": "..." },
  "tokenResult": {
    "accessToken": "...",
    "refreshToken": "..."
  }
}
```

The collection uses this response to populate the user and admin token variables.
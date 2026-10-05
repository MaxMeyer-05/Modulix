# Postman API Testing

This guide covers collection setup, request order, test variables, and cleanup.
API contracts are documented in
[Server, Authentication, and Users](../../docs/readme.server.md) and
[Module Management](../../docs/readme.module-management.md).

## Use in VS Code

The Postman extension reads the source-controlled collection and environment files below `postman/` automatically. Select `Modulix API v1` in the Postman view, then activate the required `Modulix` environment.

1. Set `baseUrl` for non-local environments to the actual deployment URL.
2. Configure `adminEmail` and `adminPassword` only when admin requests are needed.
3. Export from the Postman view when the collection must be used in a separate Postman client.

Start the server using the [project quick start](../../readme.md#quick-start)
before sending requests. The `Modulix Local` environment already targets the
development profile URL.

## Collection order

Run `00 - Prerequisites` before authenticated folders. It registers a local test user, logs in, and writes `userAccessToken`, `userRefreshToken`, and `testUserId` to the active environment. The email and test passwords are generated only when their variables are empty.

`10 - Authentication and Session` rotates the user token pair. Profile and password changes in `20 - Current User` also rotate the token pair; their test scripts update the active environment automatically.

`30 - Admin User Management` requires an existing admin account. Set `adminTargetUserId` only to a disposable test account. Do not execute `99 - Manual Cleanup` in a normal Collection Runner run; select a cleanup request deliberately.

## Module management

The module requests target `/api/modules-management/modules`. `40 - Modules` uses the standard user token for list, detail, and sub-endpoint requests. Set `moduleId` to an existing module before the detail requests, or first create a disposable module in `50 - Admin Module Management`. The folder order is not a self-contained module lifecycle: run the creation request before the read requests when starting with an empty database.

`50 - Admin Module Management` uses `adminAccessToken`. Check the
[module prerequisites](../../docs/readme.module-management.md#prerequisites)
before running it.

1. Prepare a trusted module ZIP following [Prepare a Module Archive](../../docs/readme.module-management.md#prepare-a-module-archive), including the documented upload limits.
2. Set `moduleName`, `moduleDescription`, and a unique `moduleBaseEndpointPath`. Configure `moduleZipPath` and `moduleReplacementZipPath` with local ZIP paths, or select the files directly in the requests' `ModuleFile` form-data fields. Local file paths should stay in a private environment.
3. Run **Create module - 201 or 202**. The script stores `moduleId` and a JSON array of discrepancy reports in `moduleEndpointConfirmation`. Inspect `module.status`, not just the HTTP success code; see [Create a Module](../../docs/readme.module-management.md#create-a-module) for response semantics.
4. For `202`, review the report before running **Confirm module endpoints - 200**. The request is skipped when `moduleEndpointConfirmation` is empty. The script retains the report while the module is still pending. Do not include this step in an unattended run when discrepancies require review. See [Endpoint Discrepancies and Confirmation](../../docs/readme.module-management.md#endpoint-discrepancies-and-confirmation) for how reports change registered metadata.
5. Run the read requests and metadata update as needed. For binary updates, configure the replacement ZIP according to [Replace Module Files](../../docs/readme.module-management.md#replace-module-files).
6. Delete the disposable module explicitly using **Delete configured module - 204** in `99 - Manual Cleanup`. This request uses the admin token and clears the saved module ID and confirmation report on success.

The creation request omits the optional `ContainerPort` and `InitialEndpoints`
fields. To test endpoint discrepancies, add the indexed form-data fields shown
in [Create a Module](../../docs/readme.module-management.md#create-a-module).
Let Postman generate the multipart `Content-Type` header and boundary; do not
set it manually.

## Sensitive values

Committed environment files contain no credentials or tokens. Store real credentials in a local environment named `*.private.environment.yaml`; private environments are ignored by Git. Do not use real production users for destructive requests.

The login scripts populate user and admin token variables from the
[login response](../../docs/readme.server.md#login); refresh and profile-change
scripts replace them with the returned token pair.
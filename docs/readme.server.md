# Server, Authentication, and Users

Modulix runs as an ASP.NET Core 10 Web API. The server stores users, refresh
tokens, and module metadata in SQLite and uses JWT bearer authentication for
secured endpoints.

See the [project README](../readme.md) for the repository overview and
[Module Management](readme.module-management.md) for module uploads and Docker
operations.

## Local Setup

Run the following commands from the repository root with .NET SDK 10.0
installed:

```sh
dotnet restore
dotnet build
dotnet test
```

Use [appsettings.Template.json](../src/Server/appsettings.Template.json) as a
reference for local settings. Store the JWT signing key in User Secrets, not
in a committed configuration file:

```sh
dotnet user-secrets set "Jwt:SecretKey" "replace-with-a-random-secret-of-at-least-32-bytes" --project src/Server
dotnet run --project src/Server
```

The development profile listens on `http://localhost:5284`. In Development,
Swagger UI is available at `/swagger`, Swagger JSON at `/swagger/v1/swagger.json`,
and the built-in OpenAPI document at `/openapi/v1.json`. These endpoints are
not mapped outside Development.

Docker is needed for module container operations, not for authentication or
user management. The service account needs write access to the SQLite database
location and configured log destinations. Pending EF Core migrations are
applied automatically during startup.

## Configuration

ASP.NET Core loads `appsettings.json`, environment-specific settings, and
environment variables. User Secrets are available in Development. Environment
variables override JSON settings; use `__` for nested configuration keys.

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:ServerDatabase` | SQLite connection string, for example `Data Source=modulix.db`. |
| `Jwt:Issuer` | Expected JWT issuer. |
| `Jwt:Audience` | Expected JWT audience. |
| `Jwt:SecretKey` | UTF-8 signing key with at least 32 bytes. |
| `Jwt:AccessTokenLifetimeMinutes` | Positive access-token lifetime in minutes. |
| `Jwt:RefreshTokenLifetimeMinutes` | Positive refresh-token lifetime in minutes. |
| `Serilog` | Log levels, sinks, and retention settings. |

JWT options are validated on startup. For deployments, inject secrets through
the environment or your deployment's secret store. The corresponding signing
key variable is `Jwt__SecretKey`. Keep the database and module storage persistent
and back up the database before deployments that apply migrations. Expose the
service through HTTPS in production.

## Authentication API

Requests use JSON with `Content-Type: application/json`. Secured endpoints
require `Authorization: Bearer <accessToken>`.

| Method | Route | Access | Success |
| --- | --- | --- | --- |
| `POST` | `/api/auth/register` | Anonymous | `204`, no response body. |
| `POST` | `/api/auth/login` | Anonymous | `200`, user and token pair. |
| `POST` | `/api/auth/logout` | Authenticated | `204`, refresh token revoked. |
| `POST` | `/api/users/token/refresh` | Anonymous, valid refresh token required | `200`, new token pair. |

### Register

```sh
curl -i http://localhost:5284/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"userEmail":"user@example.com","userPassword":"local-demo-password","confirm_UserPassword":"local-demo-password"}'
```

All three fields are required. The email must be valid and both passwords must
match. A duplicate email returns `409`; invalid input returns `400`.
Registration does not log the user in and accepts no role or scope assignment.
The example credentials are for disposable local testing only.

### Login

```sh
curl -i http://localhost:5284/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"userEmail":"user@example.com","userPassword":"local-demo-password"}'
```

The response has this shape; placeholders represent returned values:

```json
{
  "user": {
    "id": "<userId>",
    "userEmail": "user@example.com",
    "role": 1,
    "allowedScopes": null,
    "isActive": true,
    "createdAt": "<UTC timestamp>"
  },
  "tokenResult": {
    "accessToken": "<accessToken>",
    "refreshToken": "<refreshToken>",
    "accessTokenExpiresAtUtc": "<UTC timestamp>",
    "refreshTokenExpiresAtUtc": "<UTC timestamp>"
  }
}
```

Invalid credentials return `401`. Roles use numeric JSON values: `Admin = 0`
and `User = 1`. In Swagger UI, select **Authorize** and enter only the access
token; Swagger adds the bearer prefix.

### Refresh and Logout

Send the refresh token as the JSON body to `/api/users/token/refresh`:

```json
{
  "refreshToken": "<refreshToken>"
}
```

A successful refresh returns the four token fields directly, without a
`tokenResult` wrapper. The previous refresh token is revoked. Store the new
pair before the next request; expired, revoked, or unknown refresh tokens
return `401`. This endpoint does not require a valid access token.

Logout uses the same body at `/api/auth/logout`, but additionally requires a
valid access token. The refresh token must be unrevoked, unexpired, and owned
by the authenticated user. Logout revokes that refresh token only, not every
session for the account.

Access tokens are validated by issuer, audience, signature, lifetime, and
valid subject and role claims. They are not checked against a server-side
revocation list. Logout, password changes, and account deletion do not
immediately invalidate already-issued access tokens; those remain valid until
their configured expiration and validation rules reject them.

## Current User API

The user ID comes from the authenticated session, not from the request body.

| Method | Route | JSON body | Success |
| --- | --- | --- | --- |
| `GET` | `/api/users/me` | None | `200`, `UserDto`. |
| `PATCH` | `/api/users/me` | Optional `userEmail` | `200`, token pair; `204` if unchanged. |
| `PATCH` | `/api/users/me/password` | `currentPassword`, `newPassword`, `confirmNewPassword` | `200`, token pair. |
| `DELETE` | `/api/users/me` | `currentPassword` | `204`. |

For example, update the email with:

```sh
curl -i -X PATCH http://localhost:5284/api/users/me \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"userEmail":"updated@example.com"}'
```

For a password change, provide the current password and matching new values:

```json
{
  "currentPassword": "local-demo-password",
  "newPassword": "replacement-demo-password",
  "confirmNewPassword": "replacement-demo-password"
}
```

An actual profile change or successful password change removes all existing
refresh tokens for the account and persists a new token pair. Replace the
client's stored pair with the response. An unchanged profile returns `204`
without rotating tokens. An incorrect current password returns `401`; password
confirmation mismatches return `400`. Missing users return `404`.

Account deletion requires the current password and is destructive. Unlike
module deletion, deleting a missing user returns `404`.

## Admin User API

All routes in this table require the `Admin` role. Use an existing admin
account; registration is not an admin-provisioning endpoint.

| Method | Route | JSON body | Success |
| --- | --- | --- | --- |
| `GET` | `/api/users` | None | `200`, user list ordered by email and ID. |
| `PATCH` | `/api/users/{userId}/role` | `role` | `204`. |
| `PATCH` | `/api/users/{userId}/scopes` | `allowedScopes` | `204`. |
| `DELETE` | `/api/users/{userId}` | None | `204`, no target password required. |

Assign the admin role using `{ "role": 0 }`, or the regular user role using
`{ "role": 1 }`. Set scopes with a JSON string array:

```json
{
  "allowedScopes": ["module:read", "module:write"]
}
```

These scope names are examples, not predefined permissions. The server stores
scopes and includes them in issued tokens, but the current controllers enforce
authentication and roles, not scope policies.

Role and scope updates do not rotate tokens or modify existing JWT claims.
The affected user must refresh or log in again to obtain the new claims.
Missing target users return `404`; an authenticated non-admin receives `403`.

## Errors and Testing

MVC validation failures normally return `400` with validation problem details.
The global exception handler maps application exceptions as follows:

| Status | Meaning |
| --- | --- |
| `400` | Invalid arguments or input. |
| `401` | Invalid credentials, password, or refresh token; also missing/invalid bearer authentication. |
| `403` | Authenticated user lacks the required role. |
| `404` | Requested resource does not exist. |
| `409` | Conflicting operation, such as registering an existing email. |
| `500` | Unexpected server error; internal details are not returned. |

Application exceptions use RFC 7807 problem details. Authentication and
authorization middleware responses do not necessarily have the same body.
Serilog records errors and configured operational logs.

The [Postman testing conventions](../postman/documents/testing-conventions.md)
describe the registration/login prerequisites, token variables, admin requests,
and manual cleanup. Keep credentials and tokens in ignored private environments.
Server tests live in [tests/Server](../tests/Server).
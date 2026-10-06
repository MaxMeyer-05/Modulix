# Server and Authentication

This guide documents server configuration, Keycloak authentication,
and error responses. See the
[project README](../readme.md#quick-start) for local setup and
[Module Management](readme.module-management.md) for module uploads and Docker
operations.

## Configuration

ASP.NET Core loads `appsettings.json`, environment-specific settings, and
environment variables. User Secrets are available in Development. Environment
variables override JSON settings; use `__` for nested configuration keys.

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:ServerDatabase` | SQLite connection string, for example `Data Source=modulix.db`. |
| `Keycloak:Issuer` | Expected token issuer for the Keycloak realm. |
| `Keycloak:Audience` | Audience that must be present in an API access token. |
| `Keycloak:MetadataAddress` | Realm OpenID Connect discovery URL, ending in `/.well-known/openid-configuration`. |
| `Serilog` | Log levels, sinks, and retention settings. |

The API retrieves public signing keys from Keycloak metadata instead of
issuing or signing tokens itself. Access tokens are supplied by clients in
the bearer header, not stored in server configuration.

Keep the database and module storage persistent and back up the database
before deployments that apply migrations. Expose the service through HTTPS
in production. HTTPS metadata enforcement is currently disabled in code and
must be enabled before production deployment.

## Authentication

Keycloak owns user accounts, passwords, registration, login, logout,
refresh tokens, and role assignments. Modulix has no local user table,
password hashes, session store, or authentication and user-management APIs.
The remaining API manages modules and their endpoints.

Secured endpoints require a Keycloak access token in the
`Authorization: Bearer <access-token>` header. Bearer authentication validates
issuer, audience, signature, and lifetime using the configured realm metadata.
The API does not perform per-request Keycloak session or revocation checks.

### Authorization

Module controllers retain their authentication and `Admin` role requirements.
The `Roles` enum remains a naming convention for these requirements, not a
local role assignment store. The API disables inbound claim renaming and
uses the top-level `roles` claim for ASP.NET Core role checks. Role names are
case-sensitive: `Admin` satisfies an admin requirement; `admin` does not.

Configure a Keycloak `User Client Role` mapper for the Modulix API client to
emit a multivalued string claim named `roles` in access tokens. Assign its
client scope to the login client as a default scope. Add an audience mapper
for the API client so tokens contain the configured `Keycloak:Audience`.
Nested `realm_access.roles` or `resource_access.<client>.roles` alone do not
satisfy these checks. Obtain a new access token after changing the mappers
or role assignments.

## Database

The `InitialCreate` migration in `src/Modulix/Database/Migrations` creates
the module and module-endpoint tables. No local user or refresh-token tables
are included. Startup applies pending migrations with `Database.Migrate()`.

This initial migration is intended for a new database. If an existing database
still uses the previous user/session schema or migration history, preserve
its data and reconcile that history before applying the new initial migration.
Do not apply it over existing tables without a migration plan.

## Errors and Testing

MVC validation failures normally return `400` with validation problem details.
The global exception handler maps application exceptions as follows:

| Status | Meaning |
| --- | --- |
| `400` | Invalid arguments or input. |
| `401` | Missing or invalid bearer authentication. |
| `403` | Authenticated user lacks the required role. |
| `404` | Requested resource does not exist. |
| `409` | Conflicting operation. |
| `500` | Unexpected server error; internal details are not returned. |

Application exceptions use RFC 7807 problem details. Authentication and
authorization middleware responses do not necessarily have the same body.
Serilog records errors and configured operational logs.

The [Postman testing conventions](../postman/documents/testing-conventions.md)
describe externally supplied access tokens, module requests, and manual cleanup,
including how to store sensitive test values.
Server tests live in [tests/Modulix.Tests](../tests/Modulix.Tests).
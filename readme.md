# Modulix

TBD: general description, what is the purpose and responsiblity of ths service?

## Contribute

### Prerequisites

- .NET SDK 10.0

Restore dependencies, build the solution, and run all tests from the repository
root:

```sh
dotnet restore
dotnet build
dotnet test
```

Start the service locally:

```sh
dotnet run --project src/Server
```

The development profile listens on `http://localhost:5284`. OpenAPI and Swagger
UI are available only in the Development environment at
`http://localhost:5284/swagger`.

### Configuration

Use `src/Server/appsettings.Template.json` as the starting point for local
configuration. It contains the SQLite connection string, Serilog settings, and
non-secret JWT options.

The `Jwt` configuration section requires the following values:

| Key | Description |
| --- | --- |
| `Jwt:Issuer` | Expected token issuer. |
| `Jwt:Audience` | Expected token audience. |
| `Jwt:AccessTokenLifetimeMinutes` | Positive access-token lifetime in minutes. |
| `Jwt:RefreshTokenLifetimeMinutes` | Positive refresh-token lifetime in minutes. |
| `Jwt:SecretKey` | UTF-8 signing key with at least 32 bytes. |

Do not add `Jwt:SecretKey` to an `appsettings` file. For local development,
store it with .NET User Secrets:

```sh
dotnet user-secrets set "Jwt:SecretKey" "replace-with-a-random-secret-of-at-least-32-bytes" --project src/Server
```

For deployments, provide it through the `Jwt__SecretKey` environment variable:

```sh
export Jwt__SecretKey="replace-with-a-random-secret-of-at-least-32-bytes"
```

Environment variables override configuration values from JSON files.

### Swagger Authentication

Register a user with `POST /api/auth/register`, then use
`POST /api/auth/login` to obtain an access token and refresh token. In Swagger
UI, select **Authorize** and enter the access token. Swagger sends it as an
`Authorization: Bearer <token>` header for secured endpoints.

Use `POST /api/users/token/refresh` to exchange a valid refresh token for a new
token pair. Use `POST /api/auth/logout` to revoke a refresh token.

## Project Structure

| Path | Responsibility |
| --- | --- |
| `src/Server` | ASP.NET Core host, dependency injection, middleware, and configuration. |
| `src/Server/Controllers` | HTTP endpoints for authentication and user management. |
| `src/Server/Database` | EF Core context, entities, and SQLite migrations. |
| `src/Server/Infrastructure` | Cross-cutting infrastructure, including exception handling. |
| `src/Server/Mappers` | Mapping between persistence entities and API DTOs. |
| `src/Server/Models` | API DTOs, session data, and role definitions. |
| `src/Server/Security` | JWT configuration and issuance, password hashing, and claims helpers. |
| `src/Server/Services` | Authentication and user-management business logic. |
| `tests/Server` | Unit tests for security, services, and models. |

## Key Concepts

- **Authentication:** Users register and log in with an email address and
	password. A successful login returns a JWT access token and a refresh token.
- **Authorization:** Secured endpoints validate issuer, audience, signature,
	lifetime, subject, and role claims. Admin-only endpoints require the `Admin`
	role.
- **Sessions:** Refresh tokens are persisted in SQLite. They can be refreshed
	to obtain a new token pair or revoked during logout.
- **User management:** Authenticated users can view, update, and delete their
	own account. Administrators can list users, change roles and scopes, and
	delete user accounts.
- **Operations:** EF Core applies pending migrations during application startup.
	Serilog records application logs, while the global exception handler returns
	RFC 7807 problem-details responses.

## Related Articles

- [ASP.NET Core authentication and authorization](https://learn.microsoft.com/aspnet/core/security/authentication/)
- [JWT bearer authentication](https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication)
- [Entity Framework Core migrations](https://learn.microsoft.com/ef/core/managing-schemas/migrations/)
- [Problem Details for HTTP APIs](https://www.rfc-editor.org/rfc/rfc7807)


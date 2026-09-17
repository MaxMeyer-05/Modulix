# Modulix Service

TBD: general description, what is the purpose and responsiblity of ths service?

## Contribute

TBD: What are the requirements to build and run this application?
How to configure the application (appsettings)?

### Prerequisites

- .NET SDK 10.0

Restore, build, and run all tests from the repository root:

```sh
dotnet restore
dotnet build
dotnet test
```

Run the service locally:

```sh
dotnet run --project src/Server
```

The development launch profile listens on `http://localhost:5284`. OpenAPI and
Swagger UI are enabled only in the Development environment.

### JWT Configuration

The `Jwt` configuration section requires these values:

| Key | Description |
| --- | --- |
| `Jwt:Issuer` | Expected token issuer. |
| `Jwt:Audience` | Expected token audience. |
| `Jwt:AccessTokenLifetimeMinutes` | Positive access-token lifetime in minutes. |
| `Jwt:SecretKey` | UTF-8 signing key with at least 32 bytes. |

Copy `src/Server/appsettings.Template.json` as a starting point for the
non-secret settings. Do not commit `Jwt:SecretKey` to an `appsettings` file.

For local development, store the key in .NET User Secrets:

```sh
# Set the Jwt:SecretKey for local development
dotnet user-secrets set "Jwt:SecretKey" "replace-with-a-random-secret-of-at-least-32-bytes" --project src/Server
```

For deployments, provide the secret through the `Jwt__SecretKey` environment
variable. Environment-variable configuration overrides values from JSON files.

```sh
# Example of setting the Jwt:SecretKey environment variable for deployment
export Jwt__SecretKey="replace-with-a-random-secret-of-at-least-32-bytes"
```

## Project Structure

| Path | Responsibility |
| --- | --- |
| `src/Server` | ASP.NET Core service and application configuration. |
| `src/Server/Security` | JWT options, token issuance, token validation, roles, and claims helpers. |
| `src/Server/Models` | API and session data-transfer objects. |
| `src/Server/Database` | Reserved for the Entity Framework Core context and entities. |
| `tests/Server` | Unit tests for the service. |

## key concepts

TBD: Short description of your implementation, what are the uses cases and how do they work in general?

## related articles

TBD: links and references


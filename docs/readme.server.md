# Server, Authentication, and Users

This guide documents server configuration, authentication and token lifecycle,
user APIs, and error responses. See the
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

Authentication endpoints use JSON. Secured endpoints require a valid JWT
access token.

| Method | Route | Access | Purpose | Success |
| --- | --- | --- | --- | --- |
| `POST` | `/api/auth/register` | Anonymous | Creates a regular user account. | `204`, no response body. |
| `POST` | `/api/auth/login` | Anonymous | Verifies credentials and creates a session. | `200`, user and token pair. |
| `POST` | `/api/auth/logout` | Authenticated | Revokes one refresh token owned by the current user. | `204`. |
| `POST` | `/api/users/token/refresh` | Anonymous, valid refresh token required | Rotates the refresh token and issues new access and refresh tokens. | `200`, new token pair. |

### Register

Registration requires a valid email, a password, and matching password
confirmation. A duplicate email returns `409`; invalid input returns `400`.
Registration does not log the user in and accepts no role or scope assignment.

### Login

Login verifies the email and password. The response contains the account
details in `user` and the issued tokens in `tokenResult`. The token result
includes `accessToken`, `refreshToken`, `accessTokenExpiresAtUtc`, and
`refreshTokenExpiresAtUtc`.

Invalid credentials return `401`. Roles use numeric JSON values: `Admin = 0`
and `User = 1`.

### Refresh and Logout

A successful refresh returns the four token fields directly, without a
`tokenResult` wrapper, and revokes the previous refresh token. Expired,
revoked, or unknown refresh tokens return `401`. This endpoint does not
require a valid access token.

Logout requires a valid access token. The refresh token must be unrevoked,
unexpired, and owned
by the authenticated user. Logout revokes that refresh token only, not every
session for the account.

Access tokens are validated by issuer, audience, signature, lifetime, and
valid subject and role claims. They are not checked against a server-side
revocation list. Logout, password changes, and account deletion do not
immediately invalidate already-issued access tokens; those remain valid until
their configured expiration and validation rules reject them.

## Current User API

The user ID comes from the authenticated session, not from the request body.

| Method | Route | Purpose | Success |
| --- | --- | --- | --- |
| `GET` | `/api/users/me` | Returns the current user's profile. | `200`, `UserDto`. |
| `PATCH` | `/api/users/me` | Updates the current user's email address. | `200`, token pair; `204` if unchanged. |
| `PATCH` | `/api/users/me/password` | Changes the password after verifying the current password and new password confirmation. | `200`, token pair. |
| `DELETE` | `/api/users/me` | Deletes the current account after verifying its password. | `204`. |

An actual profile change or successful password change removes all existing
refresh tokens for the account and persists a new token pair. An unchanged profile returns `204`
without rotating tokens. An incorrect current password returns `401`; password
confirmation mismatches return `400`. Missing users return `404`.

Account deletion requires the current password and is destructive. Unlike
module deletion, deleting a missing user returns `404`.

## Admin User API

All routes in this table require the `Admin` role. Use an existing admin
account; registration is not an admin-provisioning endpoint.

| Method | Route | Purpose | Success |
| --- | --- | --- | --- |
| `GET` | `/api/users` | Lists all user accounts, ordered by email and ID. | `200`, user list. |
| `PATCH` | `/api/users/{userId}/role` | Changes the target user's role. | `204`. |
| `PATCH` | `/api/users/{userId}/scopes` | Changes the target user's allowed scopes. | `204`. |
| `DELETE` | `/api/users/{userId}` | Deletes the target account without requiring its password. | `204`. |

Scopes are stored as a list of strings, not predefined permissions. The server
includes them in issued tokens, but the current controllers enforce
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
and manual cleanup, including how to store sensitive test values.
Server tests live in [tests/Server](../tests/Server).
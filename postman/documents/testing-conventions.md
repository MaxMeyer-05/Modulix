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
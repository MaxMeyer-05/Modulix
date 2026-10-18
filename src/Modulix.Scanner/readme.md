# Modulix - Scanner

CLI scanner tool for the Modulix framework.

## setup

To build the scanner Docker image used for endpoint discovery:

```powershell
docker build -t modulix-scanner:latest -f src/Modulix.Scanner/Dockerfile src/Modulix.Scanner
```

To run the scanner directly against a published module directory:

```powershell
dotnet run --project src/Modulix.Scanner -- <path-to-module-directory>
```

## changelog

### 1.0.0
- Initial release of the Modulix endpoint scanner.

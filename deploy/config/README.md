# Deployment configuration templates

Central templates for management platform `appsettings`. Canonical documentation: [management-platform-configuration.md](../../docs/03-operations/management-platform-configuration.md).

## Layout

| Path | Purpose |
|------|---------|
| [examples/appsettings.docker-dev.json.example](examples/appsettings.docker-dev.json.example) | Local dev with Docker SQL — copy to `Management.Host/appsettings.Development.json` |
| [examples/appsettings.windows.json.example](examples/appsettings.windows.json.example) | On-premises Windows deployment skeleton |
| [examples/appsettings.azure.json.example](examples/appsettings.azure.json.example) | Azure / Entra deployment skeleton |
| [ui/appsettings.json](ui/appsettings.json) | Shared UI config (linked into Ui.Server and Ui.Client) |
| [ui/appsettings.Development.json.example](ui/appsettings.Development.json.example) | Optional WASM dev overlay for split-host API URL |
| [worker/appsettings.Development.json.example](worker/appsettings.Development.json.example) | Worker dev SQL overlay (no auth bootstrap blocks) |

## Quick copy (docker dev)

```powershell
copy deploy\config\examples\appsettings.docker-dev.json.example src\Apperture.ControlPlane\Apperture.Management.Host\appsettings.Development.json
```

Set `SqlServer:Password` via User Secrets on Management.Host (see [deploy/README.md](../README.md)).

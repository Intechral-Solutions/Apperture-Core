# Deployment configuration templates

Central **startup/default templates** for management platform `appsettings` — not the operator settings control plane. See [ADR-0039](../../docs/02-decisions/ADR-0039-environment-startup-bootstrap-and-platform-settings.md) for configuration layers.

Canonical file inventory: [management-platform-configuration.md](../../docs/03-operations/management-platform-configuration.md).

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

Set `SqlServer:Password` via User Secrets on Management.Host for the **script path** (see [deploy/README.md](../README.md)). For **Aspire**, AppHost injects SQL via `ConnectionStrings:DefaultConnection`; optional password override is on the AppHost project. Both paths still use the docker-dev appsettings template for dev auth/bootstrap flags.

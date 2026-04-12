# Blazor WebAssembly + Kestrel Windows Service (POC)

Minimal **Blazor WebAssembly** client with an **ASP.NET Core** host that serves static and `_framework` assets via **Kestrel** and registers as a **Windows Service** (`Microsoft.Extensions.Hosting.WindowsServices`). This lives under `poc/` and is not part of the main product solution (`Apperture.slnx`).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (matches repo `global.json`).
- Windows, for service installation (`sc.exe`).

## Run locally (console)

From the repository root:

```powershell
dotnet run --project poc/BlazorWasmWindowsService/BlazorWasmPoc.Host/BlazorWasmPoc.Host.csproj
```

Open `http://localhost:5088` (see `Properties/launchSettings.json`). You should see **Hello, World!**

## Publish

```powershell
dotnet publish poc/BlazorWasmWindowsService/BlazorWasmPoc.Host/BlazorWasmPoc.Host.csproj -c Release -o ./poc-publish
```

Use an output folder of your choice instead of `./poc-publish` if you prefer.

### Listen address

Release defaults bind **all interfaces** on port **5088** via `appsettings.json` (`Kestrel:Endpoints:Http:Url` = `http://0.0.0.0:5088`). Adjust as needed before deploying. Local debug uses `applicationUrl` in `launchSettings.json` (`http://localhost:5088`).

## Install as a Windows Service

Run an elevated command prompt or PowerShell.

### Framework-dependent (requires .NET runtime on the machine)

Replace `PUBLISH_DIR` with your publish folder (full path), and adjust `dotnet.exe` if not in the default location.

```powershell
sc.exe create BlazorWasmPoc binPath= "\"C:\Program Files\dotnet\dotnet.exe\" \"PUBLISH_DIR\BlazorWasmPoc.Host.dll\"" start= auto
sc.exe description BlazorWasmPoc "POC: Blazor WASM behind Kestrel"
sc.exe start BlazorWasmPoc
```

### Self-contained (no shared runtime)

Publish with a runtime identifier, then point `binPath` at the generated executable:

```powershell
dotnet publish poc/BlazorWasmWindowsService/BlazorWasmPoc.Host/BlazorWasmPoc.Host.csproj -c Release -r win-x64 --self-contained -o ./poc-publish-sc
sc.exe create BlazorWasmPoc binPath= "FULL_PATH\poc-publish-sc\BlazorWasmPoc.Host.exe" start= auto
sc.exe start BlazorWasmPoc
```

### Remove the service

```powershell
sc.exe stop BlazorWasmPoc
sc.exe delete BlazorWasmPoc
```

## Notes

- The host sets `ContentRootPath` to `AppContext.BaseDirectory` so configuration and static files resolve correctly when the process is started by the Service Control Manager (working directory is not the install folder).
- Ensure Windows Firewall (and corporate policy) allow inbound traffic on the chosen port if you browse from another machine.
- Service display name defaults from the executable; the **service name** used by `sc.exe` is set in code to `BlazorWasmPoc` (`Program.cs`).

## Build the POC solution only

```powershell
dotnet build poc/BlazorWasmWindowsService/BlazorWasmWindowsService.sln
```

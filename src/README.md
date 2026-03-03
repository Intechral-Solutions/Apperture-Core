# Source

Source projects will be added in Phase 2 (solution skeleton). Planned structure:

| Folder | Projects |
|--------|----------|
| Apperture.Shared | Kernel, Security, Observability, Serialization |
| Apperture.Runtime | Abstractions, Engine, Persistence.SqlServer, DTFx, DurableFunctions |
| Apperture.ModuleSdk | Abstractions, SDK, Testing |
| Apperture.ControlPlane | Management.Api, Host, Client, Ui.Client, Ui.Server, Ui.Shared |
| Apperture.InstancePlane | Worker.Host, Worker.ModuleHost, Instance.Agent, Scheduler.Host |
| Apperture.Cloud | FunctionApp.Template, Cloud.Shared |
| Apperture.Cli | PowerShell, Cli.Core |
| Apperture.Modules | Sample.HelloWorld (dev modules) |

See [docs/00-overview/roadmap.md](../docs/00-overview/roadmap.md) and [.ai/playbooks/project-map.md](../.ai/playbooks/project-map.md).

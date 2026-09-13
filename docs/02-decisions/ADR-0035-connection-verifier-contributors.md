# ADR-0035: `IConnectionVerifierContributor` for connector-owned test-connect registration

## Status

Accepted

## Context

Connection test-connect uses `IConnectionVerifier` implementations resolved from DI. Some verifiers were registered in `AddAppertureConnectorWorkflows` (ControlPlane.Security), which coupled the security/workflow layer to specific connector assemblies (e.g. SFTP).

`IConnectorDefinitionContributor` remains metadata-only and must not register executable services.

## Decision

1. Introduce **`IConnectionVerifierContributor`** in Core.Application with `void Register(IServiceCollection services)`.
2. Each connector package calls `new XxxConnectionVerifierContributor().Register(services)` from its `AddApperture…Connector` extension, registering `IConnectionVerifier` and verifier-only dependencies (e.g. `SftpConnectorClientFactory` scoped for the verifier).
3. Remove connector-specific verifier registration from `AddAppertureConnectorWorkflows`.
4. Register **`ConnectionVerifierRegistrationSanityHostedService`** after connector definition contributors: at startup, log a warning if a definition has `SupportsConnectionVerification` but no matching `IConnectionVerifier`.
5. Management Host calls `AddAppertureCampusNexusConnector` / `AddAppertureSftpConnector` before `AddAppertureConnectorWorkflows` for readable composition order.

## Consequences

- Connector packages own both definition metadata and verification registration; ControlPlane.Security no longer references connector assemblies solely for verifiers.
- Operators and docs: see [management-service playbook](../../.ai/playbooks/management-service.md) (Connectors and connection verification).

## Related

- [Epic59-Connection-Verification-Test-Connect-and-Bind-Time-Confidence.md](./Epic59-Connection-Verification-Test-Connect-and-Bind-Time-Confidence.md)

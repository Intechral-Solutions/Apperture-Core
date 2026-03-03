# Tenancy Model

See [ADR-0002](../02-decisions/ADR-0002-single-tenant-instance.md).

## Single-tenant Instance

Each deployment is one Instance. One customer per deployment. Simplifies isolation and compliance.

## Design for future multi-tenancy

Tables and models are designed so adding `TenantId` later would be straightforward. We do not implement multi-tenancy now; we avoid patterns that would block it.

## Auditability

Everything is auditable per instance. Audit logs, operation runs, and telemetry are scoped to the Instance.

# Control-Plane Connector Workflows

This document describes the application workflows for managing the connector resource family: connections, bindings, and capability slots.

## Overview

The control plane governs connector-related resources through application services that enforce authorization at every operation. Auth remains generic; workflows consume IAuthorizationService without introducing connector-specific logic into the auth core.

## Workflows

### Connection Management

| Workflow | Service | Auth action | Notes |
|----------|---------|-------------|-------|
| List connections | IConnectionManagementService.ListConnectionsAsync | connection.read | Returns connections visible in actor scope |
| Get connection | IConnectionManagementService.GetConnectionAsync | connection.read | Instance-level check |
| Create connection | IConnectionManagementService.CreateConnectionAsync | connection.create | Validates connector definition exists in **`IConnectorDefinitionRegistry`** (not from SQL definition rows) |
| Update connection | IConnectionManagementService.UpdateConnectionAsync | connection.update | Metadata/config only |
| Retire connection | IConnectionManagementService.RetireConnectionAsync | connection.delete | Sets ConnectionState.Retired |

### Slot Inspection

| Workflow | Service | Auth action | Notes |
|----------|---------|-------------|-------|
| List slots | IModuleSlotRegistry.GetSlotsForModule | (caller responsibility) | Slots declared by module/platform |
| Get binding | IBindingService.GetBindingAsync | binding.read | Slot scope |

### Binding Workflows

| Workflow | Service | Auth action | Notes |
|----------|---------|-------------|-------|
| Get eligible connections | IBindingService.GetEligibleConnectionsAsync | binding.assign | Filters active, connector exists |
| Create binding | IBindingService.CreateBindingAsync | binding.assign | Verifies connection in eligible list |
| Revoke binding | IBindingService.DeleteBindingAsync | binding.revoke | Instance-level check |

## Authorization Enforcement Pattern

1. Resolve actor: `var actor = await _principalContext.GetCurrentActorAsync(ct)`
2. If null: throw UnauthorizedAccessException
3. Evaluate: `var allowed = await _auth.EvaluateAsync(actor, permission, resource, resourceIdentity, ct)`
4. If not allowed: throw UnauthorizedAccessException
5. Proceed with business logic and persistence

## Eligibility Rules

Binding eligibility (IBindingEligibilityService) filters connections by:
- Connection.State == Active
- Connector definition exists in registry
- (Future: contract type match, scope visibility)

## Deferred to Runtime Phase

Runtime resolution (IConnectionResolver, slot-bound resolution, secret/material pipeline) is implemented; see [Runtime Connection Resolution](runtime-connection-resolution.md) and [Runtime Step Execution](runtime-step-execution.md). The following remain deferred:

- capability.consume at resolution time
- Runtime-created connections

## Related

- [Connector Resource Family](connector-resource-family.md)
- [Authorization and Governance Foundation](authorization-and-governance-foundation.md)

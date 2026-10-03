# ADR-0046: External-organization access and withdrawal of instance federation

## Status

Accepted (2026-10-03). Recorded by the [RC1 program gate](../01-architecture/rc1-program-gate.md), decisions D-12, D-26 and D-27.

Withdraws ADR-0005, ADR-0006, ADR-0008, ADR-0009, ADR-0010 and ADR-0011. Withdraws the relationship-authorization leg of ADR-0007; its separation of human authentication from service authentication stands.

## Context

ADR-0005 to ADR-0011 and `docs/01-architecture/federated-instance-relationships.md` proposed federated instance-to-instance collaboration: parent and child relationships, Entra-backed machine trust, a constrained cross-instance management API, signed child-to-parent notifications and delegated remote administration. All seven ADRs remained Proposed. No service, table, controller or UI implements them.

The product owner judged this to be over-engineering at the current stage. Access by people from another organization can be met with ordinary user accounts.

## Decision

### Federation is withdrawn from the active roadmap

No relationship domain, cross-instance API, machine handshake or delegated capability model is planned. The federation phases in `docs/roadmap.md` are withdrawn.

### External-organization access uses ordinary user identity

In order of preference:

1. **Local guest accounts.** An ordinary local user created through the existing invitation flow, holding a custom role.
2. **Guests in the primary Entra tenant.** A directory guest linked or provisioned through the existing Entra flows. Guest access requests stay off by default per registration.
3. **An additional mapped Entra tenant.** A future option. It is not in RC1. Exactly one Entra registration is active today. No ruling is made on whether this capability is Core or premium.

### What confinement RC1 offers

A guest is confined by **capability** and by **module**, through the permissions in their role.

A guest **cannot** be confined to one Application in RC1. ResourceContext is an ownership and view concept, not an authorization boundary. Integration, connection, schedule, audit and business-data reads authorize at instance scope, and the list of Applications needs only authentication. A guest whose role holds only module-scoped grants cannot open the integration or connection lists at all.

This limit is accepted for RC1. Application-confined guest access is the concrete trigger for context-scoped authorization, whose design scope is settled in the IAM-10.0 gate and whose implementation is post-RC1.

### Dormant code

These types exist only as scaffolding and gate nothing. They are dormant, not a commitment:

- `Apperture.Core.Domain/Trust/*` (`TrustRelationship`, `RelationshipType`, `RelationshipState`, `GrantedCapability`, `RemoteEnvironment`).
- `IEnvironmentTrustService`, which has no implementation.
- `ActorKind.RemoteInstance` and `AuthorizationEvaluationPath.Relationship`.
- The `TrustRelationship`, `ExternalInstance` and `GrantedCapability` resource-type constants and the `relationship.manage` action.

The seeded `relationship.manage` permission is visible in the permission catalog and can be granted to custom roles although it gates nothing. IAM-10 removes it from the catalog. No code is changed by this ADR.

`Apperture.Core.Security.Trust` is worker runtime enrollment, not federation. It is unaffected.

## Consequences

### Positive

- The roadmap no longer carries a large, unbuilt subsystem that could shape implementation by accident.
- External access rests on identity flows that already exist and are tested.

### Negative

- One instance cannot observe or administer another. Organizations that need that must wait for a future decision.
- Guests see every Application their permissions allow until context-scoped authorization exists.

### Neutral

- The withdrawn ADRs and the architecture document are kept as history. If federation is reopened, it starts from a new ADR, not by reviving the withdrawn set.

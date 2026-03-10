# ADR-0013: Action and Scope Naming Conventions

## Status

Accepted

## Context

Permission and resource scope naming must be consistent across the platform to avoid confusion, enable registry seeding, and support policy evaluation. The Phase 1 auth design uses action strings (e.g. `connection.create`) and scope keys (e.g. `module:{moduleId}`); without frozen conventions, different parts of the system may diverge.

## Decision

### Action Naming

Use **`{resourceType}.{action}`** — lowercase, dot-separated.

- **Resource type:** PascalCase resource type name in lowercase (e.g. `connection`, `binding`, `operation`).
- **Action:** Verb in lowercase (e.g. `create`, `read`, `update`, `delete`, `execute`, `manage`, `assign`, `revoke`).

**Examples:**

- `connection.create`, `connection.read`, `connection.update`, `connection.delete`
- `binding.create`, `binding.read`, `binding.delete`, `binding.assign`, `binding.revoke`
- `operation.execute`, `operation.read`
- `role.manage`, `permission.assign`, `audit.read`, `relationship.manage`
- `module.install`, `module.configure`
- `schedule.manage`, `secret.manage`, `mapping.manage`
- `capability.register`, `capability.consume`

**Not used:** Longer forms such as `connector.connection.read`. The shorter `connection.read` is the canonical form.

### Scope Keys

Use the following formats for scope boundaries:

| Scope | Format | Example |
|-------|--------|---------|
| Instance | `instance` | `instance` |
| Module | `module:{moduleId}` | `module:mod-001` |
| Connection | `connection:{connectionId}` | `connection:conn-abc` |
| Operation | `operation:{operationId}` | `operation:op-xyz` |

- Scope keys are case-sensitive.
- IDs use the stable resource identifier (e.g. from domain entity IDs).
- For slot scopes, use `module:{moduleId}:slot:{slotName}` when a more specific scope is needed (e.g. for binding).

## Consequences

### Positive

- Consistent permission and scope naming across registry, policies, and code.
- Easy to generate and validate permission strings.
- Aligns with Phase 1 plan Section 4 action tables.

### Negative

- Any deviation must be explicitly justified and documented.

### Neutral

- `ActionConstants` and `ScopeKeyConstants` in Core.Domain.Governance implement these conventions.

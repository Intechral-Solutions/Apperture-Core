# Durable Functions Secret Boundary

## Rule

**Orchestrator sees SecretRef only; activities resolve and return non-secret outputs.**

- Durable Functions **orchestrator** state and **history** may contain only opaque references (e.g. `SecretRef` with `Value` like `kv:secret-name` or a Key Vault URI). They must **never** contain `ResolvedSecret`, `SecretMaterial`, or plaintext secret values.
- **Activity functions** resolve secret references just-in-time via `ISecretResolver`, use the secret in memory only, and return only **non-sensitive** results. Activity return values are replayed in orchestration history; therefore they must not include resolved secret material.

## Why

- Durable Functions persists orchestrator state and replays history. Any value stored in orchestrator state or returned from an activity is serialized and may be replayed. If resolved secrets or secret material were allowed, they would be written to storage and could be exposed through history or replay.
- By keeping only `SecretRef` in orchestration boundaries, we ensure that secret material is fetched at execution time in the activity and never persisted in the workflow backend.

## Implementation

| Boundary | Allowed | Not allowed |
|----------|---------|-------------|
| StartWorkflowRequest.Parameters | `SecretRef`-shaped entries (e.g. `{"apiKey": new SecretRef("kv:api-key")}`) | `ResolvedSecret`, `SecretMaterial`, plaintext secrets |
| Orchestrator state / context | Same as above | Resolved material |
| Activity input (from orchestrator) | SecretRef only | Resolved material |
| Activity return value | Non-secret data only | ResolvedSecret, secret material, or any sensitive payload |

## Activity pattern

1. Orchestrator passes `SecretRef` (e.g. in parameters or state).
2. Activity receives the reference and injects `ISecretResolver`.
3. Activity calls `await _secretResolver.ResolveAsync(secretRef, cancellationToken)`.
4. Activity uses the resolved material in-memory (e.g. call external API).
5. Activity returns only non-secret output (e.g. success flag, count, or sanitized result).

See [Secrets and Key Management](secrets-and-key-management.md) for the Azure runtime secret-resolution path (Key Vault + Managed Identity).

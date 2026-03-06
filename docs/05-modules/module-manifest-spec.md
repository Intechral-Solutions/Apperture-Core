# Module Manifest Spec

## Fields

- **Id** — Unique module identifier
- **Name** — Display name
- **Version** — Semantic version
- **Description** — Human-readable description
- **Capabilities** — List of capabilities the module provides
- **Required Permissions / Resources** — Declarations for RBAC
- **Configuration schema** — Future: JSON Schema for UI-driven config
  - Sensitive configuration values must be represented as `SecretRef`-style references.
  - Plaintext credentials must not be transported from Control Plane to Worker by default.

## Format

TBD — JSON or embedded resource. Will be specified when Module SDK is implemented.

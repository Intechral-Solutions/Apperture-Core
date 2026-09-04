# Glossary

| Term | Definition |
|------|------------|
| **Environment** | The full deployed Apperture platform boundary for a tenant or organization. One customer per Environment. Everything is auditable per environment. (Replaces the overloaded use of "platform instance" for this concept.) |
| **Runtime Plane** | The execution/data-plane subsystem within an Environment (workers, DTFx/Durable Functions, step execution). |
| **Module** | An installable capability type (catalog entry): workflow definitions, step implementations, optional connectors, optional UI. |
| **Module Installation** | A module enabled within an Environment. |
| **Module Instance** | A configured executable occurrence of a Module Installation. One module per worker process (on-prem) or Function App (cloud). Do not use the bare word "instance" ambiguously. |
| **Workflow** | A durable procedure with a step graph, persisted state, and typed parameters. Executed by the runtime. |
| **Step** | A unit of execution within a workflow. The runtime invokes steps in order. |
| **OperationRun** | Top-level record for a workflow execution, surfaced in the UI and audit. |
| **Control plane** | Management API, UI, module registry, execution requests, schedules, audit, telemetry queries. |
| **Emergency Access** | The Entra-independent recovery capability (CLI, API, posture, audit). Not a Super Admin role and not `POST /api/auth/break-glass`. |
| **Recovery Access** | The system-managed principal for Emergency Access. Canonical username `breakglass`, display name Recovery Access, role Platform Administrator. Designated via `Users.IsBreakGlassEligible`. Omitted from ordinary Governance Users administration (operational boundary, not secrecy). Signs in with local password plus native MFA or a recovery code. Must not be linked to Entra or used as a daily administrator account. Excluded from ordinary contact-email, invitation, and forgot-password flows. |
| **Contact email** | `Users.EmailAddress` after verification: the user's Apperture notification and recovery destination. Not an authentication identity, not an Entra key, and not an `IdentityLink`. |
| **Account-action token** | One-time hashed bearer for email verification, Apperture password setup, or password reset. Plaintext is shown only at issuance. |
| **Local password policy** | Canonical length, blocklist, and guessability rules for establishing a local password. Enforced server-side by `IPasswordPolicyEvaluator`; the UI shows the same semantics locally and does not require composition rules. |
| **Apperture account setup invitation** | Onboarding link that lets a provisioned local `User` choose their own password. Not a Microsoft Entra B2B invitation. |
| **Instance plane** | Worker, DTFx/Durable Functions execution, step execution. The "data plane" for workflow runs. (When referring to deployment boundary, prefer "Environment".) |

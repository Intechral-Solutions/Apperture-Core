# SDK-2 Sample.FileImport Implementation Plan

> **For agentic workers:** Execute task-by-task. Spec: `docs/superpowers/specs/SDK-2-Sample-FileImport-Demo.md` (four fixtures).

**Goal:** Portable `Sample.FileImport` module with embedded customer CSV fixtures, process-local `IImportOutcomeStore`, fail-loud import signals, and HelloWorld-parity host registration.

**Architecture:** Contracts + Module only. Workflow `Validate → Parse → ProcessRows → Complete`. Batch keyed by run id in process-local store. Complete emits exactly one signal.

**Tech Stack:** .NET 10, Module SDK abstractions, ModuleTestHarness, xUnit/FluentAssertions.

## Global Constraints

- No Persistence project, no `.Ui`, no platform itemization
- Process-local store wording; not restart-durable
- Fixtures: `customers-valid`, `customers-mixed`, `customers-invalid-structure`, `customers-all-bad`
- Notification seed only if HelloWorld-sized

## Tasks

### Task 1: Contracts + in-memory store API
- Models, enums, setting keys, `IImportOutcomeStore`, fixture id constants

### Task 2: Module — fixtures, parse/validate, handlers, `FileImportModule`
- Embedded CSVs, DI extensions, workflow/procedure/signals/settings

### Task 3: Host registration + project refs
- Management.Host, WorkerHost, UnitTests csproj

### Task 4: Notification seed (gate)
- Mirror HelloWorld; defer if heavy

### Task 5: Tests + docs
- Harness tests for all fixture paths + one-signal rule; authoring guide

### Task 6: Build/test + completion report

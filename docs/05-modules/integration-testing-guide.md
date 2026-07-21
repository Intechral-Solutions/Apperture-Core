# Integration Testing Guide

Use `Apperture.ModuleSdk.Integration.Testing` when you want to exercise a module procedure or workflow without starting the full Aspire environment.

## When to use the testing toolkit

Use the toolkit for:

- procedure and workflow behavior
- manifest signal emission
- operation-group and operation-item assertions
- retry-handler contract validation
- cancellation and deterministic host-side timestamps

Keep full Aspire or platform integration tests for:

- real Management API authorization
- SQL persistence round-trips
- module installation and lifecycle state
- schedule dispatch timing in WorkerHost
- notification delivery and UI verification

## Basic usage

```csharp
var execution = await IntegrationProcedureTestHost
    .For(new FileImportModule())
    .ExecuteProcedureAsync(
        "Sample.FileImport.procedures.import",
        new Dictionary<string, object?> { ["fixtureId"] = "customers-mixed" });

execution.Outcome.Kind.Should().Be(ProcedureOutcomeKind.Succeeded);
execution.OperationGroups.Single().Status.Should().Be(OperationGroupStatuses.CompletedWithErrors);
execution.PublishedEvents.Should().ContainSingle();
```

## Identity and execution origin

Provide a custom identity when you need stable run metadata:

```csharp
var identity = IntegrationExecutionIdentity.Create(
    moduleId: "Sample.HelloWorld",
    procedureId: "Sample.HelloWorld.procedures.greet",
    origin: IntegrationExecutionOrigin.Scheduled);
```

The toolkit records:

- run id
- integration id
- module id
- procedure id
- correlation id
- schedule id for scheduled executions

## Deterministic time

Use `TestTimeProvider` when test code needs deterministic host-side timestamps:

```csharp
var clock = new TestTimeProvider(new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero));
clock.Advance(TimeSpan.FromMinutes(5));
```

The toolkit uses this clock for captured events, operation-group defaults, operation-item defaults, and retry identities.

## Slots and dependencies

Provide slots with `WithSlot`:

```csharp
var host = IntegrationProcedureTestHost
    .For(new HelloWorldModule())
    .WithSlot("Db", new Func<object?>(() => new StubDbConnection()));
```

For modules that resolve scoped services during step execution, construct the module with an `IServiceScopeFactory` backed by a test `ServiceProvider`, then pass that module instance into `IntegrationProcedureTestHost.For(...)`.

## Retry testing

For retryable item flows:

1. execute the parent procedure
2. locate the failed retryable item
3. call `ExecuteRetryAsync`
4. assert the child execution separately from the parent run

```csharp
var retryExecution = await host.ExecuteRetryAsync(retryItem, parentGroup, "client-key");
```

The toolkit validates module retry logic and start-plan semantics. It does not replace the platform tests that cover authorization, HTTP endpoints, SQL concurrency, or durable retry reconciliation.

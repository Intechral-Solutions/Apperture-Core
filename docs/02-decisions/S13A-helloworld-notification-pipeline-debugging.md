# S13A — HelloWorld notification pipeline debugging

Status: Accepted  
Date: 2026-07-03

## Problem

Executing the HelloWorld workflow on `Sample.HelloWorld` did not produce notifications in the platform system, personal inbox, or email — even with notification flags enabled.

## Root cause

Two separate gaps can both look like “no notification”:

1. **Emit never happens (empty `PlatformEvents`)** — HelloWorld Complete previously resolved the `Db` slot first and `return null` when unbound. That was treated as step success and **skipped emit entirely**. After the S13B emit-path fix: emit `demo.completed` first; fail loudly if emit skips or `Db` is missing.
2. **Emit persists but is never evaluated (rows in `PlatformEvents`, empty `NotificationInstances`)** — Worker uses `SqlPlatformEventStore` (append only). Management Host only evaluated its own appends via `EvaluatingPlatformEventStore`. Module events sat unevaluated until the S13A poller.

**Secondary:** `HelloWorld demo completed` is a **module-scoped** built-in definition. S9 self-subscribe (`/me/notifications`) only listed **platform-scoped** definitions, so operators could not subscribe without admin subscription setup.

## Fix

1. **`PlatformEventNotificationEvaluationHostedService`** on Management Host — polls module platform events (`ModuleId != null`) with no notification instance and runs `INotificationEvaluateOnlyCoordinator.EvaluatePlatformEventAsync`.
2. **`SubscribableNotificationDefinitions`** — include module built-ins with `StaticRuleMutableSubscription` for operator email preferences.
3. **`NotificationInstanceCoordinator`** — module built-ins with no deliverable recipients create a **suppressed** instance (`noRouteConfigured`) with recipient snapshot for S10 diagnostics.
4. **HelloWorld Complete emit guarantees** — emit before `Db`; fail when `Emitted=false` or `Db` unbound; WorkerHost wires `IWorkflowExecutor` with the real `IModulePlatformEventEmitter` and rejects `NullModulePlatformEventEmitter` at startup.

## HelloWorld trigger path

1. Operator runs HelloWorld workflow on integration (procedure launch or execution start).
2. Worker executes Complete step → `context.Signals.Events.EmitAsync(..., "demo.completed", ...)` when `emitDemoPlatformEvent` is not explicitly false (default: emit).
3. Event type: `apperture.module.sample.helloworld.demo.completed`
4. Payload: `{ "message": "..." }`; envelope includes `integrationId`, `moduleId`, `runId`.
5. Management Host poller evaluates → built-in definition `module.sample.helloworld.demo.completed` matches.
6. Operator must enable email on **My notifications** for “HelloWorld demo completed” (S9) OR admin adds a subscription.
7. Outbox + dispatch when `Notifications:Delivery:Enabled=true` and SMTP configured.

## Required settings

Same as platform pipeline (see S12 / Epic 77 runbook):

- `PlatformEvents:Enabled=true` (Worker + Management Host)
- `Notifications:Evaluation/Instances/Delivery:Enabled=true`
- `Notifications:Email` + `ShellUi:ManagementHostUrl` for email
- Operator email preference enabled for HelloWorld definition

## Diagnostics

If SQL shows **no** `PlatformEvents` for `apperture.module.sample.helloworld.demo.completed` after a Greet run, the Worker Complete step never emitted (common cause: unbound `Db` slot with the pre-fix silent `return null`). After rebuild, Complete should either persist the event or fail with an explicit emit/`Db` message.

If events exist but **no** `NotificationInstances`, check the Management Host evaluation poller (S13A) and subscription/email flags.

## Differs from expected-run-missing

| | HelloWorld demo completed | Expected run missing |
|--|---------------------------|----------------------|
| Trigger | Module signal on workflow Complete | Schedule silence observation |
| Event source | Worker module emit | Management observation dual-emit |
| Definition scope | Module built-in | Platform built-in |
| Subscription | S9 self-subscribe (after S13A) | Subscriptions + attention routing |

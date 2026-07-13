# S13A — HelloWorld notification pipeline debugging

Status: Accepted  
Date: 2026-07-03

## Problem

Executing the HelloWorld workflow on `Sample.HelloWorld` did not produce notifications in the platform system, personal inbox, or email — even with notification flags enabled.

## Root cause

**Worker-emitted platform events were persisted but never evaluated on Management Host.**

- Worker uses `SqlPlatformEventStore` (append only).
- Management Host uses `EvaluatingPlatformEventStore` (append + inline notification evaluation) for schedule/observation emits only.
- Module `demo.completed` events from HelloWorld Complete step were stored in `PlatformEvents` with no linked `NotificationInstances`.

**Secondary:** `HelloWorld demo completed` is a **module-scoped** built-in definition. S9 self-subscribe (`/me/notifications`) only listed **platform-scoped** definitions, so operators could not subscribe without admin subscription setup.

## Fix

1. **`PlatformEventNotificationEvaluationHostedService`** on Management Host — polls module platform events (`ModuleId != null`) with no notification instance and runs `INotificationEvaluateOnlyCoordinator.EvaluatePlatformEventAsync`.
2. **`SubscribableNotificationDefinitions`** — include module built-ins with `StaticRuleMutableSubscription` for operator email preferences.
3. **`NotificationInstanceCoordinator`** — module built-ins with no deliverable recipients create a **suppressed** instance (`noRouteConfigured`) with recipient snapshot for S10 diagnostics.

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

- Config → Notifications → Instances / Delivery
- Instance detail: recipient snapshot shows skipped reasons if not subscribed
- S12 pipeline verification tools remain for synthetic expected-run-missing; HelloWorld uses real workflow execution

## Differs from expected-run-missing

| | HelloWorld demo completed | Expected run missing |
|--|---------------------------|----------------------|
| Trigger | Module signal on workflow Complete | Schedule silence observation |
| Event source | Worker module emit | Management observation dual-emit |
| Definition scope | Module built-in | Platform built-in |
| Subscription | S9 self-subscribe (after S13A) | Subscriptions + attention routing |

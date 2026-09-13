# Error handling convention

Management Host and Management API follow a shared reliability contract for failures.

## API responses

- **ProblemDetails** (`application/problem+json`) is the standard error surface for unhandled exceptions handled by `ManagementHostExceptionHandler`.
- **`X-Correlation-ID`** is set on every HTTP response by `CorrelationIdMiddleware`. Error payloads include a `correlationId` field when the global handler writes the body.
- **HTTP 500** responses must not expose stack traces, exception types, or other internal implementation detail in the response body.
- **Known business failures** should be explicit: return intentional `400`, `401`, `403`, `404`, `409`, etc. from controllers/services, or throw narrow domain exceptions that map to those statuses. Do not rely on the global handler for expected control flow.

## What not to do

- Do **not** broadly map `InvalidOperationException` (or other generic exceptions) to client-facing status codes in the global handler.
- Do **not** globally suppress `DbUpdateConcurrencyException` or hide business-critical concurrency conflicts.
- Do **not** swallow API failures in Blazor UI and show misleading empty states — use `ErrorState`, `PageMessage`, or section-level error affordances.

## UI

- Page and section loaders should distinguish **loading**, **empty**, **not found**, and **error** states.
- Prefer `HttpRequestException` handling with status-aware branches where `IManagementClient` surfaces HTTP failures.

## Tests

- Unit tests: `tests/Apperture.UnitTests/ManagementHost/ManagementHostExceptionHandlerTests.cs`
- API smoke (no 500 on linked routes): `tests/Apperture.IntegrationTests/ManagementHost/ManagementHostReliabilitySmokeTests.cs`

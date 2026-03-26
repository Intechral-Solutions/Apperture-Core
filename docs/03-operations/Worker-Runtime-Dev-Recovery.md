# Worker Runtime Dev Recovery

## Verify worker launch status

- API: `GET /api/control/recovery/runtime-worker/status`
- File: `artifacts/devlogs/worker-runtime-state.json`

Status values:

- `Starting`
- `Running`
- `Failed`
- `Unknown`

## Start or restart worker without full teardown

- Start only worker:
  - `scripts/dev/start-worker.ps1`
- Restart worker:
  - `scripts/dev/restart-worker.ps1`

API equivalents:

- `POST /api/control/recovery/runtime-worker/start`
- `POST /api/control/recovery/runtime-worker/start?restart=true`

## Logs

Worker launch logs are written under:

- `artifacts/devlogs/worker-host-*.out.log`
- `artifacts/devlogs/worker-host-*.err.log`

## Manual workflow testability check

1. Start dev environment (`scripts/dev/up.ps1`) or recover worker via script/API above.
2. Confirm worker status is `Running`.
3. Create/start a test integration workflow.
4. Verify run leaves `Requested` and transitions through execution states.
5. If launch fails, inspect `worker-runtime-state.json` + latest `*.err.log`.

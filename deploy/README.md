# Deploy

Deployment configuration will be added in later phases. Planned structure:

| Folder | Purpose |
|--------|---------|
| docker/ | docker-compose.dev.yml, env files, SQL init/migrations |
| k8s/ | Kubernetes manifests (future) |
| azure/ | Azure deployment config (future) |

## Root Docker Compose shim

From the **repo root** you can control all Compose-defined services with a single interface:

```bash
docker compose up -d
docker compose down
docker compose ps
```

The root [docker-compose.yml](../docker-compose.yml) uses Compose `include` to pull in nested compose files under `deploy/docker/`. Add or remove entries in that `include` list as you add more compose files (e.g. `docker-compose.dev.yml`, `docker-compose.e2e.yml`).

See [docs/00-overview/roadmap.md](../docs/00-overview/roadmap.md).

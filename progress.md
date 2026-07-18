# Phase 1 Progress Report - RAG-PLUS

**Date:** 2026-07-18  
**Status:** COMPLETE  
**Mode:** LOCAL ONLY (No Azure)

---

## Local Infrastructure

| Service | How | Connection |
|---|---|---|
| **PostgreSQL** | Local install (PG18) | `Host=localhost;Port=5432;Database=ragplus;Username=postgres;Password=123456` |
| **Redis** | Docker or local | `redis://localhost:6379` |
| **OpenAI** | API key | `OPENAI_API_KEY=sk-...` |
| **Vector DB** | TBD (Qdrant local or pgvector) | Phase 4 |

Database `ragplus` created in local PostgreSQL.

---

## What Was Done

### 1. Project Scaffolding

```
src/Scraper/     Node.js + TypeScript (BullMQ worker, Playwright/Cheerio crawlers, robots.txt parser)
src/Processor/   .NET 8 BackgroundService (Redis Pub/Sub listener)
src/Api/         ASP.NET Core 8 Web API (existing)
src/WebUi/       React + Vite + TypeScript (3 pages: Dashboard, Search, Chat)
```

### 2. Dockerfiles

```
docker/Dockerfile.scraper    node:20-alpine multi-stage
docker/Dockerfile.processor  dotnet/sdk:8.0 multi-stage (shared by API & Processor)
docker/Dockerfile.webui      node build + nginx:alpine serve
docker/nginx.conf            reverse proxy /api -> api:8080
```

### 3. CI/CD (.github/workflows/)

- **ci-scraper.yml** - npm ci → eslint → tsc --noEmit → jest → docker build
- **ci-api.yml** - dotnet restore → format → build → test → docker build (API + Processor parallel jobs)

### 4. Infra

- `infra/provision.sh` - Azure CLI script (optional, for future Azure deployment)
- `infra/main.bicep` - Azure Bicep template (optional)

### 5. Config Files

- `docker-compose.yml` - Postgres + Redis + Scraper + Processor + API + WebUI (no Qdrant, no Azure)
- `.env` - Local env vars (REDIS_URL, POSTGRES_URL, OPENAI_API_KEY)
- `.env.example` - Template for other devs
- `src/Api/appsettings.json` - PostgreSQL + Redis + OpenAI config sections
- `src/Processor/appsettings.json` - PostgreSQL + Redis connection strings

---

## How To Run Locally

```bash
# 1. Copy .env and add your OpenAI key
cp .env.example .env
# Edit .env: OPENAI_API_KEY=sk-your-real-key

# 2. Start Redis (if you don't have it locally, Docker is easiest)
docker compose up -d redis

# 3. Install deps
cd src/Scraper
npm install

cd ../WebUi
npm install

# 4. Run API
cd ../Api
dotnet run

# 5. Run Processor
cd ../Processor
dotnet run

# 6. Run Scraper
cd ../Scraper
npm run dev

# 7. Run WebUI
cd ../WebUi
npm run dev
```

Or all-in-one: `docker compose --env-file .env up -d`

---

## Next: Phase 2

- Implement `storeRawData()` in scraper worker (PostgreSQL insert)
- Add dead-letter queue logic
- Add Bottleneck rate limiting per domain
- Test horizontal scaling with multiple worker replicas

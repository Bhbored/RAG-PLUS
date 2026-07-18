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

### 4. Config Files

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

## Phase 2 Complete - Distributed Web Scraper

**Date:** 2026-07-18  
**Status:** COMPLETE  

### What Was Built

| Component | File | Purpose |
|---|---|---|
| **PostgreSQL storage** | `src/Scraper/src/db.ts` | Pool connection, `storeRawData()`, `contentHashExists()`, `storeDeadLetter()`, auto-migration |
| **Rate limiter** | `src/Scraper/src/rate-limiter.ts` | Per-domain Bottleneck instances (2s delay, 60 req/min reservoir) |
| **Full worker** | `src/Scraper/src/worker.ts` | robots.txt → rate limit → scrape → hash dedup → store → Pub/Sub notify |
| **Seed script** | `src/Scraper/src/seed.ts` | Enqueues 6 test URLs (quotes.toscrape.com + books.toscrape.com) |
| **Bulk seed** | `src/Scraper/src/seed-500.ts` | Enqueues 500 URLs across 3 domains (quotes, books, Wikipedia) |
| **DB schema** | `src/Scraper/migrations/001_create_tables.sql` | `raw_scraped_data` + `dead_letter` tables |
| **Scrapers updated** | `src/crawlers/playwright.ts`, `cheerio.ts` | Now return `html`, `httpStatus` alongside normalized data |

### Architecture

```
URL → BullMQ Queue → Worker → robots.txt check → Bottleneck rate limit
  → Playwright/Cheerio scrape → SHA-256 hash → Redis dedup cache
  → PostgreSQL INSERT (ON CONFLICT DO NOTHING) → Redis Pub/Sub "raw-data-ready"
  → .NET Processor picks up
```

### Failure Handling

| Failure | Mechanism |
|---|---|
| **Worker crash** | BullMQ stalled job recovery (30s) → another replica picks up |
| **Network timeout** | Playwright 30s timeout → BullMQ exponential backoff retry |
| **HTTP 429 / IP block** | Bottleneck per-domain throttling prevents this |
| **3 consecutive failures** | Moved to `scrape-dead-letter` Redis list + `dead_letter` PostgreSQL table |
| **Duplicate content** | SHA-256 hash → Redis cache (24h) + PostgreSQL UNIQUE constraint |

### How to Test Horizontal Scaling

```bash
# 1. Seed queue with 500 URLs
cd src/Scraper
npm run seed:500

# 2. Scale workers (from project root)
docker compose --env-file .env --profile scale up -d --scale scraper-worker=3

# 3. Watch queue drain
# Monitor: redis-cli LLEN bull:scrape-queue:waiting
#   OR check via seed script which prints queue stats
```

### Verified

- [x] 6 URLs scraped to PostgreSQL (quotes.toscrape.com x3, books.toscrape.com x3)
- [x] Content hash dedup works (UNIQUE constraint + Redis cache)
- [x] Dead-letter table created, 0 entries (all successes)
- [x] BullMQ retries + exponential backoff configured
- [x] Per-domain Bottleneck rate limiting active
- [x] Docker Compose `scraper-worker` service ready for `--scale`

---

## Phase 3 Complete - Data Processing

**Date:** 2026-07-18  
**Status:** COMPLETE  

### What Was Built

| Component | File | Purpose |
|---|---|---|
| **Models** | `Models/RawScrapedData.cs`, `CleanedData.cs`, `ScrapedContent.cs` | EF Core entities mapping to PostgreSQL tables |
| **DbContext** | `Data/AppDbContext.cs` | Npgsql EF Core context with indexes |
| **Repositories** | `Data/RawDataRepository.cs`, `CleanDataRepository.cs` | DB access with URL-based lookup + versioning |
| **HtmlCleaner** | `Services/HtmlCleaner.cs` | HtmlAgilityPack: strips boilerplate, extracts title/body/tables/links/headings/publishDate |
| **Validator** | `Validation/ScrapedContentValidator.cs` | FluentValidation: non-empty title, min body length, table validation |
| **Processor** | `Services/ScrapedDataProcessor.cs` | Full pipeline: backlog catch-up + Pub/Sub real-time listener |
| **DI wiring** | `Program.cs` | DbContext + repositories + services + auto-migration |

### Pipeline Flow (Verified)

```
Seed 6 URLs → Scraper stores 6 rows in raw_scraped_data
  → Processor backlog picks up all 6
  → HtmlCleaner extracts (title, body text, tables, links)
  → FluentValidation validates
  → Gets next version number per URL
  → Stores 6 rows in cleaned_data (JSONB structured content)
```

### Verified In DB

```
raw_scraped_data : 6 rows (quotes.toscrape.com x3, books.toscrape.com x3)
cleaned_data     : 6 rows (version 1 for each URL)
```

---

## Next: Phase 4

- Enable pgvector extension in PostgreSQL
- Create `document_chunks` table with vector(3072)
- Implement chunking service (overlap-based, 500 tokens, 50 overlap)
- Add OpenAI embedding generation (text-embedding-3-large)
- Build RAG service (embed question → vector search → build context → GPT-4o → citations)
- Wire up Semantic Kernel in the API project



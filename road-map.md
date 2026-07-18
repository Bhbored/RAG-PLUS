# RAG-PLUS Execution Plan (Local Development)

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           LOCAL / DOCKER COMPOSE                              │
├─────────────────────────────────────────────────────────────────────────────┤
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────────────────────┐   │
│  │  React + Vite│───▶│  ASP.NET Core│───▶│     PostgreSQL 18            │   │
│  │  (TypeScript)│◀───│  Web API     │◀───│  (pgvector) Raw + Cleaned   │   │
│  └──────────────┘    └──────┬───────┘    └──────────────────────────────┘   │
│                             │                                               │
│                             │            ┌──────────────────────────────┐   │
│                             │            │    pgvector / Qdrant         │   │
│                             └───────────▶│ (Vectors + Similarity)       │   │
│                                          └──────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  Node.js Scraper Workers (Playwright + BullMQ)                      │    │
│  │  ┌─────────┐  ┌─────────┐  ┌─────────┐                            │    │
│  │  │ Worker 1│  │ Worker 2│  │ Worker N│  ◄── Docker --scale         │    │
│  │  │Container│  │Container│  │Container│    (Horizontal Replicas)     │    │
│  │  └────┬────┘  └────┬────┘  └────┬────┘                            │    │
│  │       └──────────────┴─────────────┘                                │    │
│  │                      │                                              │    │
│  │               Redis 7 (BullMQ + Pub/Sub)                            │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                             │
│  External: OpenAI API (text-embedding-3-large + GPT-4o)                    │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

# Phase 1: Foundation & CI/CD ✅ COMPLETED

### 1.1 Repository Structure

```
/RAG-PLUS
├── src/
│   ├── Scraper/          # Node.js + TypeScript (BullMQ, Playwright, Cheerio, pg)
│   ├── Api/              # ASP.NET Core 8 Web API
│   ├── Processor/        # .NET 8 Worker Service (IHostedService)
│   └── WebUi/            # React + Vite + TypeScript
├── docker/               # Dockerfiles per service + nginx.conf
├── .github/workflows/    # CI/CD pipelines
├── docker-compose.yml    # Postgres + Redis + Scraper + Processor + API + WebUI
├── .env                  # REDIS_URL, POSTGRES_URL, OPENAI_API_KEY
└── progress.md           # Current state for AI agents
```

### 1.2 Local Infrastructure

| Service | How | Connection |
|---|---|---|
| **PostgreSQL 18** | Local install | `Host=localhost;Port=5432;Database=ragplus;Username=postgres;Password=123456` |
| **Redis 7** | Docker | `redis://localhost:6379` |
| **OpenAI** | API Key | `sk-...` in `.env` file |
| **Vector DB** | pgvector (Postgres extension) or Qdrant | Phase 4 |

### 1.3 CI/CD (GitHub Actions)

- **`ci-scraper.yml`** — `npm ci` → `eslint` → `tsc --noEmit` → `jest` → `docker build`
- **`ci-api.yml`** — `dotnet restore` → `dotnet format` → `dotnet build` → `dotnet test` → `docker build`

### 1.4 Local Development

`docker compose --env-file .env up -d` starts everything (see `progress.md` for details).

---

# Phase 2: Distributed Web Scraper ✅ COMPLETED

### 2.1 Core Stack

| Component | Technology | Status |
|---|---|---|
| Browser | **Playwright** (headless) | ✅ |
| Static Parsing | **Cheerio** | ✅ |
| Queue | **BullMQ** on Redis 7 | ✅ |
| robots.txt | **`robots-parser`** | ✅ |
| Rate Limiting | **Bottleneck** per-domain | ✅ |
| Deduplication | **SHA-256** hash + Redis cache + PostgreSQL UNIQUE | ✅ |
| Raw Storage | **pg** (node-postgres) → `raw_scraped_data` | ✅ |
| Dead Letter | `scrape-dead-letter` Redis list + `dead_letter` PostgreSQL table | ✅ |

### 2.2 Worker Flow

```
URL → BullMQ "scrape-queue" → robots.txt check → Bottleneck rate limit
  → Playwright / Cheerio scrape → SHA-256 hash
  → Redis dedup cache (24h TTL) → PostgreSQL INSERT (ON CONFLICT DO NOTHING)
  → Redis Pub/Sub "raw-data-ready" → .NET Processor picks up
```

### 2.3 Failure Handling

| Failure | Handling |
|---|---|
| **Worker crash** | BullMQ stalled recovery (30s), another replica picks up |
| **Network timeout** | Playwright 30s → BullMQ exponential backoff |
| **3 consecutive failures** | → Redis `scrape-dead-letter` + PostgreSQL `dead_letter` table |
| **Duplicate content** | SHA-256 hash → Redis cache + PostgreSQL UNIQUE constraint |

### 2.4 Horizontal Scaling

```bash
# Scale from 1 to 3 workers mid-run:
docker compose --env-file .env --profile scale up -d --scale scraper-worker=3

# Queue drains ~3x faster
```

### 2.5 Seed Scripts

- `npm run seed` — 6 test URLs (quotes.toscrape.com + books.toscrape.com)
- `npm run seed:500` — 500 URLs across 3 domains (quotes, books, Wikipedia)

---

# Phase 3: Data Processing ✅ COMPLETED

See `progress.md` for implementation details. Pipeline: raw data → HtmlAgilityPack cleaning → FluentValidation → versioned storage in cleaned_data (JSONB).

---

# Phase 4: RAG Pipeline ✅ COMPLETED

### 4.1 Chunking: Overlap-Based (500 tokens / 50 overlap, paragraph boundaries)

### 4.2 Vector Storage: pgvector in PostgreSQL

`document_chunks` table with `vector(3072)` column + ivfflat index for cosine similarity search.

### 4.3 OpenAI Integration

Direct API calls for `text-embedding-3-large` (embeddings) and `gpt-4o` (chat completion). RAG pipeline: embed question → pgvector cosine search → context → GPT-4o → cited answer.

### 4.4 RAG Endpoint

`POST /api/rag/ask { question }` → `{ answer, citations[] }`

---

# Phase 5: API & Web UI ✅ COMPLETED

### API Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `/api/stats` | Dashboard stats + Redis queue depth |
| `GET` | `/api/data/raw` | Paginated raw scraped data |
| `GET` | `/api/data/cleaned` | Paginated cleaned data |
| `GET` | `/api/search?q=&type=keyword` | Keyword search with snippets |
| `POST` | `/api/rag/ask` | RAG Q&A with citations |

### React UI

3 pages fully built: Dashboard (live stats, 5s refresh), Search (keyword, results with source links), Chat (GPT-style, citations, suggestions).

---

# Phase 6: Target Websites & Documentation ✅ COMPLETED

| Site Type | Target | Technology | Compliance |
|---|---|---|---|
| **Static HTML** | `quotes.toscrape.com` | Cheerio | `robots.txt` allows all. 2s crawl delay. |
| **Static (more pages)** | `books.toscrape.com` | Cheerio | `robots.txt` allows all. |
| **Pagination (500+ pages)** | `quotes.toscrape.com/tag/*`, Wikipedia | Cheerio | Wikipedia: use `Crawl-delay`, scrape off-peak. |

See `docs/` directory for architecture diagrams, sequence diagrams, ethics report, and video script.

---

# Deliverables Checklist

| Requirement | Status |
|---|---|
| **3 websites** | ✅ quotes.toscrape.com, books.toscrape.com, Wikipedia (in seed-500) |
| **robots.txt respect** | ✅ `robots-parser` + per-domain rate limiting |
| **Git + CI** | ✅ GitHub Actions (lint + test + docker build) |
| **Containerization** | ✅ Docker per service + docker-compose |
| **Static + JS rendering** | ✅ Cheerio (static) + Playwright (JS) |
| **Parse HTML** | ✅ Cheerio/Playwright + HtmlAgilityPack in .NET |
| **Rate limiting + backoff** | ✅ Bottleneck per-domain + BullMQ exponential backoff |
| **Deduplication + incremental** | ✅ SHA-256 + Redis cache + PostgreSQL UNIQUE |
| **Distributed workers** | ✅ BullMQ on Redis. Multiple Docker replicas via `--scale` |
| **Horizontal scaling** | ✅ 1→3 workers. Queue drains ~3x faster |
| **Raw data DB** | ✅ PostgreSQL `raw_scraped_data` |
| **Worker crash recovery** | ✅ BullMQ stalled recovery + 3 retries |
| **Dead-letter** | ✅ `scrape-dead-letter` + `dead_letter` table |
| **Strip boilerplate** | ✅ HtmlCleaner (HtmlAgilityPack) |
| **Structured format + validation** | ✅ FluentValidation |
| **Versioning** | ✅ CleanedData.Version |
| **Deliberate chunking** | ✅ Overlap-based 500/50 |
| **Vector DB** | ✅ pgvector in PostgreSQL |
| **LLM** | ✅ OpenAI GPT-4o via API key |
| **Multi-source synthesis** | ✅ RAG prompt with citations |
| **API endpoints** | ✅ Stats, Raw, Cleaned, Search, RAG QA |
| **React UI** | ✅ Dashboard, Search, Chat |

---

# Execution Timeline

| Days | Phase | Status |
|---|---|---|
| 1-2 | Foundation, CI/CD, Docker Compose | ✅ DONE |
| 3-5 | Scraper (Playwright, BullMQ, rate limit, dedup, DLQ) | ✅ DONE |
| 6-8 | .NET Processor (EF Core, cleaning, validation, versioning) | ✅ DONE |
| 9-11 | RAG Pipeline (pgvector, OpenAI, chunking, citations) | ✅ DONE |
| 12-14 | API endpoints + React UI | ✅ DONE |
| 15-17 | Test on 3 sites, scaling demo, fault injection | ✅ DONE |
| 18-20 | Report, diagrams, recording | ✅ DONE |

---

# Key Decisions

| Decision | Chosen | Rejected |
|---|---|---|
| **Cloud Platform** | None (local Docker) | Azure/AWS/GCP |
| **LLM/Embeddings** | OpenAI API (direct) | Azure OpenAI Service |
| **Vector Search** | pgvector (PostgreSQL) | Azure AI Search / Pinecone |
| **Queue** | BullMQ + Redis 7 (Docker) | Azure Service Bus |
| **Compute** | Docker Compose + `--scale` | Kubernetes / Azure Container Apps |
| **Database** | PostgreSQL 18 (local) | Managed PostgreSQL |
| **.NET SDK** | Semantic Kernel + OpenAI SDK | Azure.AI.OpenAI SDK |

---

**Bottom line:** Everything runs locally. No Azure account needed. Only external dependency is an OpenAI API key (in `.env`). Docker Compose spins up the entire stack with one command.

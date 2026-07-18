# RAG-PLUS

**Distributed Web Scraper + RAG Question-Answering System**

A full-stack application that crawls websites, processes scraped HTML into structured data, generates vector embeddings, and answers questions using Retrieval-Augmented Generation (RAG) with GPT-4o.

---

## Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                       DOCKER COMPOSE                              │
│                                                                   │
│  React UI ◄──► ASP.NET Core API ◄──► PostgreSQL (pgvector)        │
│                  │                    │                            │
│                  │                    ├── raw_scraped_data         │
│                  │                    ├── cleaned_data (JSONB)     │
│                  │                    ├── document_chunks (vector) │
│                  │                    └── dead_letter              │
│                  │                                               │
│  Node.js Scraper Workers ◄──► Redis (BullMQ + Pub/Sub)            │
│    ├── Playwright (JS rendering)                                  │
│    ├── Cheerio (static HTML)                                      │
│    ├── robots-parser (compliance)                                 │
│    └── Bottleneck (rate limiting)                                 │
│                                                                   │
│  .NET Processor                                                   │
│    └── HtmlAgilityPack → FluentValidation → Chunk → Embed → Store │
│                                                                   │
│  External: OpenAI API (text-embedding-3-large + GPT-4o)           │
└──────────────────────────────────────────────────────────────────┘
```

---

## Features

| Category | Detail |
|---|---|
| **Web Scraping** | Static (Cheerio) + JS-rendered (Playwright), robots.txt compliance, per-domain rate limiting |
| **Distributed Workers** | BullMQ on Redis, horizontal scaling via `docker compose --scale` |
| **Data Processing** | HtmlAgilityPack boilerplate removal, FluentValidation, versioned storage |
| **Chunking** | Overlap-based (500 tokens / 50 overlap), paragraph boundary respect |
| **Vector Search** | pgvector cosine similarity on 3072-dim embeddings |
| **RAG** | OpenAI `text-embedding-3-large` + `gpt-4o`, cited multi-source answers |
| **Dashboard** | Real-time stats, queue depth from Redis, auto-refresh |
| **Fault Tolerance** | Dead-letter queue (3 retries), stalled job recovery (30s), content hash dedup |

---

## Tech Stack

| Layer | Technology |
|---|---|
| **Scraper** | Node.js, TypeScript, BullMQ, Playwright, Cheerio, Bottleneck, ioredis |
| **Processor** | .NET 8 Worker Service, EF Core, Npgsql, HtmlAgilityPack, FluentValidation |
| **API** | ASP.NET Core 8, EF Core, Npgsql, pgvector, StackExchange.Redis |
| **UI** | React 18, TypeScript, Vite 5, react-router-dom |
| **Database** | PostgreSQL 18 + pgvector |
| **Cache/Queue** | Redis 7 (BullMQ job queue, Pub/Sub, dedup cache) |
| **AI** | OpenAI API (embeddings + chat completion) |
| **Infra** | Docker, Docker Compose, GitHub Actions |

---

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [PostgreSQL 18](https://www.postgresql.org/download/) with `pgvector` extension
- [Node.js 20+](https://nodejs.org/) (for local dev without Docker)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for local dev)
- OpenAI API key ([platform.openai.com/api-keys](https://platform.openai.com/api-keys))

---

## Quick Start

### 1. Clone & Configure

```bash
git clone <repo-url>
cd RAG-PLUS

# Copy env template and add your OpenAI key
cp .env.example .env
# Edit .env: OPENAI_API_KEY=sk-your-real-key
```

### 2. Create Database

```sql
-- Connect to your local PostgreSQL and run:
CREATE DATABASE ragplus;
\c ragplus
CREATE EXTENSION IF NOT EXISTS vector;
```

### 3. Start Everything

```bash
docker compose --env-file .env up -d
```

This starts:
- **PostgreSQL** (port 5433)
- **Redis** (port 6379)
- **Scraper worker** (BullMQ consumer)
- **Processor** (.NET background service)
- **API** (port 8080)
- **Web UI** (port 3000)

### 4. Seed Test Data

```bash
cd src/Scraper
npm install
npm run seed        # 6 test URLs
# or
npm run seed:500    # 500 URLs for scale testing
```

### 5. Open the App

| URL | Page |
|---|---|
| http://localhost:3000 | Dashboard (stats, queue depth) |
| http://localhost:3000/search | Keyword search |
| http://localhost:3000/chat | RAG Chat |
| http://localhost:8080/swagger | API documentation |

---

## API Endpoints

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/stats` | Dashboard stats (counts, queue depth from Redis) |
| `GET` | `/api/data/raw?url=&page=&pageSize=` | Paginated raw scraped data |
| `GET` | `/api/data/cleaned?domain=&version=` | Paginated cleaned data |
| `GET` | `/api/search?q=&type=keyword` | Keyword search across indexed chunks |
| `POST` | `/api/rag/ask` | RAG Q&A with citations |

### RAG Ask Example

```bash
curl -X POST http://localhost:8080/api/rag/ask \
  -H "Content-Type: application/json" \
  -d '{"question": "What quotes about love are in the data?"}'
```

Response:
```json
{
  "answer": "Some quotes about love include:\n\n1. \"It is better to be hated...\" [Source: https://quotes.toscrape.com/]",
  "citations": [
    { "url": "https://quotes.toscrape.com/", "title": "quotes.toscrape.com/", "excerpt": "..." }
  ]
}
```

---

## How It Works

### Pipeline Flow

```
URL
 │
 ├──► BullMQ Queue ──► Scraper Worker
 │                      ├── robots.txt check
 │                      ├── Rate limit (Bottleneck)
 │                      ├── Scrape (Cheerio/Playwright)
 │                      ├── SHA-256 dedup (Redis + PostgreSQL)
 │                      └── Store in raw_scraped_data
 │                           │
 │                           ▼ Redis Pub/Sub "raw-data-ready"
 │
 ├──► .NET Processor
 │    ├── HtmlAgilityPack (strip boilerplate)
 │    ├── FluentValidation
 │    ├── Versioning (append-only)
 │    ├── Store in cleaned_data (JSONB)
 │    ├── Chunk (500 tokens / 50 overlap)
 │    ├── Embed (text-embedding-3-large)
 │    └── Store in document_chunks (pgvector)
 │
 └──► RAG Query
      ├── Embed question
      ├── Cosine similarity search (pgvector)
      ├── Build context (top-8 chunks)
      ├── GPT-4o synthesis
      └── Return answer + citations
```

### Failure Handling

| Failure | Mechanism |
|---|---|
| Worker crash | BullMQ stalled recovery (30s), another replica picks up |
| Network timeout | Playwright 30s → BullMQ exponential backoff |
| 3 consecutive failures | → Redis `scrape-dead-letter` + PostgreSQL `dead_letter` |
| robots.txt block | `UnrecoverableError` — job never retried |
| Duplicate content | SHA-256 hash → skip (Redis 24h cache + PostgreSQL UNIQUE) |
| Validation failure | Logged, skipped, no cleaned entry created |

---

## Horizontal Scaling

Scale scraper workers on demand:

```bash
# Start 3 additional workers
docker compose --env-file .env --profile scale up -d --scale scraper-worker=3

# Queue drains ~3x faster
```

---

## Project Structure

```
RAG-PLUS/
├── src/
│   ├── Scraper/                  # Node.js + TypeScript
│   │   ├── src/
│   │   │   ├── worker.ts         # BullMQ consumer
│   │   │   ├── seed.ts           # 6-URL test seed
│   │   │   ├── seed-500.ts       # 500-URL scale seed
│   │   │   ├── db.ts             # PostgreSQL storage
│   │   │   ├── rate-limiter.ts   # Bottleneck per-domain
│   │   │   ├── robots.ts         # robots.txt parser
│   │   │   └── crawlers/
│   │   │       ├── cheerio.ts    # Static HTML scraper
│   │   │       └── playwright.ts # JS-rendered scraper
│   │   ├── migrations/           # SQL schema
│   │   ├── package.json
│   │   └── tsconfig.json
│   │
│   ├── Processor/                # .NET 8 Background Service
│   │   ├── Models/               # RawScrapedData, CleanedData, DocumentChunk, ScrapedContent
│   │   ├── Data/                 # AppDbContext, repositories
│   │   ├── Services/             # ScrapedDataProcessor, HtmlCleaner, ChunkingService, EmbeddingService
│   │   ├── Validation/           # FluentValidation
│   │   ├── Program.cs
│   │   └── Processor.csproj
│   │
│   ├── Api/                      # ASP.NET Core 8 Web API
│   │   ├── Controllers/          # RagController, DataController, SearchController, StatsController
│   │   ├── Services/             # RagService, EmbeddingService
│   │   ├── Models/               # DocumentChunk, RawScrapedData, CleanedData
│   │   ├── Data/                 # ApiDbContext
│   │   ├── Program.cs
│   │   └── Api.csproj
│   │
│   └── WebUi/                    # React + Vite + TypeScript
│       └── src/
│           ├── pages/
│           │   ├── Dashboard.tsx # Live stats, queue depth
│           │   ├── Search.tsx    # Keyword search
│           │   └── Chat.tsx      # RAG chat interface
│           ├── App.tsx
│           └── index.css
│
├── docker/                       # Dockerfiles + nginx config
├── docs/                         # Architecture, ethics, video script
├── .github/workflows/            # CI/CD pipelines
├── docker-compose.yml
├── .env.example
├── road-map.md                   # Full execution plan
└── progress.md                   # Current implementation status
```

---

## Environment Variables

| Variable | Required | Default | Description |
|---|---|---|---|
| `OPENAI_API_KEY` | Yes | — | OpenAI API key for embeddings + GPT-4o |
| `REDIS_URL` | No | `redis://localhost:6379` | Redis connection string |
| `POSTGRES_URL` | No | `postgresql://postgres:123456@localhost:5432/ragplus` | PostgreSQL connection |

---

## CI/CD

GitHub Actions workflows in `.github/workflows/`:

| Workflow | Triggers | Steps |
|---|---|---|
| `ci-scraper.yml` | `src/Scraper/**` changes | npm ci → eslint → tsc → jest → docker build |
| `ci-api.yml` | `src/Api/**` or `src/Processor/**` changes | dotnet restore → format → build → test → docker build |

---

## Documentation

| File | Content |
|---|---|
| `docs/architecture.md` | System architecture diagram, sequence diagram, failure paths, component map |
| `docs/ethics-compliance.md` | robots.txt compliance, rate limiting, data privacy, incremental crawling |
| `docs/video-script.md` | 5-10 minute demo walkthrough script |
| `road-map.md` | Full execution plan with deliverables checklist |
| `progress.md` | Current implementation status for AI agents |

---

## Target Websites

| Site | Type | Technology | Compliance |
|---|---|---|---|
| [quotes.toscrape.com](https://quotes.toscrape.com) | Static HTML | Cheerio | robots.txt allows all |
| [books.toscrape.com](https://books.toscrape.com) | Static HTML | Cheerio | robots.txt allows all |
| [Wikipedia](https://en.wikipedia.org) | Static HTML | Cheerio | Crawl-delay respected |

---

## License

MIT

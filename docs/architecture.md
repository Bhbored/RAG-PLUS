# RAG-PLUS Architecture & Diagrams

## System Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           DOCKER COMPOSE (Local)                              │
│                                                                             │
│  ┌──────────────────┐        ┌──────────────────┐                           │
│  │   React + Vite   │        │  ASP.NET Core 8  │                           │
│  │   (TypeScript)    │◄──────│   Web API         │                           │
│  │   Port 3000       │        │   Port 8080       │                           │
│  └──────────────────┘        └────────┬──────────┘                           │
│        │        ▲                     │         ▲                            │
│        │        │                     │         │                            │
│        ▼        │                     ▼         │                            │
│  ┌────────────────────────┐  ┌──────────────────────────┐                   │
│  │    PostgreSQL 18       │  │    Redis 7 (Docker)      │                   │
│  │  ┌──────────────────┐  │  │  ┌────────────────────┐  │                   │
│  │  │ raw_scraped_data │  │  │  │ BullMQ Scrape Q    │  │                   │
│  │  ├──────────────────┤  │  │  │ Pub/Sub: raw-ready │  │                   │
│  │  │ cleaned_data     │  │  │  │ Dedup Hash Cache   │  │                   │
│  │  ├──────────────────┤  │  │  │ Dead-Letter Queue  │  │                   │
│  │  │ document_chunks  │  │  │  └────────────────────┘  │                   │
│  │  │  (pgvector)      │  │  └──────────────────────────┘                   │
│  │  ├──────────────────┤  │                                                 │
│  │  │ dead_letter      │  │                                                 │
│  │  └──────────────────┘  │  ┌──────────────────────────┐                   │
│  └────────────────────────┘  │  Node.js Scraper Workers │                   │
│                              │  ┌────────┐ ┌────────┐   │                   │
│                              │  │Worker 1│ │Worker N│   │                   │
│                              │  │Playwr.│ │Cheerio │   │                   │
│                              │  │Bottlenc│ │Bottlenc│   │                   │
│                              │  └────────┘ └────────┘   │                   │
│                              │     (Docker --scale)     │                   │
│                              └──────────────────────────┘                   │
│                                                                             │
│  ┌──────────────────────────────────────────────────────────────────┐      │
│  │                    .NET 8 Processor (BackgroundService)          │      │
│  │  ┌──────────────┐ ┌───────────┐ ┌────────────┐ ┌─────────────┐ │      │
│  │  │ HtmlCleaner  │→│ Validator │→│ Versioning │→│ Chunk+Embed │ │      │
│  │  │ (AgilityPack)│ │(FluentVal)│ │            │ │ (pgvector)  │ │      │
│  │  └──────────────┘ └───────────┘ └────────────┘ └─────────────┘ │      │
│  └──────────────────────────────────────────────────────────────────┘      │
│                                                                             │
│  External: OpenAI API (text-embedding-3-large + GPT-4o)                    │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Sequence Diagram: Full Pipeline Flow

```
User/API         BullMQ         Scraper        Redis     PostgreSQL    Processor    OpenAI
  │                │              │              │          │             │           │
  │ seed URLs      │              │              │          │             │           │
  │───────────────►│              │              │          │             │           │
  │                │  job added   │              │          │             │           │
  │                │─────────────►│              │          │             │           │
  │                │              │ robots.txt   │          │             │           │
  │                │              │─────────────►│          │             │           │
  │                │              │◄─────────────│          │             │           │
  │                │              │ rate limit   │          │             │           │
  │                │              │ (Bottleneck) │          │             │           │
  │                │              │              │          │             │           │
  │                │              │ scrape URL   │          │             │           │
  │                │              │──────────────┼─────────►│             │           │
  │                │              │◄─────────────┼──────────│             │           │
  │                │              │              │          │             │           │
  │                │              │ SHA-256 hash │          │             │           │
  │                │              │─────────────►│          │             │           │
  │                │              │◄──dedup test─│          │             │           │
  │                │              │              │          │             │           │
  │                │              │ store raw    │          │             │           │
  │                │              │──────────────┼─────────►│             │           │
  │                │              │◄─────────────rawId──────│             │           │
  │                │              │              │          │             │           │
  │                │              │ cache hash   │          │             │           │
  │                │              │─────────────►│          │             │           │
  │                │              │              │          │             │           │
  │                │              │ Pub/Sub      │          │             │           │
  │                │              │ "raw-ready"  │          │             │           │
  │                │              │─────────────►│─────────────────────►│           │
  │                │              │              │          │             │           │
  │                │              │              │          │             │ fetch raw │
  │                │              │              │          │◄────────────│           │
  │                │              │              │          │─────────────►           │
  │                │              │              │          │             │           │
  │                │              │              │          │             │ HtmlAgPack│
  │                │              │              │          │             │──extract──►
  │                │              │              │          │             │◄──────────│
  │                │              │              │          │             │           │
  │                │              │              │          │             │ FluentVal │
  │                │              │              │          │             │──validate─►
  │                │              │              │          │             │◄──────────│
  │                │              │              │          │             │           │
  │                │              │              │          │             │ version+1 │
  │                │              │              │          │◄────────────│ store     │
  │                │              │              │          │─────────────►           │
  │                │              │              │          │             │           │
  │                │              │              │          │             │ chunk     │
  │                │              │              │          │             │ (500/50)  │
  │                │              │              │          │             │           │
  │                │              │              │          │             │ embed     │
  │                │              │              │          │             │──────────►│
  │                │              │              │          │             │◄─float[]──│
  │                │              │              │          │             │           │
  │                │              │              │          │             │ store vec │
  │                │              │              │          │◄────────────│ pgvector  │
  │                │              │              │          │─────────────►           │
  │                │              │              │          │             │           │
  │   RAG query    │              │              │          │             │           │
  │───────────────►│              │              │          │             │           │
  │                │              │              │          │             │           │
  │                │              │              │    embed question    │           │
  │                │              │              │          │◄────────── │──────────►│
  │                │              │              │          │───────────►│◄─float[]──│
  │                │              │              │          │             │           │
  │                │              │              │    cosine search     │           │
  │                │              │              │          │◄────────── │           │
  │                │              │              │          │──top-8────→│           │
  │                │              │              │          │             │           │
  │                │              │              │          │             │ GPT-4o    │
  │                │              │              │          │             │──────────►│
  │                │              │              │          │             │◄─answer──│
  │                │              │              │          │             │           │
  │◄─answer+cites─│              │              │          │             │           │
  │                │              │              │          │             │           │
```

## Failure Paths

```
Job fails     → BullMQ retry (exponential backoff, max 3)
Attempt 1/3   → retry after 2s
Attempt 2/3   → retry after 4s
Attempt 3/3   → retry after 8s
Attempt 4     → move to dead-letter:
                  ┌─ Redis: scrape-dead-letter list
                  └─ PostgreSQL: dead_letter table

Worker crash → BullMQ stalled recovery (30s)
                 Another worker picks up job

Duplicate    → SHA-256 hash check:
                  ┌─ Redis cache (24h TTL): skip instantly
                  └─ PostgreSQL UNIQUE constraint: DB-level safety

Validation   → FluentValidation fails
fail           → logged, skipped, no cleaned entry created
```

## Component Map

| Component | Tech | File(s) |
|---|---|---|
| **Web UI** | React 18 + Vite 5 + TypeScript | `src/WebUi/` |
| **REST API** | ASP.NET Core 8 | `src/Api/` |
| **RAG Service** | OpenAI API (GPT-4o + embeddings) | `src/Api/Services/RagService.cs` |
| **Processor** | .NET 8 Worker | `src/Processor/` |
| **Scraper** | Node.js + BullMQ + Playwright + Cheerio | `src/Scraper/` |
| **Queue** | BullMQ on Redis 7 | Docker |
| **Cache** | Redis 7 (dedup hashes, queue) | Docker |
| **Database** | PostgreSQL 18 + pgvector | Local |
| **Infra** | Docker Compose | `docker-compose.yml` |

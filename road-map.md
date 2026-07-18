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

# Phase 3: Data Processing (.NET 8 Background Service)

### 3.1 Service: `ScrapedDataProcessor` (IHostedService)

Runs continuously in its own container, separate from the API.

```csharp
// Processor/Services/ScrapedDataProcessor.cs
public class ScrapedDataProcessor : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _redis.SubscribeAsync("raw-data-ready", async (channel, message) =>
        {
            var data = JsonSerializer.Deserialize<RawDataNotification>(message);
            await ProcessAndIndexAsync(data.Url, data.Id);
        });
    }

    private async Task ProcessAndIndexAsync(string url, Guid rawId)
    {
        var raw = await _rawRepo.GetByIdAsync(rawId);          // Fetch from PostgreSQL
        var doc = new HtmlDocument();
        doc.LoadHtml(raw.Html);
        var structured = cleaner.Extract(url, doc);            // HtmlAgilityPack
        var validator = new ScrapedContentValidator();
        validator.ValidateAndThrow(structured);                // FluentValidation
        var version = await _cleanRepo.GetNextVersionAsync(url);
        await _cleanRepo.InsertAsync(cleaned);                 // Append, don't overwrite
    }
}
```

### 3.2 Data Schema (PostgreSQL with EF Core)

```csharp
public class RawScrapedData
{
    public Guid Id { get; set; }
    public string Url { get; set; }
    public string Domain { get; set; }
    public string RawHtml { get; set; }
    public string ContentHash { get; set; }
    public int HttpStatus { get; set; }
    public DateTime ScrapedAt { get; set; }
    public string WorkerId { get; set; }
}

public class CleanedData
{
    public Guid Id { get; set; }
    public string Url { get; set; }
    public string Title { get; set; }
    public JsonDocument StructuredContent { get; set; }  // JSONB
    public int Version { get; set; }
    public DateTime ProcessedAt { get; set; }
    public List<DataChunk> Chunks { get; set; }
}
```

### 3.3 Tasks

- [ ] Implement `ScrapedDataProcessor.cs` - fetch from PostgreSQL, strip boilerplate
- [ ] Create `HtmlCleaner` service (extract title, body, tables, links)
- [ ] Add FluentValidation schema validation
- [ ] Add versioning (append-only, `CleanedData.Version`)
- [ ] Wire up EF Core DbContext + migrations

---

# Phase 4: RAG Pipeline (.NET 8 + OpenAI + pgvector)

### 4.1 Chunking Strategy: Overlap-Based with Semantic Boundaries

```csharp
public List<DataChunk> ChunkContent(string text, string url)
{
    const int chunkSize = 500;    // tokens (~2000 chars)
    const int overlap = 50;       // tokens (~200 chars)

    var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
    // Build chunks respecting paragraph boundaries with overlap
    // PRO: Context preserved, citations accurate
    // CON: ~10% storage overhead
}
```

### 4.2 Vector Storage: pgvector (PostgreSQL)

Use the `pgvector` extension already installed in PostgreSQL:

```sql
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE document_chunks (
    id UUID PRIMARY KEY,
    content TEXT NOT NULL,
    content_vector vector(3072),  -- text-embedding-3-large
    source_url TEXT NOT NULL,
    chunk_index INTEGER
);

CREATE INDEX ON document_chunks USING ivfflat (content_vector vector_cosine_ops);
```

### 4.3 OpenAI Integration (Semantic Kernel)

```csharp
// Program.cs - Uses OpenAI API directly (not Azure OpenAI)
builder.Services.AddOpenAIChatCompletion(
    modelId: "gpt-4o",
    apiKey: builder.Configuration["OpenAI:ApiKey"]);

builder.Services.AddOpenAITextEmbeddingGeneration(
    modelId: "text-embedding-3-large",
    apiKey: builder.Configuration["OpenAI:ApiKey"]);

// RAG Service
public async Task<RagResponse> AskAsync(string question)
{
    // 1. Generate question embedding
    var embedding = await _embeddingService.GenerateEmbeddingAsync(question);

    // 2. Vector similarity search via pgvector
    var chunks = await _db.DocumentChunks
        .OrderBy(c => c.ContentVector.CosineDistance(embedding))
        .Take(8)
        .ToListAsync();

    // 3. Build context with source citations
    // 4. Prompt engineering → GPT-4o → parse citations → return
}
```

### 4.4 Why pgvector (not Azure AI Search)

| Feature | pgvector | Azure AI Search |
|---|---|---|
| **Cost** | Free (local Postgres) | ~$0.10/hr |
| **Setup** | Already installed | Requires Azure account |
| **Vector search** | Cosine/L2/Inner Product | HNSW |
| **Hybrid search** | Manual (pg_trgm + vector) | Built-in |
| **.NET** | EF Core + Npgsql | Separate SDK |

---

# Phase 5: API & Web UI

### 5.1 ASP.NET Core API Endpoints

```csharp
app.MapGet("/api/raw-data", async (string url, int? page, IRawDataRepository repo) =>
    await repo.GetByUrlAsync(url, page));

app.MapGet("/api/processed-data", async (string? domain, int? version, ICleanDataRepository repo) =>
    await repo.QueryAsync(domain, version));

app.MapPost("/api/rag/ask", async (AskRequest req, IRagService rag) =>
    await rag.AskAsync(req.Question));
```

### 5.2 React UI (TypeScript + Vite)

**Pages already scaffolded:**
1. **Dashboard** — Live queue depth, worker count, crawl stats
2. **Search** — Keyword / Semantic / Hybrid toggle (Phase 5)
3. **RAG Chat** — ChatGPT-style with clickable citation chips

---

# Phase 6: Target Websites & Compliance

| Site Type | Target | Technology | Compliance |
|---|---|---|---|
| **Static HTML** | `quotes.toscrape.com` | Cheerio | `robots.txt` allows all. 2s crawl delay. |
| **Static (more pages)** | `books.toscrape.com` | Cheerio | `robots.txt` allows all. |
| **Pagination (500+ pages)** | `quotes.toscrape.com/tag/*`, Wikipedia | Cheerio | Wikipedia: use `Crawl-delay`, scrape off-peak. |

### Ethics/Compliance Note:

> Before crawling, the system fetches and parses `robots.txt` using `robots-parser`. If a URL is disallowed or no `User-agent: *` permits access, the job is rejected with `UnrecoverableError`. Per-domain rate limiting enforces a minimum 2-second delay. The scraper identifies as `RagScraperBot/1.0`. No personal data is extracted. Incremental crawling via content hashing minimizes unnecessary server load.

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
| **Strip boilerplate** | ☐ HtmlCleaner service (Phase 3) |
| **Structured format + validation** | ☐ FluentValidation (Phase 3) |
| **Versioning** | ☐ CleanedData.Version (Phase 3) |
| **Deliberate chunking** | ☐ Overlap-based 500/50 (Phase 4) |
| **Vector DB** | ☐ pgvector in PostgreSQL (Phase 4) |
| **LLM** | ☐ OpenAI GPT-4o via API key (Phase 4) |
| **Multi-source synthesis** | ☐ RAG prompt with citations (Phase 4) |
| **API endpoints** | ☐ Raw, Processed, RAG QA (Phase 5) |
| **React UI** | ☐ Dashboard, Search, Chat (Phase 5) |

---

# Execution Timeline

| Days | Phase | Status |
|---|---|---|
| 1-2 | Foundation, CI/CD, Docker Compose | ✅ DONE |
| 3-5 | Scraper (Playwright, BullMQ, rate limit, dedup, DLQ) | ✅ DONE |
| 6-8 | .NET Processor (EF Core, cleaning, validation, versioning) | ⬜ Next |
| 9-11 | RAG Pipeline (pgvector, OpenAI, chunking, citations) | ⬜ |
| 12-14 | API endpoints + React UI | ⬜ |
| 15-17 | Test on 3 sites, scaling demo, fault injection | ⬜ |
| 18-20 | Report, diagrams, recording | ⬜ |

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

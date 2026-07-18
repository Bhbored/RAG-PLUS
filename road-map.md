Here is your **complete, Azure-centric execution plan**, mapped precisely to every requirement in the assignment.

---

# Executive Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              AZURE CLOUD / LOCAL                             │
├─────────────────────────────────────────────────────────────────────────────┤
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────────────────────┐   │
│  │  React + Vite│───▶│  ASP.NET Core│───▶│  Azure Database for          │   │
│  │  (TypeScript)│◀───│  Web API     │◀───│  PostgreSQL (Raw + Cleaned)  │   │
│  └──────────────┘    │  + Semantic  │    └──────────────────────────────┘   │
│                      │    Kernel    │                                        │
│                      └──────┬───────┘    ┌──────────────────────────────┐   │
│                             │            │  Azure AI Search             │   │
│                             └───────────▶│  (Vectors + Hybrid Search)   │   │
│                                          └──────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  Node.js Scraper Workers (Playwright + BullMQ)                      │    │
│  │  ┌─────────┐  ┌─────────┐  ┌─────────┐                            │    │
│  │  │ Worker 1│  │ Worker 2│  │ Worker N│  ◄── Azure Container Apps   │    │
│  │  │Container│  │Container│  │Container│      (Horizontal Replicas)   │    │
│  │  └────┬────┘  └────┬────┘  └────┬────┘                            │    │
│  │       └──────────────┴─────────────┘                                │    │
│  │                      │                                              │    │
│  │            Azure Cache for Redis (BullMQ + Pub/Sub)                 │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                             │
│  External: Azure OpenAI Service (Embeddings + GPT-4o)                      │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

# Phase 1: Foundation, DevOps & Azure Provisioning

**Goal:** Get your infrastructure and CI/CD running before writing business logic.

### 1.1 Repository Structure

```
/distributed-rag-scraper
├── /src
│   ├── /Scraper          # Node.js + TypeScript (Playwright, BullMQ)
│   ├── /Api              # ASP.NET Core 8 Web API
│   ├── /Processor        # .NET Background Service (IHostedService)
│   └── /WebUi            # React + Vite + TypeScript
├── /infra                # Bicep/ARM templates or Azure CLI scripts
├── /docker               # Dockerfiles per service
├── docker-compose.yml    # Local development stack
└── .github/workflows     # CI/CD pipelines
```

### 1.2 Azure Resources to Provision

Use **Azure for Students** ($100 credit) or free tiers:

| Azure Service                                       | Purpose                                           | Why for YOU                                                                                                                                             |
| --------------------------------------------------- | ------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Azure Container Apps**                            | Host scraper workers + API + UI                   | Serverless containers. Scale scraper workers from 1 → 10 replicas with one command. Perfect for the "horizontal scaling" demo.                          |
| **Azure Cache for Redis**                           | BullMQ job queue + distributed cache + Pub/Sub    | Enterprise Redis. BullMQ runs natively on this. Enables true distributed workers across separate containers.                                            |
| **Azure Database for PostgreSQL – Flexible Server** | Raw HTML, cleaned data, crawl state, versioning   | Managed Postgres. Add `pgvector` extension if you want a backup vector store. EF Core works flawlessly.                                                 |
| **Azure AI Search**                                 | Vector index + keyword index + hybrid retrieval   | **This is your secret weapon.** Native .NET SDK, supports vector + full-text + semantic ranking in one query. Stores source URL metadata for citations. |
| **Azure OpenAI Service**                            | `text-embedding-3-large` + `gpt-4o`               | First-class .NET SDK (`Azure.AI.OpenAI`). Enterprise-grade, no credit card juggling with OpenAI's consumer API.                                         |
| **Azure Monitor + App Insights**                    | Logging, retries tracking, dead-letter monitoring | Shows fault tolerance in action. Track worker crashes and queue depths.                                                                                 |
| **Azure Container Registry**                        | Store Docker images                               | Push images from CI/CD, pull from Container Apps.                                                                                                       |

> **Fallback:** If Azure OpenAI Service approval is slow, use the OpenAI REST API directly via `HttpClient` in development, swap to Azure OpenAI later with a config flag.

### 1.3 CI/CD (GitHub Actions)

Create **two workflows**:

- **`ci-scraper.yml`**: `npm ci` → `eslint` → `tsc --noEmit` → `jest` tests → `docker build` → push to ACR
- **`ci-api.yml`**: `dotnet restore` → `dotnet format --verify-no-changes` → `dotnet test` → `docker build` → push to ACR

### 1.4 Local Development

Use `docker-compose.yml` to spin up:

- Postgres 16 (with pgvector)
- Redis 7
- Qdrant (local fallback if Azure AI Search isn't ready yet)
- Your Node.js scraper, .NET API, and React UI

---

# Phase 2: Distributed Web Scraper (TypeScript / Node.js)

### 2.1 Core Stack

| Component      | Technology                          | Justification                                                                                                       |
| -------------- | ----------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| Browser        | **Playwright**                      | One tool handles static HTML _and_ JS-rendered SPAs. Auto-waits for network idle. Headless Chromium/Firefox/WebKit. |
| Static Parsing | **Cheerio**                         | Drop-in replacement for Playwright when you know a page is static. 10x faster than spinning up a browser.           |
| Queue          | **BullMQ** on Azure Redis           | Distributed by design. Jobs persist in Redis. Multiple containerized workers pick up jobs independently.            |
| robots.txt     | **`robots-parser`**                 | Parse and honor crawl-delay, disallow rules per domain.                                                             |
| Rate Limiting  | **Bottleneck** + BullMQ delays      | Per-domain token buckets. If `robots.txt` says `Crawl-delay: 5`, Bottleneck enforces it.                            |
| Deduplication  | **SHA-256** hash of normalized HTML | Store hash in Redis. Before crawling, check hash. If unchanged, skip. Enables incremental re-crawls.                |

### 2.2 Worker Architecture (Genuinely Distributed)

**This satisfies the "multiple independent worker instances" requirement.**

```typescript
// scraper/src/worker.ts
import { Worker } from "bullmq";
import { PlaywrightCrawler } from "./crawlers/playwright";
import { CheerioCrawler } from "./crawlers/cheerio";
import { Redis } from "ioredis";

const connection = new Redis(process.env.AZURE_REDIS_CONNECTION_STRING);

const scraperWorker = new Worker(
  "scrape-queue",
  async (job) => {
    const { url, renderJs, domain } = job.data;

    // Respect robots.txt before every job
    const parser = await getRobotsParser(domain);
    if (!parser.isAllowed(url, "RagScraperBot")) {
      throw new UnrecoverableError("Blocked by robots.txt");
    }

    const crawler = renderJs ? new PlaywrightCrawler() : new CheerioCrawler();
    const result = await crawler.scrape(url);

    // Deduplication: check content hash
    const hash = crypto
      .createHash("sha256")
      .update(result.normalizedHtml)
      .digest("hex");
    const exists = await connection.get(`hash:${hash}`);
    if (exists) return { skipped: true, reason: "unchanged" };

    // Store raw + hash
    await storeRawData(result, hash);
    await connection.setex(`hash:${hash}`, 86400, "1");

    // Notify .NET processor via Redis Pub/Sub
    await connection.publish(
      "raw-data-ready",
      JSON.stringify({ url, id: result.id }),
    );
  },
  {
    connection,
    concurrency: 3,
    limiter: { max: 1, duration: 5000 }, // 1 job per 5s per worker
    stalledInterval: 30000, // If worker crashes, job reclaimed after 30s
  },
);
```

### 2.3 Failure Handling (Fault Tolerance)

| Failure                   | Handling Mechanism                                                                                                            |
| ------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| **Worker crash mid-task** | BullMQ marks job "stalled" after 30s. Another worker picks it up. Max 3 retries.                                              |
| **Network timeout**       | Playwright `page.goto({ timeout: 30000 })`. Caught → job fails → BullMQ retry with exponential backoff (2^attempt \* 1000ms). |
| **429 / IP Block**        | Detect HTTP 429. Backoff 60s → 120s → 240s. Log to Azure Monitor.                                                             |
| **Repeated failures**     | After 3 retries, move to **`scrape-dead-letter` queue**. Store error + URL in PostgreSQL for manual review.                   |

### 2.4 Horizontal Scaling Demo

1. Deploy scraper to **Azure Container Apps** with `minReplicas: 1, maxReplicas: 5`
2. Seed queue with 100 URLs
3. **Baseline:** 1 worker → measure time
4. **Scale:** `az containerapp update --name scraper --min-replicas 3`
5. **Result:** Queue drains ~3x faster. Capture logs in video.

---

# Phase 3: Data Processing (.NET 8 Background Service)

### 3.1 Service: `ScrapedDataProcessor` (IHostedService)

This runs continuously in its own container, separate from the API.

```csharp
// Processor/Services/ScrapedDataProcessor.cs
public class ScrapedDataProcessor : BackgroundService
{
    private readonly IRawDataRepository _rawRepo;
    private readonly ICleanDataRepository _cleanRepo;
    private readonly IAzureSearchIndexer _searchIndexer;
    private readonly ILogger<ScrapedDataProcessor> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Subscribe to Redis Pub/Sub for real-time processing
        await _redis.SubscribeAsync("raw-data-ready", async (channel, message) =>
        {
            var data = JsonSerializer.Deserialize<RawDataNotification>(message);
            await ProcessAndIndexAsync(data.Url, data.Id);
        });
    }

    private async Task ProcessAndIndexAsync(string url, Guid rawId)
    {
        // 1. Fetch raw HTML
        var raw = await _rawRepo.GetByIdAsync(rawId);

        // 2. Strip boilerplate (use HtmlAgilityPack or AngleSharp)
        var doc = new HtmlDocument();
        doc.LoadHtml(raw.Html);
        var cleaner = new HtmlCleaner(); // Your custom service
        var structured = cleaner.Extract(url, doc);
        // structured = { title, bodyText, tables[], links[], publishDate }

        // 3. Schema validation (FluentValidation or JSON Schema)
        var validator = new ScrapedContentValidator();
        var result = validator.Validate(structured);
        if (!result.IsValid) throw new ValidationException(result.Errors);

        // 4. Versioning: append new version, don't overwrite
        var version = await _cleanRepo.GetNextVersionAsync(url);
        var cleaned = new CleanedData
        {
            Url = url,
            Content = JsonSerializer.Serialize(structured),
            Version = version,
            ProcessedAt = DateTime.UtcNow
        };
        await _cleanRepo.InsertAsync(cleaned);

        // 5. Trigger indexing (Phase 4)
        await _searchIndexer.IndexDocumentAsync(cleaned);
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
    public string ContentHash { get; set; } // For dedup
    public int HttpStatus { get; set; }
    public DateTime ScrapedAt { get; set; }
    public string WorkerId { get; set; } // Track which container scraped it
}

public class CleanedData
{
    public Guid Id { get; set; }
    public string Url { get; set; }
    public string Title { get; set; }
    public JsonDocument StructuredContent { get; set; } // JSONB in Postgres
    public int Version { get; set; } // VERSIONING REQUIREMENT
    public DateTime ProcessedAt { get; set; }
    public List<DataChunk> Chunks { get; set; }
}
```

---

# Phase 4: RAG Pipeline (.NET 8 + Azure AI + Semantic Kernel)

### 4.1 Chunking Strategy: **Overlap-Based with Semantic Boundaries**

**Why this choice:** You are new to RAG. Semantic chunking (using an ML model to detect topic shifts) is complex to tune. Overlap-based chunking with paragraph boundary respect is deterministic, fast, and preserves context across chunk boundaries.

```csharp
// Chunking service
public List<DataChunk> ChunkContent(string text, string url)
{
    var chunks = new List<DataChunk>();
    const int chunkSize = 500;    // tokens (approximate via character count)
    const int overlap = 50;       // tokens

    // Split on paragraph boundaries first, then sentences
    var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

    var currentChunk = new StringBuilder();
    int overlapChars = overlap * 4; // ~4 chars per token

    foreach (var para in paragraphs)
    {
        if (currentChunk.Length + para.Length > chunkSize * 4)
        {
            var chunkText = currentChunk.ToString();
            chunks.Add(new DataChunk
            {
                Text = chunkText,
                SourceUrl = url,
                Index = chunks.Count,
                // Store overlap for next chunk
                Overlap = chunkText.Substring(Math.Max(0, chunkText.Length - overlapChars))
            });

            currentChunk.Clear();
            if (chunks.Last().Overlap != null)
                currentChunk.Append(chunks.Last().Overlap);
        }
        currentChunk.AppendLine(para);
    }

    // Trade-off explanation:
    // PRO: Context preserved across boundaries, citations stay accurate to source URL
    // CON: ~10% storage overhead from overlap, slightly more embedding cost
    return chunks;
}
```

### 4.2 Azure AI Search Index Design

```csharp
// Define index fields
var index = new SearchIndex("scraped-content")
{
    Fields =
    {
        new SearchableField("id") { IsKey = true },
        new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnLucene },
        new VectorSearchField("contentVector", 3072), // text-embedding-3-large
        new SimpleField("sourceUrl", SearchFieldDataType.String) { IsFacetable = true, IsFilterable = true },
        new SimpleField("title", SearchFieldDataType.String),
        new SimpleField("domain", SearchFieldDataType.String) { IsFilterable = true },
        new SimpleField("chunkIndex", SearchFieldDataType.Int32),
        new SimpleField("version", SearchFieldDataType.Int32),
        new SimpleField("timestamp", SearchFieldDataType.DateTimeOffset)
    },
    VectorSearch = new VectorSearch
    {
        Profiles = { new VectorSearchProfile("vector-profile", "vector-config") },
        Algorithms = { new HnswAlgorithmConfiguration("vector-config") }
    },
    SemanticSearch = new SemanticSearch
    {
        Configurations = { new SemanticConfiguration("semantic-config", new SemanticPrioritizedFields
        {
            TitleField = new SemanticField("title"),
            ContentFields = { new SemanticField("content") }
        })}
    }
};
```

### 4.3 Semantic Kernel Integration

```csharp
// Program.cs
builder.Services.AddAzureOpenAIChatCompletion(
    deploymentName: "gpt-4o",
    endpoint: builder.Configuration["AzureOpenAI:Endpoint"],
    apiKey: builder.Configuration["AzureOpenAI:Key"]);

builder.Services.AddAzureOpenAITextEmbeddingGeneration(
    deploymentName: "text-embedding-3-large",
    endpoint: builder.Configuration["AzureOpenAI:Endpoint"],
    apiKey: builder.Configuration["AzureOpenAI:Key"]);

// RAG Service
public class RagService
{
    private readonly Kernel _kernel;
    private readonly SearchClient _searchClient;

    public async Task<RagResponse> AskAsync(string question)
    {
        // 1. Generate embedding for the question
        var embeddingService = _kernel.GetRequiredService<ITextEmbeddingGenerationService>();
        var questionEmbedding = await embeddingService.GenerateEmbeddingAsync(question);

        // 2. HYBRID RETRIEVAL (Keyword + Vector + Semantic Ranking)
        var searchOptions = new SearchOptions
        {
            VectorSearch = new VectorSearchOptions
            {
                Queries = { new VectorizedQuery(questionEmbedding.ToFloats())
                {
                    KNearestNeighborsCount = 5,
                    Fields = { "contentVector" }
                }}
            },
            QueryType = SearchQueryType.Semantic, // Enables semantic re-ranking
            SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = "semantic-config",
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
                QueryAnswer = new QueryAnswer(QueryAnswerType.Extractive)
            },
            Size = 8
        };

        var results = await _searchClient.SearchAsync<ScrapedDocument>(question, searchOptions);

        // 3. Build context with citations
        var contextBuilder = new StringBuilder();
        var sources = new List<SourceCitation>();

        await foreach (var result in results.Value.GetResultsAsync())
        {
            contextBuilder.AppendLine($"[Source: {result.Document.SourceUrl}]");
            contextBuilder.AppendLine(result.Document.Content);
            sources.Add(new SourceCitation
            {
                Url = result.Document.SourceUrl,
                Title = result.Document.Title,
                ChunkIndex = result.Document.ChunkIndex
            });
        }

        // 4. Prompt engineering for synthesis + citations
        var prompt = $@"
You are a research assistant. Answer the question using ONLY the provided context.
If the answer requires combining information from multiple sources, synthesize it clearly.
Cite every fact using [Source: URL].

Context:
{contextBuilder}

Question: {question}

Answer:";

        var chat = _kernel.GetRequiredService<IChatCompletionService>();
        var response = await chat.GetChatMessageContentAsync(prompt);

        // 5. Parse citations from LLM response
        var parsedCitations = ExtractCitations(response.Content, sources);

        return new RagResponse
        {
            Answer = response.Content,
            Citations = parsedCitations,
            SourcesUsed = sources.Distinct().ToList()
        };
    }
}
```

### 4.4 Why Azure AI Search over Qdrant/Pinecone?

| Feature                | Azure AI Search                               | Qdrant (self-hosted)                |
| ---------------------- | --------------------------------------------- | ----------------------------------- |
| **Hybrid Search**      | Built-in (Keyword + Vector + Semantic ranker) | Requires separate keyword index     |
| **.NET SDK**           | Official, fully typed, enterprise support     | Community SDK                       |
| **Metadata filtering** | Native (`$filter=domain eq 'example.com'`)    | Manual                              |
| **Citations**          | Highlights + captions show exact source text  | Not built-in                        |
| **Cost**               | ~$0.10/hour basic tier                        | Free self-hosted, but you manage it |

> **Alternative considered:** Qdrant. Rejected because Azure AI Search handles hybrid retrieval and semantic ranking in one managed service, reducing operational complexity for a solo developer.

---

# Phase 5: API & Web UI

### 5.1 ASP.NET Core API Endpoints

```csharp
app.MapGet("/api/raw-data", async (string url, int? page, IRawDataRepository repo) =>
    await repo.GetByUrlAsync(url, page));

app.MapGet("/api/processed-data", async (string? domain, int? version, ICleanDataRepository repo) =>
    await repo.QueryAsync(domain, version));

app.MapGet("/api/search", async (string query, SearchType type, ISearchService search) => type switch
{
    SearchType.Keyword => await search.KeywordSearchAsync(query),
    SearchType.Semantic => await search.SemanticSearchAsync(query),
    _ => await search.HybridSearchAsync(query)
});

app.MapPost("/api/rag/ask", async (AskRequest req, IRagService rag) =>
    await rag.AskAsync(req.Question));
```

### 5.2 React UI (TypeScript + Vite)

**Pages:**

1. **Dashboard:** Live queue depth (from Redis), worker count, crawl stats
2. **Search:** Toggle between Keyword / Semantic / Hybrid. Results show highlighted snippets.
3. **RAG Chat:** ChatGPT-style interface. User asks question. Response streams in with clickable citation chips that expand to show source URL + excerpt.

```tsx
// React component snippet
interface RagResponse {
  answer: string;
  citations: { url: string; title: string; excerpt: string }[];
}

const ChatPage = () => {
  const [messages, setMessages] = useState<Message[]>([]);

  const askQuestion = async (question: string) => {
    const res = await fetch("/api/rag/ask", {
      method: "POST",
      body: JSON.stringify({ question }),
    });
    const data: RagResponse = await res.json();
    setMessages((prev) => [
      ...prev,
      {
        role: "assistant",
        content: data.answer,
        citations: data.citations,
      },
    ]);
  };

  return (
    <div className="chat-container">
      {messages.map((m) => (
        <div key={m.id} className={m.role}>
          <div
            dangerouslySetInnerHTML={{ __html: formatCitations(m.content) }}
          />
          <div className="citations">
            {m.citations?.map((c) => (
              <a href={c.url} target="_blank" rel="noreferrer">
                📄 {c.title}
              </a>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
};
```

---

# Phase 6: Target Websites & Compliance Strategy

You need **3 different sites** covering all requirements:

| Site Type                   | Example Target                                                              | Technology Tested                                | Compliance                                                                                           |
| --------------------------- | --------------------------------------------------------------------------- | ------------------------------------------------ | ---------------------------------------------------------------------------------------------------- |
| **Static HTML**             | `quotes.toscrape.com`                                                       | Cheerio + Playwright fallback                    | `robots.txt` allows all. Add 2s crawl delay voluntarily.                                             |
| **JS-Rendered**             | `spa.scrapingbee.com` or a React blog                                       | Playwright (wait for `networkidle`)              | Check `robots.txt`. Respect `Disallow`.                                                              |
| **Pagination (500+ pages)** | Wikipedia category pages OR a large documentation site (e.g., MDN Web Docs) | Playwright scroll + pagination. Queue 500+ URLs. | Wikipedia's `robots.txt` allows selective crawling. Use `Crawl-delay`. Scrape during off-peak hours. |

### Ethics/Compliance Note (for your report):

> "Before crawling any domain, the system fetches and parses `robots.txt` using `robots-parser`. If a URL is disallowed or no `User-agent: *` rule permits access, the job is immediately rejected with an `UnrecoverableError` and logged. Per-domain rate limiting enforces a minimum 2-second delay between requests (exceeding most `Crawl-delay` directives). The scraper identifies as `RagScraperBot/1.0` with a contact email in the User-Agent string. No personal data is extracted. Incremental crawling via content hashing minimizes unnecessary server load."

---

# Deliverables Checklist vs. Assignment Requirements

| Assignment Requirement             | How You Satisfy It                                                                  |
| ---------------------------------- | ----------------------------------------------------------------------------------- |
| **3 websites**                     | quotes.toscrape.com (static), SPA demo site (JS), MDN/Wikipedia (500+ pages)        |
| **robots.txt respect**             | `robots-parser` + per-domain rate limiting + documented compliance note             |
| **Git + CI**                       | GitHub repo + GitHub Actions (lint + test + Docker build on push)                   |
| **Containerization**               | Docker per service (Scraper, API, Processor, UI). `docker-compose.yml` for local.   |
| **Static + JS rendering**          | Cheerio (static) + Playwright (JS) with auto-detection                              |
| **Parse HTML**                     | Cheerio / Playwright extract + HtmlAgilityPack in .NET processor                    |
| **Rate limiting + backoff**        | Bottleneck (per-domain) + BullMQ exponential backoff                                |
| **Deduplication + incremental**    | SHA-256 content hashing. Skip unchanged pages.                                      |
| **Distributed workers**            | BullMQ on Azure Redis. Multiple Container App replicas.                             |
| **Horizontal scaling demo**        | Scale scraper 1→3 replicas. Measure throughput.                                     |
| **Raw data DB**                    | Azure Database for PostgreSQL                                                       |
| **Worker crash recovery**          | BullMQ stalled job recovery + retries                                               |
| **Dead-letter**                    | `scrape-dead-letter` queue after 3 failed attempts                                  |
| **Strip boilerplate**              | Custom .NET `HtmlCleaner` service                                                   |
| **Structured format + validation** | JSON schema via `FluentValidation`                                                  |
| **Multiple content types**         | Extract body text, tables (`<table>`), linked docs separately                       |
| **Versioning**                     | `CleanedData.Version` integer. Append-only history.                                 |
| **Deliberate chunking**            | Overlap-based (500 tokens, 50 overlap, paragraph boundaries). Trade-offs explained. |
| **Vector DB**                      | **Azure AI Search** (vector + hybrid)                                               |
| **LLM**                            | Azure OpenAI GPT-4o                                                                 |
| **Multi-source synthesis**         | RAG prompt instructs LLM to combine multiple `[Source: URL]` contexts               |
| **API endpoints**                  | Raw, Processed, Keyword/Semantic Search, RAG QA with citations                      |
| **React UI**                       | Vite + TypeScript. Search + Chat interface.                                         |
| **Architecture diagram**           | Provided above + create in Draw.io/Lucidchart                                       |
| **Sequence diagram**               | URL → Queue → Scrape → Process → Index → Query                                      |
| **Video recording**                | Walk through: Azure portal setup → code → scaling demo → UI query with citations    |

---

# Suggested Execution Timeline (2-3 weeks)

| Days  | Phase                                                         | Output                               |
| ----- | ------------------------------------------------------------- | ------------------------------------ |
| 1-2   | Provision Azure resources, GitHub repo, CI/CD, Docker Compose | Running local stack                  |
| 3-5   | Scraper core (Playwright, BullMQ, Redis, robots.txt, dedup)   | Can crawl 1 static site              |
| 6-7   | Failure handling (retries, DLQ), horizontal scaling test      | 3 workers draining queue             |
| 8-10  | .NET Processor (EF Core, cleaning, versioning)                | Raw → Cleaned pipeline               |
| 11-13 | Azure AI Search + Semantic Kernel RAG                         | Can ask questions, get cited answers |
| 14-15 | API + React UI                                                | End-to-end working system            |
| 16-17 | Test on 3 sites, scaling demo, fault injection                | Evidence for report                  |
| 18-20 | Report, diagrams, video                                       | Final submission                     |

---

# Key Azure Decisions Summary (for your report)

| Decision               | Chosen                         | Rejected                   | Why                                                                                             |
| ---------------------- | ------------------------------ | -------------------------- | ----------------------------------------------------------------------------------------------- |
| **Cloud Platform**     | Azure                          | AWS/GCP                    | Native .NET integration, Semantic Kernel optimized for Azure OpenAI, student credits available. |
| **LLM/Embeddings**     | Azure OpenAI Service           | Direct OpenAI API / Ollama | First-class .NET SDK, enterprise compliance, no consumer API key management.                    |
| **Vector Search**      | Azure AI Search                | Qdrant / Pinecone          | Hybrid search (vector + keyword + semantic ranker) in one service. Native .NET SDK.             |
| **Queue/Coordination** | Azure Cache for Redis (BullMQ) | Azure Service Bus          | BullMQ's built-in retries, delays, and dead-letter handling require zero custom code.           |
| **Compute**            | Azure Container Apps           | AKS                        | Simpler ops for a solo dev. Built-in scaling rules. No Kubernetes cluster management.           |
| **Database**           | Azure Database for PostgreSQL  | Azure Cosmos DB / MongoDB  | EF Core maturity, JSONB support for flexible scraped content, `pgvector` fallback.              |

---

**Bottom line:** This plan keeps you in **C# and TypeScript** (your strengths), leverages **Azure's first-class .NET ecosystem** (Azure OpenAI + AI Search + Container Apps), and satisfies every single rubric point — from distributed workers to cited RAG answers. Start with Docker Compose locally, then deploy to Azure for the final demo.

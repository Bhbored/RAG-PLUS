# RAG-PLUS Demo Video Script (5-10 minutes)

## Introduction (30 sec)
"Hi, I'm going to walk you through RAG-PLUS — a distributed web scraping and RAG question-answering system built with C# .NET, Node.js TypeScript, and React."

## Architecture Overview (1 min)
Show architecture diagram. Walk through the components:
- "The system has 4 services: scraper workers, a .NET processor, an ASP.NET Core API, and a React UI."
- "Scraped data flows through BullMQ on Redis, gets stored in PostgreSQL with pgvector for vector search."
- "Horizontal scaling works via Docker Compose — you can add worker replicas on the fly."

## Azure / DevOps (30 sec)
"Everything runs locally — no Azure account needed. Docker Compose spins up PostgreSQL, Redis, and all services. The only external dependency is an OpenAI API key."

## Demo: Scrape + Process Pipeline (2 min)
1. Show `docker compose --env-file .env up -d` starting all services
2. Show dashboard at `http://localhost:3000` — stats are empty
3. Run `npm run seed` to enqueue 6 test URLs
4. Watch the dashboard stats update in real-time:
   - raw count goes from 0 to 6
   - cleaned count goes from 0 to 6
   - chunk count goes from 0 to 50
5. Show the raw data endpoint at `/api/data/raw`
6. Show search working: `GET /api/search?q=love` returns 20 results

## Demo: Horizontal Scaling (1 min)
1. Show queue depth with `npm run seed:500` — 480 URLs waiting
2. Show 1 worker processing: ~5 jobs per 15 seconds
3. Scale up: `docker compose --env-file .env --profile scale up -d --scale scraper-worker=3`
4. Show 3 worker containers running plus accelerated job completion
5. "Queue drains ~3x faster with 3 workers"

## Demo: RAG Chat (1 min)
1. Open chat page at `/chat`
2. Ask: "What are some quotes about love?"
3. Show GPT-4o answer with inline source citations
4. Click a citation chip — opens source URL
5. Ask another question: "What books are on books.toscrape.com?"
6. "The system retrieves relevant chunks via pgvector cosine similarity, builds context, and GPT-4o synthesizes the answer."

## Demo: Dead-Letter Queue (30 sec)
1. Attempt to scrape a blocked page (if available)
2. Show dead_letter table in PostgreSQL
3. Jobs move here after 3 failed retries — no data is silently lost

## Key Features Recap (30 sec)
- "robots.txt compliance built in"
- "Per-domain rate limiting with Bottleneck"
- "SHA-256 content deduplication"
- "Overlap-based chunking: 500 tokens with 50 overlap"
- "pgvector for semantic search"
- "GPT-4o for synthesis with clickable source citations"

## Closing (30 sec)
"All code is containerized with Docker, CI/CD pipelines set up in GitHub Actions, and the full pipeline runs on a single machine. Thanks for watching."

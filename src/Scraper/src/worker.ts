import { Worker, UnrecoverableError } from 'bullmq';
import Redis from 'ioredis';
import crypto from 'crypto';
import { PlaywrightCrawler } from './crawlers/playwright';
import { CheerioCrawler } from './crawlers/cheerio';
import { getRobotsParser } from './robots';
import { getDomainLimiter } from './rate-limiter';
import { initDb, storeRawData, storeDeadLetter } from './db';

const REDIS_URL = process.env.REDIS_URL || 'redis://localhost:6379';

const WORKER_ID =
  process.env.WORKER_ID ||
  `worker-${crypto.randomBytes(4).toString('hex')}`;

const connection = new Redis(REDIS_URL, {
  maxRetriesPerRequest: null,
});

const scraperWorker = new Worker(
  'scrape-queue',
  async (job) => {
    const { url, renderJs, domain } = job.data;

    // --- robots.txt check ---
    const parser = await getRobotsParser(domain);
    if (!parser.isAllowed(url, 'RagScraperBot')) {
      throw new UnrecoverableError(`Blocked by robots.txt: ${url}`);
    }

    // --- rate limiting ---
    const limiter = getDomainLimiter(domain);
    await limiter.schedule(() => Promise.resolve());

    // --- scrape ---
    const crawler = renderJs ? new PlaywrightCrawler() : new CheerioCrawler();
    const result = await crawler.scrape(url);

    // --- content hash ---
    const hash = crypto
      .createHash('sha256')
      .update(result.normalizedHtml)
      .digest('hex');

    // Check Redis for duplicate (fast cache)
    const cached = await connection.get(`hash:${hash}`);
    if (cached) {
      return { skipped: true, reason: 'unchanged', url };
    }

    // Store in PostgreSQL (content_hash UNIQUE constraint handles race conditions)
    const rawId = await storeRawData({
      url,
      domain,
      rawHtml: result.html ?? result.normalizedHtml,
      normalizedHtml: result.normalizedHtml,
      contentHash: hash,
      title: result.title,
      httpStatus: result.httpStatus ?? 200,
      renderJs: renderJs ?? false,
      workerId: WORKER_ID,
    });

    // Cache hash in Redis (24h TTL)
    await connection.setex(`hash:${hash}`, 86400, '1');

    // Notify .NET Processor (URL is enough for lookup if rawId is empty for duplicates)
    await connection.publish(
      'raw-data-ready',
      JSON.stringify({ url, id: rawId || result.id })
    );

    if (!rawId) {
      return { success: true, url, hash, deduplicated: true };
    }

    console.log(`[${WORKER_ID}] Scraped: ${url}`);
    return { success: true, url, hash, rawId };
  },
  {
    connection,
    concurrency: 3,
    stalledInterval: 30000,
    removeOnComplete: { age: 3600 },
    removeOnFail: { age: 86400 },
  }
);

// --- Dead Letter Queue ---
const dlqQueue = new Redis(REDIS_URL, {
  maxRetriesPerRequest: null,
});

scraperWorker.on('failed', async (job, err) => {
  if (!job) return;

  const attempt = job.attemptsMade;
  console.error(
    `[${WORKER_ID}] Job ${job.id} failed (attempt ${attempt}/3): ${err.message}`
  );

  if (attempt >= 3) {
    // Move to dead-letter: store in PostgreSQL + Redis DLQ
    const { url, domain } = job.data;
    await storeDeadLetter(
      url,
      domain ?? new URL(url).hostname,
      err.message,
      err.stack,
      attempt
    );

    await dlqQueue.lpush(
      'scrape-dead-letter',
      JSON.stringify({
        url,
        domain,
        error: err.message,
        attempts: attempt,
        failedAt: new Date().toISOString(),
      })
    );

    console.warn(`[${WORKER_ID}] ${url} moved to dead-letter queue after ${attempt} attempts`);
  }
});

scraperWorker.on('completed', (job) => {
  if (job.returnvalue?.skipped) return;
  console.log(`[${WORKER_ID}] Job ${job.id} completed: ${job.data.url}`);
});

async function start() {
  await initDb();
  console.log(`[${WORKER_ID}] Scraper worker started. Waiting for jobs...`);
}

start();

process.on('SIGTERM', async () => {
  console.log(`[${WORKER_ID}] Shutting down...`);
  await scraperWorker.close();
  await connection.quit();
  process.exit(0);
});

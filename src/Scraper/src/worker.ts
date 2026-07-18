import { Worker, UnrecoverableError } from 'bullmq';
import Redis from 'ioredis';
import crypto from 'crypto';
import { PlaywrightCrawler } from './crawlers/playwright';
import { CheerioCrawler } from './crawlers/cheerio';
import { getRobotsParser } from './robots';

const REDIS_URL = process.env.REDIS_URL || 'redis://localhost:6379';
const POSTGRES_URL = process.env.POSTGRES_URL || 'postgresql://postgres:123456@localhost:5432/ragplus';

const connection = new Redis(REDIS_URL);

const scraperWorker = new Worker(
  'scrape-queue',
  async (job) => {
    const { url, renderJs, domain } = job.data;

    const parser = await getRobotsParser(domain);
    if (!parser.isAllowed(url, 'RagScraperBot')) {
      throw new UnrecoverableError('Blocked by robots.txt');
    }

    const crawler = renderJs ? new PlaywrightCrawler() : new CheerioCrawler();
    const result = await crawler.scrape(url);

    const hash = crypto
      .createHash('sha256')
      .update(result.normalizedHtml)
      .digest('hex');

    const exists = await connection.get(`hash:${hash}`);
    if (exists) {
      return { skipped: true, reason: 'unchanged' };
    }

    // TODO: Phase 2 - Store raw data in PostgreSQL via POSTGRES_URL

    await connection.setex(`hash:${hash}`, 86400, '1');

    await connection.publish(
      'raw-data-ready',
      JSON.stringify({ url, id: result.id })
    );

    return { success: true, url, hash };
  },
  {
    connection,
    concurrency: 3,
    limiter: { max: 1, duration: 5000 },
    stalledInterval: 30000,
  }
);

scraperWorker.on('completed', (job) => {
  console.log(`Job ${job.id} completed: ${job.data.url}`);
});

scraperWorker.on('failed', (job, err) => {
  console.error(`Job ${job?.id} failed: ${err.message}`);
});

console.log('Scraper worker started. Waiting for jobs...');

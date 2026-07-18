import { Queue } from 'bullmq';
import Redis from 'ioredis';

const REDIS_URL = process.env.REDIS_URL || 'redis://localhost:6379';

interface SeedUrl {
  url: string;
  renderJs?: boolean;
  domain?: string;
}

const SEED_URLS: SeedUrl[] = [
  { url: 'https://quotes.toscrape.com/', renderJs: false, domain: 'quotes.toscrape.com' },
  { url: 'https://quotes.toscrape.com/page/2/', renderJs: false, domain: 'quotes.toscrape.com' },
  { url: 'https://quotes.toscrape.com/page/3/', renderJs: false, domain: 'quotes.toscrape.com' },
  { url: 'https://books.toscrape.com/', renderJs: false, domain: 'books.toscrape.com' },
  { url: 'https://books.toscrape.com/catalogue/page-2.html', renderJs: false, domain: 'books.toscrape.com' },
  { url: 'https://books.toscrape.com/catalogue/page-3.html', renderJs: false, domain: 'books.toscrape.com' },
];

async function seed() {
  const connection = new Redis(REDIS_URL, { maxRetriesPerRequest: null });
  const queue = new Queue('scrape-queue', { connection });

  const count = await queue.getJobCounts();
  console.log(`Queue before seed: active=${count.active}, waiting=${count.waiting}, completed=${count.completed}`);

  for (const item of SEED_URLS) {
    const domain = item.domain ?? new URL(item.url).hostname;
    await queue.add(
      'scrape',
      {
        url: item.url,
        renderJs: item.renderJs ?? false,
        domain,
      },
      {
        attempts: 3,
        backoff: { type: 'exponential', delay: 2000 },
        removeOnComplete: { age: 3600 },
      }
    );
    console.log(`Enqueued: ${item.url}`);
  }

  const after = await queue.getJobCounts();
  console.log(`Queue after seed: active=${after.active}, waiting=${after.waiting}, completed=${after.completed}`);

  await queue.close();
  await connection.quit();
}

seed().catch(console.error);

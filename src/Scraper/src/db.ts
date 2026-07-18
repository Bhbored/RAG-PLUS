import { Pool, PoolClient } from 'pg';

const POSTGRES_URL =
  process.env.POSTGRES_URL ||
  'postgresql://postgres:123456@localhost:5432/ragplus';

const pool = new Pool({ connectionString: POSTGRES_URL, max: 10 });

let initialized = false;

export async function initDb(): Promise<void> {
  if (initialized) return;
  const client = await pool.connect();
  try {
    await client.query(`
      CREATE TABLE IF NOT EXISTS raw_scraped_data (
        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
        url TEXT NOT NULL,
        domain TEXT NOT NULL,
        raw_html TEXT NOT NULL,
        normalized_html TEXT NOT NULL,
        content_hash TEXT NOT NULL UNIQUE,
        title TEXT,
        http_status INTEGER,
        render_js BOOLEAN DEFAULT FALSE,
        worker_id TEXT,
        scraped_at TIMESTAMPTZ DEFAULT NOW()
      );
      CREATE INDEX IF NOT EXISTS idx_raw_url ON raw_scraped_data(url);
      CREATE INDEX IF NOT EXISTS idx_raw_domain ON raw_scraped_data(domain);
      CREATE INDEX IF NOT EXISTS idx_raw_hash ON raw_scraped_data(content_hash);
      CREATE INDEX IF NOT EXISTS idx_raw_scraped_at ON raw_scraped_data(scraped_at);
      CREATE TABLE IF NOT EXISTS dead_letter (
        id SERIAL PRIMARY KEY,
        url TEXT NOT NULL,
        domain TEXT NOT NULL,
        error_message TEXT,
        error_stack TEXT,
        attempt_count INTEGER DEFAULT 3,
        failed_at TIMESTAMPTZ DEFAULT NOW()
      );
    `);
    initialized = true;
    console.log('[DB] PostgreSQL connected, schema ready');
  } finally {
    client.release();
  }
}

export interface RawScrapedData {
  url: string;
  domain: string;
  rawHtml: string;
  normalizedHtml: string;
  contentHash: string;
  title?: string;
  httpStatus: number;
  renderJs: boolean;
  workerId: string;
}

export async function storeRawData(data: RawScrapedData): Promise<string> {
  const result = await pool.query(
    `INSERT INTO raw_scraped_data
       (url, domain, raw_html, normalized_html, content_hash, title, http_status, render_js, worker_id)
     VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9)
     ON CONFLICT (content_hash) DO NOTHING
     RETURNING id`,
    [
      data.url,
      data.domain,
      data.rawHtml,
      data.normalizedHtml,
      data.contentHash,
      data.title,
      data.httpStatus,
      data.renderJs,
      data.workerId,
    ]
  );
  return result.rows[0]?.id || '';
}

export async function contentHashExists(hash: string): Promise<boolean> {
  const result = await pool.query(
    'SELECT 1 FROM raw_scraped_data WHERE content_hash = $1 LIMIT 1',
    [hash]
  );
  return (result.rowCount ?? 0) > 0;
}

export async function storeDeadLetter(
  url: string,
  domain: string,
  errorMessage: string,
  errorStack?: string,
  attemptCount = 3
): Promise<void> {
  await pool.query(
    `INSERT INTO dead_letter (url, domain, error_message, error_stack, attempt_count)
     VALUES ($1,$2,$3,$4,$5)`,
    [url, domain, errorMessage, errorStack, attemptCount]
  );
}

export async function getPool(): Promise<PoolClient> {
  return pool.connect();
}

export { pool };

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

CREATE INDEX IF NOT EXISTS idx_dlq_url ON dead_letter(url);

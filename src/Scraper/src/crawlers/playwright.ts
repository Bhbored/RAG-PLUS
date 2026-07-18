import { chromium, Browser, Page } from 'playwright';

export interface ScrapeResult {
  id: string;
  url: string;
  normalizedHtml: string;
  title: string;
  scrapedAt: string;
}

export class PlaywrightCrawler {
  private browser: Browser | null = null;

  async scrape(url: string): Promise<ScrapeResult> {
    if (!this.browser) {
      this.browser = await chromium.launch({ headless: true });
    }

    const page: Page = await this.browser.newPage();
    try {
      await page.goto(url, { waitUntil: 'networkidle', timeout: 30000 });

      const title = await page.title();
      const html = await page.content();

      // Normalize: strip scripts and styles
      await page.evaluate(() => {
        document.querySelectorAll('script, style, noscript').forEach((el) => el.remove());
      });
      const normalizedHtml = await page.content();

      return {
        id: crypto.randomUUID(),
        url,
        normalizedHtml,
        title,
        scrapedAt: new Date().toISOString(),
      };
    } finally {
      await page.close();
    }
  }

  async close(): Promise<void> {
    if (this.browser) {
      await this.browser.close();
      this.browser = null;
    }
  }
}

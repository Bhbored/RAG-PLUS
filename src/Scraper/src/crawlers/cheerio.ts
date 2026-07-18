import * as cheerio from 'cheerio';
import { ScrapeResult } from './playwright';

export class CheerioCrawler {
  async scrape(url: string): Promise<ScrapeResult> {
    const response = await fetch(url, {
      headers: {
        'User-Agent': 'RagScraperBot/1.0 (+https://github.com/example/rag-plus)',
      },
    });

    if (!response.ok) {
      throw new Error(`HTTP ${response.status}: ${response.statusText}`);
    }

    const html = await response.text();
    const $ = cheerio.load(html);

    $('script, style, noscript').remove();

    const title = $('title').text().trim();
    const normalizedHtml = $('body').html() || html;

    return {
      id: crypto.randomUUID(),
      url,
      normalizedHtml,
      title: title || url,
      scrapedAt: new Date().toISOString(),
    };
  }
}

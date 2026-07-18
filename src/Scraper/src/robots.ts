import robotsParser from 'robots-parser';

const parserCache = new Map<string, robotsParser.Robot>();

export async function getRobotsParser(
  domain: string
): Promise<robotsParser.Robot> {
  if (parserCache.has(domain)) {
    return parserCache.get(domain)!;
  }

  try {
    const robotsUrl = `https://${domain}/robots.txt`;
    const response = await fetch(robotsUrl, {
      headers: {
        'User-Agent': 'RagScraperBot/1.0 (+https://github.com/example/rag-plus)',
      },
    });

    if (!response.ok) {
      // No robots.txt found, allow all
      const parser = robotsParser(robotsUrl, '');
      parserCache.set(domain, parser);
      return parser;
    }

    const text = await response.text();
    const parser = robotsParser(robotsUrl, text);
    parserCache.set(domain, parser);
    return parser;
  } catch {
    const parser = robotsParser(`https://${domain}/robots.txt`, '');
    parserCache.set(domain, parser);
    return parser;
  }
}

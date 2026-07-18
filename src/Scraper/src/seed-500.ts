import { Queue } from 'bullmq';
import Redis from 'ioredis';

const REDIS_URL = process.env.REDIS_URL || 'redis://localhost:6379';

async function seed500() {
  const connection = new Redis(REDIS_URL, { maxRetriesPerRequest: null });
  const queue = new Queue('scrape-queue', { connection, defaultJobOptions: {
    attempts: 3,
    backoff: { type: 'exponential', delay: 2000 },
    removeOnComplete: { age: 3600 },
  }});

  const urls: { url: string; domain: string; renderJs: boolean }[] = [];

  // --- Static site: quotes.toscrape.com (100 pages) ---
  for (let i = 1; i <= 100; i++) {
    urls.push({
      url: `https://quotes.toscrape.com/page/${i}/`,
      domain: 'quotes.toscrape.com',
      renderJs: false,
    });
  }

  // --- Static site: books.toscrape.com (50 pages) ---
  for (let i = 1; i <= 50; i++) {
    urls.push({
      url: `https://books.toscrape.com/catalogue/page-${i}.html`,
      domain: 'books.toscrape.com',
      renderJs: false,
    });
  }

  // --- Wikipedia pages for scale (350 pages) ---
  const wikiTopics = [
    'Artificial_intelligence', 'Machine_learning', 'Deep_learning',
    'Natural_language_processing', 'Computer_vision', 'Reinforcement_learning',
    'Supervised_learning', 'Unsupervised_learning', 'Neural_network',
    'Large_language_model', 'Transformer_(machine_learning_model)',
    'Generative_adversarial_network', 'Convolutional_neural_network',
    'Recurrent_neural_network', 'Backpropagation', 'Gradient_descent',
    'Overfitting', 'Regularization_(mathematics)', 'Dropout_(neural_networks)',
    'Batch_normalization', 'Activation_function', 'Loss_function',
    'Stochastic_gradient_descent', 'Adam_(optimization_algorithm)',
    'Support_vector_machine', 'Decision_tree', 'Random_forest',
    'K-means_clustering', 'Principal_component_analysis',
    'Dimensionality_reduction',
  ];

  for (const topic of wikiTopics) {
    urls.push({
      url: `https://en.wikipedia.org/wiki/${topic}`,
      domain: 'en.wikipedia.org',
      renderJs: false,
    });
  }

  // Fill remaining: additional quotes pages with varied patterns
  const tags = [
    'love', 'inspirational', 'life', 'humor', 'books', 'reading',
    'friendship', 'simile', 'truth', 'philosophy', 'science', 'poetry',
  ];

  for (const tag of tags) {
    for (let p = 1; p <= 25; p++) {
      urls.push({
        url: `https://quotes.toscrape.com/tag/${tag}/page/${p}/`,
        domain: 'quotes.toscrape.com',
        renderJs: false,
      });
    }
  }

  // Trim to exactly 500 if we went over
  const final = urls.slice(0, 500);

  console.log(`Seeding ${final.length} URLs into scrape-queue...`);

  let added = 0;
  for (const item of final) {
    await queue.add('scrape', item);
    added++;
    if (added % 50 === 0) console.log(`  ${added}/${final.length} enqueued`);
  }

  console.log(`Done. ${added} URLs added to scrape-queue.`);
  await queue.close();
  await connection.quit();
}

seed500().catch(console.error);

export default function Dashboard() {
  return (
    <div style={{ padding: '2rem' }}>
      <h1>RAG-PLUS Dashboard</h1>
      <p>Queue depth, worker count, and crawl statistics will appear here.</p>
      <nav style={{ marginTop: '1rem', display: 'flex', gap: '1rem' }}>
        <a href="/search">Search</a>
        <a href="/chat">RAG Chat</a>
      </nav>
    </div>
  );
}

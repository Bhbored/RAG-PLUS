import { useState } from 'react';

interface SearchResult {
  id: string;
  sourceUrl: string;
  chunkIndex: number;
  snippet: string;
}

export default function Search() {
  const [query, setQuery] = useState('');
  const [type, setType] = useState('keyword');
  const [results, setResults] = useState<SearchResult[]>([]);
  const [loading, setLoading] = useState(false);
  const [searched, setSearched] = useState(false);

  const doSearch = async () => {
    if (!query.trim()) return;
    setLoading(true);
    setSearched(true);
    try {
      const res = await fetch(`/api/search?q=${encodeURIComponent(query.trim())}&type=${type}`);
      const data = await res.json();
      setResults(data.results ?? []);
    } catch {
      setResults([]);
    }
    setLoading(false);
  };

  return (
    <div className="page">
      <div className="header">
        <h1>Search</h1>
        <a href="/" className="btn-link">← Dashboard</a>
      </div>

      <div className="search-bar">
        <input
          type="text"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && doSearch()}
          placeholder="Search indexed content..."
          className="search-input"
        />
        <select value={type} onChange={(e) => setType(e.target.value)} className="search-type">
          <option value="keyword">Keyword</option>
          <option value="semantic">Semantic (coming soon)</option>
        </select>
        <button onClick={doSearch} disabled={loading} className="btn">
          {loading ? 'Searching...' : 'Search'}
        </button>
      </div>

      {searched && (
        <div className="results-section">
          <p className="results-count">{results.length} results for "{query}"</p>
          {results.map((r) => (
            <div key={r.id} className="result-card">
              <a href={r.sourceUrl} target="_blank" rel="noreferrer" className="result-url">
                {r.sourceUrl}
              </a>
              <p className="result-snippet">{r.snippet}</p>
            </div>
          ))}
          {results.length === 0 && !loading && (
            <p className="no-results">No results found.</p>
          )}
        </div>
      )}
    </div>
  );
}

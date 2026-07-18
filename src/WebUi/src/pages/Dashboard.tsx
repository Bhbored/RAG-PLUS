import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';

interface Stats {
  rawCount: number;
  cleanedCount: number;
  chunkCount: number;
  uniqueDomains: number;
  queue: {
    waiting: number;
    active: number;
    completed: number;
    deadLetter: number;
  };
}

export default function Dashboard() {
  const [stats, setStats] = useState<Stats | null>(null);
  const [loading, setLoading] = useState(true);
  const navigate = useNavigate();

  const fetchStats = async () => {
    setLoading(true);
    try {
      const res = await fetch('/api/stats');
      const data = await res.json();
      setStats(data);
    } catch {
      setStats(null);
    }
    setLoading(false);
  };

  useEffect(() => {
    fetchStats();
    const interval = setInterval(fetchStats, 5000);
    return () => clearInterval(interval);
  }, []);

  const totalQueued = (stats?.queue.waiting ?? 0) + (stats?.queue.active ?? 0);

  return (
    <div className="page">
      <div className="header">
        <h1>RAG-PLUS Dashboard</h1>
        <button onClick={fetchStats} className="btn" disabled={loading}>
          {loading ? '⏳' : '🔄'} Refresh
        </button>
      </div>

      <div className="stats-grid">
        <div className="stat-card" onClick={() => navigate('/search')}>
          <div className="stat-number">{stats?.chunkCount ?? '-'}</div>
          <div className="stat-label">Indexed Chunks</div>
        </div>
        <div className="stat-card">
          <div className="stat-number">{stats?.cleanedCount ?? '-'}</div>
          <div className="stat-label">Cleaned Pages</div>
        </div>
        <div className="stat-card">
          <div className="stat-number">{stats?.rawCount ?? '-'}</div>
          <div className="stat-label">Raw Pages</div>
        </div>
        <div className="stat-card">
          <div className="stat-number">{stats?.uniqueDomains ?? '-'}</div>
          <div className="stat-label">Unique Domains</div>
        </div>
      </div>

      <h2>Queue Status</h2>
      <div className="stats-grid">
        <div className="stat-card queue-active">
          <div className="stat-number">{totalQueued}</div>
          <div className="stat-label">Active + Waiting</div>
        </div>
        <div className="stat-card queue-done">
          <div className="stat-number">{stats?.queue.completed ?? '-'}</div>
          <div className="stat-label">Completed</div>
        </div>
        <div className="stat-card queue-dlq">
          <div className="stat-number">{stats?.queue.deadLetter ?? '-'}</div>
          <div className="stat-label">Dead Letter</div>
        </div>
      </div>

      <div className="nav-links">
        <a href="/search">🔍 Search</a>
        <a href="/chat">💬 RAG Chat</a>
      </div>
    </div>
  );
}

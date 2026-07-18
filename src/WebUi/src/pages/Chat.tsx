import { useState, useRef, useEffect } from 'react';

interface Message {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  citations?: { url: string; title: string; excerpt: string }[];
}

export default function Chat() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState('');
  const [loading, setLoading] = useState(false);
  const endRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  const sendMessage = async () => {
    if (!input.trim() || loading) return;

    const question = input.trim();
    setInput('');

    const userMsg: Message = {
      id: crypto.randomUUID(),
      role: 'user',
      content: question,
    };
    setMessages((prev) => [...prev, userMsg]);
    setLoading(true);

    try {
      const res = await fetch('/api/rag/ask', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question }),
      });
      const data = await res.json();

      const assistantMsg: Message = {
        id: crypto.randomUUID(),
        role: 'assistant',
        content: data.answer ?? 'No answer received.',
        citations: data.citations ?? [],
      };
      setMessages((prev) => [...prev, assistantMsg]);
    } catch {
      setMessages((prev) => [
        ...prev,
        { id: crypto.randomUUID(), role: 'assistant', content: 'Error connecting to API.' },
      ]);
    }
    setLoading(false);
  };

  const formatContent = (text: string) => {
    return text
      .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
      .replace(/\*(.*?)\*/g, '<em>$1</em>')
      .replace(/\[Source: (.*?)\]/g,
        '<a href="$1" target="_blank" rel="noreferrer" class="inline-citation">[Source]</a>');
  };

  return (
    <div className="page chat-page">
      <div className="header">
        <h1>RAG Chat</h1>
        <a href="/" className="btn-link">← Dashboard</a>
      </div>

      <div className="chat-messages">
        {messages.length === 0 && (
          <div className="chat-empty">
            <p>Ask a question about the scraped content.</p>
            <div className="chat-suggestions">
              {[
                'What quotes about life are in the data?',
                'What books are available on books.toscrape.com?',
                'Summarize the content from quotes.toscrape.com',
              ].map((s) => (
                <button key={s} onClick={() => { setInput(s); }} className="suggestion-chip">
                  {s}
                </button>
              ))}
            </div>
          </div>
        )}

        {messages.map((msg) => (
          <div key={msg.id} className={`chat-message ${msg.role}`}>
            <div className="message-avatar">{msg.role === 'user' ? '👤' : '🤖'}</div>
            <div className="message-body">
              <div
                className="message-content"
                dangerouslySetInnerHTML={{ __html: formatContent(msg.content) }}
              />
              {msg.citations && msg.citations.length > 0 && (
                <div className="citations">
                  <div className="citations-label">Sources:</div>
                  {msg.citations.map((c, i) => (
                    <a
                      key={i}
                      href={c.url}
                      target="_blank"
                      rel="noreferrer"
                      className="citation-chip"
                      title={c.excerpt}
                    >
                      📄 {c.title}
                    </a>
                  ))}
                </div>
              )}
            </div>
          </div>
        ))}

        {loading && (
          <div className="chat-message assistant">
            <div className="message-avatar">🤖</div>
            <div className="message-body">
              <div className="typing-indicator">
                <span></span><span></span><span></span>
              </div>
            </div>
          </div>
        )}

        <div ref={endRef} />
      </div>

      <div className="chat-input-area">
        <input
          type="text"
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && sendMessage()}
          placeholder="Ask a question..."
          className="chat-input"
          disabled={loading}
        />
        <button onClick={sendMessage} disabled={loading || !input.trim()} className="btn send-btn">
          Send
        </button>
      </div>
    </div>
  );
}

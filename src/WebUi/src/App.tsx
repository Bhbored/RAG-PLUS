import { Routes, Route } from 'react-router-dom';
import Dashboard from './pages/Dashboard';
import Search from './pages/Search';
import Chat from './pages/Chat';

function App() {
  return (
    <Routes>
      <Route path="/" element={<Dashboard />} />
      <Route path="/search" element={<Search />} />
      <Route path="/chat" element={<Chat />} />
    </Routes>
  );
}

export default App;

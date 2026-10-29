import React, { useState, useEffect } from 'react';
import Header from './components/Header';
import Sidebar from './components/Sidebar';
import LoginPage from './components/LoginPage';
import FileDropzone from './components/FileDropzone';
import QuestionCard from './components/QuestionCard';
import QuestionBank from './components/QuestionBank';
import DashboardOverview from './components/DashboardOverview';
import LeaderboardView from './components/LeaderboardView';
import RegisteredUsersView from './components/RegisteredUsersView';
import { api } from './services/api';

export default function App() {
  const [activeTab, setActiveTab] = useState('dashboard');
  const [user, setUser] = useState(null);
  const [authChecked, setAuthChecked] = useState(false);
  const [health, setHealth] = useState(null);
  const [stats, setStats] = useState(null);
  const [biomes, setBiomes] = useState([]);
  const [pendingQuestions, setPendingQuestions] = useState([]);
  const [loadingPending, setLoadingPending] = useState(false);
  const [notification, setNotification] = useState(null);

  // Show transient banner notification
  const notify = (msg, type = 'success') => {
    setNotification({ msg, type });
    setTimeout(() => setNotification(null), 4000);
  };

  // Initial load
  const loadInitialData = async () => {
    try {
      const [healthData, biomesData] = await Promise.all([
        api.getHealth().catch(() => null),
        api.getBiomes().catch(() => ({ biomes: [] })),
      ]);
      setHealth(healthData);
      setBiomes(biomesData.biomes || []);
    } catch (e) {
      console.warn('Initial data load warning:', e);
    }
  };

  const loadUserDataAndStats = async () => {
    const token = localStorage.getItem('kw_admin_token');
    if (!token) {
      setUser(null);
      setAuthChecked(true);
      return;
    }

    try {
      const meData = await api.getMe();
      if (meData.user?.role !== 'admin') {
        throw new Error('Admin role required');
      }
      setUser(meData.user);

      const [statsData, pendingData] = await Promise.all([
        api.getStats().catch(() => null),
        api.getPendingQuestions().catch(() => ({ questions: [] })),
      ]);

      if (statsData) setStats(statsData);
      if (pendingData) setPendingQuestions(pendingData.questions || []);
    } catch (err) {
      console.warn('Session expired or unauthorized:', err.message);
      localStorage.removeItem('kw_admin_token');
      setUser(null);
    } finally {
      setAuthChecked(true);
    }
  };

  useEffect(() => {
    loadInitialData();
    loadUserDataAndStats();
  }, []);

  useEffect(() => {
    if (!authChecked) return;
    if (user && window.location.pathname === '/login') {
      window.history.replaceState(null, '', '/');
    }
  }, [authChecked, user]);

  const handleLogin = async (username, password) => {
    const res = await api.login(username, password);
    if (res.user?.role !== 'admin') {
      throw new Error('Access denied: This web portal is strictly for the Developer / Administrator. Players must log in inside the Unity game client.');
    }
    localStorage.setItem('kw_admin_token', res.token);
    setUser(res.user);
    notify(`Welcome back, ${res.user.username}! Admin permissions unlocked.`);
    await loadUserDataAndStats();
  };

  const handleLogout = () => {
    localStorage.removeItem('kw_admin_token');
    setUser(null);
    notify('Signed out successfully.');
  };

  // Called when AI finishes generating questions from PDF
  const handleQuestionsGenerated = async (newQuestions) => {
    notify(`Successfully generated ${newQuestions.length} questions. Added to Review Queue!`);
    setActiveTab('review');
    await loadUserDataAndStats();
  };

  const handleApproveQuestion = async (id, payload) => {
    try {
      await api.approveQuestion(id, payload);
      notify('Question approved and published to active live pool!');
      setPendingQuestions((prev) => prev.filter((q) => q.id !== id));
      await loadUserDataAndStats();
    } catch (err) {
      notify(`Failed to approve question: ${err.message}`, 'error');
    }
  };

  const handleRejectQuestion = async (id) => {
    try {
      await api.rejectQuestion(id);
      notify('Question discarded from review queue.');
      setPendingQuestions((prev) => prev.filter((q) => q.id !== id));
      await loadUserDataAndStats();
    } catch (err) {
      notify(`Failed to reject question: ${err.message}`, 'error');
    }
  };

  // 1. Initial Authentication Check State (prevents layout flash)
  if (!authChecked) {
    return (
      <div style={{
        minHeight: '100vh',
        backgroundColor: 'var(--bg-primary)',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        color: 'var(--text-secondary)',
        gap: '16px',
      }}>
        <div className="animate-spin" style={{
          width: '36px',
          height: '36px',
          border: '3px solid var(--border-color)',
          borderTopColor: 'var(--accent-primary)',
          borderRadius: '50%',
        }}></div>
        <p style={{ fontSize: '14px', fontWeight: 600 }}>
          Initializing Knowledge Warrior Admin Portal...
        </p>
      </div>
    );
  }

  // 2. Full-Screen Login Wall for Unauthenticated Guests
  if (!user) {
    return <LoginPage onLoginSuccess={handleLogin} health={health} />;
  }

  // 3. Authenticated Admin Dashboard Layout
  return (
    <div style={{ minHeight: '100vh', display: 'flex', flexDirection: 'column', backgroundColor: 'var(--bg-primary)' }}>
      {/* Toast Notification */}
      {notification && (
        <div style={{
          position: 'fixed',
          top: '20px',
          right: '24px',
          zIndex: 9999,
          backgroundColor: notification.type === 'error' ? 'var(--accent-danger)' : 'var(--accent-success)',
          color: '#ffffff',
          padding: '12px 20px',
          borderRadius: 'var(--radius-md)',
          boxShadow: 'var(--shadow-lg)',
          fontSize: '14px',
          fontWeight: 600,
          animation: 'fadeIn 0.2s ease',
        }}>
          {notification.msg}
        </div>
      )}

      {/* Header */}
      <Header
        user={user}
        onLogout={handleLogout}
        health={health}
      />

      <div style={{ display: 'flex', flex: 1 }}>
        {/* Navigation Sidebar */}
        <Sidebar
          activeTab={activeTab}
          setActiveTab={setActiveTab}
          pendingCount={pendingQuestions.length}
        />

        {/* Main Content Area */}
        <main style={{ flex: 1, padding: '36px 40px', overflowY: 'auto', maxWidth: '1300px' }}>
          {activeTab === 'dashboard' && (
            <DashboardOverview
              stats={stats}
              health={health}
              onNavigate={(tab) => setActiveTab(tab)}
            />
          )}

          {activeTab === 'ingestion' && (
            <FileDropzone
              biomes={biomes}
              onQuestionsGenerated={handleQuestionsGenerated}
              requireLogin={() => false}
            />
          )}

          {activeTab === 'review' && (
            <div>
              <div style={{ marginBottom: '24px' }}>
                <h2 style={{ fontSize: '24px', fontWeight: 800, color: 'var(--text-primary)' }}>
                  🔍 Pending Review Queue ({pendingQuestions.length})
                </h2>
                <p style={{ fontSize: '13px', color: 'var(--text-secondary)', marginTop: '4px' }}>
                  Curate, edit, and approve AI-synthesized questions before they appear in live student gameplay.
                </p>
              </div>

              {loadingPending ? (
                <div style={{ textAlign: 'center', padding: '60px 20px' }}>
                  <div className="animate-spin" style={{
                    display: 'inline-block',
                    width: '28px',
                    height: '28px',
                    border: '3px solid var(--border-color)',
                    borderTopColor: 'var(--accent-primary)',
                    borderRadius: '50%',
                  }}></div>
                </div>
              ) : pendingQuestions.length === 0 ? (
                <div style={{
                  textAlign: 'center',
                  padding: '70px 20px',
                  backgroundColor: 'var(--bg-surface)',
                  borderRadius: 'var(--radius-lg)',
                  border: '1px solid var(--border-color)',
                }}>
                  <div style={{ fontSize: '42px', marginBottom: '12px' }}>✨</div>
                  <h3 style={{ fontSize: '18px', fontWeight: 700, color: 'var(--text-primary)' }}>
                    Review Queue is Clear!
                  </h3>
                  <p style={{ fontSize: '14px', color: 'var(--text-secondary)', marginTop: '6px', maxWidth: '400px', margin: '6px auto 20px' }}>
                    All AI-generated questions have been reviewed. Upload a new PDF to generate more curriculum questions.
                  </p>
                  <button
                    onClick={() => setActiveTab('ingestion')}
                    style={{
                      backgroundColor: 'var(--accent-primary)',
                      color: '#ffffff',
                      border: 'none',
                      padding: '10px 20px',
                      borderRadius: 'var(--radius-md)',
                      fontSize: '14px',
                      fontWeight: 600,
                      cursor: 'pointer',
                    }}
                  >
                    📑 Ingest Lesson PDF
                  </button>
                </div>
              ) : (
                pendingQuestions.map((q) => (
                  <QuestionCard
                    key={q.id}
                    question={q}
                    isPending={true}
                    onApprove={handleApproveQuestion}
                    onReject={handleRejectQuestion}
                  />
                ))
              )}
            </div>
          )}

          {activeTab === 'bank' && <QuestionBank biomes={biomes} />}

          {activeTab === 'users' && <RegisteredUsersView />}

          {activeTab === 'leaderboard' && <LeaderboardView />}
        </main>
      </div>
    </div>
  );
}

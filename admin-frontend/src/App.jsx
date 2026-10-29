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

function normalizeQuestionText(text) {
  if (!text || typeof text !== 'string') return '';
  return text
    .toLowerCase()
    .replace(/[^a-z0-9\s]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

function deduplicateQuestions(questions = []) {
  const seen = new Set();
  const unique = [];
  for (const q of questions) {
    const norm = normalizeQuestionText(q.questionText || q.question || '');
    if (!norm || seen.has(norm)) continue;
    seen.add(norm);
    unique.push(q);
  }
  return unique;
}

export default function App() {
  const [activeTab, setActiveTab] = useState('dashboard');
  const [user, setUser] = useState(null);
  const [authChecked, setAuthChecked] = useState(false);
  const [health, setHealth] = useState(null);
  const [stats, setStats] = useState(null);
  const [biomes, setBiomes] = useState([]);
  const [pendingQuestions, setPendingQuestions] = useState([]);
  const [selectedQuestionIds, setSelectedQuestionIds] = useState([]);
  const [bulkProcessing, setBulkProcessing] = useState(false);
  const [reviewLayout, setReviewLayout] = useState('grid');
  const [loadingPending, setLoadingPending] = useState(false);
  const [notification, setNotification] = useState(null);

  // Show transient banner notification
  const notify = (msg, type = 'success') => {
    setNotification({ msg, type });
    setTimeout(() => setNotification(null), 4500);
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
      if (pendingData) {
        const uniquePending = deduplicateQuestions(pendingData.questions || []);
        setPendingQuestions(uniquePending);
        setSelectedQuestionIds((prev) =>
          prev.filter((id) => uniquePending.some((q) => q.id === id))
        );
      }
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
      throw new Error(
        'Access denied: This web portal is strictly for the Developer / Administrator. Players must log in inside the Unity game client.'
      );
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
    notify(`Successfully generated ${newQuestions.length} unique questions. Added to Review Queue!`);
    setActiveTab('review');
    await loadUserDataAndStats();
  };

  const handleApproveQuestion = async (id, payload) => {
    try {
      const res = await api.approveQuestion(id, payload);
      if (res?.duplicateSkipped) {
        notify(res.message || 'Duplicate question skipped and removed from queue.', 'error');
      } else {
        notify('Question approved and published to active live pool!');
      }
      setPendingQuestions((prev) => prev.filter((q) => q.id !== id));
      setSelectedQuestionIds((prev) => prev.filter((item) => item !== id));
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
      setSelectedQuestionIds((prev) => prev.filter((item) => item !== id));
      await loadUserDataAndStats();
    } catch (err) {
      notify(`Failed to reject question: ${err.message}`, 'error');
    }
  };

  const handleToggleSelectQuestion = (id) => {
    setSelectedQuestionIds((prev) =>
      prev.includes(id) ? prev.filter((item) => item !== id) : [...prev, id]
    );
  };

  const allSelected =
    pendingQuestions.length > 0 && selectedQuestionIds.length === pendingQuestions.length;

  const handleSelectAllToggle = () => {
    if (allSelected) {
      setSelectedQuestionIds([]);
    } else {
      setSelectedQuestionIds(pendingQuestions.map((q) => q.id));
    }
  };

  const handleBulkApproveSelected = async (targetIds = selectedQuestionIds, approveAllFlag = false) => {
    const idsToApprove = approveAllFlag ? pendingQuestions.map((q) => q.id) : targetIds;
    if (idsToApprove.length === 0) return;

    if (approveAllFlag) {
      setSelectedQuestionIds(idsToApprove);
    }

    setBulkProcessing(true);
    try {
      const res = await api.approveQuestionsBulk(idsToApprove, approveAllFlag);
      const approvedCount = res.approvedCount ?? idsToApprove.length;
      const dupSkipped = res.duplicatesSkipped ?? 0;
      notify(
        `Approved and published ${approvedCount} unique question(s) to live pool${
          dupSkipped > 0 ? ` (${dupSkipped} duplicate(s) automatically filtered)` : ''
        }!`
      );
      setPendingQuestions((prev) => prev.filter((q) => !idsToApprove.includes(q.id)));
      setSelectedQuestionIds([]);
      await loadUserDataAndStats();
    } catch (err) {
      notify(`Failed to bulk approve questions: ${err.message}`, 'error');
    } finally {
      setBulkProcessing(false);
    }
  };

  const handleBulkRejectSelected = async () => {
    if (selectedQuestionIds.length === 0) return;
    if (
      !window.confirm(
        `Are you sure you want to discard ${selectedQuestionIds.length} selected question(s)?`
      )
    ) {
      return;
    }

    setBulkProcessing(true);
    try {
      await api.rejectQuestionsBulk(selectedQuestionIds);
      notify(`Discarded ${selectedQuestionIds.length} selected question(s) from review queue.`);
      setPendingQuestions((prev) =>
        prev.filter((q) => !selectedQuestionIds.includes(q.id))
      );
      setSelectedQuestionIds([]);
      await loadUserDataAndStats();
    } catch (err) {
      notify(`Failed to discard selected questions: ${err.message}`, 'error');
    } finally {
      setBulkProcessing(false);
    }
  };

  // 1. Initial Authentication Check State (prevents layout flash)
  if (!authChecked) {
    return (
      <div
        style={{
          minHeight: '100vh',
          backgroundColor: 'var(--bg-primary)',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          color: 'var(--text-secondary)',
          gap: '16px',
        }}
      >
        <div
          className="animate-spin"
          style={{
            width: '36px',
            height: '36px',
            border: '3px solid var(--border-color)',
            borderTopColor: 'var(--accent-primary)',
            borderRadius: '50%',
          }}
        ></div>
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

  // 3. Authenticated Admin Dashboard Layout (Full Viewport Space Maximized)
  return (
    <div
      style={{
        minHeight: '100vh',
        width: '100%',
        display: 'flex',
        flexDirection: 'column',
        backgroundColor: 'var(--bg-primary)',
      }}
    >
      {/* Toast Notification */}
      {notification && (
        <div
          style={{
            position: 'fixed',
            top: '20px',
            right: '24px',
            zIndex: 9999,
            backgroundColor:
              notification.type === 'error' ? 'var(--accent-danger)' : 'var(--accent-success)',
            color: '#ffffff',
            padding: '12px 20px',
            borderRadius: 'var(--radius-md)',
            boxShadow: 'var(--shadow-lg)',
            fontSize: '14px',
            fontWeight: 600,
            animation: 'fadeIn 0.2s ease',
          }}
        >
          {notification.msg}
        </div>
      )}

      {/* Header */}
      <Header user={user} onLogout={handleLogout} health={health} />

      <div style={{ display: 'flex', flex: 1, width: '100%', minWidth: 0 }}>
        {/* Navigation Sidebar */}
        <Sidebar
          activeTab={activeTab}
          setActiveTab={setActiveTab}
          pendingCount={pendingQuestions.length}
        />

        {/* Main Content Area — Full Width Maximized */}
        <main
          style={{
            flex: 1,
            width: '100%',
            minWidth: 0,
            padding: '24px 32px',
            overflowY: 'auto',
          }}
        >
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
              onNavigateReview={() => setActiveTab('review')}
              pendingCount={pendingQuestions.length}
            />
          )}

          {activeTab === 'review' && (
            <div style={{ width: '100%' }}>
              {/* Header & Bulk Selection / Approval Action Bar */}
              <div
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  flexWrap: 'wrap',
                  gap: '16px',
                  marginBottom: '20px',
                  backgroundColor: 'var(--bg-surface)',
                  border: '1px solid var(--border-color)',
                  borderRadius: 'var(--radius-lg)',
                  padding: '18px 24px',
                  boxShadow: 'var(--shadow-sm)',
                }}
              >
                <div>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '12px', flexWrap: 'wrap' }}>
                    <h2 style={{ fontSize: '22px', fontWeight: 800, color: 'var(--text-primary)' }}>
                      Pending Review Queue ({pendingQuestions.length})
                    </h2>
                    <span
                      style={{
                        fontSize: '11px',
                        fontWeight: 700,
                        color: 'var(--accent-success)',
                        backgroundColor: 'rgba(16, 185, 129, 0.12)',
                        border: '1px solid rgba(16, 185, 129, 0.3)',
                        padding: '3px 10px',
                        borderRadius: 'var(--radius-full)',
                      }}
                    >
                      Duplicate Guard Active
                    </span>
                  </div>
                  <p style={{ fontSize: '13px', color: 'var(--text-secondary)', marginTop: '4px' }}>
                    Curate, edit, select all, and batch-approve unique AI-synthesized questions before they enter live student gameplay.
                  </p>
                </div>

                {pendingQuestions.length > 0 && (
                  <div
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: '10px',
                      flexWrap: 'wrap',
                    }}
                  >
                    {/* View Density Toggle */}
                    <div
                      style={{
                        display: 'inline-flex',
                        backgroundColor: 'var(--bg-card)',
                        border: '1px solid var(--border-color)',
                        borderRadius: 'var(--radius-md)',
                        padding: '3px',
                      }}
                    >
                      <button
                        type="button"
                        onClick={() => setReviewLayout('list')}
                        style={{
                          padding: '6px 10px',
                          fontSize: '12px',
                          fontWeight: 600,
                          border: 'none',
                          borderRadius: 'var(--radius-sm)',
                          cursor: 'pointer',
                          backgroundColor:
                            reviewLayout === 'list' ? 'var(--accent-primary)' : 'transparent',
                          color: reviewLayout === 'list' ? '#ffffff' : 'var(--text-secondary)',
                        }}
                      >
                        Full Width
                      </button>
                      <button
                        type="button"
                        onClick={() => setReviewLayout('grid')}
                        style={{
                          padding: '6px 10px',
                          fontSize: '12px',
                          fontWeight: 600,
                          border: 'none',
                          borderRadius: 'var(--radius-sm)',
                          cursor: 'pointer',
                          backgroundColor:
                            reviewLayout === 'grid' ? 'var(--accent-primary)' : 'transparent',
                          color: reviewLayout === 'grid' ? '#ffffff' : 'var(--text-secondary)',
                        }}
                      >
                        Multi-Column
                      </button>
                    </div>

                    {/* Select All / Deselect All Button */}
                    <button
                      type="button"
                      onClick={handleSelectAllToggle}
                      disabled={bulkProcessing}
                      style={{
                        display: 'inline-flex',
                        alignItems: 'center',
                        padding: '9px 15px',
                        backgroundColor: allSelected
                          ? 'rgba(59, 130, 246, 0.18)'
                          : 'var(--bg-card)',
                        border: allSelected
                          ? '1px solid var(--accent-primary)'
                          : '1px solid var(--border-color)',
                        color: allSelected ? '#93c5fd' : 'var(--text-primary)',
                        borderRadius: 'var(--radius-md)',
                        fontSize: '13px',
                        fontWeight: 700,
                        cursor: bulkProcessing ? 'not-allowed' : 'pointer',
                        transition: 'all var(--transition-fast)',
                      }}
                    >
                      <span>
                        {allSelected
                          ? `Deselect All (${pendingQuestions.length})`
                          : `Select All (${pendingQuestions.length})`}
                      </span>
                    </button>

                    {/* Discard Selected Button */}
                    {selectedQuestionIds.length > 0 && (
                      <button
                        type="button"
                        onClick={handleBulkRejectSelected}
                        disabled={bulkProcessing}
                        style={{
                          padding: '9px 14px',
                          backgroundColor: 'rgba(239, 68, 68, 0.12)',
                          border: '1px solid var(--accent-danger)',
                          color: '#fca5a5',
                          borderRadius: 'var(--radius-md)',
                          fontSize: '13px',
                          fontWeight: 600,
                          cursor: bulkProcessing ? 'not-allowed' : 'pointer',
                        }}
                      >
                        Discard Selected ({selectedQuestionIds.length})
                      </button>
                    )}

                    {/* Approve & Publish Selected Button */}
                    {selectedQuestionIds.length > 0 && (
                      <button
                        type="button"
                        onClick={() => handleBulkApproveSelected(selectedQuestionIds, false)}
                        disabled={bulkProcessing}
                        style={{
                          padding: '9px 16px',
                          backgroundColor: 'var(--accent-primary)',
                          color: '#ffffff',
                          border: 'none',
                          borderRadius: 'var(--radius-md)',
                          fontSize: '13px',
                          fontWeight: 700,
                          cursor: bulkProcessing ? 'not-allowed' : 'pointer',
                          display: 'inline-flex',
                          alignItems: 'center',
                          boxShadow: '0 2px 10px rgba(59, 130, 246, 0.35)',
                        }}
                      >
                        {bulkProcessing
                          ? 'Publishing...'
                          : `Approve & Publish Selected (${selectedQuestionIds.length})`}
                      </button>
                    )}

                    {/* One-Click Select All & Approve & Publish All Button */}
                    <button
                      type="button"
                      onClick={() =>
                        handleBulkApproveSelected(
                          pendingQuestions.map((q) => q.id),
                          true
                        )
                      }
                      disabled={bulkProcessing}
                      style={{
                        padding: '9px 18px',
                        backgroundColor: 'var(--accent-success)',
                        color: '#ffffff',
                        border: 'none',
                        borderRadius: 'var(--radius-md)',
                        fontSize: '13px',
                        fontWeight: 700,
                        cursor: bulkProcessing ? 'not-allowed' : 'pointer',
                        display: 'inline-flex',
                        alignItems: 'center',
                        boxShadow: '0 2px 10px rgba(16, 185, 129, 0.35)',
                      }}
                    >
                      {bulkProcessing
                        ? 'Publishing All...'
                        : `Select All & Approve (${pendingQuestions.length})`}
                    </button>
                  </div>
                )}
              </div>

              {loadingPending ? (
                <div style={{ textAlign: 'center', padding: '60px 20px' }}>
                  <div
                    className="animate-spin"
                    style={{
                      display: 'inline-block',
                      width: '28px',
                      height: '28px',
                      border: '3px solid var(--border-color)',
                      borderTopColor: 'var(--accent-primary)',
                      borderRadius: '50%',
                    }}
                  ></div>
                </div>
              ) : pendingQuestions.length === 0 ? (
                <div
                  style={{
                    textAlign: 'center',
                    padding: '70px 20px',
                    backgroundColor: 'var(--bg-surface)',
                    borderRadius: 'var(--radius-lg)',
                    border: '1px solid var(--border-color)',
                  }}
                >
                  <div style={{ fontSize: '42px', marginBottom: '12px' }}>✨</div>
                  <h3 style={{ fontSize: '18px', fontWeight: 700, color: 'var(--text-primary)' }}>
                    Review Queue is Clear!
                  </h3>
                  <p
                    style={{
                      fontSize: '14px',
                      color: 'var(--text-secondary)',
                      marginTop: '6px',
                      maxWidth: '460px',
                      margin: '6px auto 20px',
                    }}
                  >
                    All AI-generated questions have been reviewed and published. Upload a new PDF to generate more curriculum questions.
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
                    Ingest Lesson PDF
                  </button>
                </div>
              ) : (
                <div
                  style={{
                    display: 'grid',
                    gridTemplateColumns:
                      reviewLayout === 'grid' && pendingQuestions.length > 1
                        ? 'repeat(auto-fill, minmax(560px, 1fr))'
                        : '1fr',
                    gap: '16px',
                    width: '100%',
                  }}
                >
                  {pendingQuestions.map((q) => (
                    <QuestionCard
                      key={q.id}
                      question={q}
                      isPending={true}
                      selected={selectedQuestionIds.includes(q.id)}
                      onToggleSelect={handleToggleSelectQuestion}
                      onApprove={handleApproveQuestion}
                      onReject={handleRejectQuestion}
                    />
                  ))}
                </div>
              )}
            </div>
          )}

          {activeTab === 'bank' && <QuestionBank biomes={biomes} />}

          {activeTab === 'users' && (
            <RegisteredUsersView
              onNotify={notify}
              onUsersChanged={loadUserDataAndStats}
            />
          )}

          {activeTab === 'leaderboard' && <LeaderboardView />}
        </main>
      </div>
    </div>
  );
}

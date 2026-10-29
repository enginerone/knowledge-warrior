import React from 'react';

export default function Sidebar({ activeTab, setActiveTab, pendingCount = 0 }) {
  const navItems = [
    { id: 'dashboard', label: 'Dashboard', icon: '📊' },
    { id: 'users', label: 'Registered Users', icon: '👥' },
    { id: 'ingestion', label: 'PDF AI Ingestion', icon: '🤖' },
    { id: 'review', label: 'Review Queue', icon: '📝', badge: pendingCount },
    { id: 'bank', label: 'Question Bank', icon: '📚' },
    { id: 'leaderboard', label: 'Leaderboard', icon: '🏆' },
  ];

  return (
    <aside style={{
      width: '240px',
      backgroundColor: 'var(--bg-surface)',
      borderRight: '1px solid var(--border-color)',
      padding: '20px 14px',
      display: 'flex',
      flexDirection: 'column',
      gap: '6px',
      flexShrink: 0,
    }}>
      <div style={{
        fontSize: '11px',
        fontWeight: 700,
        textTransform: 'uppercase',
        letterSpacing: '0.08em',
        color: 'var(--text-muted)',
        padding: '0 12px 10px',
      }}>
        Navigation
      </div>

      {navItems.map((item) => {
        const isActive = activeTab === item.id;
        return (
          <button
            key={item.id}
            onClick={() => setActiveTab(item.id)}
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              width: '100%',
              padding: '10px 14px',
              borderRadius: 'var(--radius-md)',
              border: 'none',
              backgroundColor: isActive ? 'var(--bg-card)' : 'transparent',
              color: isActive ? 'var(--text-primary)' : 'var(--text-secondary)',
              cursor: 'pointer',
              fontSize: '14px',
              fontWeight: isActive ? 600 : 500,
              boxShadow: isActive ? 'inset 3px 0 0 var(--accent-primary)' : 'none',
              transition: 'all var(--transition-fast)',
              textAlign: 'left',
            }}
            onMouseEnter={(e) => {
              if (!isActive) {
                e.currentTarget.style.backgroundColor = 'var(--bg-hover)';
                e.currentTarget.style.color = 'var(--text-primary)';
              }
            }}
            onMouseLeave={(e) => {
              if (!isActive) {
                e.currentTarget.style.backgroundColor = 'transparent';
                e.currentTarget.style.color = 'var(--text-secondary)';
              }
            }}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
              <span style={{ fontSize: '16px' }}>{item.icon}</span>
              <span>{item.label}</span>
            </div>
            {item.badge > 0 && (
              <span style={{
                backgroundColor: 'var(--accent-warning)',
                color: '#000000',
                fontSize: '11px',
                fontWeight: 800,
                padding: '2px 7px',
                borderRadius: 'var(--radius-full)',
              }}>
                {item.badge}
              </span>
            )}
          </button>
        );
      })}

      <div style={{ marginTop: 'auto', padding: '16px 12px', borderTop: '1px solid var(--border-color)', fontSize: '12px', color: 'var(--text-muted)' }}>
        <p style={{ fontWeight: 600, color: 'var(--text-secondary)' }}>Phase 2: Cloud & AI</p>
        <p style={{ marginTop: '4px' }}>Gemini 3.8 Flash • Express • PostgreSQL</p>
      </div>
    </aside>
  );
}

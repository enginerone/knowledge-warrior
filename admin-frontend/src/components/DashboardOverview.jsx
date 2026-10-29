import React from 'react';

export default function DashboardOverview({ stats, health, onNavigate }) {
  const cards = [
    {
      title: 'Live Approved Pool',
      value: stats?.questions?.approved ?? 0,
      icon: '🛡️',
      color: 'var(--accent-success)',
      sub: 'Active in mobile client runs',
      action: () => onNavigate('bank'),
      actionLabel: 'Browse Bank',
    },
    {
      title: 'Pending Review',
      value: stats?.questions?.pending ?? 0,
      icon: '⏳',
      color: 'var(--accent-warning)',
      sub: 'AI-generated awaiting curation',
      action: () => onNavigate('review'),
      actionLabel: 'Open Queue',
    },
    {
      title: 'Registered Warriors',
      value: stats?.playersCount ?? 0,
      icon: '👥',
      color: 'var(--accent-primary)',
      sub: 'Registered player accounts',
      action: () => onNavigate('users'),
      actionLabel: 'Track Registered Users',
    },
    {
      title: 'PDF Curriculum Uploads',
      value: stats?.uploadsCount ?? 0,
      icon: '📑',
      color: '#a855f7',
      sub: 'Textbook chapters parsed',
      action: () => onNavigate('ingestion'),
      actionLabel: 'Ingest New PDF',
    },
  ];

  return (
    <div>
      <div style={{ marginBottom: '28px' }}>
        <h2 style={{ fontSize: '26px', fontWeight: 800, color: 'var(--text-primary)' }}>
          Curriculum & Game Operations Dashboard
        </h2>
        <p style={{ fontSize: '14px', color: 'var(--text-secondary)', marginTop: '4px' }}>
          Overview of live question pools, AI curriculum synthesis, and cloud infrastructure status.
        </p>
      </div>

      {/* Metric Cards Grid */}
      <div style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))',
        gap: '20px',
        marginBottom: '32px',
      }}>
        {cards.map((c, idx) => (
          <div
            key={idx}
            style={{
              backgroundColor: 'var(--bg-surface)',
              border: '1px solid var(--border-color)',
              borderRadius: 'var(--radius-lg)',
              padding: '24px',
              boxShadow: 'var(--shadow-sm)',
              display: 'flex',
              flexDirection: 'column',
              justifyContent: 'space-between',
            }}
          >
            <div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
                <span style={{ fontSize: '13px', fontWeight: 600, color: 'var(--text-secondary)' }}>
                  {c.title}
                </span>
                <span style={{ fontSize: '22px' }}>{c.icon}</span>
              </div>
              <div style={{ fontSize: '32px', fontWeight: 800, color: c.color, letterSpacing: '-0.02em' }}>
                {c.value}
              </div>
              <div style={{ fontSize: '12px', color: 'var(--text-muted)', marginTop: '4px' }}>
                {c.sub}
              </div>
            </div>

            <button
              onClick={c.action}
              style={{
                marginTop: '18px',
                padding: '8px 12px',
                backgroundColor: 'var(--bg-card)',
                border: '1px solid var(--border-color)',
                borderRadius: 'var(--radius-md)',
                color: 'var(--text-primary)',
                fontSize: '12px',
                fontWeight: 600,
                cursor: 'pointer',
                textAlign: 'center',
                transition: 'all var(--transition-fast)',
              }}
              onMouseEnter={(e) => {
                e.currentTarget.style.backgroundColor = 'var(--bg-hover)';
                e.currentTarget.style.borderColor = c.color;
              }}
              onMouseLeave={(e) => {
                e.currentTarget.style.backgroundColor = 'var(--bg-card)';
                e.currentTarget.style.borderColor = 'var(--border-color)';
              }}
            >
              {c.actionLabel}
            </button>
          </div>
        ))}
      </div>

      {/* Cloud & AI Topology Card */}
      <div style={{
        backgroundColor: 'var(--bg-surface)',
        border: '1px solid var(--border-color)',
        borderRadius: 'var(--radius-lg)',
        padding: '24px',
        boxShadow: 'var(--shadow-sm)',
      }}>
        <h3 style={{ fontSize: '16px', fontWeight: 700, color: 'var(--text-primary)', marginBottom: '16px', display: 'flex', alignItems: 'center', gap: '8px' }}>
          <span>🌐</span> Infrastructure Topology & Cloud Health
        </h3>

        <div style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
          gap: '16px',
        }}>
          <div style={{ padding: '14px', backgroundColor: 'var(--bg-card)', borderRadius: 'var(--radius-md)' }}>
            <span style={{ fontSize: '12px', color: 'var(--text-muted)', display: 'block' }}>Database Service</span>
            <span style={{ fontSize: '14px', fontWeight: 600, color: 'var(--text-primary)' }}>
              PostgreSQL v17 ({health?.databaseName || 'knowar_db'})
            </span>
          </div>

          <div style={{ padding: '14px', backgroundColor: 'var(--bg-card)', borderRadius: 'var(--radius-md)' }}>
            <span style={{ fontSize: '12px', color: 'var(--text-muted)', display: 'block' }}>AI Pipeline Model</span>
            <span style={{ fontSize: '14px', fontWeight: 600, color: 'var(--accent-primary)' }}>
              Google Gemini 3.8 Flash
            </span>
          </div>

          <div style={{ padding: '14px', backgroundColor: 'var(--bg-card)', borderRadius: 'var(--radius-md)' }}>
            <span style={{ fontSize: '12px', color: 'var(--text-muted)', display: 'block' }}>Server Uptime</span>
            <span style={{ fontSize: '14px', fontWeight: 600, color: 'var(--accent-success)' }}>
              {health?.uptimeSeconds ? `${health.uptimeSeconds} seconds online` : 'Active'}
            </span>
          </div>

          <div style={{ padding: '14px', backgroundColor: 'var(--bg-card)', borderRadius: 'var(--radius-md)' }}>
            <span style={{ fontSize: '12px', color: 'var(--text-muted)', display: 'block' }}>Client Deployment</span>
            <span style={{ fontSize: '14px', fontWeight: 600, color: 'var(--text-primary)' }}>
              Unity 2D URP Android Client
            </span>
          </div>
        </div>
      </div>
    </div>
  );
}

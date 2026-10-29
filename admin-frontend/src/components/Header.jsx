import React from 'react';

export default function Header({ user, onLogout, health }) {
  return (
    <header style={{
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      padding: '16px 28px',
      backgroundColor: 'var(--bg-surface)',
      borderBottom: '1px solid var(--border-color)',
      position: 'sticky',
      top: 0,
      zIndex: 40,
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: '14px' }}>
        <div style={{
          width: '38px',
          height: '38px',
          borderRadius: '10px',
          background: 'linear-gradient(135deg, #3b82f6, #1d4ed8)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontSize: '20px',
          boxShadow: '0 0 16px rgba(59, 130, 246, 0.4)',
        }}>
          ⚔️
        </div>
        <div>
          <h1 style={{ fontSize: '18px', fontWeight: 800, color: 'var(--text-primary)', letterSpacing: '-0.01em' }}>
            KNOWLEDGE WARRIOR
          </h1>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '12px', color: 'var(--text-secondary)' }}>
            <span>Curriculum Admin Portal</span>
            <span>•</span>
            <span style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: '4px',
              color: health?.database === 'connected' ? 'var(--accent-success)' : 'var(--accent-warning)',
              fontWeight: 600,
            }}>
              <span style={{
                width: '7px',
                height: '7px',
                borderRadius: '50%',
                backgroundColor: health?.database === 'connected' ? 'var(--accent-success)' : 'var(--accent-warning)',
              }}></span>
              {health?.database === 'connected' ? 'PostgreSQL Online' : 'Connecting DB...'}
            </span>
          </div>
        </div>
      </div>

      <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
        {user ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
            <div style={{
              display: 'flex',
              alignItems: 'center',
              gap: '8px',
              padding: '6px 14px',
              backgroundColor: 'var(--bg-card)',
              border: '1px solid var(--border-color)',
              borderRadius: 'var(--radius-full)',
              fontSize: '13px',
            }}>
              <span style={{
                width: '8px',
                height: '8px',
                borderRadius: '50%',
                backgroundColor: 'var(--accent-success)',
              }}></span>
              <span style={{ fontWeight: 600, color: 'var(--text-primary)' }}>{user.username}</span>
              <span style={{
                fontSize: '11px',
                backgroundColor: 'rgba(59, 130, 246, 0.2)',
                color: 'var(--accent-primary)',
                padding: '1px 7px',
                borderRadius: '4px',
                textTransform: 'uppercase',
                fontWeight: 700,
              }}>
                {user.role}
              </span>
            </div>
            <button
              onClick={onLogout}
              style={{
                backgroundColor: 'transparent',
                border: '1px solid var(--border-color)',
                color: 'var(--text-secondary)',
                padding: '7px 14px',
                borderRadius: 'var(--radius-md)',
                cursor: 'pointer',
                fontSize: '13px',
                fontWeight: 500,
                transition: 'all var(--transition-fast)',
              }}
              onMouseEnter={(e) => {
                e.currentTarget.style.color = 'var(--accent-danger)';
                e.currentTarget.style.borderColor = 'var(--accent-danger)';
              }}
              onMouseLeave={(e) => {
                e.currentTarget.style.color = 'var(--text-secondary)';
                e.currentTarget.style.borderColor = 'var(--border-color)';
              }}
            >
              Sign Out
            </button>
          </div>
        ) : (
          <button
            onClick={onOpenLogin}
            style={{
              backgroundColor: 'var(--accent-primary)',
              color: '#ffffff',
              border: 'none',
              padding: '8px 18px',
              borderRadius: 'var(--radius-md)',
              cursor: 'pointer',
              fontSize: '13px',
              fontWeight: 600,
              boxShadow: 'var(--shadow-sm)',
              display: 'flex',
              alignItems: 'center',
              gap: '6px',
            }}
          >
            🔑 Educator Login
          </button>
        )}
      </div>
    </header>
  );
}

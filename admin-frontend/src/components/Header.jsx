import React from 'react';
import brandLogo from '../assets/brand-logo.png';

export default function Header({ user, onLogout }) {
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
        <img
          src={brandLogo}
          alt="Knowledge Warrior Logo"
          style={{
            width: '42px',
            height: '42px',
            borderRadius: '10px',
            objectFit: 'cover',
            border: '1px solid rgba(255, 210, 77, 0.55)',
            boxShadow: '0 0 16px rgba(77, 225, 255, 0.32)',
          }}
        />
        <div>
          <h1 style={{ fontSize: '18px', fontWeight: 800, color: 'var(--text-primary)', letterSpacing: '-0.01em' }}>
            KNOWLEDGE WARRIOR
          </h1>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '12px', color: 'var(--text-secondary)' }}>
            <span>Curriculum Admin Portal</span>
          </div>
        </div>
      </div>

      <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
        {user && (
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
        )}
      </div>
    </header>
  );
}

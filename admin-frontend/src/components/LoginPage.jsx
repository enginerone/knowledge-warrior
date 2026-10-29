import React, { useState } from 'react';

export default function LoginPage({ onLoginSuccess, health }) {
  const [username, setUsername] = useState('admin');
  const [password, setPassword] = useState('AdminPassword123!');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!username.trim() || !password.trim()) {
      setError('Please provide both administrator username and password.');
      return;
    }

    setLoading(true);
    setError('');

    try {
      await onLoginSuccess(username.trim(), password);
    } catch (err) {
      setError(err.message || 'Authentication failed. Please verify your admin credentials.');
    } finally {
      setLoading(false);
    }
  };

  const fillDevCredentials = () => {
    setUsername('admin');
    setPassword('AdminPassword123!');
    setError('');
  };

  return (
    <div style={{
      minHeight: '100vh',
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      justifyContent: 'center',
      backgroundColor: 'var(--bg-primary)',
      backgroundImage: 'radial-gradient(circle at 50% 20%, rgba(59, 130, 246, 0.12) 0%, transparent 60%)',
      padding: '24px',
      position: 'relative',
    }}>
      {/* Brand Card */}
      <div style={{
        width: '100%',
        maxWidth: '440px',
        backgroundColor: 'var(--bg-surface)',
        border: '1px solid var(--border-color)',
        borderRadius: 'var(--radius-lg)',
        padding: '40px 36px',
        boxShadow: 'var(--shadow-lg), 0 0 35px rgba(59, 130, 246, 0.1)',
        backdropFilter: 'blur(16px)',
        animation: 'fadeIn 0.3s ease-out',
      }}>
        {/* Header Icon & Title */}
        <div style={{ textAlign: 'center', marginBottom: '28px' }}>
          <div style={{
            width: '56px',
            height: '56px',
            borderRadius: '16px',
            background: 'linear-gradient(135deg, #3b82f6, #1d4ed8)',
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: '28px',
            boxShadow: '0 0 24px rgba(59, 130, 246, 0.45)',
            marginBottom: '16px',
          }}>
            ⚔️
          </div>

          <h1 style={{
            fontSize: '24px',
            fontWeight: 800,
            color: 'var(--text-primary)',
            letterSpacing: '-0.02em',
          }}>
            KNOWLEDGE WARRIOR
          </h1>
          <p style={{
            fontSize: '13px',
            color: 'var(--text-secondary)',
            marginTop: '6px',
            fontWeight: 500,
          }}>
            Developer & Administrator Operations Portal
          </p>
        </div>

        {/* Security Notice */}
        <div style={{
          backgroundColor: 'rgba(59, 130, 246, 0.06)',
          border: '1px solid rgba(59, 130, 246, 0.2)',
          borderRadius: 'var(--radius-md)',
          padding: '10px 14px',
          fontSize: '12px',
          color: 'var(--text-secondary)',
          marginBottom: '22px',
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
        }}>
          <span>🔒</span>
          <span>Developer-only console. Player registration & login take place inside the Unity game client.</span>
        </div>

        {/* Error message */}
        {error && (
          <div style={{
            padding: '12px 14px',
            backgroundColor: 'var(--accent-danger-glow)',
            border: '1px solid var(--accent-danger)',
            borderRadius: 'var(--radius-md)',
            color: '#fca5a5',
            fontSize: '13px',
            marginBottom: '20px',
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
          }}>
            <span>⚠️</span>
            <span>{error}</span>
          </div>
        )}

        {/* Admin Login Form */}
        <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '18px' }}>
          <div>
            <label style={{
              display: 'block',
              fontSize: '13px',
              fontWeight: 600,
              color: 'var(--text-secondary)',
              marginBottom: '6px',
            }}>
              Administrator Username
            </label>
            <input
              type="text"
              required
              autoFocus
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              placeholder="admin"
              style={{
                width: '100%',
                padding: '11px 14px',
                backgroundColor: 'var(--bg-card)',
                border: '1px solid var(--border-color)',
                borderRadius: 'var(--radius-md)',
                color: 'var(--text-primary)',
                fontSize: '14px',
                outline: 'none',
                transition: 'border-color var(--transition-fast)',
              }}
              onFocus={(e) => { e.currentTarget.style.borderColor = 'var(--accent-primary)'; }}
              onBlur={(e) => { e.currentTarget.style.borderColor = 'var(--border-color)'; }}
            />
          </div>

          <div>
            <label style={{
              display: 'block',
              fontSize: '13px',
              fontWeight: 600,
              color: 'var(--text-secondary)',
              marginBottom: '6px',
            }}>
              Password
            </label>
            <input
              type="password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••••••"
              style={{
                width: '100%',
                padding: '11px 14px',
                backgroundColor: 'var(--bg-card)',
                border: '1px solid var(--border-color)',
                borderRadius: 'var(--radius-md)',
                color: 'var(--text-primary)',
                fontSize: '14px',
                outline: 'none',
                transition: 'border-color var(--transition-fast)',
              }}
              onFocus={(e) => { e.currentTarget.style.borderColor = 'var(--accent-primary)'; }}
              onBlur={(e) => { e.currentTarget.style.borderColor = 'var(--border-color)'; }}
            />
          </div>

          {/* Quick Dev Credentials helper */}
          <div style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            fontSize: '12px',
            color: 'var(--text-muted)',
            padding: '2px 0',
          }}>
            <span>Admin Account: <strong>admin</strong></span>
            <button
              type="button"
              onClick={fillDevCredentials}
              style={{
                background: 'transparent',
                border: 'none',
                color: 'var(--accent-primary)',
                cursor: 'pointer',
                fontWeight: 600,
                fontSize: '12px',
                textDecoration: 'underline',
              }}
            >
              Fill Default Credentials
            </button>
          </div>

          {/* Sign In Button */}
          <button
            type="submit"
            disabled={loading}
            style={{
              marginTop: '8px',
              padding: '13px',
              backgroundColor: 'var(--accent-primary)',
              color: '#ffffff',
              border: 'none',
              borderRadius: 'var(--radius-md)',
              fontSize: '15px',
              fontWeight: 700,
              cursor: loading ? 'not-allowed' : 'pointer',
              opacity: loading ? 0.75 : 1,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              gap: '8px',
              boxShadow: '0 4px 14px rgba(59, 130, 246, 0.4)',
              transition: 'all var(--transition-fast)',
            }}
            onMouseEnter={(e) => { if (!loading) e.currentTarget.style.backgroundColor = 'var(--accent-primary-hover)'; }}
            onMouseLeave={(e) => { if (!loading) e.currentTarget.style.backgroundColor = 'var(--accent-primary)'; }}
          >
            {loading ? (
              <span>Authenticating...</span>
            ) : (
              <span>Sign In to Admin Console</span>
            )}
          </button>
        </form>
      </div>

      {/* Cloud & DB Status Footer */}
      <div style={{
        marginTop: '28px',
        display: 'flex',
        alignItems: 'center',
        gap: '12px',
        fontSize: '12px',
        color: 'var(--text-muted)',
      }}>
        <span style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '6px',
          color: health?.database === 'connected' ? 'var(--accent-success)' : 'var(--text-muted)',
          fontWeight: 600,
        }}>
          <span style={{
            width: '7px',
            height: '7px',
            borderRadius: '50%',
            backgroundColor: health?.database === 'connected' ? 'var(--accent-success)' : 'var(--accent-warning)',
          }}></span>
          {health?.database === 'connected' ? 'PostgreSQL Online' : 'Connecting Database...'}
        </span>
        <span>•</span>
        <span>AI Engine: Gemini 3.8 Flash</span>
        <span>•</span>
        <span>v1.0.0</span>
      </div>
    </div>
  );
}

import React, { useState, useEffect } from 'react';
import { api } from '../services/api';

export default function RegisteredUsersView({ onNotify, onUsersChanged }) {
  const [users, setUsers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [actionUserId, setActionUserId] = useState(null);
  const [search, setSearch] = useState('');
  const [error, setError] = useState('');

  const fetchUsers = async () => {
    setLoading(true);
    setError('');
    try {
      const data = await api.getRegisteredUsers();
      setUsers(data.users || []);
    } catch (err) {
      setError(err.message || 'Failed to load registered users.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchUsers();
  }, []);

  const handleToggleBan = async (user) => {
    const nextBanned = !user.isBanned;
    const actionVerb = nextBanned ? 'ban (disable)' : 'unban (enable)';
    if (!window.confirm(`Are you sure you want to ${actionVerb} user @${user.username}?`)) {
      return;
    }

    setActionUserId(user.id);
    setError('');
    try {
      const res = await api.setUserBanStatus(user.id, nextBanned);
      setUsers((prev) =>
        prev.map((u) =>
          u.id === user.id
            ? { ...u, isBanned: res.user?.isBanned ?? nextBanned, bannedAt: res.user?.bannedAt ?? null }
            : u
        )
      );
      if (onNotify) {
        onNotify(res.message || `Updated account status for @${user.username}.`);
      }
      if (onUsersChanged) {
        onUsersChanged();
      }
    } catch (err) {
      const msg = err.message || `Failed to ${actionVerb} user.`;
      setError(msg);
      if (onNotify) onNotify(msg, 'error');
    } finally {
      setActionUserId(null);
    }
  };

  const handleDeleteUser = async (user) => {
    if (
      !window.confirm(
        `Are you sure you want to permanently delete user @${user.username}? This action cannot be undone.`
      )
    ) {
      return;
    }

    setActionUserId(user.id);
    setError('');
    try {
      const res = await api.deleteUser(user.id);
      setUsers((prev) => prev.filter((u) => u.id !== user.id));
      if (onNotify) {
        onNotify(res.message || `User @${user.username} deleted.`);
      }
      if (onUsersChanged) {
        onUsersChanged();
      }
    } catch (err) {
      const msg = err.message || 'Failed to delete user.';
      setError(msg);
      if (onNotify) onNotify(msg, 'error');
    } finally {
      setActionUserId(null);
    }
  };

  const filtered = users.filter((u) => {
    const q = search.trim().toLowerCase();
    if (!q) return true;
    return (
      (u.username || '').toLowerCase().includes(q) ||
      (u.email || '').toLowerCase().includes(q)
    );
  });

  const registeredWithEmailCount = users.filter((u) => Boolean(u.email)).length;
  const bannedUsersCount = users.filter((u) => Boolean(u.isBanned)).length;

  return (
    <div>
      {/* Header & Refresh */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px', flexWrap: 'wrap', gap: '14px' }}>
        <div>
          <h2 style={{ fontSize: '24px', fontWeight: 800, color: 'var(--text-primary)' }}>
            Registered Users Directory ({users.length})
          </h2>
          <p style={{ fontSize: '13px', color: 'var(--text-secondary)', marginTop: '4px' }}>
            Real-time PostgreSQL audit of every player account, verified email address, registration timestamp, learning progress, and account moderation controls.
          </p>
        </div>

        <div style={{ display: 'flex', gap: '12px', alignItems: 'center' }}>
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search username or email..."
            style={{
              padding: '9px 14px',
              backgroundColor: 'var(--bg-surface)',
              border: '1px solid var(--border-color)',
              borderRadius: 'var(--radius-md)',
              color: 'var(--text-primary)',
              fontSize: '13px',
              width: '240px',
              outline: 'none',
            }}
          />
          <button
            onClick={fetchUsers}
            disabled={loading}
            style={{
              padding: '9px 16px',
              backgroundColor: 'var(--accent-primary)',
              color: '#ffffff',
              border: 'none',
              borderRadius: 'var(--radius-md)',
              fontSize: '13px',
              fontWeight: 600,
              cursor: loading ? 'not-allowed' : 'pointer',
            }}
          >
            {loading ? 'Refreshing...' : 'Refresh List'}
          </button>
        </div>
      </div>

      {/* Summary Pills */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '16px', marginBottom: '24px' }}>
        <div style={{ padding: '18px', backgroundColor: 'var(--bg-surface)', border: '1px solid var(--border-color)', borderRadius: 'var(--radius-md)' }}>
          <div style={{ fontSize: '12px', color: 'var(--text-muted)', fontWeight: 600 }}>Total Tracked Players</div>
          <div style={{ fontSize: '26px', fontWeight: 800, color: 'var(--accent-primary)', marginTop: '4px' }}>{users.length}</div>
        </div>
        <div style={{ padding: '18px', backgroundColor: 'var(--bg-surface)', border: '1px solid var(--border-color)', borderRadius: 'var(--radius-md)' }}>
          <div style={{ fontSize: '12px', color: 'var(--text-muted)', fontWeight: 600 }}>Verified Email Accounts</div>
          <div style={{ fontSize: '26px', fontWeight: 800, color: 'var(--accent-success)', marginTop: '4px' }}>{registeredWithEmailCount}</div>
        </div>
        <div style={{ padding: '18px', backgroundColor: 'var(--bg-surface)', border: '1px solid var(--border-color)', borderRadius: 'var(--radius-md)' }}>
          <div style={{ fontSize: '12px', color: 'var(--text-muted)', fontWeight: 600 }}>Banned (Disabled) Accounts</div>
          <div style={{ fontSize: '26px', fontWeight: 800, color: 'var(--accent-danger)', marginTop: '4px' }}>{bannedUsersCount}</div>
        </div>
      </div>

      {error && (
        <div style={{
          padding: '12px 16px',
          backgroundColor: 'var(--accent-danger-glow)',
          border: '1px solid var(--accent-danger)',
          borderRadius: 'var(--radius-md)',
          color: '#fca5a5',
          fontSize: '13px',
          marginBottom: '20px',
        }}>
          {error}
        </div>
      )}

      {/* Users Table */}
      <div style={{
        backgroundColor: 'var(--bg-surface)',
        border: '1px solid var(--border-color)',
        borderRadius: 'var(--radius-lg)',
        overflow: 'hidden',
      }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left', fontSize: '13px' }}>
          <thead>
            <tr style={{ backgroundColor: 'var(--bg-card)', borderBottom: '1px solid var(--border-color)', color: 'var(--text-muted)', fontSize: '11px', textTransform: 'uppercase', letterSpacing: '0.06em' }}>
              <th style={{ padding: '14px 18px' }}>Username</th>
              <th style={{ padding: '14px 18px' }}>Email Address</th>
              <th style={{ padding: '14px 18px' }}>Status</th>
              <th style={{ padding: '14px 18px' }}>Level & XP</th>
              <th style={{ padding: '14px 18px' }}>Accuracy</th>
              <th style={{ padding: '14px 18px' }}>Registered At</th>
              <th style={{ padding: '14px 18px' }}>Last Login / Sync</th>
              <th style={{ padding: '14px 18px', textAlign: 'right' }}>Actions</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={8} style={{ padding: '40px', textAlign: 'center', color: 'var(--text-secondary)' }}>
                  Loading registered users from PostgreSQL...
                </td>
              </tr>
            ) : filtered.length === 0 ? (
              <tr>
                <td colSpan={8} style={{ padding: '40px', textAlign: 'center', color: 'var(--text-muted)' }}>
                  No matching registered users found.
                </td>
              </tr>
            ) : (
              filtered.map((u) => {
                const acc = u.questionsAnswered > 0 ? Math.round((100 * u.correctAnswers) / u.questionsAnswered) : 0;
                const isBusy = actionUserId === u.id;
                return (
                  <tr
                    key={u.id}
                    style={{
                      borderBottom: '1px solid var(--border-color)',
                      opacity: u.isBanned ? 0.68 : 1,
                      backgroundColor: u.isBanned ? 'rgba(239, 68, 68, 0.04)' : 'transparent',
                    }}
                  >
                    <td style={{ padding: '14px 18px', fontWeight: 700, color: u.isBanned ? 'var(--text-muted)' : 'var(--text-primary)' }}>
                      @{u.username}
                    </td>
                    <td style={{ padding: '14px 18px', color: u.email ? 'var(--text-secondary)' : 'var(--text-muted)' }}>
                      {u.email || '— (Unlinked Device Guest)'}
                    </td>
                    <td style={{ padding: '14px 18px' }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
                        <span style={{
                          padding: '3px 9px',
                          borderRadius: '999px',
                          fontSize: '11px',
                          fontWeight: 700,
                          backgroundColor: u.isRegisteredAccount ? 'rgba(16, 185, 129, 0.15)' : 'rgba(245, 158, 11, 0.15)',
                          color: u.isRegisteredAccount ? 'var(--accent-success)' : 'var(--accent-warning)',
                        }}>
                          {u.isRegisteredAccount ? 'REGISTERED' : 'GUEST'}
                        </span>
                        {u.isBanned && (
                          <span style={{
                            padding: '3px 9px',
                            borderRadius: '999px',
                            fontSize: '11px',
                            fontWeight: 700,
                            backgroundColor: 'rgba(239, 68, 68, 0.18)',
                            border: '1px solid rgba(239, 68, 68, 0.4)',
                            color: '#fca5a5',
                          }}>
                            BANNED (DISABLED)
                          </span>
                        )}
                      </div>
                    </td>
                    <td style={{ padding: '14px 18px', color: 'var(--text-primary)', fontWeight: 600 }}>
                      Lv.{u.currentLevel} <span style={{ color: 'var(--accent-warning)', marginLeft: '6px' }}>({u.totalXp} XP)</span>
                    </td>
                    <td style={{ padding: '14px 18px', color: 'var(--text-secondary)' }}>
                      {u.questionsAnswered > 0 ? `${acc}% (${u.correctAnswers}/${u.questionsAnswered})` : 'No battles yet'}
                    </td>
                    <td style={{ padding: '14px 18px', color: 'var(--text-secondary)' }}>
                      {u.createdAt ? new Date(u.createdAt).toLocaleString() : '—'}
                    </td>
                    <td style={{ padding: '14px 18px', color: 'var(--text-muted)' }}>
                      {u.lastSyncedAt ? new Date(u.lastSyncedAt).toLocaleString() : '—'}
                    </td>
                    <td style={{ padding: '14px 18px', textAlign: 'right' }}>
                      <div style={{ display: 'inline-flex', alignItems: 'center', gap: '8px' }}>
                        <button
                          type="button"
                          onClick={() => handleToggleBan(u)}
                          disabled={isBusy}
                          style={{
                            padding: '6px 12px',
                            borderRadius: 'var(--radius-sm)',
                            fontSize: '12px',
                            fontWeight: 600,
                            cursor: isBusy ? 'not-allowed' : 'pointer',
                            backgroundColor: u.isBanned
                              ? 'rgba(16, 185, 129, 0.14)'
                              : 'rgba(245, 158, 11, 0.14)',
                            border: u.isBanned
                              ? '1px solid var(--accent-success)'
                              : '1px solid var(--accent-warning)',
                            color: u.isBanned ? '#6ee7b7' : '#fcd34d',
                          }}
                        >
                          {u.isBanned ? 'Unban' : 'Ban'}
                        </button>
                        <button
                          type="button"
                          onClick={() => handleDeleteUser(u)}
                          disabled={isBusy}
                          style={{
                            padding: '6px 12px',
                            borderRadius: 'var(--radius-sm)',
                            fontSize: '12px',
                            fontWeight: 600,
                            cursor: isBusy ? 'not-allowed' : 'pointer',
                            backgroundColor: 'rgba(239, 68, 68, 0.14)',
                            border: '1px solid var(--accent-danger)',
                            color: '#fca5a5',
                          }}
                        >
                          Delete
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}


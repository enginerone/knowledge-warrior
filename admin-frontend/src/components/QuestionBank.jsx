import React, { useState, useEffect } from 'react';
import { api } from '../services/api';

export default function QuestionBank({ biomes }) {
  const [questions, setQuestions] = useState([]);
  const [versionHash, setVersionHash] = useState('');
  const [loading, setLoading] = useState(true);
  const [subjectFilter, setSubjectFilter] = useState('');
  const [difficultyFilter, setDifficultyFilter] = useState('');
  const [searchQuery, setSearchQuery] = useState('');

  const fetchQuestions = async () => {
    setLoading(true);
    try {
      const params = {};
      if (subjectFilter) params.subject = subjectFilter;
      if (difficultyFilter) params.difficulty = difficultyFilter;

      const data = await api.getQuestions(params);
      setQuestions(data.questions || []);
      setVersionHash(data.versionHash || '');
    } catch (err) {
      console.error('Failed to load questions:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchQuestions();
  }, [subjectFilter, difficultyFilter]);

  const filteredQuestions = questions.filter((q) => {
    if (!searchQuery) return true;
    const text = (q.question || q.questionText || '').toLowerCase();
    const explanation = (q.explanation || '').toLowerCase();
    return text.includes(searchQuery.toLowerCase()) || explanation.includes(searchQuery.toLowerCase());
  });

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '24px', flexWrap: 'wrap', gap: '16px' }}>
        <div>
          <h2 style={{ fontSize: '24px', fontWeight: 800, color: 'var(--text-primary)' }}>
            📚 Active Question Bank ({filteredQuestions.length})
          </h2>
          <p style={{ fontSize: '13px', color: 'var(--text-secondary)', marginTop: '4px' }}>
            All approved questions deployed to mobile clients.
          </p>
        </div>

        {versionHash && (
          <div style={{
            backgroundColor: 'var(--bg-surface)',
            border: '1px solid var(--border-color)',
            padding: '6px 12px',
            borderRadius: 'var(--radius-md)',
            fontSize: '12px',
          }}>
            <span style={{ color: 'var(--text-muted)' }}>Dataset SHA-256: </span>
            <code style={{ color: 'var(--accent-primary)', fontWeight: 600 }}>
              {versionHash.slice(0, 12)}...
            </code>
          </div>
        )}
      </div>

      {/* Filter Bar */}
      <div style={{
        backgroundColor: 'var(--bg-surface)',
        border: '1px solid var(--border-color)',
        borderRadius: 'var(--radius-lg)',
        padding: '16px 20px',
        marginBottom: '24px',
        display: 'flex',
        gap: '14px',
        flexWrap: 'wrap',
      }}>
        <div style={{ flex: '1 1 240px' }}>
          <input
            type="text"
            placeholder="🔍 Search question text or concepts..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            style={{
              width: '100%',
              padding: '9px 14px',
              backgroundColor: 'var(--bg-card)',
              border: '1px solid var(--border-color)',
              borderRadius: 'var(--radius-md)',
              color: 'var(--text-primary)',
              fontSize: '13px',
              outline: 'none',
            }}
          />
        </div>

        <div style={{ width: '180px' }}>
          <select
            value={subjectFilter}
            onChange={(e) => setSubjectFilter(e.target.value)}
            style={{
              width: '100%',
              padding: '9px 12px',
              backgroundColor: 'var(--bg-card)',
              border: '1px solid var(--border-color)',
              borderRadius: 'var(--radius-md)',
              color: 'var(--text-primary)',
              fontSize: '13px',
              outline: 'none',
            }}
          >
            <option value="">All Subjects (Biomes)</option>
            {(biomes || []).map((b) => (
              <option key={b.id} value={b.slug}>
                {b.name}
              </option>
            ))}
          </select>
        </div>

        <div style={{ width: '160px' }}>
          <select
            value={difficultyFilter}
            onChange={(e) => setDifficultyFilter(e.target.value)}
            style={{
              width: '100%',
              padding: '9px 12px',
              backgroundColor: 'var(--bg-card)',
              border: '1px solid var(--border-color)',
              borderRadius: 'var(--radius-md)',
              color: 'var(--text-primary)',
              fontSize: '13px',
              outline: 'none',
            }}
          >
            <option value="">All Difficulties</option>
            <option value="1">Tier 1 (Beginner)</option>
            <option value="2">Tier 2 (Intermediate)</option>
            <option value="3">Tier 3 (Advanced)</option>
          </select>
        </div>
      </div>

      {/* Question Cards */}
      {loading ? (
        <div style={{ textAlign: 'center', padding: '60px 20px', color: 'var(--text-secondary)' }}>
          <div className="animate-spin" style={{
            display: 'inline-block',
            width: '28px',
            height: '28px',
            border: '3px solid var(--border-color)',
            borderTopColor: 'var(--accent-primary)',
            borderRadius: '50%',
            marginBottom: '10px',
          }}></div>
          <p>Loading question bank...</p>
        </div>
      ) : filteredQuestions.length === 0 ? (
        <div style={{
          textAlign: 'center',
          padding: '60px 20px',
          backgroundColor: 'var(--bg-surface)',
          borderRadius: 'var(--radius-lg)',
          border: '1px solid var(--border-color)',
        }}>
          <p style={{ fontSize: '16px', color: 'var(--text-primary)', fontWeight: 600 }}>
            No questions found matching your filter criteria.
          </p>
        </div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
          {filteredQuestions.map((q) => {
            const choices = q.choices || q.options || [];
            const diffColor = q.difficulty === 1 ? 'var(--accent-success)' : q.difficulty === 2 ? 'var(--accent-warning)' : 'var(--accent-danger)';

            return (
              <div
                key={q.id}
                style={{
                  backgroundColor: 'var(--bg-surface)',
                  border: '1px solid var(--border-color)',
                  borderRadius: 'var(--radius-lg)',
                  padding: '20px 24px',
                  boxShadow: 'var(--shadow-sm)',
                }}
              >
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px', flexWrap: 'wrap', gap: '8px' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <span style={{
                      fontSize: '11px',
                      fontWeight: 700,
                      color: diffColor,
                      border: `1px solid ${diffColor}`,
                      padding: '2px 7px',
                      borderRadius: 'var(--radius-full)',
                    }}>
                      Tier {q.difficulty}
                    </span>
                    <span style={{ fontSize: '12px', fontWeight: 600, color: 'var(--text-secondary)' }}>
                      {q.subject} {q.topic ? `> ${q.topic}` : ''}
                    </span>
                  </div>
                  <span style={{ fontSize: '12px', color: 'var(--accent-primary)', fontWeight: 600 }}>
                    +{q.xpReward || q.difficulty * 20} XP
                  </span>
                </div>

                <h4 style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)', marginBottom: '14px' }}>
                  {q.question || q.questionText}
                </h4>

                <div style={{
                  display: 'grid',
                  gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
                  gap: '8px',
                  marginBottom: '12px',
                }}>
                  {choices.map((c, idx) => {
                    const isCorrect = idx === q.correctIndex;
                    return (
                      <div
                        key={idx}
                        style={{
                          padding: '8px 12px',
                          borderRadius: 'var(--radius-md)',
                          backgroundColor: isCorrect ? 'rgba(16, 185, 129, 0.12)' : 'var(--bg-card)',
                          border: isCorrect ? '1px solid var(--accent-success)' : '1px solid var(--border-color)',
                          fontSize: '13px',
                          color: isCorrect ? '#6ee7b7' : 'var(--text-secondary)',
                          fontWeight: isCorrect ? 600 : 400,
                        }}
                      >
                        <span style={{ fontWeight: 700, marginRight: '6px' }}>{String.fromCharCode(65 + idx)}.</span>
                        {c}
                      </div>
                    );
                  })}
                </div>

                {q.explanation && (
                  <p style={{ fontSize: '12px', color: 'var(--text-muted)', borderLeft: '2px solid var(--border-color)', paddingLeft: '8px' }}>
                    💡 {q.explanation}
                  </p>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

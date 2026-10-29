import React, { useState } from 'react';

export default function QuestionCard({ question, onApprove, onReject, isPending = false }) {
  const [isEditing, setIsEditing] = useState(false);
  const [questionText, setQuestionText] = useState(question.questionText || question.question);
  const [options, setOptions] = useState(question.options || question.choices || ['', '', '', '']);
  const [correctIndex, setCorrectIndex] = useState(question.correctIndex ?? 0);
  const [explanation, setExplanation] = useState(question.explanation || '');
  const [difficulty, setDifficulty] = useState(question.difficulty || 1);
  const [loading, setLoading] = useState(false);

  const handleOptionChange = (idx, value) => {
    const updated = [...options];
    updated[idx] = value;
    setOptions(updated);
  };

  const handleSaveAndApprove = async () => {
    setLoading(true);
    try {
      await onApprove(question.id, {
        questionText,
        options,
        correctIndex,
        explanation,
        difficulty,
      });
      setIsEditing(false);
    } finally {
      setLoading(false);
    }
  };

  const handleRejectClick = async () => {
    if (window.confirm('Are you sure you want to discard this question?')) {
      setLoading(true);
      try {
        await onReject(question.id);
      } finally {
        setLoading(false);
      }
    }
  };

  const difficultyLabel = difficulty === 1 ? 'Tier 1 • Novice' : difficulty === 2 ? 'Tier 2 • Apprentice' : 'Tier 3 • Master';
  const difficultyColor = difficulty === 1 ? 'var(--accent-success)' : difficulty === 2 ? 'var(--accent-warning)' : 'var(--accent-danger)';

  return (
    <div style={{
      backgroundColor: 'var(--bg-surface)',
      border: '1px solid var(--border-color)',
      borderRadius: 'var(--radius-lg)',
      padding: '24px',
      marginBottom: '16px',
      boxShadow: 'var(--shadow-sm)',
      transition: 'border-color var(--transition-fast)',
    }}>
      {/* Header Info */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '14px', flexWrap: 'wrap', gap: '8px' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
          <span style={{
            fontSize: '11px',
            fontWeight: 700,
            color: difficultyColor,
            border: `1px solid ${difficultyColor}`,
            padding: '2px 8px',
            borderRadius: 'var(--radius-full)',
            textTransform: 'uppercase',
          }}>
            {difficultyLabel}
          </span>
          <span style={{
            fontSize: '12px',
            fontWeight: 600,
            color: 'var(--accent-primary)',
            backgroundColor: 'rgba(59, 130, 246, 0.1)',
            padding: '2px 8px',
            borderRadius: 'var(--radius-sm)',
          }}>
            +{difficulty * 20} XP
          </span>
          {question.topicName && (
            <span style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>
              {question.biomeName ? `${question.biomeName} > ` : ''}{question.topicName}
            </span>
          )}
        </div>

        {isPending && (
          <span style={{
            fontSize: '11px',
            fontWeight: 700,
            color: '#fbbf24',
            backgroundColor: 'rgba(245, 158, 11, 0.15)',
            padding: '3px 9px',
            borderRadius: 'var(--radius-full)',
          }}>
            AWAITING EDUCATOR REVIEW
          </span>
        )}
      </div>

      {/* Question Content */}
      {isEditing ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', marginBottom: '16px' }}>
          <div>
            <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '4px' }}>
              Question Text:
            </label>
            <textarea
              value={questionText}
              onChange={(e) => setQuestionText(e.target.value)}
              rows={2}
              style={{
                width: '100%',
                padding: '8px 12px',
                backgroundColor: 'var(--bg-card)',
                border: '1px solid var(--border-color)',
                borderRadius: 'var(--radius-md)',
                color: 'var(--text-primary)',
                fontSize: '14px',
                outline: 'none',
              }}
            />
          </div>

          <div>
            <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '6px' }}>
              Options (Select radio for correct answer):
            </label>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: '8px' }}>
              {options.map((opt, idx) => (
                <div key={idx} style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '8px',
                  backgroundColor: 'var(--bg-card)',
                  padding: '6px 10px',
                  borderRadius: 'var(--radius-md)',
                  border: correctIndex === idx ? '1px solid var(--accent-success)' : '1px solid var(--border-color)',
                }}>
                  <input
                    type="radio"
                    name={`correct-${question.id}`}
                    checked={correctIndex === idx}
                    onChange={() => setCorrectIndex(idx)}
                    style={{ cursor: 'pointer' }}
                  />
                  <input
                    type="text"
                    value={opt}
                    onChange={(e) => handleOptionChange(idx, e.target.value)}
                    style={{
                      width: '100%',
                      background: 'transparent',
                      border: 'none',
                      color: 'var(--text-primary)',
                      fontSize: '13px',
                      outline: 'none',
                    }}
                  />
                </div>
              ))}
            </div>
          </div>

          <div>
            <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '4px' }}>
              Pedagogical Explanation:
            </label>
            <input
              type="text"
              value={explanation}
              onChange={(e) => setExplanation(e.target.value)}
              style={{
                width: '100%',
                padding: '8px 12px',
                backgroundColor: 'var(--bg-card)',
                border: '1px solid var(--border-color)',
                borderRadius: 'var(--radius-md)',
                color: 'var(--text-primary)',
                fontSize: '13px',
                outline: 'none',
              }}
            />
          </div>

          <div style={{ display: 'flex', gap: '10px' }}>
            <div>
              <label style={{ fontSize: '12px', fontWeight: 600, color: 'var(--text-secondary)', marginRight: '6px' }}>
                Difficulty:
              </label>
              <select
                value={difficulty}
                onChange={(e) => setDifficulty(parseInt(e.target.value, 10))}
                style={{
                  padding: '6px 10px',
                  backgroundColor: 'var(--bg-card)',
                  border: '1px solid var(--border-color)',
                  borderRadius: 'var(--radius-sm)',
                  color: 'var(--text-primary)',
                  fontSize: '13px',
                }}
              >
                <option value={1}>Tier 1</option>
                <option value={2}>Tier 2</option>
                <option value={3}>Tier 3</option>
              </select>
            </div>
          </div>
        </div>
      ) : (
        <div>
          <h3 style={{ fontSize: '16px', fontWeight: 700, color: 'var(--text-primary)', marginBottom: '16px' }}>
            {questionText}
          </h3>

          <div style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))',
            gap: '10px',
            marginBottom: '16px',
          }}>
            {options.map((opt, idx) => {
              const isCorrect = idx === correctIndex;
              return (
                <div
                  key={idx}
                  style={{
                    padding: '10px 14px',
                    borderRadius: 'var(--radius-md)',
                    backgroundColor: isCorrect ? 'rgba(16, 185, 129, 0.12)' : 'var(--bg-card)',
                    border: isCorrect ? '1px solid var(--accent-success)' : '1px solid var(--border-color)',
                    display: 'flex',
                    alignItems: 'center',
                    gap: '10px',
                    fontSize: '14px',
                    color: isCorrect ? '#6ee7b7' : 'var(--text-secondary)',
                    fontWeight: isCorrect ? 600 : 400,
                  }}
                >
                  <span style={{
                    width: '20px',
                    height: '20px',
                    borderRadius: '50%',
                    backgroundColor: isCorrect ? 'var(--accent-success)' : 'transparent',
                    border: isCorrect ? 'none' : '1px solid var(--text-muted)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    fontSize: '11px',
                    color: isCorrect ? '#000000' : 'var(--text-muted)',
                    fontWeight: 700,
                    flexShrink: 0,
                  }}>
                    {String.fromCharCode(65 + idx)}
                  </span>
                  <span>{opt}</span>
                </div>
              );
            })}
          </div>

          {explanation && (
            <div style={{
              fontSize: '13px',
              color: 'var(--text-secondary)',
              backgroundColor: 'var(--bg-card)',
              padding: '10px 14px',
              borderRadius: 'var(--radius-md)',
              borderLeft: '3px solid var(--accent-primary)',
              marginBottom: '16px',
            }}>
              <strong style={{ color: 'var(--text-primary)' }}>Explanation: </strong>
              {explanation}
            </div>
          )}
        </div>
      )}

      {/* Action Footer */}
      {isPending && (
        <div style={{
          display: 'flex',
          justifyContent: 'flex-end',
          alignItems: 'center',
          gap: '10px',
          borderTop: '1px solid var(--border-color)',
          paddingTop: '14px',
        }}>
          {isEditing ? (
            <>
              <button
                onClick={() => setIsEditing(false)}
                style={{
                  backgroundColor: 'transparent',
                  border: '1px solid var(--border-color)',
                  color: 'var(--text-secondary)',
                  padding: '7px 14px',
                  borderRadius: 'var(--radius-md)',
                  cursor: 'pointer',
                  fontSize: '13px',
                }}
              >
                Cancel
              </button>
              <button
                onClick={handleSaveAndApprove}
                disabled={loading}
                style={{
                  backgroundColor: 'var(--accent-success)',
                  color: '#ffffff',
                  border: 'none',
                  padding: '8px 16px',
                  borderRadius: 'var(--radius-md)',
                  cursor: 'pointer',
                  fontSize: '13px',
                  fontWeight: 600,
                }}
              >
                {loading ? 'Saving...' : 'Save & Publish to DB'}
              </button>
            </>
          ) : (
            <>
              <button
                onClick={handleRejectClick}
                disabled={loading}
                style={{
                  backgroundColor: 'transparent',
                  border: '1px solid var(--border-color)',
                  color: 'var(--accent-danger)',
                  padding: '7px 14px',
                  borderRadius: 'var(--radius-md)',
                  cursor: 'pointer',
                  fontSize: '13px',
                  fontWeight: 500,
                }}
              >
                Discard
              </button>
              <button
                onClick={() => setIsEditing(true)}
                style={{
                  backgroundColor: 'var(--bg-card)',
                  border: '1px solid var(--border-color)',
                  color: 'var(--text-primary)',
                  padding: '7px 14px',
                  borderRadius: 'var(--radius-md)',
                  cursor: 'pointer',
                  fontSize: '13px',
                  fontWeight: 500,
                }}
              >
                ✏️ Edit
              </button>
              <button
                onClick={handleSaveAndApprove}
                disabled={loading}
                style={{
                  backgroundColor: 'var(--accent-success)',
                  color: '#ffffff',
                  border: 'none',
                  padding: '8px 18px',
                  borderRadius: 'var(--radius-md)',
                  cursor: 'pointer',
                  fontSize: '13px',
                  fontWeight: 700,
                  display: 'flex',
                  alignItems: 'center',
                  gap: '6px',
                  boxShadow: '0 2px 8px rgba(16, 185, 129, 0.3)',
                }}
              >
                <span>✓</span> {loading ? 'Publishing...' : 'Approve & Publish'}
              </button>
            </>
          )}
        </div>
      )}
    </div>
  );
}

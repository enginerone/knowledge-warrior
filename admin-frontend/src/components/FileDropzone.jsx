import React, { useState, useRef } from 'react';
import { api } from '../services/api';

export default function FileDropzone({
  biomes,
  onQuestionsGenerated,
  requireLogin,
  onNavigateReview,
  pendingCount = 0,
}) {
  const [file, setFile] = useState(null);
  const [topicId, setTopicId] = useState('');
  const [count, setCount] = useState(5);
  const [difficulty, setDifficulty] = useState(2);
  const [isDragging, setIsDragging] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [stepMessage, setStepMessage] = useState('');
  const [error, setError] = useState('');
  const [successMsg, setSuccessMsg] = useState('');

  const fileInputRef = useRef(null);

  // Flatten topics for selection dropdown
  const allTopics = (biomes || []).flatMap((b) =>
    (b.topics || []).map((t) => ({ ...t, biomeName: b.name }))
  );

  const handleDragOver = (e) => {
    e.preventDefault();
    setIsDragging(true);
  };

  const handleDragLeave = () => {
    setIsDragging(false);
  };

  const handleDrop = (e) => {
    e.preventDefault();
    setIsDragging(false);
    if (e.dataTransfer.files && e.dataTransfer.files[0]) {
      const droppedFile = e.dataTransfer.files[0];
      if (droppedFile.type === 'application/pdf' || droppedFile.name.endsWith('.pdf')) {
        setFile(droppedFile);
        setError('');
      } else {
        setError('Please drop a valid PDF document.');
      }
    }
  };

  const handleFileSelect = (e) => {
    if (e.target.files && e.target.files[0]) {
      setFile(e.target.files[0]);
      setError('');
    }
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!file) {
      setError('Please select or drop a PDF file first.');
      return;
    }

    if (requireLogin()) {
      return;
    }

    setUploading(true);
    setError('');
    setSuccessMsg('');
    setStepMessage('Extracting text and checking existing database questions to prevent duplicates...');

    try {
      const formData = new FormData();
      formData.append('pdf', file);
      formData.append('count', count);
      formData.append('difficulty', difficulty);
      if (topicId) formData.append('topicId', topicId);

      setStepMessage('Synthesizing unique curriculum questions via Gemini multi-model pipeline...');

      const result = await api.uploadPdf(formData);

      setSuccessMsg(
        `🎉 Generated ${result.questions.length} unique questions from "${file.name}"${
          result.duplicatesFiltered > 0
            ? ` (${result.duplicatesFiltered} duplicate(s) automatically filtered)`
            : ''
        }!`
      );
      setFile(null);
      if (fileInputRef.current) fileInputRef.current.value = '';

      if (onQuestionsGenerated) {
        onQuestionsGenerated(result.questions);
      }
    } catch (err) {
      setError(err.message || 'Failed to process PDF with AI pipeline.');
    } finally {
      setUploading(false);
      setStepMessage('');
    }
  };

  return (
    <div style={{ width: '100%' }}>
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(420px, 1fr))',
          gap: '24px',
          alignItems: 'start',
        }}
      >
        {/* Left Column: Primary PDF Upload & Synthesis Controls */}
        <div
          style={{
            backgroundColor: 'var(--bg-surface)',
            border: '1px solid var(--border-color)',
            borderRadius: 'var(--radius-lg)',
            padding: '28px',
            boxShadow: 'var(--shadow-md)',
          }}
        >
          <div style={{ marginBottom: '22px' }}>
            <h2
              style={{
                fontSize: '22px',
                fontWeight: 800,
                color: 'var(--text-primary)',
                display: 'flex',
                alignItems: 'center',
                gap: '10px',
              }}
            >
              <span>📄</span> PDF AI Question Ingestion Pipeline
            </h2>
            <p style={{ fontSize: '13.5px', color: 'var(--text-secondary)', marginTop: '6px' }}>
              Upload textbook chapters, syllabi, or lecture slides. Gemini extracts key concepts and synthesizes non-repeating RPG multiple-choice questions with educational distractors.
            </p>
          </div>

          {error && (
            <div
              style={{
                padding: '12px 16px',
                backgroundColor: 'var(--accent-danger-glow)',
                border: '1px solid var(--accent-danger)',
                borderRadius: 'var(--radius-md)',
                color: '#fca5a5',
                fontSize: '14px',
                marginBottom: '20px',
              }}
            >
              ❌ {error}
            </div>
          )}

          {successMsg && (
            <div
              style={{
                padding: '12px 16px',
                backgroundColor: 'var(--accent-success-glow)',
                border: '1px solid var(--accent-success)',
                borderRadius: 'var(--radius-md)',
                color: '#6ee7b7',
                fontSize: '14px',
                marginBottom: '20px',
              }}
            >
              {successMsg}
            </div>
          )}

          <form onSubmit={handleSubmit}>
            {/* Dropzone */}
            <div
              onDragOver={handleDragOver}
              onDragLeave={handleDragLeave}
              onDrop={handleDrop}
              onClick={() => fileInputRef.current?.click()}
              style={{
                border: isDragging
                  ? '2px dashed var(--accent-primary)'
                  : '2px dashed var(--border-color)',
                backgroundColor: isDragging ? 'rgba(59, 130, 246, 0.08)' : 'var(--bg-card)',
                borderRadius: 'var(--radius-lg)',
                padding: '36px 20px',
                textAlign: 'center',
                cursor: 'pointer',
                transition: 'all var(--transition-fast)',
                marginBottom: '22px',
              }}
            >
              <input
                type="file"
                ref={fileInputRef}
                onChange={handleFileSelect}
                accept="application/pdf"
                style={{ display: 'none' }}
              />
              <div style={{ fontSize: '40px', marginBottom: '10px' }}>{file ? '📑' : '📥'}</div>
              {file ? (
                <div>
                  <p style={{ fontSize: '16px', fontWeight: 700, color: 'var(--text-primary)' }}>
                    {file.name}
                  </p>
                  <p style={{ fontSize: '13px', color: 'var(--text-secondary)', marginTop: '4px' }}>
                    {(file.size / 1024 / 1024).toFixed(2)} MB • Ready for ingestion
                  </p>
                  <span
                    style={{
                      display: 'inline-block',
                      marginTop: '10px',
                      fontSize: '12px',
                      color: 'var(--accent-primary)',
                      fontWeight: 600,
                    }}
                  >
                    Click or drop another PDF to replace
                  </span>
                </div>
              ) : (
                <div>
                  <p style={{ fontSize: '16px', fontWeight: 600, color: 'var(--text-primary)' }}>
                    Drag & Drop Lesson PDF here
                  </p>
                  <p style={{ fontSize: '13px', color: 'var(--text-secondary)', marginTop: '4px' }}>
                    or click to browse from your computer (maximum 10MB)
                  </p>
                </div>
              )}
            </div>

            {/* Config options */}
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
                gap: '16px',
                marginBottom: '24px',
              }}
            >
              <div>
                <label
                  style={{
                    display: 'block',
                    fontSize: '13px',
                    fontWeight: 600,
                    color: 'var(--text-secondary)',
                    marginBottom: '6px',
                  }}
                >
                  Target Curriculum Topic
                </label>
                <select
                  value={topicId}
                  onChange={(e) => setTopicId(e.target.value)}
                  style={{
                    width: '100%',
                    padding: '10px 12px',
                    backgroundColor: 'var(--bg-card)',
                    border: '1px solid var(--border-color)',
                    borderRadius: 'var(--radius-md)',
                    color: 'var(--text-primary)',
                    fontSize: '14px',
                    outline: 'none',
                  }}
                >
                  <option value="">Auto-Assign / Default Topic</option>
                  {allTopics.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.biomeName} &gt; {t.name} (Tier {t.tier})
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label
                  style={{
                    display: 'block',
                    fontSize: '13px',
                    fontWeight: 600,
                    color: 'var(--text-secondary)',
                    marginBottom: '6px',
                  }}
                >
                  Difficulty Tier
                </label>
                <select
                  value={difficulty}
                  onChange={(e) => setDifficulty(parseInt(e.target.value, 10))}
                  style={{
                    width: '100%',
                    padding: '10px 12px',
                    backgroundColor: 'var(--bg-card)',
                    border: '1px solid var(--border-color)',
                    borderRadius: 'var(--radius-md)',
                    color: 'var(--text-primary)',
                    fontSize: '14px',
                    outline: 'none',
                  }}
                >
                  <option value={1}>Tier 1: Beginner (Lv 1-15 • 20 XP)</option>
                  <option value={2}>Tier 2: Intermediate (Lv 16-30 • 40 XP)</option>
                  <option value={3}>Tier 3: Advanced (Lv 31-60 • 60 XP)</option>
                  <option value={4}>Tier 4: Expert (Lv 61-100 • 80 XP)</option>
                </select>
              </div>

              <div>
                <label
                  style={{
                    display: 'block',
                    fontSize: '13px',
                    fontWeight: 600,
                    color: 'var(--text-secondary)',
                    marginBottom: '6px',
                  }}
                >
                  Question Count: <strong style={{ color: 'var(--accent-primary)' }}>{count}</strong>
                </label>
                <input
                  type="range"
                  min="1"
                  max="15"
                  value={count}
                  onChange={(e) => setCount(parseInt(e.target.value, 10))}
                  style={{ width: '100%', marginTop: '10px' }}
                />
                <div
                  style={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    fontSize: '11px',
                    color: 'var(--text-muted)',
                  }}
                >
                  <span>1</span>
                  <span>5</span>
                  <span>10</span>
                  <span>15</span>
                </div>
              </div>
            </div>

            {/* Submit & Progress */}
            {uploading ? (
              <div
                style={{
                  padding: '20px',
                  backgroundColor: 'var(--bg-card)',
                  borderRadius: 'var(--radius-md)',
                  border: '1px solid var(--border-color)',
                  textAlign: 'center',
                }}
              >
                <div
                  className="animate-spin"
                  style={{
                    display: 'inline-block',
                    width: '32px',
                    height: '32px',
                    border: '3px solid var(--border-color)',
                    borderTopColor: 'var(--accent-primary)',
                    borderRadius: '50%',
                    marginBottom: '12px',
                  }}
                ></div>
                <p style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)' }}>
                  AI Curriculum Pipeline in Progress
                </p>
                <p style={{ fontSize: '13px', color: 'var(--text-secondary)', marginTop: '4px' }}>
                  {stepMessage}
                </p>
              </div>
            ) : (
              <button
                type="submit"
                disabled={!file}
                style={{
                  width: '100%',
                  padding: '14px',
                  backgroundColor: file ? 'var(--accent-primary)' : 'var(--bg-card)',
                  color: file ? '#ffffff' : 'var(--text-muted)',
                  border: 'none',
                  borderRadius: 'var(--radius-md)',
                  fontSize: '15px',
                  fontWeight: 700,
                  cursor: file ? 'pointer' : 'not-allowed',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  boxShadow: file ? '0 4px 14px rgba(59, 130, 246, 0.4)' : 'none',
                  transition: 'all var(--transition-fast)',
                }}
              >
                Upload & Extract Unique Questions via Gemini AI
              </button>
            )}
          </form>
        </div>

        {/* Right Column: Deduplication Safeguards & Pipeline Status */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
          <div
            style={{
              backgroundColor: 'var(--bg-surface)',
              border: '1px solid var(--border-color)',
              borderRadius: 'var(--radius-lg)',
              padding: '24px',
              boxShadow: 'var(--shadow-sm)',
            }}
          >
            <h3
              style={{
                fontSize: '16px',
                fontWeight: 700,
                color: 'var(--text-primary)',
                marginBottom: '14px',
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
              }}
            >
              <span>🛡️</span> Anti-Repetition & Quality Gates
            </h3>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', fontSize: '13px' }}>
              <div
                style={{
                  padding: '12px 14px',
                  backgroundColor: 'var(--bg-card)',
                  borderRadius: 'var(--radius-md)',
                  borderLeft: '3px solid var(--accent-success)',
                }}
              >
                <div style={{ fontWeight: 700, color: 'var(--text-primary)' }}>
                  1. Prompt-Level Exclusion Injection
                </div>
                <div style={{ color: 'var(--text-secondary)', marginTop: '3px' }}>
                  Existing pending and approved questions for the selected topic are injected into the Gemini prompt so the model avoids repeating previously covered questions.
                </div>
              </div>

              <div
                style={{
                  padding: '12px 14px',
                  backgroundColor: 'var(--bg-card)',
                  borderRadius: 'var(--radius-md)',
                  borderLeft: '3px solid var(--accent-primary)',
                }}
              >
                <div style={{ fontWeight: 700, color: 'var(--text-primary)' }}>
                  2. Trigram Similarity Deduplication
                </div>
                <div style={{ color: 'var(--text-secondary)', marginTop: '3px' }}>
                  Every synthesized candidate is compared against both the current batch and PostgreSQL records using Sørensen-Dice character trigram similarity.
                </div>
              </div>

              <div
                style={{
                  padding: '12px 14px',
                  backgroundColor: 'var(--bg-card)',
                  borderRadius: 'var(--radius-md)',
                  borderLeft: '3px solid var(--accent-warning)',
                }}
              >
                <div style={{ fontWeight: 700, color: 'var(--text-primary)' }}>
                  3. Positional Answer Randomization
                </div>
                <div style={{ color: 'var(--text-secondary)', marginTop: '3px' }}>
                  Option choices (A, B, C, D) are automatically shuffled prior to database persistence so the correct answer index is evenly distributed.
                </div>
              </div>
            </div>
          </div>

          <div
            style={{
              backgroundColor: 'var(--bg-surface)',
              border: '1px solid var(--border-color)',
              borderRadius: 'var(--radius-lg)',
              padding: '24px',
              boxShadow: 'var(--shadow-sm)',
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
              gap: '16px',
              flexWrap: 'wrap',
            }}
          >
            <div>
              <div style={{ fontSize: '12px', fontWeight: 700, color: 'var(--text-muted)', textTransform: 'uppercase' }}>
                Current Pending Queue
              </div>
              <div style={{ fontSize: '24px', fontWeight: 800, color: 'var(--accent-warning)', marginTop: '2px' }}>
                {pendingCount} Question{pendingCount === 1 ? '' : 's'} Awaiting Review
              </div>
              <div style={{ fontSize: '12.5px', color: 'var(--text-secondary)', marginTop: '2px' }}>
                Use Select All in the Review Queue to batch-approve and publish.
              </div>
            </div>

            {onNavigateReview && (
              <button
                type="button"
                onClick={onNavigateReview}
                style={{
                  padding: '10px 18px',
                  backgroundColor: 'var(--bg-card)',
                  border: '1px solid var(--border-color)',
                  borderRadius: 'var(--radius-md)',
                  color: 'var(--text-primary)',
                  fontSize: '13px',
                  fontWeight: 700,
                  cursor: 'pointer',
                }}
              >
                Open Review Queue
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

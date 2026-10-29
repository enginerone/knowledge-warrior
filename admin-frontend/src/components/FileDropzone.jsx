import React, { useState, useRef } from 'react';
import { api } from '../services/api';

export default function FileDropzone({ biomes, onQuestionsGenerated, requireLogin }) {
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
    setStepMessage('Extracting text and cleaning document structure...');

    try {
      const formData = new FormData();
      formData.append('pdf', file);
      formData.append('count', count);
      formData.append('difficulty', difficulty);
      if (topicId) formData.append('topicId', topicId);

      setStepMessage('Sending curriculum chunk to Gemini 3.8 Flash for question generation...');

      const result = await api.uploadPdf(formData);

      setSuccessMsg(`🎉 Generated ${result.questions.length} questions from "${file.name}"!`);
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
    <div style={{ maxWidth: '800px', margin: '0 auto' }}>
      <div style={{
        backgroundColor: 'var(--bg-surface)',
        border: '1px solid var(--border-color)',
        borderRadius: 'var(--radius-lg)',
        padding: '32px',
        boxShadow: 'var(--shadow-md)',
      }}>
        <div style={{ marginBottom: '24px' }}>
          <h2 style={{ fontSize: '22px', fontWeight: 800, color: 'var(--text-primary)', display: 'flex', alignItems: 'center', gap: '10px' }}>
            <span>📄</span> PDF AI Question Ingestion Pipeline
          </h2>
          <p style={{ fontSize: '14px', color: 'var(--text-secondary)', marginTop: '6px' }}>
            Upload textbook chapters, syllabi, or lecture slides. Google Gemini 3.8 Flash will extract key concepts and synthesize balanced RPG multiple-choice questions with educational distractors.
          </p>
        </div>

        {error && (
          <div style={{
            padding: '12px 16px',
            backgroundColor: 'var(--accent-danger-glow)',
            border: '1px solid var(--accent-danger)',
            borderRadius: 'var(--radius-md)',
            color: '#fca5a5',
            fontSize: '14px',
            marginBottom: '20px',
          }}>
            ❌ {error}
          </div>
        )}

        {successMsg && (
          <div style={{
            padding: '12px 16px',
            backgroundColor: 'var(--accent-success-glow)',
            border: '1px solid var(--accent-success)',
            borderRadius: 'var(--radius-md)',
            color: '#6ee7b7',
            fontSize: '14px',
            marginBottom: '20px',
          }}>
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
              border: isDragging ? '2px dashed var(--accent-primary)' : '2px dashed var(--border-color)',
              backgroundColor: isDragging ? 'rgba(59, 130, 246, 0.08)' : 'var(--bg-card)',
              borderRadius: 'var(--radius-lg)',
              padding: '40px 20px',
              textAlign: 'center',
              cursor: 'pointer',
              transition: 'all var(--transition-fast)',
              marginBottom: '24px',
            }}
          >
            <input
              type="file"
              ref={fileInputRef}
              onChange={handleFileSelect}
              accept="application/pdf"
              style={{ display: 'none' }}
            />
            <div style={{ fontSize: '42px', marginBottom: '12px' }}>
              {file ? '📑' : '📥'}
            </div>
            {file ? (
              <div>
                <p style={{ fontSize: '16px', fontWeight: 700, color: 'var(--text-primary)' }}>
                  {file.name}
                </p>
                <p style={{ fontSize: '13px', color: 'var(--text-secondary)', marginTop: '4px' }}>
                  {(file.size / 1024 / 1024).toFixed(2)} MB • Ready for ingestion
                </p>
                <span style={{
                  display: 'inline-block',
                  marginTop: '10px',
                  fontSize: '12px',
                  color: 'var(--accent-primary)',
                  fontWeight: 600,
                }}>
                  Click or drop another PDF to replace
                </span>
              </div>
            ) : (
              <div>
                <p style={{ fontSize: '16px', fontWeight: 600, color: 'var(--text-primary)' }}>
                  Drag & Drop Lesson PDF here
                </p>
                <p style={{ fontSize: '13px', color: 'var(--text-secondary)', marginTop: '4px' }}>
                  or click to browse from your computer (max 10MB)
                </p>
              </div>
            )}
          </div>

          {/* Config options */}
          <div style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
            gap: '18px',
            marginBottom: '26px',
          }}>
            <div>
              <label style={{ display: 'block', fontSize: '13px', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '6px' }}>
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
              <label style={{ display: 'block', fontSize: '13px', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '6px' }}>
                Difficulty Level: {difficulty === 1 ? 'Tier 1 (Concepts)' : difficulty === 2 ? 'Tier 2 (Application)' : 'Tier 3 (Mastery)'}
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
                <option value={1}>Tier 1: Beginner (Novice Scribe - 20 XP)</option>
                <option value={2}>Tier 2: Intermediate (Logic Apprentice - 40 XP)</option>
                <option value={3}>Tier 3: Advanced (Code Knight - 60 XP)</option>
              </select>
            </div>

            <div>
              <label style={{ display: 'block', fontSize: '13px', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '6px' }}>
                Question Count: <strong style={{ color: 'var(--accent-primary)' }}>{count}</strong>
              </label>
              <input
                type="range"
                min="1"
                max="10"
                value={count}
                onChange={(e) => setCount(parseInt(e.target.value, 10))}
                style={{ width: '100%', marginTop: '10px' }}
              />
              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '11px', color: 'var(--text-muted)' }}>
                <span>1</span>
                <span>5</span>
                <span>10</span>
              </div>
            </div>
          </div>

          {/* Submit & Progress */}
          {uploading ? (
            <div style={{
              padding: '20px',
              backgroundColor: 'var(--bg-card)',
              borderRadius: 'var(--radius-md)',
              border: '1px solid var(--border-color)',
              textAlign: 'center',
            }}>
              <div className="animate-spin" style={{
                display: 'inline-block',
                width: '32px',
                height: '32px',
                border: '3px solid var(--border-color)',
                borderTopColor: 'var(--accent-primary)',
                borderRadius: '50%',
                marginBottom: '12px',
              }}></div>
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
                gap: '8px',
                boxShadow: file ? '0 4px 14px rgba(59, 130, 246, 0.4)' : 'none',
                transition: 'all var(--transition-fast)',
              }}
            >
              <span>⚡</span> Upload & Extract Questions via Gemini AI
            </button>
          )}
        </form>
      </div>
    </div>
  );
}

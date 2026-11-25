const express = require('express');
const crypto = require('crypto');
const db = require('../db');
const { authenticateToken, optionalToken } = require('../middleware/auth');
const { normalizeQuestionText } = require('../services/questionDeduplicator');
const {
  countUnseenApprovedQuestions,
  replenishSubjectTierQuestions,
  triggerBackgroundReplenishIfLow,
} = require('../services/questionReplenisher');

const router = express.Router();

const UUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// GET /api/v1/questions/biomes
router.get('/biomes', async (req, res, next) => {
  try {
    const biomesRes = await db.query(
      'SELECT id, name, slug, description, display_order FROM biomes ORDER BY display_order ASC, name ASC'
    );
    const topicsRes = await db.query(
      'SELECT id, biome_id, name, slug, tier FROM topics ORDER BY tier ASC, name ASC'
    );

    const biomes = biomesRes.rows.map((biome) => ({
      ...biome,
      topics: topicsRes.rows.filter((t) => t.biome_id === biome.id),
    }));

    res.json({ biomes });
  } catch (err) {
    next(err);
  }
});

// GET /api/v1/questions - Returns approved unique questions + seenQuestionIds for authenticated user
router.get('/', optionalToken, async (req, res, next) => {
  try {
    const { subject, topic, difficulty, limit, offset } = req.query;

    const conditions = ["q.status = 'APPROVED'"];
    const params = [];

    if (subject) {
      params.push(subject.toLowerCase());
      conditions.push(`(LOWER(b.slug) = $${params.length} OR LOWER(b.name) = $${params.length})`);
    }

    if (topic) {
      params.push(topic.toLowerCase());
      conditions.push(`(LOWER(t.slug) = $${params.length} OR LOWER(t.name) = $${params.length})`);
    }

    if (difficulty) {
      params.push(parseInt(difficulty, 10));
      conditions.push(`q.difficulty = $${params.length}`);
    }

    let queryText = `
      SELECT 
        q.id,
        COALESCE(q.question_type, 'multiple_choice') AS "questionType",
        q.question_text AS question,
        q.options AS choices,
        q.correct_index AS "correctIndex",
        q.explanation,
        q.difficulty,
        q.xp_reward AS "xpReward",
        t.name AS topic,
        b.name AS subject,
        b.slug AS "subjectSlug",
        q.created_at AS "createdAt",
        q.approved_at AS "approvedAt"
      FROM questions q
      JOIN topics t ON q.topic_id = t.id
      JOIN biomes b ON t.biome_id = b.id
      WHERE ${conditions.join(' AND ')}
      ORDER BY b.name ASC, q.difficulty ASC, q.created_at ASC
    `;

    if (limit) {
      params.push(parseInt(limit, 10));
      queryText += ` LIMIT $${params.length}`;
    }

    if (offset) {
      params.push(parseInt(offset, 10));
      queryText += ` OFFSET $${params.length}`;
    }

    const result = await db.query(queryText, params);

    const seenNormalized = new Set();
    const questions = [];
    for (const row of result.rows) {
      const norm = normalizeQuestionText(row.question);
      if (!norm || seenNormalized.has(norm)) {
        continue;
      }
      seenNormalized.add(norm);
      questions.push(row);
    }

    let seenQuestionIds = [];
    if (req.user && req.user.id) {
      const seenRes = await db.query(
        `SELECT DISTINCT question_id::text AS id
         FROM (
           SELECT question_id FROM user_seen_questions WHERE user_id = $1
           UNION
           SELECT question_id FROM progress_logs WHERE user_id = $1 AND question_id IS NOT NULL
         ) combined`,
        [req.user.id]
      );
      seenQuestionIds = seenRes.rows.map((r) => r.id);
    }

    const hashData = questions.map((q) => `${q.id}:${q.approvedAt || q.createdAt}`).join('|');
    const versionHash = crypto.createHash('sha256').update(hashData).digest('hex');

    res.json({
      versionHash,
      count: questions.length,
      seenQuestionIds,
      questions,
    });
  } catch (err) {
    next(err);
  }
});

// POST /api/v1/questions/mark-seen - Permanently records a question the moment it appears to a user
router.post('/mark-seen', authenticateToken, async (req, res, next) => {
  try {
    const questionId = String(req.body.questionId || '').trim();
    const subject = String(req.body.subject || '').trim();
    const difficulty = Math.max(1, Math.min(4, parseInt(req.body.difficulty, 10) || 1));

    if (!UUID_REGEX.test(questionId)) {
      return res.status(400).json({ error: 'Valid question UUID is required.' });
    }

    await db.query(
      `INSERT INTO user_seen_questions (user_id, question_id, seen_at)
       VALUES ($1, $2, NOW())
       ON CONFLICT (user_id, question_id) DO NOTHING`,
      [req.user.id, questionId]
    );

    if (subject) {
      triggerBackgroundReplenishIfLow(req.user.id, subject, difficulty);
    }

    res.json({ status: 'recorded', questionId });
  } catch (err) {
    next(err);
  }
});

// POST /api/v1/questions/replenish - On-demand replenishment when a subject tier runs low on unseen questions
router.post('/replenish', authenticateToken, async (req, res, next) => {
  try {
    const subject = String(req.body.subject || 'Programming').trim();
    const difficulty = Math.max(1, Math.min(4, parseInt(req.body.difficulty, 10) || 1));

    let unseen = await countUnseenApprovedQuestions(req.user.id, subject, difficulty);
    let generatedCount = 0;

    if (unseen < 6) {
      generatedCount = await replenishSubjectTierQuestions(subject, difficulty, 8);
      unseen = await countUnseenApprovedQuestions(req.user.id, subject, difficulty);
    }

    res.json({
      status: 'ok',
      subject,
      difficulty,
      generatedCount,
      unseenRemaining: unseen,
    });
  } catch (err) {
    next(err);
  }
});

module.exports = router;

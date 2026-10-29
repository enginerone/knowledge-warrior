const express = require('express');
const crypto = require('crypto');
const db = require('../db');

const router = express.Router();

// GET /api/v1/questions/biomes - List all biomes with their topics
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

// GET /api/v1/questions - Fetch approved questions with filtering & version hash
router.get('/', async (req, res, next) => {
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
      ORDER BY q.difficulty ASC, q.created_at ASC
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
    const questions = result.rows;

    // Compute dataset version hash for client cache validation
    const hashData = questions.map((q) => `${q.id}:${q.approvedAt || q.createdAt}`).join('|');
    const versionHash = crypto.createHash('sha256').update(hashData).digest('hex');

    res.json({
      versionHash,
      count: questions.length,
      questions,
    });
  } catch (err) {
    next(err);
  }
});

module.exports = router;

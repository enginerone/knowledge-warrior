const db = require('../db');
const { generateSubjectCurriculumQuestions } = require('./geminiService');
const { isDuplicateQuestion } = require('./questionDeduplicator');

const MIN_UNSEEN_THRESHOLD = 10;
const REPLENISH_BATCH_SIZE = 8;
const activeReplenishLocks = new Set();

async function countUnseenApprovedQuestions(userId, subject, difficulty) {
  const res = await db.query(
    `SELECT COUNT(*)::int AS unseen_count
     FROM questions q
     JOIN topics t ON q.topic_id = t.id
     JOIN biomes b ON t.biome_id = b.id
     WHERE q.status = 'APPROVED'
       AND LOWER(b.name) = LOWER($1)
       AND q.difficulty = $2
       AND NOT EXISTS (
         SELECT 1 FROM user_seen_questions usq
         WHERE usq.user_id = $3 AND usq.question_id = q.id
       )`,
    [subject, difficulty, userId]
  );
  return res.rows[0]?.unseen_count || 0;
}

async function replenishSubjectTierQuestions(subject, difficulty, batchSize = REPLENISH_BATCH_SIZE) {
  const cleanSubject = String(subject || 'Programming').trim();
  const cleanDifficulty = Math.max(1, Math.min(4, parseInt(difficulty, 10) || 1));
  const lockKey = `${cleanSubject.toLowerCase()}:${cleanDifficulty}`;

  if (activeReplenishLocks.has(lockKey)) {
    return 0;
  }

  if (!process.env.GEMINI_API_KEY) {
    return 0;
  }

  activeReplenishLocks.add(lockKey);
  try {
    const biomeRes = await db.query(
      'SELECT id, name, slug FROM biomes WHERE LOWER(name) = LOWER($1) OR LOWER(slug) = LOWER($1) LIMIT 1',
      [cleanSubject]
    );
    if (biomeRes.rows.length === 0) {
      return 0;
    }
    const biome = biomeRes.rows[0];

    const topicsRes = await db.query(
      'SELECT id, name FROM topics WHERE biome_id = $1 ORDER BY RANDOM()',
      [biome.id]
    );

    let targetTopic = topicsRes.rows[0];
    if (!targetTopic) {
      const slug = `${biome.slug}-core-tier-${cleanDifficulty}`;
      const createdTopic = await db.query(
        `INSERT INTO topics (biome_id, name, slug, tier)
         VALUES ($1, $2, $3, $4)
         ON CONFLICT (slug) DO UPDATE SET name = EXCLUDED.name
         RETURNING id, name`,
        [biome.id, `${biome.name} Core`, slug, cleanDifficulty]
      );
      targetTopic = createdTopic.rows[0];
    }

    const existingRes = await db.query(
      `SELECT q.question_text
       FROM questions q
       JOIN topics t ON q.topic_id = t.id
       WHERE t.biome_id = $1`,
      [biome.id]
    );
    const existingTexts = existingRes.rows.map((r) => r.question_text);

    const generated = await generateSubjectCurriculumQuestions({
      subject: biome.name,
      topic: targetTopic.name,
      difficulty: cleanDifficulty,
      count: batchSize,
      existingQuestions: existingTexts,
    });

    let inserted = 0;
    for (const item of generated) {
      if (isDuplicateQuestion(item.questionText, existingTexts, 0.82)) {
        continue;
      }
      existingTexts.push(item.questionText);

      await db.query(
        `INSERT INTO questions (
           topic_id, question_type, question_text, options, correct_index,
           explanation, difficulty, xp_reward, status, approved_at
         ) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, 'APPROVED', NOW())`,
        [
          targetTopic.id,
          item.questionType || 'multiple_choice',
          item.questionText,
          JSON.stringify(item.options),
          item.correctIndex,
          item.explanation || '',
          cleanDifficulty,
          cleanDifficulty * 20,
        ]
      );
      inserted += 1;
    }

    if (inserted > 0) {
      console.log(`[QuestionReplenisher] Generated ${inserted} new unique ${biome.name} (Tier ${cleanDifficulty}) questions.`);
    }
    return inserted;
  } catch (err) {
    console.warn(`[QuestionReplenisher] Auto-replenish skipped for ${cleanSubject} Tier ${cleanDifficulty}: ${err.message}`);
    return 0;
  } finally {
    activeReplenishLocks.delete(lockKey);
  }
}

function triggerBackgroundReplenishIfLow(userId, subject, difficulty) {
  if (!userId || !subject) return;
  const cleanDifficulty = Math.max(1, Math.min(4, parseInt(difficulty, 10) || 1));

  setImmediate(async () => {
    try {
      const unseen = await countUnseenApprovedQuestions(userId, subject, cleanDifficulty);
      if (unseen < MIN_UNSEEN_THRESHOLD) {
        await replenishSubjectTierQuestions(subject, cleanDifficulty, REPLENISH_BATCH_SIZE);
      }
    } catch (err) {
      console.warn(`[QuestionReplenisher] Background check error: ${err.message}`);
    }
  });
}

module.exports = {
  MIN_UNSEEN_THRESHOLD,
  countUnseenApprovedQuestions,
  replenishSubjectTierQuestions,
  triggerBackgroundReplenishIfLow,
};

const express = require('express');
const db = require('../db');
const { authenticateToken } = require('../middleware/auth');
const { triggerBackgroundReplenishIfLow } = require('../services/questionReplenisher');
const {
  DEFAULT_SUBJECTS,
  MAX_BATTLE_XP_AWARD,
  STAGE_WIN_COIN_REWARD,
  xpToNextSubjectLevel,
  getBadgeTierForLevel,
  getReviveCoinCostForLevel,
  calculateGlobalLevel,
  applySequentialSubjectXp,
  buildDefaultSubjectRow,
} = require('../services/progressionService');

const router = express.Router();

async function ensureUserDefaultSubjects(client, userId) {
  const biomesRes = await client.query('SELECT name FROM biomes ORDER BY display_order ASC, name ASC');
  const allSubjectNames = new Set(DEFAULT_SUBJECTS);
  for (const row of biomesRes.rows) {
    if (row.name && row.name.trim()) {
      allSubjectNames.add(row.name.trim());
    }
  }

  for (const subject of allSubjectNames) {
    await client.query(
      `INSERT INTO user_subject_progress (user_id, subject, subject_level, subject_xp, subject_points, badge_tier, updated_at)
       VALUES ($1, $2, 1, 0, 0, 'Beginner', NOW())
       ON CONFLICT (user_id, subject) DO NOTHING`,
      [userId, subject]
    );
  }
}

async function fetchUserSummaryAndRank(client, userId) {
  const userRes = await client.query(
    `SELECT id,
            username,
            GREATEST(0, COALESCE(total_xp, 0))::int AS "totalXp",
            GREATEST(1, COALESCE(current_level, 1))::int AS "currentLevel",
            COALESCE(battles_won, 0)::int AS "battlesWon",
            COALESCE(battles_lost, 0)::int AS "battlesLost",
            GREATEST(0, COALESCE(coins, 0))::int AS "coins",
            COALESCE(is_banned, false) AS "isBanned"
     FROM users
     WHERE id = $1`,
    [userId]
  );

  if (userRes.rows.length === 0) return null;
  const user = userRes.rows[0];

  const statsRes = await client.query(
    `SELECT COUNT(*)::int AS "questionsAnswered",
            COUNT(*) FILTER (WHERE was_correct = true)::int AS "correctAnswers"
     FROM progress_logs
     WHERE user_id = $1`,
    [userId]
  );

  const rankRes = await client.query(
    `SELECT COUNT(*)::int + 1 AS rank
     FROM users
     WHERE (role != 'admin' OR role IS NULL)
       AND COALESCE(is_banned, false) = false
       AND total_xp > $1`,
    [user.totalXp]
  );

  return {
    ...user,
    questionsAnswered: statsRes.rows[0]?.questionsAnswered || 0,
    correctAnswers: statsRes.rows[0]?.correctAnswers || 0,
    globalRank: rankRes.rows[0]?.rank || 1,
  };
}

// GET /api/v1/sync/subject-progress
router.get('/subject-progress', authenticateToken, async (req, res, next) => {
  const client = await db.pool.connect();
  try {
    await client.query('BEGIN');

    const summary = await fetchUserSummaryAndRank(client, req.user.id);
    if (!summary) {
      await client.query('ROLLBACK');
      return res.status(404).json({ error: 'Authenticated user account was not found.' });
    }
    if (summary.isBanned) {
      await client.query('ROLLBACK');
      return res.status(403).json({ error: 'This account has been disabled by an administrator.' });
    }

    await ensureUserDefaultSubjects(client, req.user.id);
    await client.query('UPDATE users SET last_active_at = NOW() WHERE id = $1', [req.user.id]);

    const result = await client.query(
      `SELECT subject,
              subject_level AS "subjectLevel",
              COALESCE(subject_xp, 0)::int AS "subjectXp",
              COALESCE(subject_points, 0)::int AS "subjectPoints",
              badge_tier AS "badgeTier",
              COALESCE(saved_stage_index, 0)::int AS "savedStageIndex",
              COALESCE(saved_player_hp, 0)::int AS "savedPlayerHp",
              COALESCE(saved_enemy_hp, 0)::int AS "savedEnemyHp",
              COALESCE(has_active_checkpoint, false) AS "hasActiveCheckpoint",
              updated_at AS "updatedAt"
       FROM user_subject_progress
       WHERE user_id = $1
       ORDER BY subject ASC`,
      [req.user.id]
    );

    const seenRes = await client.query(
      `SELECT DISTINCT question_id::text AS id
       FROM (
         SELECT question_id FROM user_seen_questions WHERE user_id = $1
         UNION
         SELECT question_id FROM progress_logs WHERE user_id = $1 AND question_id IS NOT NULL
       ) combined`,
      [req.user.id]
    );

    await client.query('COMMIT');

    const subjects = result.rows.map((row) => {
      const lv = Math.max(1, Math.min(100, row.subjectLevel || 1));
      return {
        subject: row.subject,
        subjectLevel: lv,
        subjectXp: Math.max(0, row.subjectXp || 0),
        subjectPoints: Math.max(0, row.subjectPoints || 0),
        xpToNextLevel: xpToNextSubjectLevel(lv),
        badgeTier: getBadgeTierForLevel(lv),
        reviveCoinCost: getReviveCoinCostForLevel(lv),
        savedStageIndex: Math.max(0, Math.min(3, row.savedStageIndex || 0)),
        savedPlayerHp: Math.max(0, row.savedPlayerHp || 0),
        savedEnemyHp: Math.max(0, row.savedEnemyHp || 0),
        hasActiveCheckpoint: Boolean(row.hasActiveCheckpoint),
        updatedAt: row.updatedAt,
      };
    });

    res.json({
      player: summary,
      seenQuestionIds: seenRes.rows.map((r) => r.id),
      subjects,
    });
  } catch (err) {
    await client.query('ROLLBACK');
    next(err);
  } finally {
    client.release();
  }
});

// POST /api/v1/sync/battle
router.post('/battle', authenticateToken, async (req, res, next) => {
  const client = await db.pool.connect();
  try {
    const subject = String(req.body.subject || 'Programming').trim() || 'Programming';
    const victory = Boolean(req.body.victory);
    const stageIndex = Math.max(0, Math.min(3, parseInt(req.body.stageIndex, 10) || 0));
    const clearCheckpoint = Boolean(req.body.clearCheckpoint);
    const totalAnswers = Math.max(0, Math.min(50, parseInt(req.body.totalAnswers, 10) || 0));
    const correctAnswers = Math.max(0, Math.min(totalAnswers, parseInt(req.body.correctAnswers, 10) || 0));
    const xpGained = Math.max(0, Math.min(MAX_BATTLE_XP_AWARD, parseInt(req.body.xpGained, 10) || 0));
    const coinsEarned = victory ? STAGE_WIN_COIN_REWARD : 0;
    const answerLogs = Array.isArray(req.body.answerLogs) ? req.body.answerLogs : [];

    await client.query('BEGIN');

    const userRes = await client.query(
      `SELECT id,
              GREATEST(0, COALESCE(total_xp, 0))::int AS total_xp,
              GREATEST(1, COALESCE(current_level, 1))::int AS current_level,
              COALESCE(is_banned, false) AS is_banned
       FROM users
       WHERE id = $1
       FOR UPDATE`,
      [req.user.id]
    );

    if (userRes.rows.length === 0) {
      await client.query('ROLLBACK');
      return res.status(404).json({ error: 'Authenticated user account was not found.' });
    }
    if (userRes.rows[0].is_banned) {
      await client.query('ROLLBACK');
      return res.status(403).json({ error: 'This account has been disabled by an administrator.' });
    }

    await ensureUserDefaultSubjects(client, req.user.id);

    const subjRes = await client.query(
      `SELECT subject_level, COALESCE(subject_xp, 0)::int AS subject_xp, COALESCE(subject_points, 0)::int AS subject_points
       FROM user_subject_progress
       WHERE user_id = $1 AND LOWER(subject) = LOWER($2)
       FOR UPDATE`,
      [req.user.id, subject]
    );

    const existingSubj = subjRes.rows[0] || buildDefaultSubjectRow(subject);
    const nextSubj = applySequentialSubjectXp(
      existingSubj.subject_level ?? existingSubj.subjectLevel,
      existingSubj.subject_xp ?? existingSubj.subjectXp,
      existingSubj.subject_points ?? existingSubj.subjectPoints,
      xpGained
    );

    let nextSavedStage = 0;
    let nextHasCheckpoint = false;
    if (!clearCheckpoint) {
      if (victory && stageIndex < 3) {
        nextSavedStage = stageIndex + 1;
        nextHasCheckpoint = true;
      } else if (!victory) {
        nextSavedStage = stageIndex;
        nextHasCheckpoint = true;
      }
    }

    await client.query(
      `INSERT INTO user_subject_progress (
         user_id, subject, subject_level, subject_xp, subject_points, badge_tier,
         saved_stage_index, saved_player_hp, saved_enemy_hp, has_active_checkpoint, updated_at
       )
       VALUES ($1, $2, $3, $4, $5, $6, $7, 0, 0, $8, NOW())
       ON CONFLICT (user_id, subject)
       DO UPDATE SET
         subject_level = EXCLUDED.subject_level,
         subject_xp = EXCLUDED.subject_xp,
         subject_points = EXCLUDED.subject_points,
         badge_tier = EXCLUDED.badge_tier,
         saved_stage_index = EXCLUDED.saved_stage_index,
         saved_player_hp = 0,
         saved_enemy_hp = 0,
         has_active_checkpoint = EXCLUDED.has_active_checkpoint,
         updated_at = NOW()`,
      [
        req.user.id,
        subject,
        nextSubj.subjectLevel,
        nextSubj.subjectXp,
        nextSubj.subjectPoints,
        nextSubj.badgeTier,
        nextSavedStage,
        nextHasCheckpoint,
      ]
    );

    if (answerLogs.length > 0) {
      for (const entry of answerLogs.slice(0, 50)) {
        const questionId = entry && typeof entry.questionId === 'string' && entry.questionId.length === 36
          ? entry.questionId
          : null;
        await client.query(
          `INSERT INTO progress_logs (user_id, question_id, was_correct, response_time_ms, answered_at)
           VALUES ($1, $2, $3, $4, NOW())`,
          [req.user.id, questionId, Boolean(entry?.wasCorrect), Math.max(0, parseInt(entry?.responseTimeMs, 10) || 0)]
        );
        if (questionId) {
          await client.query(
            `INSERT INTO user_seen_questions (user_id, question_id, seen_at)
             VALUES ($1, $2, NOW())
             ON CONFLICT (user_id, question_id) DO NOTHING`,
            [req.user.id, questionId]
          );
        }
      }
    } else if (totalAnswers > 0) {
      for (let i = 0; i < totalAnswers; i++) {
        await client.query(
          `INSERT INTO progress_logs (user_id, question_id, was_correct, response_time_ms, answered_at)
           VALUES ($1, NULL, $2, 0, NOW())`,
          [req.user.id, i < correctAnswers]
        );
      }
    }

    const updatedTotalXp = userRes.rows[0].total_xp + xpGained;
    const updatedGlobalLevel = calculateGlobalLevel(updatedTotalXp);

    await client.query(
      `UPDATE users
       SET total_xp = $1,
           current_level = $2,
           battles_won = COALESCE(battles_won, 0) + $3,
           battles_lost = COALESCE(battles_lost, 0) + $4,
           coins = GREATEST(0, COALESCE(coins, 0)) + $5,
           last_synced_at = NOW(),
           last_active_at = NOW()
       WHERE id = $6`,
      [updatedTotalXp, updatedGlobalLevel, victory ? 1 : 0, victory ? 0 : 1, coinsEarned, req.user.id]
    );

    const playerSummary = await fetchUserSummaryAndRank(client, req.user.id);

    await client.query('COMMIT');

    const nextTierDifficulty = nextSubj.subjectLevel <= 15 ? 1 : (nextSubj.subjectLevel <= 30 ? 2 : (nextSubj.subjectLevel <= 60 ? 3 : 4));
    triggerBackgroundReplenishIfLow(req.user.id, subject, nextTierDifficulty);

    res.json({
      status: 'success',
      coinsEarned,
      reviveCoinCost: getReviveCoinCostForLevel(nextSubj.subjectLevel),
      player: playerSummary,
      subjectProgress: {
        subject,
        ...nextSubj,
        savedStageIndex: nextSavedStage,
        hasActiveCheckpoint: nextHasCheckpoint,
      },
    });
  } catch (err) {
    await client.query('ROLLBACK');
    next(err);
  } finally {
    client.release();
  }
});

// POST /api/v1/sync/subject-progress (blocks direct level jumps; only sequential XP increments allowed)
router.post('/subject-progress', authenticateToken, async (req, res, next) => {
  const client = await db.pool.connect();
  try {
    const subject = String(req.body.subject || 'Programming').trim() || 'Programming';
    const requestedLevel = req.body.subjectLevel !== undefined ? parseInt(req.body.subjectLevel, 10) : null;
    const xpGained = Math.max(0, Math.min(MAX_BATTLE_XP_AWARD, parseInt(req.body.xpGained, 10) || 0));

    await client.query('BEGIN');
    await ensureUserDefaultSubjects(client, req.user.id);

    const existingRes = await client.query(
      `SELECT subject_level, COALESCE(subject_xp, 0)::int AS subject_xp, COALESCE(subject_points, 0)::int AS subject_points
       FROM user_subject_progress
       WHERE user_id = $1 AND LOWER(subject) = LOWER($2)
       FOR UPDATE`,
      [req.user.id, subject]
    );

    const current = existingRes.rows[0] || { subject_level: 1, subject_xp: 0, subject_points: 0 };

    if (requestedLevel !== null && requestedLevel !== current.subject_level) {
      await client.query('ROLLBACK');
      return res.status(400).json({
        error: 'Jumping directly to another subject level is not allowed. Subject levels advance sequentially from Level 1 through earned battle XP.',
      });
    }

    const nextSubj = applySequentialSubjectXp(
      current.subject_level,
      current.subject_xp,
      current.subject_points,
      xpGained
    );

    await client.query(
      `UPDATE user_subject_progress
       SET subject_level = $1,
           subject_xp = $2,
           subject_points = $3,
           badge_tier = $4,
           updated_at = NOW()
       WHERE user_id = $5 AND LOWER(subject) = LOWER($6)`,
      [nextSubj.subjectLevel, nextSubj.subjectXp, nextSubj.subjectPoints, nextSubj.badgeTier, req.user.id, subject]
    );

    await client.query('COMMIT');

    res.json({
      status: 'success',
      progress: {
        subject,
        ...nextSubj,
      },
    });
  } catch (err) {
    await client.query('ROLLBACK');
    next(err);
  } finally {
    client.release();
  }
});

// POST /api/v1/sync/progress (authenticated online event batch endpoint)
router.post('/progress', authenticateToken, async (req, res, next) => {
  const client = await db.pool.connect();
  try {
    const { events } = req.body;
    if (!Array.isArray(events) || events.length === 0) {
      return res.status(400).json({ error: 'Array of progress events is required.' });
    }

    await client.query('BEGIN');

    const currentRes = await client.query(
      'SELECT GREATEST(0, COALESCE(total_xp, 0))::int AS total_xp, COALESCE(is_banned, false) AS is_banned FROM users WHERE id = $1 FOR UPDATE',
      [req.user.id]
    );
    if (currentRes.rows.length === 0) {
      await client.query('ROLLBACK');
      return res.status(404).json({ error: 'User account not found.' });
    }
    if (currentRes.rows[0].is_banned) {
      await client.query('ROLLBACK');
      return res.status(403).json({ error: 'User account is disabled.' });
    }

    let addedXp = 0;
    for (const evt of events.slice(0, 20)) {
      const xpGained = Math.max(0, Math.min(MAX_BATTLE_XP_AWARD, parseInt(evt.xpGained, 10) || 0));
      addedXp += xpGained;

      if (evt.questionId) {
        await client.query(
          `INSERT INTO progress_logs (user_id, question_id, was_correct, response_time_ms, answered_at)
           VALUES ($1, $2, $3, $4, $5)`,
          [
            req.user.id,
            evt.questionId,
            evt.wasCorrect ?? true,
            evt.responseTimeMs || 0,
            evt.timestamp ? new Date(evt.timestamp) : new Date(),
          ]
        );
      }
    }

    const currentXp = currentRes.rows[0].total_xp + addedXp;
    const newLevel = calculateGlobalLevel(currentXp);

    await client.query(
      `UPDATE users
       SET total_xp = $1, current_level = $2, last_synced_at = NOW()
       WHERE id = $3`,
      [currentXp, newLevel, req.user.id]
    );

    const summary = await fetchUserSummaryAndRank(client, req.user.id);
    await client.query('COMMIT');

    res.json({
      status: 'success',
      processedEvents: events.length,
      currentLevel: newLevel,
      totalXp: currentXp,
      globalRank: summary?.globalRank || 1,
    });
  } catch (err) {
    await client.query('ROLLBACK');
    next(err);
  } finally {
    client.release();
  }
});

// POST /api/v1/sync/revive - Spend coins aligned to Subject Badge Tier to revive on the stage where the hero died
router.post('/revive', authenticateToken, async (req, res, next) => {
  const client = await db.pool.connect();
  try {
    const subject = String(req.body.subject || 'Programming').trim() || 'Programming';
    const stageIndex = Math.max(0, Math.min(3, parseInt(req.body.stageIndex, 10) || 0));

    await client.query('BEGIN');

    const userRes = await client.query(
      `SELECT id,
              GREATEST(0, COALESCE(coins, 0))::int AS coins,
              COALESCE(is_banned, false) AS is_banned
       FROM users
       WHERE id = $1
       FOR UPDATE`,
      [req.user.id]
    );

    if (userRes.rows.length === 0) {
      await client.query('ROLLBACK');
      return res.status(404).json({ error: 'Authenticated user account was not found.' });
    }
    if (userRes.rows[0].is_banned) {
      await client.query('ROLLBACK');
      return res.status(403).json({ error: 'This account has been disabled by an administrator.' });
    }

    await ensureUserDefaultSubjects(client, req.user.id);

    const subjRes = await client.query(
      `SELECT GREATEST(1, COALESCE(subject_level, 1))::int AS subject_level
       FROM user_subject_progress
       WHERE user_id = $1 AND LOWER(subject) = LOWER($2)
       FOR UPDATE`,
      [req.user.id, subject]
    );

    const subjectLevel = subjRes.rows[0]?.subject_level || 1;
    const requiredCoins = getReviveCoinCostForLevel(subjectLevel);
    const currentCoins = userRes.rows[0].coins;

    if (currentCoins < requiredCoins) {
      await client.query('ROLLBACK');
      return res.status(400).json({
        error: `Insufficient coins to revive on Stage ${stageIndex + 1}. Required: ${requiredCoins} coins, available: ${currentCoins} coins.`,
        requiredCoins,
        currentCoins,
      });
    }

    const remainingCoins = currentCoins - requiredCoins;

    await client.query(
      `UPDATE users
       SET coins = $1,
           last_active_at = NOW(),
           last_synced_at = NOW()
       WHERE id = $2`,
      [remainingCoins, req.user.id]
    );

    await client.query(
      `UPDATE user_subject_progress
       SET saved_stage_index = $1,
           saved_player_hp = 0,
           saved_enemy_hp = 0,
           has_active_checkpoint = true,
           updated_at = NOW()
       WHERE user_id = $2 AND LOWER(subject) = LOWER($3)`,
      [stageIndex, req.user.id, subject]
    );

    await client.query('COMMIT');

    res.json({
      status: 'revived',
      subject,
      badgeTier: getBadgeTierForLevel(subjectLevel),
      coinsSpent: requiredCoins,
      remainingCoins,
    });
  } catch (err) {
    await client.query('ROLLBACK');
    next(err);
  } finally {
    client.release();
  }
});

// POST /api/v1/sync/checkpoint - Save or clear a per-subject battle checkpoint
router.post('/checkpoint', authenticateToken, async (req, res, next) => {
  const client = await db.pool.connect();
  try {
    const subject = String(req.body.subject || 'Programming').trim() || 'Programming';
    const stageIndex = Math.max(0, Math.min(3, parseInt(req.body.stageIndex, 10) || 0));
    const playerHp = Math.max(0, parseInt(req.body.playerHp, 10) || 0);
    const enemyHp = Math.max(0, parseInt(req.body.enemyHp, 10) || 0);
    const hasActiveCheckpoint = Boolean(req.body.hasActiveCheckpoint);

    await client.query('BEGIN');
    await ensureUserDefaultSubjects(client, req.user.id);

    await client.query(
      `UPDATE users
       SET last_active_at = NOW()
       WHERE id = $1`,
      [req.user.id]
    );

    await client.query(
      `UPDATE user_subject_progress
       SET saved_stage_index = $1,
           saved_player_hp = $2,
           saved_enemy_hp = $3,
           has_active_checkpoint = $4,
           updated_at = NOW()
       WHERE user_id = $5 AND LOWER(subject) = LOWER($6)`,
      [
        hasActiveCheckpoint ? stageIndex : 0,
        hasActiveCheckpoint ? playerHp : 0,
        hasActiveCheckpoint ? enemyHp : 0,
        hasActiveCheckpoint,
        req.user.id,
        subject,
      ]
    );

    await client.query('COMMIT');

    res.json({
      status: 'saved',
      subject,
      savedStageIndex: hasActiveCheckpoint ? stageIndex : 0,
      savedPlayerHp: hasActiveCheckpoint ? playerHp : 0,
      savedEnemyHp: hasActiveCheckpoint ? enemyHp : 0,
      hasActiveCheckpoint,
    });
  } catch (err) {
    await client.query('ROLLBACK');
    next(err);
  } finally {
    client.release();
  }
});

module.exports = router;

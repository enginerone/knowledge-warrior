const express = require('express');
const db = require('../db');
const { optionalToken } = require('../middleware/auth');

const router = express.Router();

function getBadgeTierForLevel(level) {
  const lv = Math.max(1, Math.min(100, parseInt(level, 10) || 1));
  if (lv <= 15) return 'Beginner';
  if (lv <= 30) return 'Intermediate';
  if (lv <= 60) return 'Advanced';
  return 'Expert';
}

function calculateLevelFromSubjectXp(xp) {
  const safeXp = Math.max(0, parseInt(xp, 10) || 0);
  const lv = Math.floor(safeXp / 120) + 1;
  return Math.max(1, Math.min(100, lv));
}

// GET /api/v1/pvp/subject-progress
router.get('/subject-progress', optionalToken, async (req, res, next) => {
  try {
    if (!req.user || !req.user.id) {
      return res.json({ subjects: [] });
    }

    const result = await db.query(
      `SELECT subject,
              subject_level AS "subjectLevel",
              subject_xp AS "subjectXp",
              badge_tier AS "badgeTier",
              pvp_wins AS "pvpWins",
              pvp_losses AS "pvpLosses",
              updated_at AS "updatedAt"
       FROM user_subject_progress
       WHERE user_id = $1
       ORDER BY subject ASC`,
      [req.user.id]
    );

    res.json({ subjects: result.rows });
  } catch (err) {
    next(err);
  }
});

// POST /api/v1/pvp/subject-progress
router.post('/subject-progress', optionalToken, async (req, res, next) => {
  try {
    if (!req.user || !req.user.id) {
      return res.status(401).json({ error: 'Authentication required to sync subject progress.' });
    }

    const subject = String(req.body.subject || 'Programming').trim();
    const subjectLevel = Math.max(1, Math.min(100, parseInt(req.body.subjectLevel, 10) || 1));
    const subjectXp = Math.max(0, parseInt(req.body.subjectXp, 10) || 100);
    const badgeTier = getBadgeTierForLevel(subjectLevel);

    const result = await db.query(
      `INSERT INTO user_subject_progress (user_id, subject, subject_level, subject_xp, badge_tier, updated_at)
       VALUES ($1, $2, $3, $4, $5, NOW())
       ON CONFLICT (user_id, subject)
       DO UPDATE SET
         subject_level = EXCLUDED.subject_level,
         subject_xp = EXCLUDED.subject_xp,
         badge_tier = EXCLUDED.badge_tier,
         updated_at = NOW()
       RETURNING subject, subject_level AS "subjectLevel", subject_xp AS "subjectXp", badge_tier AS "badgeTier", pvp_wins AS "pvpWins", pvp_losses AS "pvpLosses"`,
      [req.user.id, subject, subjectLevel, subjectXp, badgeTier]
    );

    res.json({
      status: 'success',
      progress: result.rows[0],
    });
  } catch (err) {
    next(err);
  }
});

// GET /api/v1/pvp/lobby?subject=Databases&level=10
router.get('/lobby', optionalToken, async (req, res, next) => {
  try {
    const subject = String(req.query.subject || 'Programming').trim();
    const level = Math.max(1, Math.min(100, parseInt(req.query.level, 10) || 1));
    const badgeTier = getBadgeTierForLevel(level);
    const currentUserId = req.user?.id || null;

    if (currentUserId) {
      await db.query(
        `INSERT INTO user_subject_progress (user_id, subject, subject_level, subject_xp, badge_tier, updated_at)
         VALUES ($1, $2, $3, $4, $5, NOW())
         ON CONFLICT (user_id, subject)
         DO UPDATE SET
           subject_level = EXCLUDED.subject_level,
           badge_tier = EXCLUDED.badge_tier,
           updated_at = NOW()`,
        [currentUserId, subject, level, Math.max(100, level * 120), badgeTier]
      );
    }

    const params = [subject, level];
    let excludeClause = '';
    if (currentUserId) {
      params.push(currentUserId);
      excludeClause = `AND u.id != $3`;
    }

    const opponentsRes = await db.query(
      `SELECT u.id AS "userId",
              u.username,
              usp.subject,
              usp.subject_level AS "subjectLevel",
              usp.subject_xp AS "subjectXp",
              usp.badge_tier AS "badgeTier",
              usp.pvp_wins AS "pvpWins",
              usp.pvp_losses AS "pvpLosses",
              usp.updated_at AS "updatedAt"
       FROM user_subject_progress usp
       JOIN users u ON u.id = usp.user_id
       WHERE LOWER(usp.subject) = LOWER($1)
         AND usp.subject_level = $2
         AND (u.role != 'admin' OR u.role IS NULL)
         ${excludeClause}
       ORDER BY usp.subject_xp DESC, u.username ASC
       LIMIT 30`,
      params
    );

    res.json({
      subject,
      subjectLevel: level,
      badgeTier,
      count: opponentsRes.rows.length,
      opponents: opponentsRes.rows,
    });
  } catch (err) {
    next(err);
  }
});

// POST /api/v1/pvp/resolve
router.post('/resolve', optionalToken, async (req, res, next) => {
  const client = await db.pool.connect();
  try {
    const subject = String(req.body.subject || 'Programming').trim();
    const subjectLevel = Math.max(1, Math.min(100, parseInt(req.body.subjectLevel, 10) || 1));
    const opponentUserId = req.body.opponentUserId || null;
    const opponentUsername = String(req.body.opponentUsername || '').trim();
    const victory = Boolean(req.body.victory);
    const playerCurrentPoints = Math.max(50, parseInt(req.body.playerCurrentPoints, 10) || (subjectLevel * 120));
    const rewardPoints = Math.max(25, parseInt(req.body.rewardPoints, 10) || (40 + subjectLevel * 5));

    await client.query('BEGIN');

    let challengerId = req.user?.id || null;
    if (!challengerId && req.body.challengerUsername) {
      const chRes = await client.query(
        'SELECT id FROM users WHERE LOWER(username) = LOWER($1)',
        [String(req.body.challengerUsername).trim()]
      );
      if (chRes.rows.length > 0) challengerId = chRes.rows[0].id;
    }

    let resolvedOpponentId = opponentUserId;
    if (!resolvedOpponentId && opponentUsername) {
      const oppRes = await client.query(
        'SELECT id FROM users WHERE LOWER(username) = LOWER($1)',
        [opponentUsername]
      );
      if (oppRes.rows.length > 0) resolvedOpponentId = oppRes.rows[0].id;
    }

    let playerPointsBefore = playerCurrentPoints;
    if (challengerId) {
      const pRow = await client.query(
        'SELECT subject_xp FROM user_subject_progress WHERE user_id = $1 AND LOWER(subject) = LOWER($2)',
        [challengerId, subject]
      );
      if (pRow.rows.length > 0) {
        playerPointsBefore = Math.max(50, pRow.rows[0].subject_xp);
      }
    }

    let opponentPointsBefore = Math.max(100, subjectLevel * 120);
    if (resolvedOpponentId) {
      const oRow = await client.query(
        'SELECT subject_xp FROM user_subject_progress WHERE user_id = $1 AND LOWER(subject) = LOWER($2)',
        [resolvedOpponentId, subject]
      );
      if (oRow.rows.length > 0) {
        opponentPointsBefore = Math.max(50, oRow.rows[0].subject_xp);
      }
    }

    const badgeTier = getBadgeTierForLevel(subjectLevel);
    let playerDeduction = 0;
    let opponentDeduction = 0;
    let playerPointsAfter = playerPointsBefore;
    let opponentPointsAfter = opponentPointsBefore;

    if (victory) {
      // Opponent lost: 20% point deduction from opponent
      opponentDeduction = Math.max(1, Math.round(opponentPointsBefore * 0.20));
      opponentPointsAfter = Math.max(0, opponentPointsBefore - opponentDeduction);
      playerPointsAfter = playerPointsBefore + rewardPoints;
    } else {
      // Challenger lost: 20% point deduction from challenger
      playerDeduction = Math.max(1, Math.round(playerPointsBefore * 0.20));
      playerPointsAfter = Math.max(0, playerPointsBefore - playerDeduction);
      opponentPointsAfter = opponentPointsBefore + rewardPoints;
    }

    const playerLevelAfter = Math.max(subjectLevel, calculateLevelFromSubjectXp(playerPointsAfter));
    const playerBadgeAfter = getBadgeTierForLevel(playerLevelAfter);

    if (challengerId) {
      await client.query(
        `INSERT INTO user_subject_progress (user_id, subject, subject_level, subject_xp, badge_tier, pvp_wins, pvp_losses, updated_at)
         VALUES ($1, $2, $3, $4, $5, $6, $7, NOW())
         ON CONFLICT (user_id, subject)
         DO UPDATE SET
           subject_level = $3,
           subject_xp = $4,
           badge_tier = $5,
           pvp_wins = user_subject_progress.pvp_wins + $6,
           pvp_losses = user_subject_progress.pvp_losses + $7,
           updated_at = NOW()`,
        [
          challengerId,
          subject,
          playerLevelAfter,
          playerPointsAfter,
          playerBadgeAfter,
          victory ? 1 : 0,
          victory ? 0 : 1,
        ]
      );

      const xpDelta = victory ? rewardPoints : -playerDeduction;
      await client.query(
        `UPDATE users
         SET total_xp = GREATEST(0, total_xp + $1),
             last_synced_at = NOW()
         WHERE id = $2`,
        [xpDelta, challengerId]
      );
    }

    if (resolvedOpponentId) {
      await client.query(
        `INSERT INTO user_subject_progress (user_id, subject, subject_level, subject_xp, badge_tier, pvp_wins, pvp_losses, updated_at)
         VALUES ($1, $2, $3, $4, $5, $6, $7, NOW())
         ON CONFLICT (user_id, subject)
         DO UPDATE SET
           subject_xp = $4,
           badge_tier = $5,
           pvp_wins = user_subject_progress.pvp_wins + $6,
           pvp_losses = user_subject_progress.pvp_losses + $7,
           updated_at = NOW()`,
        [
          resolvedOpponentId,
          subject,
          subjectLevel,
          opponentPointsAfter,
          badgeTier,
          victory ? 0 : 1,
          victory ? 1 : 0,
        ]
      );

      const oppXpDelta = victory ? -opponentDeduction : rewardPoints;
      await client.query(
        `UPDATE users
         SET total_xp = GREATEST(0, total_xp + $1),
             last_synced_at = NOW()
         WHERE id = $2`,
        [oppXpDelta, resolvedOpponentId]
      );
    }

    if (challengerId || resolvedOpponentId) {
      const winnerId = victory ? challengerId : resolvedOpponentId;
      const loserId = victory ? resolvedOpponentId : challengerId;
      const deducted = victory ? opponentDeduction : playerDeduction;
      await client.query(
        `INSERT INTO pvp_matches (
           challenger_id, opponent_id, subject, subject_level, winner_id, loser_id, winner_xp_gained, loser_points_deducted
         ) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)`,
        [challengerId, resolvedOpponentId, subject, subjectLevel, winnerId, loserId, rewardPoints, deducted]
      );
    }

    await client.query('COMMIT');

    res.json({
      status: 'success',
      subject,
      subjectLevel: playerLevelAfter,
      badgeTier: playerBadgeAfter,
      victory,
      rewardPoints: victory ? rewardPoints : 0,
      playerPointsBefore,
      playerPointsAfter,
      playerDeduction,
      opponentPointsBefore,
      opponentPointsAfter,
      opponentDeduction,
    });
  } catch (err) {
    await client.query('ROLLBACK');
    next(err);
  } finally {
    client.release();
  }
});

module.exports = router;


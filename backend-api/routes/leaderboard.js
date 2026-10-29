const express = require('express');
const db = require('../db');

const router = express.Router();

// GET /api/v1/leaderboard/global - Top 100 players (supports ?period=all|weekly|biome)
router.get('/global', async (req, res, next) => {
  try {
    const limit = Math.min(parseInt(req.query.limit, 10) || 100, 100);
    const period = (req.query.period || 'all').toLowerCase();

    let whereClause = "WHERE (u.role != 'admin' OR u.role IS NULL)";
    if (period === 'weekly') {
      whereClause += " AND u.last_synced_at >= NOW() - INTERVAL '7 days'";
    }

    const result = await db.query(
      `SELECT 
        ROW_NUMBER() OVER (ORDER BY u.total_xp DESC, u.created_at ASC)::int AS rank,
        u.id,
        u.username,
        u.role,
        u.total_xp AS "totalXp",
        u.current_level AS "currentLevel",
        u.last_synced_at AS "lastSyncedAt",
        COALESCE(stats.questions_answered, 0)::int AS "questionsAnswered",
        COALESCE(stats.correct_answers, 0)::int AS "correctAnswers"
       FROM users u
       LEFT JOIN (
         SELECT
           user_id,
           COUNT(*) AS questions_answered,
           COUNT(*) FILTER (WHERE was_correct = true) AS correct_answers
         FROM progress_logs
         GROUP BY user_id
       ) stats ON stats.user_id = u.id
       ${whereClause}
       ORDER BY ${period === 'biome' ? 'u.current_level DESC, u.total_xp DESC' : 'u.total_xp DESC, u.created_at ASC'}
       LIMIT $1`,
      [limit]
    );

    res.json({
      period,
      count: result.rows.length,
      leaderboard: result.rows,
    });
  } catch (err) {
    next(err);
  }
});

module.exports = router;

const express = require('express');
const db = require('../db');
const { optionalToken } = require('../middleware/auth');

const router = express.Router();

function calculateLevel(totalXp) {
  if (!totalXp || totalXp <= 0) return 1;
  // Formula from GDD: XP = 100 * (L - 1)^1.6  =>  L = floor((XP / 100)^(1 / 1.6)) + 1
  return Math.floor(Math.pow(totalXp / 100, 1 / 1.6)) + 1;
}

// POST /api/v1/sync/progress
router.post('/progress', optionalToken, async (req, res, next) => {
  const client = await db.pool.connect();
  try {
    const { events } = req.body;

    if (!Array.isArray(events) || events.length === 0) {
      return res.status(400).json({ error: 'Array of progress events is required' });
    }

    await client.query('BEGIN');

    let userId = req.user ? req.user.id : null;
    const clientHash = events[0].clientHash;

    // If unauthenticated guest, check or create guest warrior user for this device
    if (!userId) {
      if (clientHash) {
        const guestName = `guest_${clientHash.slice(0, 10)}`;
        const userRes = await client.query('SELECT id FROM users WHERE username = $1', [guestName]);
        if (userRes.rows.length > 0) {
          userId = userRes.rows[0].id;
        } else {
          const insertRes = await client.query(
            `INSERT INTO users (username, role, total_xp, current_level)
             VALUES ($1, 'player', 0, 1) RETURNING id`,
            [guestName]
          );
          userId = insertRes.rows[0].id;
        }
      } else {
        return res.status(400).json({ error: 'Authentication token or clientHash is required' });
      }
    }

    let addedXp = 0;

    for (const evt of events) {
      const xpGained = parseInt(evt.xpGained, 10) || 0;
      addedXp += xpGained;

      // Log to progress_logs if questionId is provided
      if (evt.questionId) {
        await client.query(
          `INSERT INTO progress_logs (user_id, question_id, was_correct, response_time_ms, answered_at)
           VALUES ($1, $2, $3, $4, $5)`,
          [
            userId,
            evt.questionId,
            evt.wasCorrect ?? true,
            evt.responseTimeMs || 0,
            evt.timestamp ? new Date(evt.timestamp) : new Date(),
          ]
        );
      }
    }

    // Fetch current total_xp and check if user is banned
    const currentRes = await client.query('SELECT total_xp, COALESCE(is_banned, false) AS is_banned FROM users WHERE id = $1', [userId]);
    if (currentRes.rows.length === 0) {
      await client.query('ROLLBACK');
      return res.status(404).json({ error: 'User account not found' });
    }
    if (currentRes.rows[0].is_banned) {
      await client.query('ROLLBACK');
      return res.status(403).json({ error: 'User account is banned (disabled)' });
    }
    const currentXp = (currentRes.rows[0]?.total_xp || 0) + addedXp;
    const newLevel = calculateLevel(currentXp);

    // Update user stats
    await client.query(
      `UPDATE users 
       SET total_xp = $1, current_level = $2, last_synced_at = NOW()
       WHERE id = $3`,
      [currentXp, newLevel, userId]
    );

    // Calculate global rank
    const rankRes = await client.query(
      'SELECT COUNT(*)::int + 1 AS rank FROM users WHERE total_xp > $1',
      [currentXp]
    );
    const globalRank = rankRes.rows[0]?.rank || 1;

    await client.query('COMMIT');

    res.json({
      status: 'success',
      processedEvents: events.length,
      currentLevel: newLevel,
      totalXp: currentXp,
      globalRank,
    });
  } catch (err) {
    await client.query('ROLLBACK');
    next(err);
  } finally {
    client.release();
  }
});

module.exports = router;

const express = require('express');
const multer = require('multer');
const db = require('../db');
const { authenticateToken, requireAdmin } = require('../middleware/auth');
const { extractTextFromPDF } = require('../services/pdfService');
const { generateQuestionsFromText } = require('../services/geminiService');
const {
  isDuplicateQuestion,
  filterUniqueQuestions,
} = require('../services/questionDeduplicator');

const router = express.Router();

let userBanColumnsEnsured = false;
async function ensureUserBanColumns() {
  if (userBanColumnsEnsured) return;
  await db.query(`
    ALTER TABLE users ADD COLUMN IF NOT EXISTS is_banned BOOLEAN DEFAULT FALSE;
    ALTER TABLE users ADD COLUMN IF NOT EXISTS banned_at TIMESTAMPTZ;
  `);
  userBanColumnsEnsured = true;
}

// Configure multer for memory storage (max 10MB PDF)
const upload = multer({
  storage: multer.memoryStorage(),
  limits: { fileSize: 10 * 1024 * 1024 },
  fileFilter: (req, file, cb) => {
    if (file.mimetype === 'application/pdf' || file.originalname.endsWith('.pdf')) {
      cb(null, true);
    } else {
      cb(new Error('Only PDF files are supported'));
    }
  },
});

// Protect all admin routes
router.use(authenticateToken, requireAdmin);

// GET /api/v1/admin/stats - Overview metrics
router.get('/stats', async (req, res, next) => {
  try {
    const qStats = await db.query(`
      SELECT 
        COUNT(*) FILTER (WHERE status = 'APPROVED') AS approved_count,
        COUNT(*) FILTER (WHERE status = 'PENDING') AS pending_count,
        COUNT(*) FILTER (WHERE status = 'REJECTED') AS rejected_count,
        COUNT(*) AS total_count
      FROM questions
    `);

    const userCount = await db.query("SELECT COUNT(*) AS count FROM users WHERE role = 'player'");
    const uploadCount = await db.query('SELECT COUNT(*) AS count FROM admin_uploads');

    res.json({
      questions: {
        approved: parseInt(qStats.rows[0].approved_count, 10),
        pending: parseInt(qStats.rows[0].pending_count, 10),
        rejected: parseInt(qStats.rows[0].rejected_count, 10),
        total: parseInt(qStats.rows[0].total_count, 10),
      },
      playersCount: parseInt(userCount.rows[0].count, 10),
      uploadsCount: parseInt(uploadCount.rows[0].count, 10),
    });
  } catch (err) {
    next(err);
  }
});

// GET /api/v1/admin/users - Track all registered users in PostgreSQL
router.get('/users', async (req, res, next) => {
  try {
    await ensureUserBanColumns();
    const result = await db.query(`
      SELECT
        u.id,
        u.username,
        u.email,
        u.role,
        u.total_xp AS "totalXp",
        u.current_level AS "currentLevel",
        COALESCE(u.is_banned, false) AS "isBanned",
        u.banned_at AS "bannedAt",
        u.created_at AS "createdAt",
        u.last_synced_at AS "lastSyncedAt",
        (u.password_hash IS NOT NULL) AS "isRegisteredAccount",
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
      WHERE u.role != 'admin' OR u.role IS NULL
      ORDER BY u.created_at DESC
    `);

    res.json({
      count: result.rows.length,
      users: result.rows,
    });
  } catch (err) {
    next(err);
  }
});

// PUT /api/v1/admin/users/:id/ban - Ban (disable) or unban (enable) a user account
router.put('/users/:id/ban', async (req, res, next) => {
  try {
    await ensureUserBanColumns();
    const { id } = req.params;
    const { banned } = req.body || {};

    const existing = await db.query('SELECT id, username, role, COALESCE(is_banned, false) AS "isBanned" FROM users WHERE id = $1', [id]);
    if (existing.rows.length === 0) {
      return res.status(404).json({ error: 'User not found' });
    }

    const targetUser = existing.rows[0];
    if (targetUser.role === 'admin') {
      return res.status(403).json({ error: 'Administrator accounts cannot be banned' });
    }

    const nextBanState = typeof banned === 'boolean' ? banned : !targetUser.isBanned;
    const updateRes = await db.query(
      `UPDATE users
       SET is_banned = $1,
           banned_at = CASE WHEN $1 = true THEN NOW() ELSE NULL END
       WHERE id = $2
       RETURNING id, username, email, role, total_xp AS "totalXp", current_level AS "currentLevel", COALESCE(is_banned, false) AS "isBanned", banned_at AS "bannedAt"`,
      [nextBanState, id]
    );

    res.json({
      message: nextBanState
        ? `User @${targetUser.username} has been banned (disabled).`
        : `User @${targetUser.username} has been unbanned (enabled).`,
      user: updateRes.rows[0],
    });
  } catch (err) {
    next(err);
  }
});

// DELETE /api/v1/admin/users/:id - Permanently delete a user account and cascading logs
router.delete('/users/:id', async (req, res, next) => {
  try {
    const { id } = req.params;

    const existing = await db.query('SELECT id, username, role FROM users WHERE id = $1', [id]);
    if (existing.rows.length === 0) {
      return res.status(404).json({ error: 'User not found' });
    }

    const targetUser = existing.rows[0];
    if (targetUser.role === 'admin') {
      return res.status(403).json({ error: 'Administrator accounts cannot be deleted' });
    }

    await db.query('DELETE FROM users WHERE id = $1', [id]);

    res.json({
      message: `User @${targetUser.username} has been permanently deleted.`,
      deletedId: id,
      username: targetUser.username,
    });
  } catch (err) {
    next(err);
  }
});

// GET /api/v1/admin/pending-questions - Questions awaiting review (automatically deduplicated)
router.get('/pending-questions', async (req, res, next) => {
  try {
    const approvedRes = await db.query(
      "SELECT question_text FROM questions WHERE status = 'APPROVED'"
    );
    const approvedTexts = approvedRes.rows.map((r) => r.question_text);

    const result = await db.query(`
      SELECT 
        q.id,
        q.question_text AS "questionText",
        q.options,
        q.correct_index AS "correctIndex",
        q.explanation,
        q.difficulty,
        q.xp_reward AS "xpReward",
        q.status,
        q.created_at AS "createdAt",
        t.name AS "topicName",
        b.name AS "biomeName",
        u.original_filename AS "sourceFilename"
      FROM questions q
      LEFT JOIN topics t ON q.topic_id = t.id
      LEFT JOIN biomes b ON t.biome_id = b.id
      LEFT JOIN admin_uploads u ON q.upload_id = u.id
      WHERE q.status = 'PENDING'
      ORDER BY q.created_at DESC
    `);

    const seenTexts = [...approvedTexts];
    const uniquePending = [];
    const duplicateIdsToClean = [];

    for (const row of result.rows) {
      if (isDuplicateQuestion(row.questionText, seenTexts)) {
        duplicateIdsToClean.push(row.id);
      } else {
        seenTexts.push(row.questionText);
        uniquePending.push(row);
      }
    }

    if (duplicateIdsToClean.length > 0) {
      await db.query(
        "UPDATE questions SET status = 'REJECTED' WHERE id = ANY($1::uuid[])",
        [duplicateIdsToClean]
      );
    }

    res.json({
      count: uniquePending.length,
      duplicatesRemoved: duplicateIdsToClean.length,
      questions: uniquePending,
    });
  } catch (err) {
    next(err);
  }
});

// POST /api/v1/admin/upload-pdf - Upload PDF and trigger AI question generation
router.post('/upload-pdf', upload.single('pdf'), async (req, res, next) => {
  if (!req.file) {
    return res.status(400).json({ error: 'Please upload a PDF file' });
  }

  try {
    const count = parseInt(req.body.count, 10) || 5;
    const difficulty = parseInt(req.body.difficulty, 10) || 2;
    let topicId = req.body.topicId;

    // Resolve or default topic before starting the AI call
    if (!topicId) {
      const defaultTopic = await db.query('SELECT id, name FROM topics LIMIT 1');
      if (defaultTopic.rows.length > 0) {
        topicId = defaultTopic.rows[0].id;
      }
    }

    const topicRes = await db.query('SELECT name FROM topics WHERE id = $1', [topicId]);
    const topicName = topicRes.rows[0]?.name || 'General Computer Science';

    // Fetch existing PENDING and APPROVED questions to prevent duplicate generation
    const existingRes = await db.query(
      `SELECT question_text
       FROM questions
       WHERE status IN ('PENDING', 'APPROVED')
       ORDER BY (topic_id = $1) DESC, created_at DESC`,
      [topicId]
    );
    const existingQuestionTexts = existingRes.rows.map((r) => r.question_text);

    // 1. Extract text from PDF
    const extractedText = await extractTextFromPDF(req.file.buffer);
    if (!extractedText || extractedText.length < 50) {
      return res.status(400).json({ error: 'The uploaded PDF contains insufficient extractable text' });
    }

    // 2. Generate questions using Gemini (with automatic multi-model failover and deduplication)
    const generated = await generateQuestionsFromText(extractedText, {
      count,
      topic: topicName,
      difficulty,
      existingQuestions: existingQuestionTexts,
    });

    const { uniqueQuestions, skippedDuplicates } = filterUniqueQuestions(
      generated,
      existingQuestionTexts
    );

    if (uniqueQuestions.length === 0) {
      return res.status(409).json({
        error: 'All generated questions matched existing questions in the database. Try selecting a different topic, difficulty tier, or chapter.',
      });
    }

    // 3. Persist upload record and unique generated questions in a fast atomic transaction
    const client = await db.pool.connect();
    try {
      await client.query('BEGIN');

      const uploadRes = await client.query(
        `INSERT INTO admin_uploads (original_filename, status)
         VALUES ($1, 'PROCESSING') RETURNING id`,
        [req.file.originalname]
      );
      const uploadId = uploadRes.rows[0].id;

      const insertedQuestions = [];
      for (const q of uniqueQuestions) {
        const qRes = await client.query(
          `INSERT INTO questions (
            topic_id, upload_id, question_text, options, correct_index, explanation, difficulty, xp_reward, status
          ) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, 'PENDING')
          RETURNING id, question_text AS "questionText", options, correct_index AS "correctIndex", explanation, difficulty, xp_reward AS "xpReward", status`,
          [
            topicId,
            uploadId,
            q.questionText,
            JSON.stringify(q.options),
            q.correctIndex,
            q.explanation,
            q.difficulty,
            q.xpReward,
          ]
        );
        insertedQuestions.push(qRes.rows[0]);
      }

      await client.query("UPDATE admin_uploads SET status = 'COMPLETED' WHERE id = $1", [uploadId]);
      await client.query('COMMIT');

      res.status(201).json({
        message: `Successfully generated ${insertedQuestions.length} unique questions awaiting approval`,
        uploadId,
        filename: req.file.originalname,
        duplicatesFiltered: skippedDuplicates.length,
        questions: insertedQuestions,
      });
    } catch (dbErr) {
      await client.query('ROLLBACK');
      throw dbErr;
    } finally {
      client.release();
    }
  } catch (err) {
    next(err);
  }
});

// PUT /api/v1/admin/approve-question/:id - Approve and optionally edit a question
router.put('/approve-question/:id', async (req, res, next) => {
  try {
    const { id } = req.params;
    const { questionText, options, correctIndex, explanation, difficulty } = req.body || {};

    const existing = await db.query('SELECT * FROM questions WHERE id = $1', [id]);
    if (existing.rows.length === 0) {
      return res.status(404).json({ error: 'Question not found' });
    }

    const current = existing.rows[0];
    const newText = (questionText || current.question_text).trim();
    const newOptions = options ? JSON.stringify(options) : current.options;
    const newCorrectIdx = correctIndex !== undefined ? correctIndex : current.correct_index;
    const newExpl = explanation !== undefined ? explanation : current.explanation;
    const newDiff = difficulty || current.difficulty;
    const newXp = newDiff * 20;

    // Prevent duplicate questions from entering the APPROVED pool
    const approvedCheck = await db.query(
      "SELECT question_text FROM questions WHERE status = 'APPROVED' AND id != $1",
      [id]
    );
    const approvedTexts = approvedCheck.rows.map((r) => r.question_text);

    if (isDuplicateQuestion(newText, approvedTexts)) {
      await db.query("UPDATE questions SET status = 'REJECTED' WHERE id = $1", [id]);
      return res.status(200).json({
        duplicateSkipped: true,
        message: 'Duplicate question detected and automatically discarded because this question is already approved in the live bank.',
      });
    }

    const result = await db.query(
      `UPDATE questions
       SET question_text = $1,
           options = $2,
           correct_index = $3,
           explanation = $4,
           difficulty = $5,
           xp_reward = $6,
           status = 'APPROVED',
           approved_at = NOW()
       WHERE id = $7
       RETURNING id, question_text AS "questionText", options, correct_index AS "correctIndex", explanation, difficulty, xp_reward AS "xpReward", status, approved_at AS "approvedAt"`,
      [newText, newOptions, newCorrectIdx, newExpl, newDiff, newXp, id]
    );

    res.json({
      duplicateSkipped: false,
      message: 'Question approved and moved to active pool',
      question: result.rows[0],
    });
  } catch (err) {
    next(err);
  }
});

// PUT /api/v1/admin/approve-questions-bulk - Approve and publish selected or all pending questions
router.put('/approve-questions-bulk', async (req, res, next) => {
  const client = await db.pool.connect();
  try {
    const { ids, approveAll } = req.body || {};

    await client.query('BEGIN');

    const approvedRes = await client.query(
      "SELECT question_text FROM questions WHERE status = 'APPROVED'"
    );
    const seenApprovedTexts = approvedRes.rows.map((r) => r.question_text);

    let pendingRes;
    if (Array.isArray(ids) && ids.length > 0 && !approveAll) {
      pendingRes = await client.query(
        `SELECT id, question_text, difficulty
         FROM questions
         WHERE id = ANY($1::uuid[]) AND status = 'PENDING'
         ORDER BY created_at ASC`,
        [ids]
      );
    } else {
      pendingRes = await client.query(
        `SELECT id, question_text, difficulty
         FROM questions
         WHERE status = 'PENDING'
         ORDER BY created_at ASC`
      );
    }

    const idsToApprove = [];
    const duplicateIdsToReject = [];

    for (const q of pendingRes.rows) {
      if (isDuplicateQuestion(q.question_text, seenApprovedTexts)) {
        duplicateIdsToReject.push(q.id);
      } else {
        seenApprovedTexts.push(q.question_text);
        idsToApprove.push(q.id);
      }
    }

    if (idsToApprove.length > 0) {
      await client.query(
        `UPDATE questions
         SET status = 'APPROVED',
             xp_reward = difficulty * 20,
             approved_at = NOW()
         WHERE id = ANY($1::uuid[])`,
        [idsToApprove]
      );
    }

    if (duplicateIdsToReject.length > 0) {
      await client.query(
        `UPDATE questions
         SET status = 'REJECTED'
         WHERE id = ANY($1::uuid[])`,
        [duplicateIdsToReject]
      );
    }

    await client.query('COMMIT');

    res.json({
      message: `Approved and published ${idsToApprove.length} unique question(s)${
        duplicateIdsToReject.length > 0
          ? ` (${duplicateIdsToReject.length} duplicate(s) automatically filtered)`
          : ''
      }`,
      approvedCount: idsToApprove.length,
      duplicatesSkipped: duplicateIdsToReject.length,
      approvedIds: idsToApprove,
      duplicateIds: duplicateIdsToReject,
    });
  } catch (err) {
    await client.query('ROLLBACK');
    next(err);
  } finally {
    client.release();
  }
});

// PUT /api/v1/admin/reject-questions-bulk - Reject selected pending questions in bulk
router.put('/reject-questions-bulk', async (req, res, next) => {
  try {
    const { ids } = req.body || {};
    if (!Array.isArray(ids) || ids.length === 0) {
      return res.status(400).json({ error: 'Please provide at least one question ID to discard' });
    }

    const result = await db.query(
      `UPDATE questions
       SET status = 'REJECTED'
       WHERE id = ANY($1::uuid[])
       RETURNING id`,
      [ids]
    );

    res.json({
      message: `Discarded ${result.rows.length} question(s) from the review queue`,
      rejectedCount: result.rows.length,
      rejectedIds: result.rows.map((r) => r.id),
    });
  } catch (err) {
    next(err);
  }
});

// PUT /api/v1/admin/reject-question/:id - Reject a question
router.put('/reject-question/:id', async (req, res, next) => {
  try {
    const { id } = req.params;

    const result = await db.query(
      `UPDATE questions 
       SET status = 'REJECTED' 
       WHERE id = $1 
       RETURNING id, status`,
      [id]
    );

    if (result.rows.length === 0) {
      return res.status(404).json({ error: 'Question not found' });
    }

    res.json({
      message: 'Question marked as REJECTED',
      question: result.rows[0],
    });
  } catch (err) {
    next(err);
  }
});

module.exports = router;

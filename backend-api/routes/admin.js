const express = require('express');
const multer = require('multer');
const db = require('../db');
const { authenticateToken, requireAdmin } = require('../middleware/auth');
const { extractTextFromPDF } = require('../services/pdfService');
const { generateQuestionsFromText } = require('../services/geminiService');

const router = express.Router();

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
    const result = await db.query(`
      SELECT
        u.id,
        u.username,
        u.email,
        u.role,
        u.total_xp AS "totalXp",
        u.current_level AS "currentLevel",
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

// GET /api/v1/admin/pending-questions - Questions awaiting review
router.get('/pending-questions', async (req, res, next) => {
  try {
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

    res.json({
      count: result.rows.length,
      questions: result.rows,
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

    // 1. Extract text from PDF
    const extractedText = await extractTextFromPDF(req.file.buffer);
    if (!extractedText || extractedText.length < 50) {
      return res.status(400).json({ error: 'The uploaded PDF contains insufficient extractable text' });
    }

    // 2. Generate questions using Gemini (with automatic multi-model failover) outside DB transaction
    const generated = await generateQuestionsFromText(extractedText, {
      count,
      topic: topicName,
      difficulty,
    });

    // 3. Persist upload record and generated questions in a fast atomic transaction
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
      for (const q of generated) {
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
        message: `Successfully generated ${insertedQuestions.length} questions awaiting approval`,
        uploadId,
        filename: req.file.originalname,
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
    const { questionText, options, correctIndex, explanation, difficulty } = req.body;

    const existing = await db.query('SELECT * FROM questions WHERE id = $1', [id]);
    if (existing.rows.length === 0) {
      return res.status(404).json({ error: 'Question not found' });
    }

    const current = existing.rows[0];
    const newText = questionText || current.question_text;
    const newOptions = options ? JSON.stringify(options) : current.options;
    const newCorrectIdx = correctIndex !== undefined ? correctIndex : current.correct_index;
    const newExpl = explanation !== undefined ? explanation : current.explanation;
    const newDiff = difficulty || current.difficulty;
    const newXp = newDiff * 20;

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
      message: 'Question approved and moved to active pool',
      question: result.rows[0],
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

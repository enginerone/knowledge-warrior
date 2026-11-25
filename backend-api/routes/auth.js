const express = require('express');
const crypto = require('crypto');
const bcrypt = require('bcryptjs');
const jwt = require('jsonwebtoken');
const db = require('../db');
const { authenticateToken, JWT_SECRET } = require('../middleware/auth');
const { sendPasswordResetOtpEmail } = require('../services/emailService');
const { DEFAULT_SUBJECTS } = require('../services/progressionService');

const router = express.Router();

const EMAIL_REGEX = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

// POST /api/v1/auth/register
router.post('/register', async (req, res, next) => {
  const client = await db.pool.connect();
  try {
    const rawUsername = (req.body.username || '').trim();
    const rawEmail = (req.body.email || '').trim().toLowerCase();
    const password = req.body.password || '';

    if (!rawUsername || !rawEmail || !password) {
      return res.status(400).json({ error: 'Username, valid email address, and password are required.' });
    }

    if (rawUsername.length < 3) {
      return res.status(400).json({ error: 'Username must be at least 3 characters long.' });
    }

    if (!EMAIL_REGEX.test(rawEmail)) {
      return res.status(400).json({ error: 'Please provide a valid email address (used for password recovery OTP).' });
    }

    if (password.length < 6) {
      return res.status(400).json({ error: 'Password must be at least 6 characters long.' });
    }

    await client.query('BEGIN');

    // 1. Check if username is already taken (case-insensitive)
    const existingUsername = await client.query(
      'SELECT id FROM users WHERE LOWER(username) = LOWER($1)',
      [rawUsername]
    );
    if (existingUsername.rows.length > 0) {
      await client.query('ROLLBACK');
      return res.status(409).json({ error: `Username "${rawUsername}" is already taken. Please choose a unique username.` });
    }

    // 2. Check if email is already registered (case-insensitive)
    const existingEmail = await client.query(
      'SELECT id FROM users WHERE LOWER(email) = LOWER($1)',
      [rawEmail]
    );
    if (existingEmail.rows.length > 0) {
      await client.query('ROLLBACK');
      return res.status(409).json({ error: 'An account with this email address is already registered.' });
    }

    const passwordHash = await bcrypt.hash(password, 10);
    const result = await client.query(
      `INSERT INTO users (username, email, password_hash, role, total_xp, current_level, battles_won, battles_lost)
       VALUES ($1, $2, $3, 'player', 0, 1, 0, 0)
       RETURNING id, username, email, role, total_xp, current_level, created_at`,
      [rawUsername, rawEmail, passwordHash]
    );
    const user = result.rows[0];

    for (const subject of DEFAULT_SUBJECTS) {
      await client.query(
        `INSERT INTO user_subject_progress (user_id, subject, subject_level, subject_xp, subject_points, badge_tier, updated_at)
         VALUES ($1, $2, 1, 0, 0, 'Beginner', NOW())
         ON CONFLICT (user_id, subject) DO NOTHING`,
        [user.id, subject]
      );
    }

    await client.query('COMMIT');

    const token = jwt.sign(
      { id: user.id, username: user.username, role: user.role },
      JWT_SECRET,
      { expiresIn: '30d' }
    );

    res.status(201).json({
      message: 'Registration successful! Please proceed to login with your username and password.',
      token,
      user: {
        id: user.id,
        username: user.username,
        email: user.email,
        role: user.role,
        totalXp: 0,
        currentLevel: 1,
        createdAt: user.created_at,
      },
    });
  } catch (err) {
    await client.query('ROLLBACK');
    next(err);
  } finally {
    client.release();
  }
});

// POST /api/v1/auth/login
router.post('/login', async (req, res, next) => {
  try {
    const rawUsername = (req.body.username || '').trim();
    const password = req.body.password || '';

    if (!rawUsername || !password) {
      return res.status(400).json({ error: 'Username and password are required.' });
    }

    const result = await db.query(
      'SELECT * FROM users WHERE LOWER(username) = LOWER($1) OR LOWER(email) = LOWER($1)',
      [rawUsername]
    );

    if (result.rows.length === 0 || !result.rows[0].password_hash) {
      return res.status(401).json({ error: 'Account not found or invalid credentials. Please register first.' });
    }

    const user = result.rows[0];
    if (user.is_banned) {
      return res.status(403).json({
        error: 'This account has been banned (disabled) by an administrator.',
      });
    }

    const valid = await bcrypt.compare(password, user.password_hash);
    if (!valid) {
      return res.status(401).json({ error: 'Incorrect password. Use Forgot Password if you need to reset it via Email OTP.' });
    }

    // Update last_synced_at to track active login timestamp
    await db.query('UPDATE users SET last_synced_at = NOW() WHERE id = $1', [user.id]);

    const token = jwt.sign(
      { id: user.id, username: user.username, role: user.role },
      JWT_SECRET,
      { expiresIn: '30d' }
    );

    res.json({
      message: `Authenticated as ${user.username}`,
      token,
      user: {
        id: user.id,
        username: user.username,
        email: user.email,
        role: user.role,
        totalXp: Math.max(0, user.total_xp || 0),
        currentLevel: Math.max(1, user.current_level || 1),
        battlesWon: user.battles_won || 0,
        battlesLost: user.battles_lost || 0,
      },
    });
  } catch (err) {
    next(err);
  }
});

// POST /api/v1/auth/forgot-password - Send 6-digit OTP to registered email
router.post('/forgot-password', async (req, res, next) => {
  try {
    const rawEmail = (req.body.email || '').trim().toLowerCase();

    if (!rawEmail || !EMAIL_REGEX.test(rawEmail)) {
      return res.status(400).json({ error: 'Please enter the valid email address linked to your account.' });
    }

    const result = await db.query(
      'SELECT id, username, email FROM users WHERE LOWER(email) = LOWER($1)',
      [rawEmail]
    );

    if (result.rows.length === 0) {
      return res.status(404).json({ error: 'No registered user found with that email address.' });
    }

    const user = result.rows[0];
    const otpCode = crypto.randomInt(100000, 1000000).toString();
    const otpHash = await bcrypt.hash(otpCode, 10);

    await db.query(
      `UPDATE users
       SET reset_otp_hash = $1,
           reset_otp_expires_at = NOW() + INTERVAL '10 minutes'
       WHERE id = $2`,
      [otpHash, user.id]
    );

    const mailResult = await sendPasswordResetOtpEmail(user.email, user.username, otpCode);

    res.json({
      message: `A 6-digit OTP has been sent to ${user.email}. Please check your inbox and enter the OTP to reset your password.`,
      username: user.username,
      email: user.email,
      deliveryMode: mailResult.mode,
      previewUrl: mailResult.previewUrl,
    });
  } catch (err) {
    next(err);
  }
});

// POST /api/v1/auth/verify-otp - Verify the 6-digit OTP sent to email
router.post('/verify-otp', async (req, res, next) => {
  try {
    const rawEmail = (req.body.email || '').trim().toLowerCase();
    const rawOtp = String(req.body.otp || '').trim();

    if (!rawEmail || !rawOtp) {
      return res.status(400).json({ error: 'Email and 6-digit OTP are required.' });
    }

    const result = await db.query(
      'SELECT id, username, email, reset_otp_hash, reset_otp_expires_at FROM users WHERE LOWER(email) = LOWER($1)',
      [rawEmail]
    );

    if (result.rows.length === 0) {
      return res.status(404).json({ error: 'No registered user found with that email address.' });
    }

    const user = result.rows[0];
    if (!user.reset_otp_hash || !user.reset_otp_expires_at) {
      return res.status(400).json({ error: 'No active OTP request found. Please request a new OTP.' });
    }

    if (new Date(user.reset_otp_expires_at) < new Date()) {
      return res.status(400).json({ error: 'Your OTP has expired (10-minute limit). Please request a new OTP.' });
    }

    const isOtpValid = await bcrypt.compare(rawOtp, user.reset_otp_hash);
    if (!isOtpValid) {
      return res.status(401).json({ error: 'Invalid OTP code. Please check the 6-digit code sent to your email.' });
    }

    res.json({
      valid: true,
      username: user.username,
      message: `OTP verified for @${user.username}! You can now enter your new password.`,
    });
  } catch (err) {
    next(err);
  }
});

// POST /api/v1/auth/reset-password - Verify OTP and update password in DB
router.post('/reset-password', async (req, res, next) => {
  try {
    const rawEmail = (req.body.email || '').trim().toLowerCase();
    const rawOtp = String(req.body.otp || '').trim();
    const newPassword = req.body.newPassword || '';

    if (!rawEmail || !rawOtp || !newPassword) {
      return res.status(400).json({ error: 'Email, 6-digit OTP, and new password are required.' });
    }

    if (newPassword.length < 6) {
      return res.status(400).json({ error: 'New password must be at least 6 characters long.' });
    }

    const result = await db.query(
      'SELECT id, username, email, reset_otp_hash, reset_otp_expires_at FROM users WHERE LOWER(email) = LOWER($1)',
      [rawEmail]
    );

    if (result.rows.length === 0) {
      return res.status(404).json({ error: 'No registered user found with that email address.' });
    }

    const user = result.rows[0];
    if (!user.reset_otp_hash || !user.reset_otp_expires_at) {
      return res.status(400).json({ error: 'No active OTP request found. Please request a new OTP first.' });
    }

    if (new Date(user.reset_otp_expires_at) < new Date()) {
      return res.status(400).json({ error: 'Your OTP has expired. Please request a new OTP.' });
    }

    const isOtpValid = await bcrypt.compare(rawOtp, user.reset_otp_hash);
    if (!isOtpValid) {
      return res.status(401).json({ error: 'Invalid OTP code. Password could not be changed.' });
    }

    const newPasswordHash = await bcrypt.hash(newPassword, 10);
    await db.query(
      `UPDATE users
       SET password_hash = $1,
           reset_otp_hash = NULL,
           reset_otp_expires_at = NULL,
           last_synced_at = NOW()
       WHERE id = $2`,
      [newPasswordHash, user.id]
    );

    res.json({
      message: `Password updated successfully for @${user.username}! Please log in with your new password.`,
      username: user.username,
    });
  } catch (err) {
    next(err);
  }
});

// GET /api/v1/auth/me
router.get('/me', authenticateToken, async (req, res, next) => {
  try {
    const result = await db.query(
      `SELECT id,
              username,
              email,
              role,
              GREATEST(0, COALESCE(total_xp, 0))::int AS "totalXp",
              GREATEST(1, COALESCE(current_level, 1))::int AS "currentLevel",
              COALESCE(battles_won, 0)::int AS "battlesWon",
              COALESCE(battles_lost, 0)::int AS "battlesLost",
              COALESCE(is_banned, false) AS is_banned,
              created_at,
              last_synced_at
       FROM users
       WHERE id = $1`,
      [req.user.id]
    );

    if (result.rows.length === 0) {
      return res.status(404).json({ error: 'User not found' });
    }

    if (result.rows[0].is_banned) {
      return res.status(403).json({ error: 'This account has been banned (disabled) by an administrator.' });
    }

    res.json({ user: result.rows[0] });
  } catch (err) {
    next(err);
  }
});

module.exports = router;

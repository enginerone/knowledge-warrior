const express = require('express');
const crypto = require('crypto');
const bcrypt = require('bcryptjs');
const jwt = require('jsonwebtoken');
const db = require('../db');
const { authenticateToken, JWT_SECRET } = require('../middleware/auth');
const { sendPasswordResetOtpEmail } = require('../services/emailService');

const router = express.Router();

const EMAIL_REGEX = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

function calculateLevel(totalXp) {
  if (!totalXp || totalXp <= 0) return 1;
  return Math.floor(Math.pow(totalXp / 100, 1 / 1.6)) + 1;
}

// POST /api/v1/auth/register
router.post('/register', async (req, res, next) => {
  try {
    const rawUsername = (req.body.username || '').trim();
    const rawEmail = (req.body.email || '').trim().toLowerCase();
    const password = req.body.password || '';
    const { clientHash, totalXp } = req.body;

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

    // 1. Check if username is already taken (case-insensitive)
    const existingUsername = await db.query(
      'SELECT id FROM users WHERE LOWER(username) = LOWER($1)',
      [rawUsername]
    );
    if (existingUsername.rows.length > 0) {
      return res.status(409).json({ error: `Username "${rawUsername}" is already taken. Please choose a unique username.` });
    }

    // 2. Check if email is already registered (case-insensitive)
    const existingEmail = await db.query(
      'SELECT id FROM users WHERE LOWER(email) = LOWER($1)',
      [rawEmail]
    );
    if (existingEmail.rows.length > 0) {
      return res.status(409).json({ error: 'An account with this email address is already registered.' });
    }

    const passwordHash = await bcrypt.hash(password, 10);
    const initialXp = Math.max(0, parseInt(totalXp, 10) || 0);
    let user = null;

    // If a guest device clientHash is provided, upgrade the guest row in-place to preserve logs & XP
    if (clientHash && typeof clientHash === 'string') {
      const guestName = `guest_${clientHash.slice(0, 10)}`;
      const guestRes = await db.query(
        'SELECT id, total_xp FROM users WHERE username = $1 AND password_hash IS NULL',
        [guestName]
      );
      if (guestRes.rows.length > 0) {
        const mergedXp = Math.max(guestRes.rows[0].total_xp || 0, initialXp);
        const mergedLevel = calculateLevel(mergedXp);
        const updateRes = await db.query(
          `UPDATE users
           SET username = $1, email = $2, password_hash = $3, total_xp = $4, current_level = $5, last_synced_at = NOW()
           WHERE id = $6
           RETURNING id, username, email, role, total_xp, current_level, created_at`,
          [rawUsername, rawEmail, passwordHash, mergedXp, mergedLevel, guestRes.rows[0].id]
        );
        user = updateRes.rows[0];
      }
    }

    if (!user) {
      const initialLevel = calculateLevel(initialXp);
      const result = await db.query(
        `INSERT INTO users (username, email, password_hash, role, total_xp, current_level)
         VALUES ($1, $2, $3, 'player', $4, $5)
         RETURNING id, username, email, role, total_xp, current_level, created_at`,
        [rawUsername, rawEmail, passwordHash, initialXp, initialLevel]
      );
      user = result.rows[0];
    }

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
        totalXp: user.total_xp,
        currentLevel: user.current_level,
        createdAt: user.created_at,
      },
    });
  } catch (err) {
    next(err);
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
        totalXp: user.total_xp,
        currentLevel: user.current_level,
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
      'SELECT id, username, email, role, total_xp, current_level, COALESCE(is_banned, false) AS is_banned, created_at, last_synced_at FROM users WHERE id = $1',
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

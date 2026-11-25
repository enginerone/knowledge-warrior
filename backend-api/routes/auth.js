const express = require('express');
const crypto = require('crypto');
const bcrypt = require('bcryptjs');
const jwt = require('jsonwebtoken');
const db = require('../db');
const { authenticateToken, JWT_SECRET } = require('../middleware/auth');
const { sendPasswordResetOtpEmail } = require('../services/emailService');
const { DEFAULT_SUBJECTS } = require('../services/progressionService');

const router = express.Router();

const USERNAME_REGEX = /^[a-zA-Z0-9_.-]{3,30}$/;
const GMAIL_LOCAL_CHARS_REGEX = /^[a-z0-9.]+$/;
const OTP_REGEX = /^\d{6}$/;
const MAX_OTP_ATTEMPTS = 5;
const OTP_RESEND_COOLDOWN_SECONDS = 60;
const OTP_IP_WINDOW_MS = 15 * 60 * 1000;
const OTP_IP_MAX_REQUESTS = 10;

const otpIpBuckets = new Map();

function validateUsernameFormat(rawInput) {
  const username = String(rawInput || '').trim();
  if (!username) {
    return { valid: false, username: '', error: 'A unique username is required.' };
  }
  if (username.length < 3 || username.length > 30) {
    return { valid: false, username, error: 'Username must be between 3 and 30 characters long.' };
  }
  if (!USERNAME_REGEX.test(username)) {
    return {
      valid: false,
      username,
      error: 'Username may only contain letters, numbers, underscores, hyphens, and periods without spaces.',
    };
  }
  return { valid: true, username };
}

function validateGmailAddress(rawInput, forForgotPassword = false) {
  const email = String(rawInput || '').trim().toLowerCase();
  if (!email) {
    return {
      valid: false,
      email: '',
      canonicalEmail: '',
      error: forForgotPassword
        ? 'Please enter the registered @gmail.com address linked to your account.'
        : 'A valid @gmail.com address is required for registration.',
    };
  }

  const parts = email.split('@');
  if (parts.length !== 2 || parts[1] !== 'gmail.com') {
    return {
      valid: false,
      email,
      canonicalEmail: '',
      error: forForgotPassword
        ? 'Only registered @gmail.com accounts are allowed in Forgot Password.'
        : 'Registration requires a valid @gmail.com account. Non-Gmail addresses are not accepted.',
    };
  }

  const localPart = parts[0];
  if (localPart.length < 6 || localPart.length > 30) {
    return {
      valid: false,
      email,
      canonicalEmail: '',
      error: 'Invalid Gmail address. The username before @gmail.com must be between 6 and 30 characters long.',
    };
  }

  if (localPart.startsWith('.') || localPart.endsWith('.') || localPart.includes('..')) {
    return {
      valid: false,
      email,
      canonicalEmail: '',
      error: 'Invalid Gmail address. Gmail usernames cannot start or end with a period or contain consecutive periods.',
    };
  }

  if (!GMAIL_LOCAL_CHARS_REGEX.test(localPart)) {
    return {
      valid: false,
      email,
      canonicalEmail: '',
      error: 'Invalid Gmail address. Gmail usernames can only contain letters, numbers, and periods before @gmail.com.',
    };
  }

  const canonicalLocal = localPart.replace(/\./g, '');
  if (canonicalLocal.length < 6) {
    return {
      valid: false,
      email,
      canonicalEmail: '',
      error: 'Invalid Gmail address. Gmail usernames must contain at least 6 letters or numbers before @gmail.com.',
    };
  }

  return {
    valid: true,
    email,
    canonicalEmail: `${canonicalLocal}@gmail.com`,
  };
}

function isOtpRequestRateLimited(ipAddress) {
  const key = ipAddress || 'unknown';
  const now = Date.now();
  const recent = (otpIpBuckets.get(key) || []).filter((ts) => now - ts < OTP_IP_WINDOW_MS);
  if (recent.length >= OTP_IP_MAX_REQUESTS) {
    otpIpBuckets.set(key, recent);
    return true;
  }
  recent.push(now);
  otpIpBuckets.set(key, recent);
  return false;
}

// GET /api/v1/auth/check-username?username=...
router.get('/check-username', async (req, res, next) => {
  try {
    const check = validateUsernameFormat(req.query.username);
    if (!check.valid) {
      return res.status(400).json({ available: false, error: check.error });
    }

    const existing = await db.query(
      'SELECT id FROM users WHERE LOWER(username) = LOWER($1) LIMIT 1',
      [check.username]
    );

    if (existing.rows.length > 0) {
      return res.json({
        available: false,
        message: `Username "${check.username}" is already taken. Please choose a unique username.`,
      });
    }

    res.json({
      available: true,
      message: `Username "${check.username}" is available.`,
    });
  } catch (err) {
    next(err);
  }
});

// POST /api/v1/auth/register
router.post('/register', async (req, res, next) => {
  if (req.body && String(req.body.role || '').trim().toLowerCase() === 'admin') {
    return res.status(403).json({
      error: 'Creating another administrator account is strictly disallowed. Only a single administrator account is permitted.',
    });
  }

  const usernameCheck = validateUsernameFormat(req.body.username);
  if (!usernameCheck.valid) {
    return res.status(400).json({ error: usernameCheck.error });
  }

  if (usernameCheck.username.toLowerCase() === 'admin') {
    return res.status(403).json({
      error: 'Username "admin" is reserved for the system administrator and cannot be registered.',
    });
  }

  const gmailCheck = validateGmailAddress(req.body.email, false);
  if (!gmailCheck.valid) {
    return res.status(400).json({ error: gmailCheck.error });
  }

  const password = req.body.password || '';
  if (!password || password.length < 6) {
    return res.status(400).json({ error: 'Password must be at least 6 characters long.' });
  }

  const rawUsername = usernameCheck.username;
  const rawEmail = gmailCheck.email;
  const canonicalGmail = gmailCheck.canonicalEmail;

  const client = await db.pool.connect();
  try {
    await client.query('BEGIN');

    const existingUsername = await client.query(
      'SELECT id FROM users WHERE LOWER(username) = LOWER($1) LIMIT 1',
      [rawUsername]
    );
    if (existingUsername.rows.length > 0) {
      await client.query('ROLLBACK');
      return res.status(409).json({
        error: `Username "${rawUsername}" is already taken. Please choose a unique username.`,
      });
    }

    const existingEmail = await client.query(
      `SELECT id FROM users
       WHERE LOWER(email) = LOWER($1)
          OR (LOWER(email) LIKE '%@gmail.com'
              AND REPLACE(SPLIT_PART(LOWER(email), '@', 1), '.', '') || '@gmail.com' = $2)
       LIMIT 1`,
      [rawEmail, canonicalGmail]
    );
    if (existingEmail.rows.length > 0) {
      await client.query('ROLLBACK');
      return res.status(409).json({
        error: 'An account with this Gmail address is already registered.',
      });
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
    if (err && err.code === '23505') {
      const detail = String(err.detail || err.constraint || '').toLowerCase();
      if (detail.includes('email')) {
        return res.status(409).json({ error: 'An account with this Gmail address is already registered.' });
      }
      return res.status(409).json({
        error: `Username "${rawUsername}" is already taken. Please choose a unique username.`,
      });
    }
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
      'SELECT * FROM users WHERE LOWER(username) = LOWER($1)',
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
      const hint =
        user.role === 'admin'
          ? 'Incorrect administrator password.'
          : 'Incorrect password. Use Forgot Password if you need to reset it via your registered Gmail OTP.';
      return res.status(401).json({ error: hint });
    }

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

// POST /api/v1/auth/forgot-password - Send 6-digit OTP strictly to registered player Gmail accounts
router.post('/forgot-password', async (req, res, next) => {
  try {
    const gmailCheck = validateGmailAddress(req.body.email, true);
    if (!gmailCheck.valid) {
      return res.status(400).json({ error: gmailCheck.error });
    }

    const rawEmail = gmailCheck.email;

    if (isOtpRequestRateLimited(req.ip)) {
      return res.status(429).json({
        error: 'Too many password recovery requests from this connection. Please wait 15 minutes and try again.',
      });
    }

    const result = await db.query(
      `SELECT id, username, email, role, COALESCE(is_banned, false) AS is_banned, reset_otp_requested_at
       FROM users
       WHERE LOWER(email) = LOWER($1)
         AND LOWER(email) LIKE '%@gmail.com'`,
      [rawEmail]
    );

    if (result.rows.length === 0) {
      return res.status(404).json({
        error: 'This Gmail account is not registered in the system. Only registered Gmail accounts can use Forgot Password.',
      });
    }

    const user = result.rows[0];

    if (user.role === 'admin') {
      return res.status(403).json({
        error: 'Administrator passwords cannot be reset via Email OTP. Update the administrator account directly in PostgreSQL.',
      });
    }

    if (user.is_banned) {
      return res.status(403).json({
        error: 'This account has been banned (disabled) by an administrator.',
      });
    }

    if (user.reset_otp_requested_at) {
      const elapsedSeconds = (Date.now() - new Date(user.reset_otp_requested_at).getTime()) / 1000;
      if (elapsedSeconds < OTP_RESEND_COOLDOWN_SECONDS) {
        const waitSeconds = Math.max(1, Math.ceil(OTP_RESEND_COOLDOWN_SECONDS - elapsedSeconds));
        return res.status(429).json({
          error: `Please wait ${waitSeconds} second(s) before requesting another verification code.`,
        });
      }
    }

    const otpCode = crypto.randomInt(100000, 1000000).toString();
    const otpHash = await bcrypt.hash(otpCode, 10);

    const mailResult = await sendPasswordResetOtpEmail(user.email, user.username, otpCode);
    if (!mailResult.delivered) {
      return res.status(503).json({
        error: mailResult.error || 'Unable to deliver the verification email right now. Please try again later.',
      });
    }

    await db.query(
      `UPDATE users
       SET reset_otp_hash = $1,
           reset_otp_expires_at = NOW() + INTERVAL '10 minutes',
           reset_otp_attempts = 0,
           reset_otp_requested_at = NOW()
       WHERE id = $2`,
      [otpHash, user.id]
    );

    const isDevPreview =
      process.env.NODE_ENV !== 'production' &&
      mailResult.mode === 'ethereal' &&
      Boolean(mailResult.previewUrl);

    const responsePayload = {
      message: isDevPreview
        ? `Local dev mode: 6-digit OTP generated for ${user.email} in test inbox (set BREVO_API_KEY, RESEND_API_KEY, or SMTP credentials in .env for real Gmail delivery).`
        : `A 6-digit OTP has been sent to ${user.email}. Check your Gmail inbox and spam folder, then enter the code within 10 minutes.`,
      email: user.email,
      deliveryMode: mailResult.mode,
    };

    if (isDevPreview) {
      responsePayload.previewUrl = mailResult.previewUrl;
    }

    res.json(responsePayload);
  } catch (err) {
    next(err);
  }
});

async function validateRegisteredGmailOtpSubmission(rawEmailInput, rawOtp) {
  const gmailCheck = validateGmailAddress(rawEmailInput, true);
  if (!gmailCheck.valid) {
    return { status: 400, error: gmailCheck.error };
  }

  const rawEmail = gmailCheck.email;

  const result = await db.query(
    `SELECT id,
            username,
            email,
            password_hash,
            role,
            COALESCE(is_banned, false) AS is_banned,
            reset_otp_hash,
            reset_otp_expires_at,
            COALESCE(reset_otp_attempts, 0)::int AS reset_otp_attempts
     FROM users
     WHERE LOWER(email) = LOWER($1)
       AND LOWER(email) LIKE '%@gmail.com'`,
    [rawEmail]
  );

  if (result.rows.length === 0) {
    return {
      status: 404,
      error: 'This Gmail account is not registered in the system. Only registered Gmail accounts can use Forgot Password.',
    };
  }

  const user = result.rows[0];

  if (user.role === 'admin') {
    return {
      status: 403,
      error: 'Administrator passwords cannot be reset via Email OTP. Update the administrator account directly in PostgreSQL.',
    };
  }

  if (user.is_banned) {
    return { status: 403, error: 'This account has been banned (disabled) by an administrator.' };
  }

  if (!user.reset_otp_hash || !user.reset_otp_expires_at) {
    return { status: 400, error: 'No active OTP request found. Please request a new OTP first.' };
  }

  if (new Date(user.reset_otp_expires_at) < new Date()) {
    await db.query(
      `UPDATE users
       SET reset_otp_hash = NULL,
           reset_otp_expires_at = NULL,
           reset_otp_attempts = 0
       WHERE id = $1`,
      [user.id]
    );
    return { status: 400, error: 'Your OTP has expired (10-minute limit). Please request a new OTP.' };
  }

  if (user.reset_otp_attempts >= MAX_OTP_ATTEMPTS) {
    await db.query(
      `UPDATE users
       SET reset_otp_hash = NULL,
           reset_otp_expires_at = NULL,
           reset_otp_attempts = 0
       WHERE id = $1`,
      [user.id]
    );
    return {
      status: 429,
      error: 'Too many incorrect OTP attempts. This code has been invalidated; please request a new OTP.',
    };
  }

  const isOtpValid = await bcrypt.compare(rawOtp, user.reset_otp_hash);
  if (!isOtpValid) {
    const nextAttempts = user.reset_otp_attempts + 1;
    if (nextAttempts >= MAX_OTP_ATTEMPTS) {
      await db.query(
        `UPDATE users
         SET reset_otp_hash = NULL,
             reset_otp_expires_at = NULL,
             reset_otp_attempts = 0
         WHERE id = $1`,
        [user.id]
      );
      return {
        status: 429,
        error: 'Too many incorrect OTP attempts. This code has been invalidated; please request a new OTP.',
      };
    }

    await db.query('UPDATE users SET reset_otp_attempts = $1 WHERE id = $2', [nextAttempts, user.id]);
    const remaining = MAX_OTP_ATTEMPTS - nextAttempts;
    return {
      status: 401,
      error: `Invalid OTP code (${remaining} attempt${remaining === 1 ? '' : 's'} remaining). Please check the 6-digit code sent to your Gmail.`,
    };
  }

  return { status: 200, user };
}

// POST /api/v1/auth/verify-otp - Verify the 6-digit OTP sent to registered player Gmail
router.post('/verify-otp', async (req, res, next) => {
  try {
    const rawOtp = String(req.body.otp || '').trim();
    if (!OTP_REGEX.test(rawOtp)) {
      return res.status(400).json({ error: 'Please enter the 6-digit numeric OTP sent to your Gmail.' });
    }

    const check = await validateRegisteredGmailOtpSubmission(req.body.email, rawOtp);
    if (check.error) {
      return res.status(check.status).json({ error: check.error });
    }

    res.json({
      valid: true,
      username: check.user.username,
      message: `OTP verified for @${check.user.username}! Enter your new password below.`,
    });
  } catch (err) {
    next(err);
  }
});

// POST /api/v1/auth/reset-password - Verify OTP and update player password in DB
router.post('/reset-password', async (req, res, next) => {
  try {
    const rawOtp = String(req.body.otp || '').trim();
    const newPassword = req.body.newPassword || '';

    if (!OTP_REGEX.test(rawOtp) || !newPassword) {
      return res.status(400).json({ error: 'Registered Gmail address, 6-digit numeric OTP, and new password are required.' });
    }

    if (newPassword.length < 6) {
      return res.status(400).json({ error: 'New password must be at least 6 characters long.' });
    }

    const check = await validateRegisteredGmailOtpSubmission(req.body.email, rawOtp);
    if (check.error) {
      return res.status(check.status).json({ error: check.error });
    }

    if (check.user.password_hash) {
      const isSameAsPrevious = await bcrypt.compare(newPassword, check.user.password_hash);
      if (isSameAsPrevious) {
        return res.status(400).json({
          error: 'New password cannot be the same as your previous password. Please choose a different password.',
        });
      }
    }

    const newPasswordHash = await bcrypt.hash(newPassword, 10);
    await db.query(
      `UPDATE users
       SET password_hash = $1,
           reset_otp_hash = NULL,
           reset_otp_expires_at = NULL,
           reset_otp_attempts = 0,
           reset_otp_requested_at = NULL,
           last_synced_at = NOW()
       WHERE id = $2`,
      [newPasswordHash, check.user.id]
    );

    res.json({
      message: `Password updated successfully for @${check.user.username}! Sign in with your new password.`,
      username: check.user.username,
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

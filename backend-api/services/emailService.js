const nodemailer = require('nodemailer');

let cachedTransporter = null;
let cachedTransportKey = null;
let isEthereal = false;

function parseSenderAddress(rawFrom, fallbackEmail) {
  const defaultName = 'Knowledge Warrior Security';
  const defaultEmail = fallbackEmail || 'no-reply@knowledgewarrior.com';

  if (!rawFrom || !rawFrom.trim()) {
    return {
      name: defaultName,
      email: defaultEmail,
      formatted: `"${defaultName}" <${defaultEmail}>`,
    };
  }

  const trimmed = rawFrom.trim();
  const bracketMatch = trimmed.match(/^(?:"?([^"<]*)"?\s*)?<([^>]+)>$/);
  if (bracketMatch) {
    const parsedName = (bracketMatch[1] || defaultName).trim() || defaultName;
    const parsedEmail = bracketMatch[2].trim();
    return {
      name: parsedName,
      email: parsedEmail,
      formatted: `"${parsedName}" <${parsedEmail}>`,
    };
  }

  return {
    name: defaultName,
    email: trimmed,
    formatted: `"${defaultName}" <${trimmed}>`,
  };
}

function normalizeSmtpPassword(host, rawPassword) {
  const pass = (rawPassword || '').trim();
  if (/gmail\.com$/i.test(host) && /^[a-z]{4}(\s+[a-z]{4}){3}$/i.test(pass)) {
    return pass.replace(/\s+/g, '');
  }
  return pass;
}

function buildOtpEmailContent(toEmail, username, otpCode) {
  const subject = `Your Knowledge Warrior Password Reset OTP: ${otpCode}`;
  const text = `Hello ${username}, your Knowledge Warrior password reset OTP is: ${otpCode}. This verification code expires in 10 minutes. If you did not request this reset, you can safely ignore this email.`;
  const html = `
    <div style="font-family: Arial, sans-serif; max-width: 520px; margin: 0 auto; background: #0b1026; color: #eef2ff; padding: 32px; border-radius: 12px; border: 1px solid #1e293b;">
      <h2 style="color: #4de1ff; margin-top: 0;">Knowledge Warrior Password Reset</h2>
      <p>Hello <strong>${username}</strong>,</p>
      <p>We received a request to reset the password for your Knowledge Warrior player account associated with <strong>${toEmail}</strong>.</p>
      <p>Enter the following 6-digit verification code in the game client to verify your identity and set a new password:</p>
      <div style="background: #141c3a; border: 2px solid #4de1ff; border-radius: 8px; padding: 18px; text-align: center; font-size: 32px; font-weight: bold; letter-spacing: 8px; color: #ffd24d; margin: 24px 0;">
        ${otpCode}
      </div>
      <p style="font-size: 13px; color: #8c9bd0;">This verification code expires in <strong>10 minutes</strong>. If you did not request a password reset, you can safely ignore this email.</p>
    </div>
  `;
  return { subject, text, html };
}

async function sendViaBrevoApi({ apiKey, sender, toEmail, username, subject, html, text }) {
  const response = await fetch('https://api.brevo.com/v3/smtp/email', {
    method: 'POST',
    headers: {
      accept: 'application/json',
      'content-type': 'application/json',
      'api-key': apiKey,
    },
    body: JSON.stringify({
      sender: { name: sender.name, email: sender.email },
      to: [{ email: toEmail, name: username }],
      subject,
      htmlContent: html,
      textContent: text,
    }),
    signal: AbortSignal.timeout(12000),
  });

  if (!response.ok) {
    const errBody = await response.text().catch(() => '');
    throw new Error(`Brevo API HTTP ${response.status}: ${errBody}`);
  }

  return { delivered: true, mode: 'brevo', previewUrl: null };
}

async function sendViaResendApi({ apiKey, sender, toEmail, subject, html, text }) {
  const response = await fetch('https://api.resend.com/emails', {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${apiKey}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      from: sender.formatted,
      to: [toEmail],
      subject,
      html,
      text,
    }),
    signal: AbortSignal.timeout(12000),
  });

  if (!response.ok) {
    const errBody = await response.text().catch(() => '');
    throw new Error(`Resend API HTTP ${response.status}: ${errBody}`);
  }

  return { delivered: true, mode: 'resend', previewUrl: null };
}

async function getSmtpOrDevTransporter() {
  const smtpUser = (process.env.SMTP_USER || '').trim();
  const smtpPassRaw = process.env.SMTP_PASS || '';
  const inferredHost =
    (process.env.SMTP_HOST || '').trim() ||
    (/@(gmail|googlemail)\.com$/i.test(smtpUser) ? 'smtp.gmail.com' : '');

  if (inferredHost && smtpUser && smtpPassRaw.trim()) {
    const port = parseInt(process.env.SMTP_PORT, 10) || 587;
    const transportKey = `smtp:${inferredHost}:${port}:${smtpUser}`;
    if (cachedTransporter && cachedTransportKey === transportKey) {
      return cachedTransporter;
    }

    cachedTransporter = nodemailer.createTransport({
      host: inferredHost,
      port,
      secure: port === 465,
      auth: {
        user: smtpUser,
        pass: normalizeSmtpPassword(inferredHost, smtpPassRaw),
      },
      connectionTimeout: 10000,
      greetingTimeout: 10000,
      socketTimeout: 15000,
    });
    cachedTransportKey = transportKey;
    isEthereal = false;
    return cachedTransporter;
  }

  if (process.env.NODE_ENV === 'production') {
    return null;
  }

  if (cachedTransporter && cachedTransportKey === 'ethereal') {
    return cachedTransporter;
  }

  const testAccount = await nodemailer.createTestAccount();
  cachedTransporter = nodemailer.createTransport({
    host: testAccount.smtp.host,
    port: testAccount.smtp.port,
    secure: testAccount.smtp.secure,
    auth: {
      user: testAccount.user,
      pass: testAccount.pass,
    },
    connectionTimeout: 10000,
    greetingTimeout: 10000,
    socketTimeout: 15000,
  });
  cachedTransportKey = 'ethereal';
  isEthereal = true;
  return cachedTransporter;
}

/**
 * Sends a 6-digit password reset OTP to a player's registered email address.
 * Supports Brevo HTTPS API, Resend HTTPS API, and Gmail/Standard SMTP in all environments,
 * with Nodemailer Ethereal test inbox strictly restricted to non-production local development.
 */
async function sendPasswordResetOtpEmail(toEmail, username, otpCode) {
  const isProd = process.env.NODE_ENV === 'production';
  const rawFrom = process.env.EMAIL_FROM || process.env.SMTP_FROM || '';
  const sender = parseSenderAddress(rawFrom, process.env.SMTP_USER);
  const { subject, text, html } = buildOtpEmailContent(toEmail, username, otpCode);

  if (!isProd) {
    console.log(`\n[DEV PASSWORD RESET OTP] To: ${toEmail} (User: ${username}) | OTP Code: ${otpCode}\n`);
  }

  const brevoApiKey = (process.env.BREVO_API_KEY || '').trim();
  if (brevoApiKey) {
    try {
      return await sendViaBrevoApi({
        apiKey: brevoApiKey,
        sender,
        toEmail,
        username,
        subject,
        html,
        text,
      });
    } catch (err) {
      console.error('[EmailService] Brevo delivery failed:', err.message);
      return {
        delivered: false,
        mode: 'brevo',
        previewUrl: null,
        error: 'Unable to send verification email right now. Please verify email sender configuration or try again shortly.',
      };
    }
  }

  const resendApiKey = (process.env.RESEND_API_KEY || '').trim();
  if (resendApiKey) {
    try {
      return await sendViaResendApi({
        apiKey: resendApiKey,
        sender,
        toEmail,
        subject,
        html,
        text,
      });
    } catch (err) {
      console.error('[EmailService] Resend delivery failed:', err.message);
      return {
        delivered: false,
        mode: 'resend',
        previewUrl: null,
        error: 'Unable to send verification email right now. Please verify email sender configuration or try again shortly.',
      };
    }
  }

  try {
    const transporter = await getSmtpOrDevTransporter();
    if (!transporter) {
      console.error('[EmailService] No email provider configured in production (set BREVO_API_KEY, RESEND_API_KEY, or SMTP_USER/SMTP_PASS).');
      return {
        delivered: false,
        mode: 'unconfigured',
        previewUrl: null,
        error: 'Password recovery email service is not configured on the server yet.',
      };
    }

    const info = await transporter.sendMail({
      from: sender.formatted,
      to: toEmail,
      subject,
      text,
      html,
    });

    const previewUrl = !isProd && isEthereal ? nodemailer.getTestMessageUrl(info) : null;
    if (previewUrl) {
      console.log(`[Ethereal Dev Email Preview URL]: ${previewUrl}`);
    }

    return {
      delivered: true,
      mode: isEthereal ? 'ethereal' : 'smtp',
      previewUrl: previewUrl || null,
    };
  } catch (err) {
    console.error('[EmailService] Transport delivery failed:', err.message);
    return {
      delivered: false,
      mode: isEthereal ? 'ethereal' : 'smtp',
      previewUrl: null,
      error: 'Failed to send verification email. Please check your email address or try again later.',
    };
  }
}

module.exports = {
  sendPasswordResetOtpEmail,
};

const nodemailer = require('nodemailer');

let cachedTransporter = null;
let isEthereal = false;

async function getTransporter() {
  if (cachedTransporter) return cachedTransporter;

  if (process.env.SMTP_HOST && process.env.SMTP_USER && process.env.SMTP_PASS) {
    cachedTransporter = nodemailer.createTransport({
      host: process.env.SMTP_HOST,
      port: parseInt(process.env.SMTP_PORT, 10) || 587,
      secure: parseInt(process.env.SMTP_PORT, 10) === 465,
      auth: {
        user: process.env.SMTP_USER,
        pass: process.env.SMTP_PASS,
      },
    });
    isEthereal = false;
    return cachedTransporter;
  }

  // Fallback to Nodemailer Ethereal test account for local testing when SMTP_USER is not set
  try {
    const testAccount = await nodemailer.createTestAccount();
    cachedTransporter = nodemailer.createTransport({
      host: testAccount.smtp.host,
      port: testAccount.smtp.port,
      secure: testAccount.smtp.secure,
      auth: {
        user: testAccount.user,
        pass: testAccount.pass,
      },
    });
    isEthereal = true;
    return cachedTransporter;
  } catch (err) {
    console.warn('⚠️ Could not create Ethereal test account, using console log transport:', err.message);
    return null;
  }
}

/**
 * Sends a 6-digit password reset OTP to the user's registered email address.
 */
async function sendPasswordResetOtpEmail(toEmail, username, otpCode) {
  console.log(`\n📧 [PASSWORD RESET OTP] To: ${toEmail} (User: ${username}) | OTP Code: ${otpCode}\n`);

  const fromAddress = process.env.SMTP_FROM || '"Knowledge Warrior Security" <no-reply@knowledgewarrior.com>';
  const subject = `Your Knowledge Warrior Password Reset OTP: ${otpCode}`;
  const html = `
    <div style="font-family: Arial, sans-serif; max-width: 520px; margin: 0 auto; background: #0b1026; color: #eef2ff; padding: 32px; border-radius: 12px; border: 1px solid #1e293b;">
      <h2 style="color: #4de1ff; margin-top: 0;">⚔️ Knowledge Warrior — Password Reset</h2>
      <p>Hello <strong>${username}</strong>,</p>
      <p>We received a request to reset the password for your Knowledge Warrior account associated with <strong>${toEmail}</strong>.</p>
      <p>Use the following 6-digit verification code (OTP) to verify your identity and set a new password:</p>
      <div style="background: #141c3a; border: 2px solid #4de1ff; border-radius: 8px; padding: 18px; text-align: center; font-size: 32px; font-weight: bold; letter-spacing: 8px; color: #ffd24d; margin: 24px 0;">
        ${otpCode}
      </div>
      <p style="font-size: 13px; color: #8c9bd0;">This OTP code expires in <strong>10 minutes</strong>. If you did not request a password reset, you can safely ignore this email.</p>
    </div>
  `;

  try {
    const transporter = await getTransporter();
    if (!transporter) {
      return { delivered: false, mode: 'console', previewUrl: null };
    }

    const info = await transporter.sendMail({
      from: fromAddress,
      to: toEmail,
      subject,
      text: `Hello ${username}, your Knowledge Warrior password reset OTP is: ${otpCode}. It expires in 10 minutes.`,
      html,
    });

    const previewUrl = isEthereal ? nodemailer.getTestMessageUrl(info) : null;
    if (previewUrl) {
      console.log(`🔗 [Ethereal Email Preview URL]: ${previewUrl}`);
    }

    return {
      delivered: true,
      mode: isEthereal ? 'ethereal' : 'smtp',
      previewUrl: previewUrl || null,
    };
  } catch (err) {
    console.warn('⚠️ SMTP send warning (OTP still valid in DB):', err.message);
    return { delivered: false, mode: 'console', previewUrl: null };
  }
}

module.exports = {
  sendPasswordResetOtpEmail,
};


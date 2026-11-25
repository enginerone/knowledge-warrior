const readline = require('readline');
const bcrypt = require('bcryptjs');
const db = require('../db');

const USERNAME_REGEX = /^[a-zA-Z0-9_.-]{3,30}$/;

function validateUsername(rawInput) {
  const username = String(rawInput || '').trim();
  if (username.length < 3 || username.length > 30) {
    return { valid: false, error: 'Username must be between 3 and 30 characters long.' };
  }
  if (!USERNAME_REGEX.test(username)) {
    return {
      valid: false,
      error: 'Username may only contain letters, numbers, underscores, hyphens, and periods without spaces.',
    };
  }
  return { valid: true, username };
}

function createPromptHelper() {
  const isTty = Boolean(process.stdin.isTTY && process.stdout.isTTY);

  if (!isTty) {
    const rl = readline.createInterface({ input: process.stdin, terminal: false });
    const lines = [];
    const waiters = [];
    let closed = false;

    rl.on('line', (line) => {
      if (waiters.length > 0) {
        waiters.shift()(line);
      } else {
        lines.push(line);
      }
    });
    rl.on('close', () => {
      closed = true;
      while (waiters.length > 0) {
        waiters.shift()('');
      }
    });

    const nextLine = (promptText) => {
      process.stdout.write(promptText);
      if (lines.length > 0) {
        const val = lines.shift();
        process.stdout.write('\n');
        return Promise.resolve(val);
      }
      if (closed) {
        return Promise.resolve('');
      }
      return new Promise((resolve) => waiters.push(resolve));
    };

    return {
      askVisible: nextLine,
      askHidden: nextLine,
      close: () => rl.close(),
    };
  }

  const askVisible = (promptText) =>
    new Promise((resolve) => {
      const rl = readline.createInterface({
        input: process.stdin,
        output: process.stdout,
      });
      rl.question(promptText, (answer) => {
        rl.close();
        resolve(answer);
      });
    });

  const askHidden = (promptText) =>
    new Promise((resolve, reject) => {
      process.stdout.write(promptText);
      const stdin = process.stdin;
      const wasRaw = stdin.isRaw;
      stdin.setRawMode(true);
      stdin.resume();
      stdin.setEncoding('utf8');

      let value = '';

      const cleanup = () => {
        stdin.removeListener('data', onData);
        stdin.setRawMode(Boolean(wasRaw));
        stdin.pause();
      };

      const onData = (chunk) => {
        for (const ch of chunk) {
          if (ch === '\r' || ch === '\n') {
            process.stdout.write('\n');
            cleanup();
            resolve(value);
            return;
          }
          if (ch === '\u0003') {
            process.stdout.write('\n');
            cleanup();
            reject(new Error('Cancelled by user.'));
            return;
          }
          if (ch === '\u007f' || ch === '\b') {
            if (value.length > 0) {
              value = value.slice(0, -1);
              process.stdout.write('\b \b');
            }
          } else if (ch >= ' ') {
            value += ch;
            process.stdout.write('*');
          }
        }
      };

      stdin.on('data', onData);
    });

  return {
    askVisible,
    askHidden,
    close: () => {},
  };
}

async function runSetAdminCredentials() {
  const prompter = createPromptHelper();
  const client = await db.pool.connect();

  try {
    console.log('\nKnowledge Warrior — Administrator Account Setup (PostgreSQL)\n');

    const adminRes = await client.query(
      `SELECT id, username, password_hash
       FROM users
       WHERE role = 'admin'
       ORDER BY created_at ASC`
    );

    if (adminRes.rows.length > 1) {
      throw new Error(
        'Multiple admin accounts detected in users table. Only a single administrator account is allowed.'
      );
    }

    const existingAdmin = adminRes.rows[0] || null;
    if (existingAdmin) {
      console.log(`Current Admin Username : ${existingAdmin.username}`);
      console.log('Mode                   : Updating the single allowed administrator account (creating a second admin is disallowed).\n');
    } else {
      console.log('No existing admin row found; initializing the single allowed administrator account.\n');
    }

    let finalUsername = '';
    while (!finalUsername) {
      const defaultHint = existingAdmin ? ` [Press Enter to keep "${existingAdmin.username}"]` : '';
      const rawUser = await prompter.askVisible(`1. Administrator Username${defaultHint}: `);
      const candidate = !rawUser.trim() && existingAdmin ? existingAdmin.username : rawUser;
      const check = validateUsername(candidate);
      if (!check.valid) {
        console.log(`   Error: ${check.error}`);
        continue;
      }

      const conflict = await client.query(
        existingAdmin
          ? 'SELECT id FROM users WHERE LOWER(username) = LOWER($1) AND id <> $2 LIMIT 1'
          : 'SELECT id FROM users WHERE LOWER(username) = LOWER($1) LIMIT 1',
        existingAdmin ? [check.username, existingAdmin.id] : [check.username]
      );

      if (conflict.rows.length > 0) {
        console.log(`   Error: Username "${check.username}" is already used by another account.`);
        continue;
      }

      finalUsername = check.username;
    }

    let finalPassword = '';
    while (!finalPassword) {
      const newPass = await prompter.askHidden('2. New Administrator Password (min. 6 characters): ');
      if (!newPass || newPass.length < 6) {
        console.log('   Error: Password must be at least 6 characters long.');
        continue;
      }

      if (existingAdmin && existingAdmin.password_hash) {
        const sameAsOld = await bcrypt.compare(newPass, existingAdmin.password_hash);
        if (sameAsOld) {
          console.log('   Error: New password cannot be the same as the previous administrator password.');
          continue;
        }
      }

      const confirmPass = await prompter.askHidden('3. Confirm New Administrator Password: ');
      if (newPass !== confirmPass) {
        console.log('   Error: Passwords did not match. Please try again.');
        continue;
      }

      finalPassword = newPass;
    }

    const passwordHash = await bcrypt.hash(finalPassword, 10);

    if (existingAdmin) {
      await client.query(
        `UPDATE users
         SET username = $1,
             email = NULL,
             password_hash = $2,
             reset_otp_hash = NULL,
             reset_otp_expires_at = NULL,
             reset_otp_attempts = 0,
             last_synced_at = NOW()
         WHERE id = $3`,
        [finalUsername, passwordHash, existingAdmin.id]
      );
    } else {
      await client.query(
        `INSERT INTO users (username, email, password_hash, role, total_xp, current_level)
         VALUES ($1, NULL, $2, 'admin', 9999, 50)`,
        [finalUsername, passwordHash]
      );
    }

    console.log('\nAdministrator credentials updated successfully in PostgreSQL!');
    console.log(`  Username : ${finalUsername}`);
    console.log('  Password : [updated securely with bcrypt]\n');
  } catch (err) {
    console.error(`\nFailed to update administrator credentials: ${err.message}`);
    process.exitCode = 1;
  } finally {
    prompter.close();
    client.release();
    await db.pool.end();
  }
}

if (require.main === module) {
  runSetAdminCredentials();
}

module.exports = runSetAdminCredentials;

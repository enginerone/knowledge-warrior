const fs = require('fs');
const path = require('path');
const bcrypt = require('bcryptjs');
const db = require('../db');

function getBadgeTier(level) {
  if (level <= 15) return 'Beginner';
  if (level <= 30) return 'Intermediate';
  if (level <= 60) return 'Advanced';
  return 'Expert';
}

async function seed() {
  console.log('🌱 Starting database seeding...');
  const client = await db.pool.connect();

  try {
    await client.query('BEGIN');

    const adminCheck = await client.query('SELECT id FROM users WHERE username = $1', ['admin']);
    if (adminCheck.rows.length === 0) {
      const passwordHash = await bcrypt.hash('AdminPassword123!', 10);
      await client.query(
        `INSERT INTO users (username, email, password_hash, role, total_xp, current_level)
         VALUES ($1, $2, $3, $4, $5, $6)`,
        ['admin', 'admin@knowledgewarrior.com', passwordHash, 'admin', 9999, 50]
      );
      console.log('  👑 Created default admin user: admin / AdminPassword123!');
    } else {
      console.log('  ℹ️ Admin user already exists.');
    }

    const questionsPath = path.resolve(__dirname, '../../unity-client/Assets/Data/questions.json');
    if (!fs.existsSync(questionsPath)) {
      console.warn('  ⚠️ unity-client/Assets/Data/questions.json not found, skipping questions seeding.');
      await client.query('COMMIT');
      return;
    }

    const data = JSON.parse(fs.readFileSync(questionsPath, 'utf8'));
    const rawQuestions = data.questions || [];

    const biomeMap = new Map();
    const topicMap = new Map();

    for (const q of rawQuestions) {
      const subjectName = q.subject || 'General';
      const biomeSlug = subjectName.toLowerCase().replace(/[^a-z0-9]+/g, '-');

      if (!biomeMap.has(biomeSlug)) {
        let biomeRes = await client.query('SELECT id FROM biomes WHERE slug = $1', [biomeSlug]);
        if (biomeRes.rows.length === 0) {
          biomeRes = await client.query(
            `INSERT INTO biomes (name, slug, description, display_order)
             VALUES ($1, $2, $3, $4) RETURNING id`,
            [subjectName, biomeSlug, `Curriculum realm for ${subjectName}`, biomeMap.size + 1]
          );
        }
        biomeMap.set(biomeSlug, biomeRes.rows[0].id);
      }

      const topicName = q.topic || 'General';
      const topicKey = `${biomeSlug}:${topicName}`;

      if (!topicMap.has(topicKey)) {
        const biomeId = biomeMap.get(biomeSlug);
        const topicSlug = `${biomeSlug}-${topicName.toLowerCase().replace(/[^a-z0-9]+/g, '-')}`;
        let topicRes = await client.query('SELECT id FROM topics WHERE slug = $1', [topicSlug]);
        if (topicRes.rows.length === 0) {
          topicRes = await client.query(
            `INSERT INTO topics (biome_id, name, slug, tier)
             VALUES ($1, $2, $3, $4) RETURNING id`,
            [biomeId, topicName, topicSlug, q.difficulty || 1]
          );
        }
        topicMap.set(topicKey, topicRes.rows[0].id);
      }
    }

    console.log(`  🗺️ Seeded ${biomeMap.size} biomes and ${topicMap.size} topics.`);

    let insertedQuestions = 0;
    for (const q of rawQuestions) {
      const subjectName = q.subject || 'General';
      const biomeSlug = subjectName.toLowerCase().replace(/[^a-z0-9]+/g, '-');
      const topicName = q.topic || 'General';
      const topicKey = `${biomeSlug}:${topicName}`;
      const topicId = topicMap.get(topicKey);

      const existing = await client.query(
        'SELECT id FROM questions WHERE question_text = $1',
        [q.question]
      );

      if (existing.rows.length === 0) {
        await client.query(
          `INSERT INTO questions (
            topic_id, question_type, question_text, options, correct_index, explanation, difficulty, xp_reward, status, approved_at
          ) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, 'APPROVED', NOW())`,
          [
            topicId,
            q.questionType || 'multiple_choice',
            q.question,
            JSON.stringify(q.choices),
            q.correctIndex,
            q.explanation || '',
            q.difficulty || 1,
            (q.difficulty || 1) * 20,
          ]
        );
        insertedQuestions++;
      } else {
        await client.query(
          `UPDATE questions
           SET question_type = $1,
               options = $2,
               correct_index = $3,
               explanation = $4,
               difficulty = $5,
               xp_reward = $6
           WHERE id = $7`,
          [
            q.questionType || 'multiple_choice',
            JSON.stringify(q.choices),
            q.correctIndex,
            q.explanation || '',
            q.difficulty || 1,
            (q.difficulty || 1) * 20,
            existing.rows[0].id,
          ]
        );
      }
    }

    console.log(`  ❓ Seeded/updated questions (${insertedQuestions} new).`);

    const sampleWarriors = [
      {
        username: 'Aria_Bitweaver',
        email: 'aria@warrior.dev',
        xp: 3450,
        level: 16,
        subjects: { Programming: 1, Databases: 10, Networking: 16, 'Web Development': 1, Cybersecurity: 31, 'Computer Systems': 1 },
      },
      {
        username: 'ProfessorByte',
        email: 'byte@warrior.dev',
        xp: 5980,
        level: 40,
        subjects: { Programming: 6, Databases: 40, Networking: 1, 'Web Development': 16, Cybersecurity: 1, 'Computer Systems': 61 },
      },
      {
        username: 'NullSlayer99',
        email: 'nullslayer@warrior.dev',
        xp: 2640,
        level: 10,
        subjects: { Programming: 1, Databases: 1, Networking: 10, 'Web Development': 1, Cybersecurity: 1, 'Computer Systems': 1 },
      },
      {
        username: 'SyntaxKnight',
        email: 'knight@warrior.dev',
        xp: 1890,
        level: 6,
        subjects: { Programming: 6, Databases: 10, Networking: 1, 'Web Development': 6, Cybersecurity: 16, 'Computer Systems': 1 },
      },
      {
        username: 'BinaryApprentice',
        email: 'apprentice@warrior.dev',
        xp: 980,
        level: 2,
        subjects: { Programming: 2, Databases: 1, Networking: 2, 'Web Development': 2, Cybersecurity: 2, 'Computer Systems': 2 },
      },
      {
        username: 'CipherValkyrie',
        email: 'valkyrie@warrior.dev',
        xp: 8200,
        level: 65,
        subjects: { Programming: 31, Databases: 40, Networking: 31, 'Web Development': 31, Cybersecurity: 65, 'Computer Systems': 31 },
      },
    ];

    const defaultPlayerHash = await bcrypt.hash('Warrior123!', 10);
    for (const w of sampleWarriors) {
      const userRes = await client.query(
        `INSERT INTO users (username, email, password_hash, role, total_xp, current_level, last_synced_at)
         VALUES ($1, $2, $3, 'player', $4, $5, NOW())
         ON CONFLICT (username) DO UPDATE SET last_synced_at = NOW()
         RETURNING id`,
        [w.username, w.email, defaultPlayerHash, w.xp, w.level]
      );
      const userId = userRes.rows[0].id;

      for (const [subj, lv] of Object.entries(w.subjects)) {
        const subjXp = Math.max(120, lv * 140);
        const badge = getBadgeTier(lv);
        await client.query(
          `INSERT INTO user_subject_progress (user_id, subject, subject_level, subject_xp, badge_tier, updated_at)
           VALUES ($1, $2, $3, $4, $5, NOW())
           ON CONFLICT (user_id, subject) DO NOTHING`,
          [userId, subj, lv, subjXp, badge]
        );
      }
    }
    console.log('  🏆 Ensured sample leaderboard & subject PvP warriors exist (password: Warrior123!).');

    await client.query('COMMIT');
    console.log('✅ Seeding completed successfully.');
  } catch (err) {
    await client.query('ROLLBACK');
    console.error('❌ Seeding failed:', err);
    process.exit(1);
  } finally {
    client.release();
    await db.pool.end();
  }
}

if (require.main === module) {
  seed();
}

module.exports = seed;

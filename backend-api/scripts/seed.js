const fs = require('fs');
const path = require('path');
const bcrypt = require('bcryptjs');
const db = require('../db');
const expandedQuestions = require('../data/expandedQuestions');
const { isDuplicateQuestion } = require('../services/questionDeduplicator');

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

    const adminCheck = await client.query("SELECT COUNT(*)::int AS count FROM users WHERE role = 'admin'");
    if (adminCheck.rows[0].count === 0) {
      const initialAdminUsername = (process.env.ADMIN_USERNAME || 'admin').trim();
      const initialAdminPassword = process.env.ADMIN_INITIAL_PASSWORD || 'AdminPassword123!';
      const passwordHash = await bcrypt.hash(initialAdminPassword, 10);
      await client.query(
        `INSERT INTO users (username, email, password_hash, role, total_xp, current_level)
         VALUES ($1, NULL, $2, $3, $4, $5)`,
        [initialAdminUsername, passwordHash, 'admin', 9999, 50]
      );
      console.log('  👑 Created initial single admin user account.');
    } else {
      console.log('  ℹ️ Single admin user already exists (additional admin creation strictly disallowed).');
    }

    const unityQuestionsPath = path.resolve(__dirname, '../../unity-client/Assets/Data/questions.json');
    const bundledQuestionsPath = path.resolve(__dirname, '../data/baseQuestions.json');
    const resolvedQuestionsPath = fs.existsSync(unityQuestionsPath)
      ? unityQuestionsPath
      : (fs.existsSync(bundledQuestionsPath) ? bundledQuestionsPath : null);

    let baseQuestions = [];
    if (resolvedQuestionsPath) {
      const data = JSON.parse(fs.readFileSync(resolvedQuestionsPath, 'utf8'));
      baseQuestions = data.questions || [];
    } else {
      console.warn('  ⚠️ Base questions.json not found, seeding expanded curriculum questions only.');
    }

    const rawQuestions = [...baseQuestions, ...expandedQuestions];

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

    const existingRowsRes = await client.query('SELECT id, question_text FROM questions');
    const knownTexts = existingRowsRes.rows.map((r) => r.question_text);

    let insertedQuestions = 0;
    for (const q of rawQuestions) {
      const subjectName = q.subject || 'General';
      const biomeSlug = subjectName.toLowerCase().replace(/[^a-z0-9]+/g, '-');
      const topicName = q.topic || 'General';
      const topicKey = `${biomeSlug}:${topicName}`;
      const topicId = topicMap.get(topicKey);

      const existing = await client.query(
        'SELECT id FROM questions WHERE LOWER(question_text) = LOWER($1)',
        [q.question]
      );

      if (existing.rows.length === 0) {
        if (isDuplicateQuestion(q.question, knownTexts, 0.85)) {
          continue;
        }
        knownTexts.push(q.question);
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
      { username: 'Aria_Bitweaver', email: 'aria@warrior.dev' },
      { username: 'ProfessorByte', email: 'byte@warrior.dev' },
      { username: 'NullSlayer99', email: 'nullslayer@warrior.dev' },
      { username: 'SyntaxKnight', email: 'knight@warrior.dev' },
      { username: 'BinaryApprentice', email: 'apprentice@warrior.dev' },
      { username: 'CipherValkyrie', email: 'valkyrie@warrior.dev' },
    ];

    const defaultSubjects = [
      'Programming',
      'Web Development',
      'Networking',
      'Databases',
      'Cybersecurity',
      'Computer Systems',
      'Artificial Intelligence',
      'UI/UX & Software Engineering',
    ];

    const defaultPlayerHash = await bcrypt.hash('Warrior123!', 10);
    for (const w of sampleWarriors) {
      const userRes = await client.query(
        `INSERT INTO users (username, email, password_hash, role, total_xp, current_level, battles_won, battles_lost, last_synced_at)
         VALUES ($1, $2, $3, 'player', 0, 1, 0, 0, NOW())
         ON CONFLICT (username) DO UPDATE SET last_synced_at = NOW()
         RETURNING id`,
        [w.username, w.email, defaultPlayerHash]
      );
      const userId = userRes.rows[0].id;

      for (const subj of defaultSubjects) {
        await client.query(
          `INSERT INTO user_subject_progress (user_id, subject, subject_level, subject_xp, subject_points, badge_tier, updated_at)
           VALUES ($1, $2, 1, 0, 0, 'Beginner', NOW())
           ON CONFLICT (user_id, subject) DO NOTHING`,
          [userId, subj]
        );
      }
    }
    console.log('  🏆 Ensured sample warriors start at Subject Level 1 across all categories (password: Warrior123!).');

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

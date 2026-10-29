const db = require('../db');

async function runMigrations() {
  console.log('🔄 Running database migrations...');
  const client = await db.pool.connect();

  try {
    await client.query('BEGIN');

    await client.query('CREATE EXTENSION IF NOT EXISTS "pgcrypto";');

    await client.query(`
      CREATE TABLE IF NOT EXISTS biomes (
        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
        name VARCHAR(100) NOT NULL,
        slug VARCHAR(100) UNIQUE NOT NULL,
        description TEXT,
        display_order INT DEFAULT 0,
        created_at TIMESTAMPTZ DEFAULT NOW()
      );
    `);

    await client.query(`
      CREATE TABLE IF NOT EXISTS topics (
        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
        biome_id UUID REFERENCES biomes(id) ON DELETE CASCADE,
        name VARCHAR(100) NOT NULL,
        slug VARCHAR(100) UNIQUE,
        tier INT DEFAULT 1,
        created_at TIMESTAMPTZ DEFAULT NOW()
      );
    `);

    await client.query(`
      CREATE TABLE IF NOT EXISTS admin_uploads (
        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
        original_filename VARCHAR(255) NOT NULL,
        storage_path VARCHAR(500),
        status VARCHAR(50) DEFAULT 'PENDING',
        uploaded_at TIMESTAMPTZ DEFAULT NOW()
      );
    `);

    await client.query(`
      CREATE TABLE IF NOT EXISTS questions (
        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
        topic_id UUID REFERENCES topics(id) ON DELETE CASCADE,
        upload_id UUID REFERENCES admin_uploads(id) ON DELETE SET NULL,
        question_type VARCHAR(40) DEFAULT 'multiple_choice',
        question_text TEXT NOT NULL,
        options JSONB NOT NULL,
        correct_index SMALLINT NOT NULL CHECK (correct_index BETWEEN 0 AND 3),
        explanation TEXT,
        difficulty SMALLINT DEFAULT 1 CHECK (difficulty BETWEEN 1 AND 4),
        xp_reward INT DEFAULT 20,
        status VARCHAR(20) DEFAULT 'PENDING' CHECK (status IN ('PENDING', 'APPROVED', 'REJECTED')),
        created_at TIMESTAMPTZ DEFAULT NOW(),
        approved_at TIMESTAMPTZ
      );
      ALTER TABLE questions ADD COLUMN IF NOT EXISTS question_type VARCHAR(40) DEFAULT 'multiple_choice';
      ALTER TABLE questions DROP CONSTRAINT IF EXISTS questions_difficulty_check;
      ALTER TABLE questions ADD CONSTRAINT questions_difficulty_check CHECK (difficulty BETWEEN 1 AND 4);
      CREATE INDEX IF NOT EXISTS idx_questions_topic_status ON questions(topic_id, status);
      CREATE INDEX IF NOT EXISTS idx_questions_difficulty ON questions(difficulty);
    `);

    await client.query(`
      CREATE TABLE IF NOT EXISTS users (
        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
        username VARCHAR(50) UNIQUE NOT NULL,
        email VARCHAR(255) UNIQUE,
        password_hash VARCHAR(255),
        role VARCHAR(20) DEFAULT 'player' CHECK (role IN ('player', 'admin')),
        total_xp INT DEFAULT 0,
        current_level INT DEFAULT 1,
        reset_otp_hash VARCHAR(255),
        reset_otp_expires_at TIMESTAMPTZ,
        created_at TIMESTAMPTZ DEFAULT NOW(),
        last_synced_at TIMESTAMPTZ DEFAULT NOW()
      );
      ALTER TABLE users ADD COLUMN IF NOT EXISTS reset_otp_hash VARCHAR(255);
      ALTER TABLE users ADD COLUMN IF NOT EXISTS reset_otp_expires_at TIMESTAMPTZ;
      CREATE INDEX IF NOT EXISTS idx_users_xp ON users(total_xp DESC);
    `);

    await client.query(`
      CREATE TABLE IF NOT EXISTS progress_logs (
        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
        user_id UUID REFERENCES users(id) ON DELETE CASCADE,
        question_id UUID REFERENCES questions(id) ON DELETE SET NULL,
        was_correct BOOLEAN NOT NULL,
        response_time_ms INT,
        answered_at TIMESTAMPTZ DEFAULT NOW()
      );
      CREATE INDEX IF NOT EXISTS idx_progress_logs_user ON progress_logs(user_id);
    `);

    await client.query(`
      CREATE TABLE IF NOT EXISTS user_subject_progress (
        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
        user_id UUID REFERENCES users(id) ON DELETE CASCADE,
        subject VARCHAR(100) NOT NULL,
        subject_level INT DEFAULT 1 CHECK (subject_level BETWEEN 1 AND 100),
        subject_xp INT DEFAULT 100,
        badge_tier VARCHAR(30) DEFAULT 'Beginner',
        pvp_wins INT DEFAULT 0,
        pvp_losses INT DEFAULT 0,
        updated_at TIMESTAMPTZ DEFAULT NOW(),
        UNIQUE (user_id, subject)
      );
      CREATE INDEX IF NOT EXISTS idx_user_subject_level ON user_subject_progress(LOWER(subject), subject_level);
    `);

    await client.query(`
      CREATE TABLE IF NOT EXISTS pvp_matches (
        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
        challenger_id UUID REFERENCES users(id) ON DELETE CASCADE,
        opponent_id UUID REFERENCES users(id) ON DELETE CASCADE,
        subject VARCHAR(100) NOT NULL,
        subject_level INT NOT NULL,
        winner_id UUID REFERENCES users(id) ON DELETE SET NULL,
        loser_id UUID REFERENCES users(id) ON DELETE SET NULL,
        winner_xp_gained INT DEFAULT 0,
        loser_points_deducted INT DEFAULT 0,
        created_at TIMESTAMPTZ DEFAULT NOW()
      );
      CREATE INDEX IF NOT EXISTS idx_pvp_matches_subject ON pvp_matches(LOWER(subject), subject_level);
    `);

    await client.query('COMMIT');
    console.log('✅ Migrations completed successfully.');
  } catch (error) {
    await client.query('ROLLBACK');
    console.error('❌ Migration failed:', error);
    process.exit(1);
  } finally {
    client.release();
    await db.pool.end();
  }
}

if (require.main === module) {
  runMigrations();
}

module.exports = runMigrations;

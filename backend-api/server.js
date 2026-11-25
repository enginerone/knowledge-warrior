const express = require('express');
const cors = require('cors');
require('dotenv').config();

const db = require('./db');
const errorHandler = require('./middleware/errorHandler');

const authRoutes = require('./routes/auth');
const questionsRoutes = require('./routes/questions');
const syncRoutes = require('./routes/sync');
const leaderboardRoutes = require('./routes/leaderboard');
const adminRoutes = require('./routes/admin');

const app = express();
const PORT = process.env.PORT || 5000;
const NODE_ENV = process.env.NODE_ENV || 'development';

const configuredCorsOrigins = (process.env.CORS_ORIGIN || '')
  .split(',')
  .map((origin) => origin.trim().replace(/\/+$/, ''))
  .filter(Boolean);

const localDevOrigins = [
  'http://localhost:5173',
  'http://127.0.0.1:5173',
  'http://localhost:5000',
  'http://127.0.0.1:5000',
];

const corsOptions = {
  origin(origin, callback) {
    if (!origin) {
      return callback(null, true);
    }
    const normalizedOrigin = origin.trim().replace(/\/+$/, '');
    if (NODE_ENV === 'production') {
      if (configuredCorsOrigins.length === 0 || configuredCorsOrigins.includes(normalizedOrigin)) {
        return callback(null, true);
      }
      return callback(new Error(`Origin ${origin} is not permitted by CORS_ORIGIN`));
    }
    if (localDevOrigins.includes(normalizedOrigin) || configuredCorsOrigins.includes(normalizedOrigin)) {
      return callback(null, true);
    }
    return callback(null, true);
  },
  credentials: true,
};

// Middleware
app.use(cors(corsOptions));
app.use(express.json({ limit: '10mb' }));
app.use(express.urlencoded({ extended: true, limit: '10mb' }));

// Health check endpoint for Railway / load balancers
app.get('/api/v1/health', async (req, res) => {
  try {
    const dbRes = await db.query('SELECT NOW() AS now, current_database() AS db');
    res.json({
      status: 'ok',
      service: 'Knowledge Warrior API',
      environment: NODE_ENV,
      timestamp: dbRes.rows[0].now,
      database: 'connected',
      databaseName: dbRes.rows[0].db,
      uptimeSeconds: Math.floor(process.uptime()),
    });
  } catch (err) {
    res.status(503).json({
      status: 'error',
      database: 'disconnected',
      error: err.message,
    });
  }
});

// API Routes
app.use('/api/v1/auth', authRoutes);
app.use('/api/v1/questions', questionsRoutes);
app.use('/api/v1/sync', syncRoutes);
app.use('/api/v1/leaderboard', leaderboardRoutes);
app.use('/api/v1/admin', adminRoutes);

// 404 Handler
app.use((req, res) => {
  res.status(404).json({
    status: 'error',
    message: `Cannot ${req.method} ${req.originalUrl}`,
  });
});

// Central Error Handler
app.use(errorHandler);

// Start Server
if (require.main === module) {
  const server = app.listen(PORT, () => {
    console.log(`🚀 Knowledge Warrior Backend API listening on http://localhost:${PORT}`);
    console.log(`📡 Health check available at: http://localhost:${PORT}/api/v1/health`);
  });

  // Graceful shutdown
  const shutdown = () => {
    console.log('\n🛑 Gracefully shutting down...');
    server.close(() => {
      db.pool.end(() => {
        console.log('🔒 Database pool closed.');
        process.exit(0);
      });
    });
  };

  process.on('SIGTERM', shutdown);
  process.on('SIGINT', shutdown);
}

module.exports = app;

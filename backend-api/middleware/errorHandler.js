function errorHandler(err, req, res, next) {
  console.error('❌ Server Error:', err);
  const status = err.status || err.statusCode || 500;
  res.status(status).json({
    status: 'error',
    message: err.message || 'Internal Server Error',
    ...(process.env.NODE_ENV !== 'production' && { stack: err.stack }),
  });
}

module.exports = errorHandler;

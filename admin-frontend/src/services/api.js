const API_BASE = '/api/v1';

function getAuthHeaders(isMultipart = false) {
  const token = localStorage.getItem('kw_admin_token');
  const headers = {};
  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }
  if (!isMultipart) {
    headers['Content-Type'] = 'application/json';
  }
  return headers;
}

export async function request(endpoint, options = {}) {
  const isMultipart = options.body instanceof FormData;
  const headers = {
    ...getAuthHeaders(isMultipart),
    ...options.headers,
  };

  const response = await fetch(`${API_BASE}${endpoint}`, {
    ...options,
    headers,
  });

  const data = await response.json().catch(() => ({}));

  if (!response.ok) {
    const errorMsg = data.message || data.error || `HTTP error ${response.status}`;
    const err = new Error(errorMsg);
    err.status = response.status;
    err.data = data;
    throw err;
  }

  return data;
}

export const api = {
  // Auth
  register: (username, email, password) =>
    request('/auth/register', {
      method: 'POST',
      body: JSON.stringify({ username, email, password }),
    }),

  login: (username, password) =>
    request('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    }),

  forgotPassword: (email) =>
    request('/auth/forgot-password', {
      method: 'POST',
      body: JSON.stringify({ email }),
    }),

  verifyOtp: (email, otp) =>
    request('/auth/verify-otp', {
      method: 'POST',
      body: JSON.stringify({ email, otp }),
    }),

  resetPassword: (email, otp, newPassword) =>
    request('/auth/reset-password', {
      method: 'POST',
      body: JSON.stringify({ email, otp, newPassword }),
    }),

  getMe: () => request('/auth/me'),

  // Public / Shared
  getHealth: () => request('/health'),
  getBiomes: () => request('/questions/biomes'),
  getQuestions: (params = {}) => {
    const qs = new URLSearchParams(params).toString();
    return request(`/questions${qs ? `?${qs}` : ''}`);
  },
  getLeaderboard: () => request('/leaderboard/global'),

  // Admin
  getStats: () => request('/admin/stats'),
  getRegisteredUsers: () => request('/admin/users'),
  getPendingQuestions: () => request('/admin/pending-questions'),
  uploadPdf: (formData) =>
    request('/admin/upload-pdf', {
      method: 'POST',
      body: formData,
    }),
  approveQuestion: (id, payload) =>
    request(`/admin/approve-question/${id}`, {
      method: 'PUT',
      body: JSON.stringify(payload),
    }),
  rejectQuestion: (id) =>
    request(`/admin/reject-question/${id}`, {
      method: 'PUT',
    }),
};

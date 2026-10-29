const { GoogleGenerativeAI } = require('@google/generative-ai');
const { filterUniqueQuestions } = require('./questionDeduplicator');

const DEFAULT_FALLBACK_MODELS = [
  'gemini-3.5-flash-lite',
  'gemini-flash-lite-latest',
  'gemini-3.1-flash-lite',
  'gemini-3.5-flash',
  'gemini-3.8-flash',
  'gemini-3.7-flash',
  'gemini-2.5-flash',
];

const MODEL_TIMEOUT_MS = 18000;

/**
 * Builds the ordered list of Gemini models to attempt.
 */
function getCandidateModels() {
  const configured = process.env.GEMINI_MODEL ? process.env.GEMINI_MODEL.trim() : '';
  if (!configured) {
    return [...DEFAULT_FALLBACK_MODELS];
  }
  return [configured, ...DEFAULT_FALLBACK_MODELS.filter((m) => m !== configured)];
}

/**
 * Shuffles an array and returns a new array with the updated correct index.
 */
function shuffleOptions(options, originalCorrectIndex) {
  const correctOptionText = options[originalCorrectIndex];
  const items = [...options];

  for (let i = items.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [items[i], items[j]] = [items[j], items[i]];
  }

  const newCorrectIndex = items.indexOf(correctOptionText);
  return {
    options: items,
    correctIndex: newCorrectIndex >= 0 ? newCorrectIndex : 0,
  };
}

/**
 * Validates a single question object against the ingestion schema.
 */
function validateQuestion(q) {
  if (!q || typeof q !== 'object') return false;
  if (typeof q.questionText !== 'string' || q.questionText.trim().length < 10) return false;
  if (!Array.isArray(q.options) || q.options.length !== 4) return false;
  if (q.options.some((opt) => typeof opt !== 'string' || opt.trim().length === 0)) return false;
  if (typeof q.correctIndex !== 'number' || q.correctIndex < 0 || q.correctIndex > 3) return false;

  return true;
}

/**
 * Parses, sanitizes, and deduplicates raw LLM JSON response text into validated question objects.
 */
function parseAndSanitizeQuestions(responseText, defaultDifficulty, existingQuestions = []) {
  let parsed = [];
  try {
    parsed = JSON.parse(responseText);
  } catch (err) {
    const match = responseText.match(/\[[\s\S]*\]/);
    if (match) {
      parsed = JSON.parse(match[0]);
    } else {
      throw new Error(`Failed to parse Gemini output as JSON: ${err.message}`);
    }
  }

  if (!Array.isArray(parsed)) {
    throw new Error('LLM did not return a JSON array');
  }

  const sanitized = [];
  for (const item of parsed) {
    if (!validateQuestion(item)) {
      console.warn('⚠️ Discarded malformed question from LLM output:', item);
      continue;
    }

    const { options: shuffledOpts, correctIndex: shuffledIdx } = shuffleOptions(
      item.options,
      item.correctIndex
    );

    const resolvedDifficulty = item.difficulty || defaultDifficulty;
    sanitized.push({
      questionText: item.questionText.trim(),
      options: shuffledOpts,
      correctIndex: shuffledIdx,
      explanation: item.explanation ? item.explanation.trim() : '',
      difficulty: resolvedDifficulty,
      xpReward: resolvedDifficulty * 20,
    });
  }

  const { uniqueQuestions, skippedDuplicates } = filterUniqueQuestions(sanitized, existingQuestions);
  if (skippedDuplicates.length > 0) {
    console.info(`ℹ️ Filtered out ${skippedDuplicates.length} duplicate question(s) from LLM output.`);
  }

  return uniqueQuestions;
}

/**
 * Calls a single Gemini model with an AbortController timeout so congested endpoints fail fast.
 */
async function callModelWithTimeout(genAI, modelName, prompt, timeoutMs) {
  const model = genAI.getGenerativeModel({
    model: modelName,
    generationConfig: {
      responseMimeType: 'application/json',
      temperature: 0.35,
    },
  });

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const result = await model.generateContent(prompt, { signal: controller.signal });
    return result.response.text();
  } finally {
    clearTimeout(timer);
  }
}

/**
 * Generates RPG multiple-choice questions from educational text using Gemini with multi-model failover.
 * @param {string} textContent - Textbook excerpt
 * @param {object} options - { count: 5, topic: 'Data Structures', difficulty: 2, existingQuestions: [] }
 * @returns {Promise<Array>}
 */
async function generateQuestionsFromText(textContent, options = {}) {
  const apiKey = process.env.GEMINI_API_KEY;
  if (!apiKey) {
    throw new Error('GEMINI_API_KEY is not configured in environment variables');
  }

  const genAI = new GoogleGenerativeAI(apiKey);
  const count = options.count || 5;
  const topic = options.topic || 'General Computer Science';
  const difficulty = options.difficulty || 2;
  const existingQuestions = Array.isArray(options.existingQuestions) ? options.existingQuestions : [];

  const exclusionBlock =
    existingQuestions.length > 0
      ? `\nExisting Questions in Database (DO NOT repeat, rephrase, or duplicate any of these questions):\n${existingQuestions
          .slice(0, 50)
          .map((qText, idx) => `${idx + 1}. ${qText}`)
          .join('\n')}\n`
      : '';

  const prompt = `
You are a senior computer science educator and game designer for the educational RPG "Knowledge Warrior".
Your task is to analyze the following textbook excerpt and generate ${count} distinct, non-repeating, high-quality multiple-choice questions for grade 11+ students on the topic "${topic}".

Guidelines:
1. Each question must test conceptual comprehension or code tracing (Bloom's Taxonomy Level 2 or 3).
2. Every generated question must cover a unique sub-concept from the excerpt and must NOT repeat or closely paraphrase any other question in this batch or in the Existing Questions list.
3. Exactly 4 distinct options per question.
4. Distractors must represent common student misconceptions such as off-by-one errors or confusing average-case with worst-case complexity.
5. Provide a clear, educational explanation (max 2 sentences) for why the correct option is right.
6. Difficulty should be set to ${difficulty} (1 = Beginner Levels 1-15, 2 = Intermediate Levels 16-30, 3 = Advanced Levels 31-60, 4 = Expert Levels 61-100).
7. xpReward should be ${difficulty * 20}.
8. Return ONLY a valid JSON array conforming to this schema without Markdown fences:

[
  {
    "questionText": "Question text here?",
    "options": ["Option A", "Option B", "Option C", "Option D"],
    "correctIndex": 0,
    "explanation": "Clear explanation of the correct answer.",
    "difficulty": ${difficulty},
    "xpReward": ${difficulty * 20}
  }
]
${exclusionBlock}
Textbook Excerpt:
${textContent.slice(0, 10000)}
`;

  const candidateModels = getCandidateModels();
  const errors = [];

  for (const modelName of candidateModels) {
    try {
      const responseText = await callModelWithTimeout(genAI, modelName, prompt, MODEL_TIMEOUT_MS);
      const questions = parseAndSanitizeQuestions(responseText, difficulty, existingQuestions);
      if (questions.length > 0) {
        return questions.slice(0, count);
      }
      throw new Error(`Model ${modelName} returned 0 unique valid questions`);
    } catch (err) {
      const statusInfo = err.status ? ` [HTTP ${err.status}]` : '';
      const reason = err.name === 'AbortError'
        ? `timed out after ${MODEL_TIMEOUT_MS}ms`
        : err.message;
      console.warn(`⚠️ Gemini model "${modelName}" failed${statusInfo}: ${reason}. Trying next fallback model...`);
      errors.push(`${modelName}: ${reason}`);
    }
  }

  throw new Error(
    `All Gemini models in fallback chain failed due to high demand or network errors (${errors.join(' | ')})`
  );
}

module.exports = {
  generateQuestionsFromText,
  parseAndSanitizeQuestions,
  shuffleOptions,
  validateQuestion,
  getCandidateModels,
};

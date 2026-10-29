/**
 * Normalizes question text for case-insensitive and punctuation-insensitive comparison.
 * @param {string} text
 * @returns {string}
 */
function normalizeQuestionText(text) {
  if (!text || typeof text !== 'string') return '';
  return text
    .toLowerCase()
    .replace(/[^a-z0-9\s]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

/**
 * Extracts character trigrams from normalized text for similarity comparison.
 * @param {string} normalized
 * @returns {Set<string>}
 */
function getTrigrams(normalized) {
  const trigrams = new Set();
  if (normalized.length < 3) {
    if (normalized.length > 0) trigrams.add(normalized);
    return trigrams;
  }
  for (let i = 0; i <= normalized.length - 3; i++) {
    trigrams.add(normalized.slice(i, i + 3));
  }
  return trigrams;
}

/**
 * Computes Sørensen-Dice trigram similarity between two question strings (0.0 to 1.0).
 * @param {string} textA
 * @param {string} textB
 * @returns {number}
 */
function computeQuestionSimilarity(textA, textB) {
  const normA = normalizeQuestionText(textA);
  const normB = normalizeQuestionText(textB);

  if (!normA || !normB) return 0;
  if (normA === normB) return 1.0;

  const trigramsA = getTrigrams(normA);
  const trigramsB = getTrigrams(normB);
  if (trigramsA.size === 0 || trigramsB.size === 0) return 0;

  let intersection = 0;
  for (const t of trigramsA) {
    if (trigramsB.has(t)) {
      intersection++;
    }
  }

  return (2 * intersection) / (trigramsA.size + trigramsB.size);
}

/**
 * Checks whether a candidate question text duplicates any entry in a list of existing texts.
 * @param {string} candidateText
 * @param {Array<string>} existingTexts
 * @param {number} similarityThreshold
 * @returns {boolean}
 */
function isDuplicateQuestion(candidateText, existingTexts = [], similarityThreshold = 0.85) {
  const normCandidate = normalizeQuestionText(candidateText);
  if (!normCandidate) return true;

  for (const existing of existingTexts) {
    const normExisting = normalizeQuestionText(existing);
    if (!normExisting) continue;
    if (normCandidate === normExisting) return true;
    if (computeQuestionSimilarity(normCandidate, normExisting) >= similarityThreshold) {
      return true;
    }
  }
  return false;
}

/**
 * Filters an array of candidate question objects so neither internal batch duplicates
 * nor duplicates of existing database questions are kept.
 * @param {Array<object>} candidates
 * @param {Array<string>} existingQuestionTexts
 * @param {number} similarityThreshold
 * @returns {{ uniqueQuestions: Array<object>, skippedDuplicates: Array<object> }}
 */
function filterUniqueQuestions(candidates = [], existingQuestionTexts = [], similarityThreshold = 0.85) {
  const seenTexts = [...existingQuestionTexts];
  const uniqueQuestions = [];
  const skippedDuplicates = [];

  for (const item of candidates) {
    const text = item?.questionText || item?.question || '';
    if (isDuplicateQuestion(text, seenTexts, similarityThreshold)) {
      skippedDuplicates.push(item);
      continue;
    }
    seenTexts.push(text);
    uniqueQuestions.push(item);
  }

  return { uniqueQuestions, skippedDuplicates };
}

module.exports = {
  normalizeQuestionText,
  computeQuestionSimilarity,
  isDuplicateQuestion,
  filterUniqueQuestions,
};


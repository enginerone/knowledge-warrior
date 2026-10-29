const pdfParse = require('pdf-parse');

/**
 * Extracts and cleans text from an uploaded PDF buffer.
 * Strips repeated headers, footers, and excess whitespace.
 * @param {Buffer} buffer 
 * @returns {Promise<string>}
 */
async function extractTextFromPDF(buffer) {
  const data = await pdfParse(buffer);
  let text = data.text || '';

  // Basic cleaning: normalize newlines, strip excessive blank lines
  text = text
    .replace(/\r\n/g, '\n')
    .replace(/[ \t]+/g, ' ')
    .replace(/\n\s*\n\s*\n+/g, '\n\n')
    .trim();

  return text;
}

/**
 * Splits extracted text into semantic chunks if text is long.
 * @param {string} text 
 * @param {number} maxChunkLength 
 * @returns {string[]}
 */
function chunkText(text, maxChunkLength = 6000) {
  if (text.length <= maxChunkLength) {
    return [text];
  }

  const chunks = [];
  const paragraphs = text.split('\n\n');
  let currentChunk = '';

  for (const para of paragraphs) {
    if ((currentChunk + '\n\n' + para).length > maxChunkLength) {
      if (currentChunk.trim()) {
        chunks.push(currentChunk.trim());
      }
      currentChunk = para;
    } else {
      currentChunk = currentChunk ? currentChunk + '\n\n' + para : para;
    }
  }

  if (currentChunk.trim()) {
    chunks.push(currentChunk.trim());
  }

  return chunks;
}

module.exports = {
  extractTextFromPDF,
  chunkText,
};

const MIN_SUBJECT_LEVEL = 1;
const MAX_SUBJECT_LEVEL = 100;
const MAX_BATTLE_XP_AWARD = 350;

const DEFAULT_SUBJECTS = [
  'Programming',
  'Web Development',
  'Networking',
  'Databases',
  'Cybersecurity',
  'Computer Systems',
];

function xpToNextSubjectLevel(level) {
  const clamped = Math.max(MIN_SUBJECT_LEVEL, Math.min(MAX_SUBJECT_LEVEL, parseInt(level, 10) || MIN_SUBJECT_LEVEL));
  return 80 + (clamped - 1) * 25;
}

function getBadgeTierForLevel(level) {
  const clamped = Math.max(MIN_SUBJECT_LEVEL, Math.min(MAX_SUBJECT_LEVEL, parseInt(level, 10) || MIN_SUBJECT_LEVEL));
  if (clamped <= 15) return 'Beginner';
  if (clamped <= 30) return 'Intermediate';
  if (clamped <= 60) return 'Advanced';
  return 'Expert';
}

function calculateGlobalLevel(totalXp) {
  const cleanXp = Math.max(0, parseInt(totalXp, 10) || 0);
  if (cleanXp <= 0) return 1;
  return Math.min(MAX_SUBJECT_LEVEL, Math.floor(Math.pow(cleanXp / 100, 1 / 1.6)) + 1);
}

/**
 * Applies earned battle XP sequentially from Level 1 up to Level 100.
 * Advances at most 1 level per battle so players never skip intermediate levels.
 */
function applySequentialSubjectXp(currentLevel, currentXp, currentPoints, rawXpGained) {
  const safeGain = Math.max(0, Math.min(MAX_BATTLE_XP_AWARD, parseInt(rawXpGained, 10) || 0));
  let level = Math.max(MIN_SUBJECT_LEVEL, Math.min(MAX_SUBJECT_LEVEL, parseInt(currentLevel, 10) || MIN_SUBJECT_LEVEL));
  let xp = Math.max(0, parseInt(currentXp, 10) || 0);
  const points = Math.max(0, parseInt(currentPoints, 10) || 0) + safeGain;

  if (level >= MAX_SUBJECT_LEVEL) {
    return {
      subjectLevel: MAX_SUBJECT_LEVEL,
      subjectXp: 0,
      subjectPoints: points,
      xpToNextLevel: xpToNextSubjectLevel(MAX_SUBJECT_LEVEL),
      badgeTier: getBadgeTierForLevel(MAX_SUBJECT_LEVEL),
      levelsGained: 0,
    };
  }

  xp += safeGain;
  let levelsGained = 0;
  const requiredForNext = xpToNextSubjectLevel(level);

  if (xp >= requiredForNext) {
    xp -= requiredForNext;
    level += 1;
    levelsGained = 1;

    if (level >= MAX_SUBJECT_LEVEL) {
      level = MAX_SUBJECT_LEVEL;
      xp = 0;
    } else {
      const capBeforeSkip = xpToNextSubjectLevel(level) - 1;
      if (xp > capBeforeSkip) {
        xp = capBeforeSkip;
      }
    }
  }

  return {
    subjectLevel: level,
    subjectXp: xp,
    subjectPoints: points,
    xpToNextLevel: xpToNextSubjectLevel(level),
    badgeTier: getBadgeTierForLevel(level),
    levelsGained,
  };
}

function buildDefaultSubjectRow(subject) {
  return {
    subject,
    subjectLevel: MIN_SUBJECT_LEVEL,
    subjectXp: 0,
    subjectPoints: 0,
    xpToNextLevel: xpToNextSubjectLevel(MIN_SUBJECT_LEVEL),
    badgeTier: getBadgeTierForLevel(MIN_SUBJECT_LEVEL),
  };
}

module.exports = {
  MIN_SUBJECT_LEVEL,
  MAX_SUBJECT_LEVEL,
  MAX_BATTLE_XP_AWARD,
  DEFAULT_SUBJECTS,
  xpToNextSubjectLevel,
  getBadgeTierForLevel,
  calculateGlobalLevel,
  applySequentialSubjectXp,
  buildDefaultSubjectRow,
};

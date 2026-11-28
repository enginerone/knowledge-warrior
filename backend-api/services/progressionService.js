const MIN_SUBJECT_LEVEL = 1;
const MAX_SUBJECT_LEVEL = 100;
const MAX_BATTLE_XP_AWARD = 350;
const STAGE_WIN_COIN_REWARD = 15;
const BOSS_WIN_COIN_REWARD = 25;
const INACTIVITY_TIMEOUT_SECONDS = 3600;

const DEFAULT_SUBJECTS = [
  'Programming',
  'Web Development',
  'Networking',
  'Databases',
  'Cybersecurity',
  'Computer Systems',
  'Artificial Intelligence',
  'UI/UX & Software Engineering',
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

function getReviveCoinCostForLevel(level) {
  const tier = getBadgeTierForLevel(level);
  if (tier === 'Beginner') return 30;
  if (tier === 'Intermediate') return 50;
  if (tier === 'Advanced') return 70;
  return 100;
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
      isMaxLevelMastery: true,
      reviveCoinCost: getReviveCoinCostForLevel(MAX_SUBJECT_LEVEL),
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
    isMaxLevelMastery: level >= MAX_SUBJECT_LEVEL,
    reviveCoinCost: getReviveCoinCostForLevel(level),
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
    reviveCoinCost: getReviveCoinCostForLevel(MIN_SUBJECT_LEVEL),
  };
}

function getStageWinCoinReward(stageIndex, isBoss = false) {
  const numericStage = parseInt(stageIndex, 10) || 0;
  return Boolean(isBoss) || numericStage >= 3 ? BOSS_WIN_COIN_REWARD : STAGE_WIN_COIN_REWARD;
}

module.exports = {
  MIN_SUBJECT_LEVEL,
  MAX_SUBJECT_LEVEL,
  MAX_BATTLE_XP_AWARD,
  STAGE_WIN_COIN_REWARD,
  BOSS_WIN_COIN_REWARD,
  INACTIVITY_TIMEOUT_SECONDS,
  DEFAULT_SUBJECTS,
  xpToNextSubjectLevel,
  getBadgeTierForLevel,
  getReviveCoinCostForLevel,
  getStageWinCoinReward,
  calculateGlobalLevel,
  applySequentialSubjectXp,
  buildDefaultSubjectRow,
};

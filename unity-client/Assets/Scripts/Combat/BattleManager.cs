using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using KnowledgeWarrior.Audio;
using KnowledgeWarrior.Data;
using KnowledgeWarrior.Progress;
using KnowledgeWarrior.UI;
using KnowledgeWarrior.Visuals;
using UnityEngine;
using UnityEngine.Networking;
using Random = UnityEngine.Random;

namespace KnowledgeWarrior.Combat
{
    public enum BattleState { Idle, Intro, AwaitingAnswer, Resolving, AwaitingContinue, BattleOver }
    public enum TurnType { Attack, Defend }

    /// <summary>
    /// Orchestrates turn-based combat for both Subject Monster/Boss expeditions and
    /// 1v1 Same-Level Real-User duels, coordinating UI, themes, audio, animations, and 20% PvP stakes.
    /// </summary>
    public class BattleManager : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private QuestionManager questionManager;
        [SerializeField] private SyncManager syncManager;
        [SerializeField] private BattleUI ui;
        [SerializeField] private CombatUnit player;
        [SerializeField] private CombatUnit enemy;
        [SerializeField] private CameraShake cameraShake;

        [Header("Biome")]
        [SerializeField] private string biomeName = "Citadel of Code";
        [SerializeField] private bool showBiomeSelectOnStart = true;
        [SerializeField] private List<EnemyDefinition> stages = new();

        [Header("Balance")]
        [Tooltip("Fraction of max HP restored between stages.")]
        [Range(0f, 1f)] [SerializeField] private float healBetweenStages = 0.35f;
        [Tooltip("Extra damage per consecutive correct answer (combo).")]
        [SerializeField] private float comboBonusPerStreak = 0.25f;
        [SerializeField] private int maxComboSteps = 4;
        [Tooltip("XP per correct answer, multiplied by question difficulty.")]
        [SerializeField] private int xpPerCorrectAnswer = 5;

        [Header("Colors")]
        [SerializeField] private Color damageColor = new(1f, 0.42f, 0.42f);
        [SerializeField] private Color critColor = new(1f, 0.82f, 0.3f);
        [SerializeField] private Color blockColor = new(0.45f, 0.85f, 1f);
        [SerializeField] private Color missColor = new(0.75f, 0.78f, 0.9f);
        [SerializeField] private Color victoryColor = new(0.35f, 0.95f, 0.75f);
        [SerializeField] private Color defeatColor = new(1f, 0.4f, 0.5f);
        [SerializeField] private Color bossColor = new(1f, 0.55f, 0.25f);

        public BattleState State { get; private set; } = BattleState.Idle;

        private readonly List<EnemyDefinition> _baseSceneStages = new();
        private List<EnemyDefinition> _activeRunStages = new();
        private int _stageIndex;
        private TurnType _turn;
        private QuestionData _currentQuestion;
        private int _streak;
        private int _battleCorrect;
        private int _battleTotal;
        private int _battleXp;
        private bool _runFinished;
        private bool _isPvpMatch;
        private string _selectedSubject = "Programming";
        private Sprite _heroBaseSprite;

        private BiomeSelectModal _biomeModal;
        private SubjectLobbyModal _subjectLobbyModal;
        private AuthGateModal _authGateModal;

        private EnemyDefinition CurrentStage => _activeRunStages[_stageIndex];

        private void Start()
        {
            if (questionManager == null || ui == null || player == null || enemy == null || stages.Count == 0)
            {
                Debug.LogError("[BattleManager] Missing references or no stages configured. " +
                               "Run 'Knowledge Warrior > Build Battle Scene' to generate a wired-up scene.");
                enabled = false;
                return;
            }

            _baseSceneStages.Clear();
            _baseSceneStages.AddRange(stages);
            if (player.Renderer != null) _heroBaseSprite = player.Renderer.sprite;

            BattleAudioManager.EnsureInstance();
            ThemeManager.EnsureInstance();

            ui.Initialize(OnAnswerSelected, OnContinue, OnResultButton);
            player.OnHealthChanged += ui.SetPlayerHp;
            enemy.OnHealthChanged += ui.SetEnemyHp;

            if (syncManager == null)
            {
                syncManager = SyncManager.Instance != null
                    ? SyncManager.Instance
                    : new GameObject("SyncManager").AddComponent<SyncManager>();
            }

            if (AuthManager.Instance == null)
            {
                new GameObject("AuthManager").AddComponent<AuthManager>();
            }

            var canvas = ui.GetComponent<Canvas>();
            if (canvas != null)
            {
                LeaderboardModal.EnsureOnCanvas(canvas, RefreshPlayerInfo);
                _biomeModal = BiomeSelectModal.EnsureOnCanvas(canvas, questionManager, OnSubjectBiomeChosen);
                _subjectLobbyModal = SubjectLobbyModal.EnsureOnCanvas(canvas, StartSubjectMonsterExpedition, StartRealUserOneOnOneDuel);
                _authGateModal = AuthGateModal.EnsureOnCanvas(canvas, OnAuthenticatedSessionReady);
            }

            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnAuthStateChanged += HandleAuthStateChanged;
            }

            if (questionManager.Count == 0)
            {
                ui.ShowResult("NO QUESTIONS", "questions.json could not be loaded.\nCheck the Console for details.", "Retry", defeatColor);
                return;
            }

            // Once logged in, the user stays authenticated on reopen and skips AuthGateModal automatically.
            if (AuthManager.Instance != null && !AuthManager.Instance.IsAuthenticated && _authGateModal != null)
            {
                State = BattleState.Idle;
                ui.HideQuestionPanel();
                ui.HideResult();
                _authGateModal.Show(AuthGateModal.AuthPage.Register);
                return;
            }

            StartSubjectMonsterExpedition(biomeName, _selectedSubject);

            if (showBiomeSelectOnStart && _biomeModal != null)
            {
                _biomeModal.Show(allowClose: true);
            }
        }

        private void OnDestroy()
        {
            if (player != null && ui != null) player.OnHealthChanged -= ui.SetPlayerHp;
            if (enemy != null && ui != null) enemy.OnHealthChanged -= ui.SetEnemyHp;
            if (AuthManager.Instance != null) AuthManager.Instance.OnAuthStateChanged -= HandleAuthStateChanged;
        }

        private void OnAuthenticatedSessionReady()
        {
            StopAllCoroutines();
            StartSubjectMonsterExpedition(biomeName, _selectedSubject);
            if (showBiomeSelectOnStart && _biomeModal != null)
            {
                _biomeModal.Show(allowClose: true);
            }
        }

        private void HandleAuthStateChanged()
        {
            RefreshPlayerInfo();
            if (AuthManager.Instance != null && !AuthManager.Instance.IsAuthenticated && _authGateModal != null)
            {
                StopAllCoroutines();
                State = BattleState.Idle;
                ui.HideQuestionPanel();
                ui.HideResult();
                if (_biomeModal != null) _biomeModal.Hide();
                if (_subjectLobbyModal != null) _subjectLobbyModal.Hide();
                _authGateModal.Show(AuthGateModal.AuthPage.Register);
            }
        }

        private void OnSubjectBiomeChosen(string selectedBiomeTitle, string subjectFilter)
        {
            biomeName = !string.IsNullOrWhiteSpace(selectedBiomeTitle) ? selectedBiomeTitle : "Citadel of Code";
            _selectedSubject = !string.IsNullOrWhiteSpace(subjectFilter) ? subjectFilter : "Programming";

            if (_subjectLobbyModal != null)
            {
                _subjectLobbyModal.ShowForSubject(biomeName, _selectedSubject);
            }
            else
            {
                StartSubjectMonsterExpedition(biomeName, _selectedSubject);
            }
        }

        private void StartSubjectMonsterExpedition(string selectedBiomeTitle, string subjectFilter)
        {
            StopAllCoroutines();
            _isPvpMatch = false;
            biomeName = !string.IsNullOrWhiteSpace(selectedBiomeTitle) ? selectedBiomeTitle : "Citadel of Code";
            _selectedSubject = !string.IsNullOrWhiteSpace(subjectFilter) ? subjectFilter : "Programming";

            int subjectLevel = ProgressManager.GetSubjectLevel(_selectedSubject);
            _activeRunStages = MonsterCatalog.BuildStagesForSubjectAndLevel(_selectedSubject, subjectLevel, _baseSceneStages);

            ApplySubjectThemeAndAudio(isBossOrPvp: false);
            ReviveAndSetupPlayer();
            StartStage(0);
        }

        private void StartRealUserOneOnOneDuel(string selectedBiomeTitle, string subjectFilter, RealUserLobbyEntry opponent)
        {
            StopAllCoroutines();
            _isPvpMatch = true;
            biomeName = !string.IsNullOrWhiteSpace(selectedBiomeTitle) ? selectedBiomeTitle : "Citadel of Code";
            _selectedSubject = !string.IsNullOrWhiteSpace(subjectFilter) ? subjectFilter : "Programming";

            int subjectLevel = ProgressManager.GetSubjectLevel(_selectedSubject);
            var rivalDef = MonsterCatalog.BuildRealUserOpponent(
                opponent.userId,
                opponent.username,
                _selectedSubject,
                subjectLevel,
                opponent.subjectXp,
                _heroBaseSprite);

            _activeRunStages = new List<EnemyDefinition> { rivalDef };

            ApplySubjectThemeAndAudio(isBossOrPvp: true);
            ReviveAndSetupPlayer();
            StartStage(0);
        }

        private void ApplySubjectThemeAndAudio(bool isBossOrPvp)
        {
            int subjectLevel = ProgressManager.GetSubjectLevel(_selectedSubject);
            var tier = SubjectTierSystem.GetTierForLevel(subjectLevel);
            ThemeManager.Instance?.ApplyTheme(_selectedSubject, subjectLevel);
            BattleAudioManager.Instance?.PlaySubjectThemeMusic(_selectedSubject, tier, isBossOrPvp);
        }

        private void StartStage(int index)
        {
            if (_activeRunStages == null || _activeRunStages.Count == 0)
            {
                int lv = ProgressManager.GetSubjectLevel(_selectedSubject);
                _activeRunStages = MonsterCatalog.BuildStagesForSubjectAndLevel(_selectedSubject, lv, _baseSceneStages);
            }

            _stageIndex = Mathf.Clamp(index, 0, _activeRunStages.Count - 1);
            _runFinished = false;
            _streak = 0;
            _battleCorrect = 0;
            _battleTotal = 0;
            _battleXp = 0;
            _turn = TurnType.Attack;

            var def = CurrentStage;
            int subjLv = ProgressManager.GetSubjectLevel(_selectedSubject);
            string badgeName = SubjectTierSystem.GetBadgeNameForLevel(subjLv);

            ApplySubjectThemeAndAudio(isBossOrPvp: def.isBoss || def.isRealUserOpponent);

            enemy.Setup(def.displayName, def.maxHp, def.attack, def.sprite, def.tint, def.worldHeight, def.attackGesture);
            ui.SetEnemyName(def.displayName, def.isBoss || def.isRealUserOpponent);

            if (def.isRealUserOpponent)
            {
                ui.SetStage($"{_selectedSubject} 1v1 DUEL  •  LV {subjLv} {badgeName.ToUpperInvariant()} BADGE");
            }
            else
            {
                ui.SetStage($"{biomeName} ({badgeName} Lv {subjLv})  •  Stage {_stageIndex + 1}/{_activeRunStages.Count}");
            }

            ui.SetStreak(0);
            ui.HideResult();
            StartCoroutine(IntroRoutine(def));
        }

        private IEnumerator IntroRoutine(EnemyDefinition def)
        {
            State = BattleState.Intro;
            ui.HideQuestionPanel();
            if (def.isRealUserOpponent)
            {
                BattleAudioManager.Instance?.PlayBossEntranceRoar();
                cameraShake?.Shake(0.35f, 0.12f);
                yield return ui.ShowBanner("1v1 REAL USER FIGHT", $"{def.displayName} • 20% Point Stake!", bossColor);
            }
            else if (def.isBoss)
            {
                BattleAudioManager.Instance?.PlayBossEntranceRoar();
                cameraShake?.Shake(0.5f, 0.15f);
                yield return ui.ShowBanner("BOSS BATTLE", def.displayName, bossColor);
            }
            else
            {
                yield return ui.ShowBanner("A wild foe appears!", def.displayName, victoryColor);
            }
            NextTurn();
        }

        private void NextTurn()
        {
            var def = CurrentStage;
            string effectiveSubject = !string.IsNullOrWhiteSpace(_selectedSubject) ? _selectedSubject : def.subject;
            int subjectLevel = ProgressManager.GetSubjectLevel(effectiveSubject);

            // Strictly align question difficulty with the player's current Subject Level & Badge Tier (1-15, 16-30, 31-60, 61-100)
            _currentQuestion = questionManager.GetQuestionForSubjectAndLevel(effectiveSubject, subjectLevel);
            if (_currentQuestion == null)
            {
                ui.ShowResult("NO QUESTIONS", "The question bank is empty.", "Retry", defeatColor);
                State = BattleState.BattleOver;
                return;
            }

            ui.SetTurn(_turn);
            ui.ShowQuestion(_currentQuestion);
            State = BattleState.AwaitingAnswer;
        }

        private void OnAnswerSelected(int index)
        {
            if (State != BattleState.AwaitingAnswer) return;
            BattleAudioManager.Instance?.PlayButtonClick();
            State = BattleState.Resolving;
            StartCoroutine(ResolveRoutine(index));
        }

        private IEnumerator ResolveRoutine(int selectedIndex)
        {
            var q = _currentQuestion;
            bool correct = selectedIndex == q.correctIndex;
            _battleTotal++;

            ui.RevealAnswer(selectedIndex, q.correctIndex);

            string headline;
            if (correct)
            {
                _streak++;
                _battleCorrect++;
                _battleXp += xpPerCorrectAnswer * Mathf.Max(1, q.difficulty);
            }
            else
            {
                _streak = 0;
            }
            ui.SetStreak(_streak);

            yield return new WaitForSeconds(0.45f);

            if (_turn == TurnType.Attack)
            {
                if (correct)
                {
                    int combo = Mathf.Min(_streak - 1, maxComboSteps);
                    float multiplier = (1f + comboBonusPerStreak * combo) * (1f + 0.15f * (q.difficulty - 1));
                    int damage = Mathf.Max(1, Mathf.RoundToInt(player.Attack * multiplier * Random.Range(0.9f, 1.1f)));
                    bool isCrit = combo >= 2;

                    BattleAudioManager.Instance?.PlayHeroAttack(isCrit);
                    yield return player.AttackRoutine(enemy, () =>
                    {
                        BattleAudioManager.Instance?.PlayHitImpact();
                        enemy.TakeDamage(damage);
                        cameraShake?.Shake(isCrit ? 0.25f : 0.15f, isCrit ? 0.18f : 0.1f);
                        ui.SpawnPopup(enemy.PopupAnchor, damage.ToString(), isCrit ? critColor : damageColor, isCrit ? 1.4f : 1.1f);
                        if (combo > 0) ui.SpawnPopup(enemy.PopupAnchor + Vector3.up * 0.7f, $"COMBO x{_streak}", critColor, 0.8f);
                    });
                    headline = isCrit ? $"Correct! Combo x{_streak}!" : "Correct! Direct hit!";
                }
                else
                {
                    BattleAudioManager.Instance?.PlayMissWhoosh();
                    yield return player.AttackRoutine(enemy, () =>
                        ui.SpawnPopup(enemy.PopupAnchor, "MISS", missColor, 1f));
                    headline = "Wrong! Your attack missed.";
                }
            }
            else
            {
                var def = CurrentStage;
                int damage = Mathf.Max(1, Mathf.RoundToInt(enemy.Attack * Random.Range(0.9f, 1.1f)));
                BattleAudioManager.Instance?.PlayMonsterAttack(def.isBoss || def.isRealUserOpponent);
                yield return enemy.AttackRoutine(player, () =>
                {
                    if (correct)
                    {
                        BattleAudioManager.Instance?.PlayHeroBlock();
                        player.PlayBlock();
                        ui.SpawnPopup(player.PopupAnchor, "BLOCKED", blockColor, 1.1f);
                    }
                    else
                    {
                        BattleAudioManager.Instance?.PlayHitImpact();
                        player.TakeDamage(damage);
                        cameraShake?.Shake(0.2f, 0.14f);
                        ui.SpawnPopup(player.PopupAnchor, damage.ToString(), damageColor, 1.2f);
                    }
                });
                headline = correct ? "Correct! Attack blocked!" : "Wrong! You took a hit.";
            }

            if (enemy.IsDead) yield return enemy.DeathRoutine();
            if (player.IsDead) yield return player.DeathRoutine();

            ui.ShowFeedback(correct, headline, q.explanation);
            State = BattleState.AwaitingContinue;
        }

        private void OnContinue()
        {
            if (State != BattleState.AwaitingContinue) return;
            BattleAudioManager.Instance?.PlayButtonClick();

            if (enemy.IsDead) { EndBattle(true); return; }
            if (player.IsDead) { EndBattle(false); return; }

            _turn = _turn == TurnType.Attack ? TurnType.Defend : TurnType.Attack;
            NextTurn();
        }

        private void EndBattle(bool victory)
        {
            State = BattleState.BattleOver;
            ui.HideQuestionPanel();

            if (victory) BattleAudioManager.Instance?.PlayVictoryFanfare();
            else BattleAudioManager.Instance?.PlayDefeatStinger();

            var def = CurrentStage;
            int accuracy = _battleTotal > 0 ? Mathf.RoundToInt(100f * _battleCorrect / _battleTotal) : 0;

            if (_isPvpMatch || def.isRealUserOpponent)
            {
                HandlePvpBattleOutcome(def, victory, accuracy);
                return;
            }

            int xp = _battleXp + (victory ? def.xpReward : 0);
            int levelsGained = ProgressManager.AddXp(xp, _selectedSubject);
            AuthManager.Instance?.SyncSubjectProgressToServer(_selectedSubject);

            ProgressManager.RecordBattle(new PendingScore
            {
                subject = _selectedSubject,
                xpGained = xp,
                stageIndex = _stageIndex,
                enemyName = def.displayName,
                victory = victory,
                isPvpDuel = false,
                correctAnswers = _battleCorrect,
                totalAnswers = _battleTotal,
            });

            var sp = ProgressManager.GetSubjectProgress(_selectedSubject);
            if (levelsGained > 0)
            {
                player.UpdateStats(ProgressManager.MaxHpForLevel(sp.level), ProgressManager.AttackForLevel(sp.level), fullHeal: true);
            }
            RefreshPlayerInfo();

            string badgeName = SubjectTierSystem.GetBadgeNameForLevel(sp.level);
            string body = $"+{xp} {_selectedSubject} XP  •  Accuracy: {_battleCorrect}/{_battleTotal} ({accuracy}%)\n" +
                          $"Subject Standing: LV {sp.level}/100 ({badgeName} Badge • {sp.points} PTS)";
            if (levelsGained > 0)
            {
                body += $"\n<color=#FFD24D>SUBJECT LEVEL UP! You are now LV {sp.level} ({badgeName} Badge) in {_selectedSubject}!</color>";
            }

            if (victory)
            {
                bool lastStage = _stageIndex >= _activeRunStages.Count - 1;
                _runFinished = lastStage;
                ui.ShowResult(lastStage ? "SUBJECT BIOME CLEARED!" : "VICTORY!",
                    $"{def.displayName} was defeated.\n{body}",
                    lastStage ? "Open Subject Lobby" : "Next Monster", victoryColor);
            }
            else
            {
                ui.ShowResult("DEFEATED", $"{def.displayName} overpowered you.\n{body}\nTry again or return to the Subject Lobby!",
                    "Try Again", defeatColor);
            }
        }

        private void HandlePvpBattleOutcome(EnemyDefinition def, bool victory, int accuracy)
        {
            var spBefore = ProgressManager.GetSubjectProgress(_selectedSubject);
            int playerPointsBefore = spBefore.points;
            int opponentPointsBefore = Mathf.Max(100, def.opponentSubjectPoints);
            int rewardPoints = _battleXp + def.xpReward;

            int playerDeduction = 0;
            int opponentDeduction = 0;

            if (victory)
            {
                opponentDeduction = SubjectTierSystem.CalculateTwentyPercentDeduction(opponentPointsBefore);
                ProgressManager.RecordPvpWin(_selectedSubject, rewardPoints);
            }
            else
            {
                playerDeduction = ProgressManager.ApplyPvpTwentyPercentDeduction(_selectedSubject);
            }

            StartCoroutine(PostPvpResolutionToServerRoutine(def, victory, playerPointsBefore, rewardPoints));

            ProgressManager.RecordBattle(new PendingScore
            {
                subject = _selectedSubject,
                xpGained = victory ? rewardPoints : -playerDeduction,
                stageIndex = 0,
                enemyName = def.displayName,
                victory = victory,
                isPvpDuel = true,
                correctAnswers = _battleCorrect,
                totalAnswers = _battleTotal,
            });

            RefreshPlayerInfo();
            var spAfter = ProgressManager.GetSubjectProgress(_selectedSubject);
            string badgeName = SubjectTierSystem.GetBadgeNameForLevel(spAfter.level);

            if (victory)
            {
                _runFinished = true;
                int oppAfter = Mathf.Max(0, opponentPointsBefore - opponentDeduction);
                string msg =
                    $"You defeated real user {def.displayName} in a 1v1 {_selectedSubject} Duel!\n" +
                    $"• Your Reward: <color=#3EE6A0>+{rewardPoints} PTS</color> (Now {spAfter.points} PTS • LV {spAfter.level} {badgeName})\n" +
                    $"• Opponent 20% Penalty: <color=#FF5C7A>-{opponentDeduction} PTS</color> ({opponentPointsBefore} -> {oppAfter} PTS)\n" +
                    $"• Accuracy: {_battleCorrect}/{_battleTotal} ({accuracy}%)";
                ui.ShowResult("1v1 DUEL VICTORY!", msg, "Return to Subject Lobby", victoryColor);
            }
            else
            {
                _runFinished = true;
                string msg =
                    $"You lost the 1v1 {_selectedSubject} Duel against {def.displayName}.\n" +
                    $"• Your 20% Point Deduction: <color=#FF5C7A>-{playerDeduction} PTS</color> ({playerPointsBefore} -> {spAfter.points} PTS)\n" +
                    $"• Subject Standing: LV {spAfter.level} ({badgeName} Badge)\n" +
                    $"• Accuracy: {_battleCorrect}/{_battleTotal} ({accuracy}%)";
                ui.ShowResult("1v1 DUEL DEFEAT (-20% PTS)", msg, "Return to Subject Lobby", defeatColor);
            }
        }

        private IEnumerator PostPvpResolutionToServerRoutine(
            EnemyDefinition def,
            bool victory,
            int playerCurrentPoints,
            int rewardPoints)
        {
            string baseUrl = SyncManager.Instance != null
                ? SyncManager.Instance.EffectiveApiBaseUrl
                : PlayerPrefs.GetString(SyncManager.ApiUrlPrefKey, "http://localhost:5000/api/v1").TrimEnd('/');

            var payload = new PvpResolveRequestDto
            {
                subject = _selectedSubject,
                subjectLevel = ProgressManager.GetSubjectLevel(_selectedSubject),
                opponentUserId = def.opponentUserId,
                opponentUsername = def.displayName,
                challengerUsername = ProgressManager.Current.playerName,
                victory = victory,
                playerCurrentPoints = playerCurrentPoints,
                rewardPoints = rewardPoints
            };

            using var req = new UnityWebRequest($"{baseUrl}/pvp/resolve", UnityWebRequest.kHttpVerbPOST);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 8;
            req.SetRequestHeader("Content-Type", "application/json");
            if (AuthManager.Instance != null && AuthManager.Instance.IsAuthenticated)
            {
                req.SetRequestHeader("Authorization", $"Bearer {AuthManager.Instance.Token}");
            }

            yield return req.SendWebRequest();
        }

        private void OnResultButton()
        {
            BattleAudioManager.Instance?.PlayButtonClick();
            if (questionManager.Count == 0)
            {
                questionManager.Load();
                if (questionManager.Count > 0) StartSubjectMonsterExpedition(biomeName, _selectedSubject);
                return;
            }

            if (State != BattleState.BattleOver) return;

            if (_isPvpMatch || _runFinished)
            {
                ui.HideResult();
                if (_subjectLobbyModal != null)
                {
                    _subjectLobbyModal.ShowForSubject(biomeName, _selectedSubject);
                }
                else if (_biomeModal != null)
                {
                    _biomeModal.Show(allowClose: true);
                }
                else
                {
                    StartSubjectMonsterExpedition(biomeName, _selectedSubject);
                }
            }
            else if (player.IsDead)
            {
                ReviveAndSetupPlayer();
                StartStage(_stageIndex);
            }
            else
            {
                player.Heal(Mathf.RoundToInt(player.MaxHp * healBetweenStages));
                StartStage(_stageIndex + 1);
            }
        }

        private void ReviveAndSetupPlayer()
        {
            var progress = ProgressManager.Current;
            int subjLv = ProgressManager.GetSubjectLevel(_selectedSubject);
            var tier = SubjectTierSystem.GetTierForLevel(subjLv);
            var gesture = tier >= BadgeTier.Advanced
                ? AttackGestureType.HeroArcaneBurst
                : AttackGestureType.HeroBladeSlash;

            player.Setup(
                progress.playerName,
                ProgressManager.MaxHpForLevel(subjLv),
                ProgressManager.AttackForLevel(subjLv),
                _heroBaseSprite,
                Color.white,
                3.1f,
                gesture);
            RefreshPlayerInfo();
        }

        private void RefreshPlayerInfo()
        {
            var p = ProgressManager.Current;
            var sp = ProgressManager.GetSubjectProgress(_selectedSubject);
            ui.SetPlayerInfo(
                p.playerName,
                sp.level,
                sp.xp,
                SubjectTierSystem.XpToNextSubjectLevel(sp.level),
                _selectedSubject);
        }

        [Serializable]
        private class PvpResolveRequestDto
        {
            public string subject;
            public int subjectLevel;
            public string opponentUserId;
            public string opponentUsername;
            public string challengerUsername;
            public bool victory;
            public int playerCurrentPoints;
            public int rewardPoints;
        }
    }
}

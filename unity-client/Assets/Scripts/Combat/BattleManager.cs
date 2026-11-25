using System.Collections;
using System.Collections.Generic;
using KnowledgeWarrior.Audio;
using KnowledgeWarrior.Data;
using KnowledgeWarrior.Progress;
using KnowledgeWarrior.UI;
using KnowledgeWarrior.Visuals;
using UnityEngine;
using Random = UnityEngine.Random;

namespace KnowledgeWarrior.Combat
{
    public enum BattleState { Idle, Intro, AwaitingAnswer, Resolving, AwaitingContinue, BattleOver }
    public enum TurnType { Attack, Defend }

    /// <summary>
    /// Orchestrates online-only turn-based combat for Subject Monster and Boss expeditions,
    /// enforcing live server connectivity, aligned question tiers, and sequential per-subject progression.
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
        private readonly List<BattleAnswerLogDto> _battleAnswerLogs = new();
        private List<EnemyDefinition> _activeRunStages = new();
        private int _stageIndex;
        private TurnType _turn;
        private QuestionData _currentQuestion;
        private int _streak;
        private int _battleCorrect;
        private int _battleTotal;
        private int _battleXp;
        private bool _runFinished;
        private bool _subjectPoolExhausted;
        private bool _backgroundQuestionRefreshInFlight;
        private bool _sessionBootstrapped;
        private string _selectedSubject = "Programming";
        private Sprite _heroBaseSprite;
        private GameObject _arenaBackgroundObject;
        private static readonly Color StandaloneAuthScreenColor = new(0.03f, 0.04f, 0.11f, 1f);

        private BattleResultSubmission _uncommittedBattleResult;
        private bool _uncommittedBattleVictory;
        private EnemyDefinition _uncommittedStageDef;

        private BiomeSelectModal _biomeModal;
        private SubjectLobbyModal _subjectLobbyModal;
        private AuthGateModal _authGateModal;
        private NetworkGateModal _networkGateModal;

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
            _arenaBackgroundObject = GameObject.Find("Background");

            BattleAudioManager.EnsureInstance();
            ThemeManager.EnsureInstance();

            ui.Initialize(OnAnswerSelected, OnContinue, OnResultButton);
            PortraitLayoutController.ApplyPortraitArenaAndHud(ui, player, enemy, _arenaBackgroundObject);
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
                _subjectLobbyModal = SubjectLobbyModal.EnsureOnCanvas(canvas, StartSubjectMonsterExpedition);
                _authGateModal = AuthGateModal.EnsureOnCanvas(canvas, OnAuthenticatedSessionReady);
                _networkGateModal = NetworkGateModal.EnsureOnCanvas(canvas, OnRetryOnlineConnection);
            }

            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnAuthStateChanged += HandleAuthStateChanged;
            }
            if (syncManager != null)
            {
                syncManager.OnOnlineAvailabilityChanged += HandleOnlineAvailabilityChanged;
            }

            State = BattleState.Idle;
            SetGameplayActive(false);
            StartCoroutine(BootstrapOnlineSessionRoutine());
        }

        private void OnDestroy()
        {
            if (player != null && ui != null) player.OnHealthChanged -= ui.SetPlayerHp;
            if (enemy != null && ui != null) enemy.OnHealthChanged -= ui.SetEnemyHp;
            if (AuthManager.Instance != null) AuthManager.Instance.OnAuthStateChanged -= HandleAuthStateChanged;
            if (syncManager != null) syncManager.OnOnlineAvailabilityChanged -= HandleOnlineAvailabilityChanged;
        }

        private void HandleOnlineAvailabilityChanged(bool online, string reason)
        {
            if (online || _networkGateModal == null) return;
            _networkGateModal.Show(reason, isConnecting: false);
        }

        private void OnRetryOnlineConnection()
        {
            StopAllCoroutines();
            StartCoroutine(BootstrapOnlineSessionRoutine());
        }

        private IEnumerator BootstrapOnlineSessionRoutine()
        {
            if (_networkGateModal != null)
            {
                _networkGateModal.SetConnectingState("Verifying internet data and connecting to Knowledge Warrior server...");
            }

            bool onlineOk = false;
            string onlineErr = null;
            yield return syncManager.VerifyOnlineAndLoadQuestionsRoutine((ok, err) =>
            {
                onlineOk = ok;
                onlineErr = err;
            });

            if (!onlineOk)
            {
                if (_networkGateModal != null)
                {
                    _networkGateModal.Show(onlineErr, isConnecting: false);
                }
                yield break;
            }

            if (AuthManager.Instance != null && AuthManager.Instance.IsAuthenticated)
            {
                bool netOk = false;
                bool authOk = false;
                string authErr = null;
                yield return AuthManager.Instance.ValidateSessionAndLoadProgressRoutine((nOk, aOk, err) =>
                {
                    netOk = nOk;
                    authOk = aOk;
                    authErr = err;
                });

                if (!netOk)
                {
                    if (_networkGateModal != null)
                    {
                        _networkGateModal.Show(authErr, isConnecting: false);
                    }
                    yield break;
                }

                if (!authOk)
                {
                    if (_networkGateModal != null) _networkGateModal.Hide();
                    _authGateModal?.Show(AuthGateModal.AuthPage.Login);
                    yield break;
                }
            }
            else
            {
                if (_networkGateModal != null) _networkGateModal.Hide();
                _authGateModal?.Show(AuthGateModal.AuthPage.Register);
                yield break;
            }

            if (_networkGateModal != null) _networkGateModal.Hide();

            if (_uncommittedBattleResult != null)
            {
                yield return CommitBattleResultOnlineRoutine(_uncommittedBattleVictory);
                yield break;
            }

            if (!_sessionBootstrapped)
            {
                _sessionBootstrapped = true;
                StartSubjectMonsterExpedition(biomeName, _selectedSubject);
                if (showBiomeSelectOnStart && _biomeModal != null)
                {
                    _biomeModal.Show(allowClose: true);
                }
            }
        }

        private void OnAuthenticatedSessionReady()
        {
            StopAllCoroutines();
            StartCoroutine(PostLoginBootstrapRoutine());
        }

        private IEnumerator PostLoginBootstrapRoutine()
        {
            if (questionManager.Count == 0)
            {
                bool qOk = false;
                string qErr = null;
                yield return syncManager.VerifyOnlineAndLoadQuestionsRoutine((ok, err) =>
                {
                    qOk = ok;
                    qErr = err;
                });

                if (!qOk)
                {
                    _networkGateModal?.Show(qErr, isConnecting: false);
                    yield break;
                }
            }

            _sessionBootstrapped = true;
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
                _sessionBootstrapped = false;
                _uncommittedBattleResult = null;
                State = BattleState.Idle;
                SetGameplayActive(false);
                if (_biomeModal != null) _biomeModal.Hide();
                if (_subjectLobbyModal != null) _subjectLobbyModal.Hide();
                _authGateModal.Show(AuthGateModal.AuthPage.Register);
            }
        }

        private void SetGameplayActive(bool visible)
        {
            if (ui != null) ui.SetGameplayHudVisible(visible);
            if (player != null) player.gameObject.SetActive(visible);
            if (enemy != null) enemy.gameObject.SetActive(visible);
            if (_arenaBackgroundObject != null) _arenaBackgroundObject.SetActive(visible);
            if (!visible && Camera.main != null)
            {
                Camera.main.backgroundColor = StandaloneAuthScreenColor;
            }
        }

        private void OnSubjectBiomeChosen(string selectedBiomeTitle, string subjectFilter)
        {
            if (!EnsureConnectedOrBlock()) return;

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
            if (!EnsureConnectedOrBlock()) return;

            StopAllCoroutines();
            SetGameplayActive(true);
            biomeName = !string.IsNullOrWhiteSpace(selectedBiomeTitle) ? selectedBiomeTitle : "Citadel of Code";
            _selectedSubject = !string.IsNullOrWhiteSpace(subjectFilter) ? subjectFilter : "Programming";

            int subjectLevel = ProgressManager.GetSubjectLevel(_selectedSubject);
            _activeRunStages = MonsterCatalog.BuildStagesForSubjectAndLevel(_selectedSubject, subjectLevel, _baseSceneStages);

            ApplySubjectThemeAndAudio(isBoss: false);
            ReviveAndSetupPlayer();
            StartStage(0);
        }

        private bool EnsureConnectedOrBlock()
        {
            if (syncManager != null && !syncManager.IsOnline)
            {
                syncManager.ReportConnectionLost("Internet data connection required. Reconnect to continue playing.");
                return false;
            }
            return true;
        }

        private void ApplySubjectThemeAndAudio(bool isBoss)
        {
            int subjectLevel = ProgressManager.GetSubjectLevel(_selectedSubject);
            var tier = SubjectTierSystem.GetTierForLevel(subjectLevel);
            ThemeManager.Instance?.ApplyTheme(_selectedSubject, subjectLevel);
            BattleAudioManager.Instance?.PlaySubjectThemeMusic(_selectedSubject, tier, isBoss);
        }

        private void StartStage(int index)
        {
            if (!EnsureConnectedOrBlock()) return;

            if (_activeRunStages == null || _activeRunStages.Count == 0)
            {
                int lv = ProgressManager.GetSubjectLevel(_selectedSubject);
                _activeRunStages = MonsterCatalog.BuildStagesForSubjectAndLevel(_selectedSubject, lv, _baseSceneStages);
            }

            _stageIndex = Mathf.Clamp(index, 0, _activeRunStages.Count - 1);
            _runFinished = false;
            _subjectPoolExhausted = false;
            _streak = 0;
            _battleCorrect = 0;
            _battleTotal = 0;
            _battleXp = 0;
            _battleAnswerLogs.Clear();
            _turn = TurnType.Attack;

            var def = CurrentStage;
            int subjLv = ProgressManager.GetSubjectLevel(_selectedSubject);
            string badgeName = SubjectTierSystem.GetBadgeNameForLevel(subjLv);

            ApplySubjectThemeAndAudio(isBoss: def.isBoss);

            enemy.Setup(def.displayName, def.maxHp, def.attack, def.sprite, def.tint, def.worldHeight, def.attackGesture);
            ui.SetEnemyName(def.displayName, def.isBoss);
            ui.SetStage($"{biomeName} ({badgeName} Lv {subjLv})  •  Stage {_stageIndex + 1}/{_activeRunStages.Count}");

            ui.SetStreak(0);
            ui.HideResult();
            StartCoroutine(IntroRoutine(def));
        }

        private IEnumerator IntroRoutine(EnemyDefinition def)
        {
            State = BattleState.Intro;
            ui.HideQuestionPanel();
            if (def.isBoss)
            {
                BattleAudioManager.Instance?.PlayBossEntranceRoar();
                cameraShake?.Shake(0.5f, 0.15f);
                yield return ui.ShowBanner("BOSS BATTLE", def.displayName, bossColor);
            }
            else
            {
                yield return ui.ShowBanner("A wild monster appears!", def.displayName, victoryColor);
            }
            NextTurn();
        }

        private void NextTurn()
        {
            if (!EnsureConnectedOrBlock()) return;

            var def = CurrentStage;
            string effectiveSubject = !string.IsNullOrWhiteSpace(_selectedSubject) ? _selectedSubject : def.subject;
            int subjectLevel = ProgressManager.GetSubjectLevel(effectiveSubject);

            _currentQuestion = questionManager.GetQuestionForSubjectAndLevel(effectiveSubject, subjectLevel);
            if (_currentQuestion == null)
            {
                StartCoroutine(ReplenishAndRetryTurnRoutine(effectiveSubject, subjectLevel));
                return;
            }

            PresentCurrentQuestion(effectiveSubject, subjectLevel);
        }

        private void PresentCurrentQuestion(string effectiveSubject, int subjectLevel)
        {
            syncManager?.NotifyQuestionSeenOnline(
                _currentQuestion.id,
                effectiveSubject,
                _currentQuestion.difficulty);

            if (!_backgroundQuestionRefreshInFlight &&
                questionManager.CountUnseenForSubjectAndLevel(effectiveSubject, subjectLevel) <= 4 &&
                syncManager != null)
            {
                _backgroundQuestionRefreshInFlight = true;
                int tierDiff = SubjectTierSystem.GetAlignedQuestionDifficulty(subjectLevel);
                StartCoroutine(syncManager.ReplenishAndReloadSubjectQuestionsRoutine(
                    effectiveSubject,
                    tierDiff,
                    (_, _) => _backgroundQuestionRefreshInFlight = false));
            }

            ui.SetTurn(_turn);
            ui.ShowQuestion(_currentQuestion);
            State = BattleState.AwaitingAnswer;
        }

        private IEnumerator ReplenishAndRetryTurnRoutine(string effectiveSubject, int subjectLevel)
        {
            State = BattleState.Intro;
            ui.HideQuestionPanel();

            int tierDifficulty = SubjectTierSystem.GetAlignedQuestionDifficulty(subjectLevel);
            bool onlineOk = true;
            string onlineErr = null;

            if (syncManager != null)
            {
                yield return syncManager.ReplenishAndReloadSubjectQuestionsRoutine(
                    effectiveSubject,
                    tierDifficulty,
                    (ok, err) =>
                    {
                        onlineOk = ok;
                        onlineErr = err;
                    });
            }

            if (!onlineOk)
            {
                _networkGateModal?.Show(onlineErr ?? "Lost connection while loading new subject questions.", isConnecting: false);
                State = BattleState.Idle;
                yield break;
            }

            _currentQuestion = questionManager.GetQuestionForSubjectAndLevel(effectiveSubject, subjectLevel);
            if (_currentQuestion != null)
            {
                PresentCurrentQuestion(effectiveSubject, subjectLevel);
                yield break;
            }

            _subjectPoolExhausted = true;
            State = BattleState.BattleOver;
            string tierName = SubjectTierSystem.GetBadgeNameForLevel(subjectLevel);
            ui.ShowResult(
                "SUBJECT POOL COMPLETED",
                $"You have answered every available unique {tierName} Tier question for {effectiveSubject}.\n" +
                "Questions never repeat once shown, and each subject uses only its own curriculum pool.\n" +
                "Return to the Subject Lobby or try another subject category while new questions are added.",
                "Open Subject Lobby",
                victoryColor);
        }

        private void OnAnswerSelected(int index)
        {
            if (State != BattleState.AwaitingAnswer) return;
            if (!EnsureConnectedOrBlock()) return;

            BattleAudioManager.Instance?.PlayButtonClick();
            State = BattleState.Resolving;
            StartCoroutine(ResolveRoutine(index));
        }

        private IEnumerator ResolveRoutine(int selectedIndex)
        {
            var q = _currentQuestion;
            bool correct = selectedIndex == q.correctIndex;
            _battleTotal++;
            _battleAnswerLogs.Add(new BattleAnswerLogDto
            {
                questionId = q.id,
                wasCorrect = correct,
                responseTimeMs = 0
            });

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
                BattleAudioManager.Instance?.PlayMonsterAttack(def.isBoss);
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
            if (!EnsureConnectedOrBlock()) return;

            BattleAudioManager.Instance?.PlayButtonClick();

            if (enemy.IsDead)
            {
                StartCoroutine(CommitBattleResultOnlineRoutine(true));
                return;
            }
            if (player.IsDead)
            {
                StartCoroutine(CommitBattleResultOnlineRoutine(false));
                return;
            }

            _turn = _turn == TurnType.Attack ? TurnType.Defend : TurnType.Attack;
            NextTurn();
        }

        private IEnumerator CommitBattleResultOnlineRoutine(bool victory)
        {
            State = BattleState.Resolving;
            ui.HideQuestionPanel();

            var def = _uncommittedStageDef ?? CurrentStage;
            if (_uncommittedBattleResult == null)
            {
                int xp = _battleXp + (victory ? def.xpReward : 0);
                _uncommittedStageDef = def;
                _uncommittedBattleVictory = victory;
                _uncommittedBattleResult = new BattleResultSubmission
                {
                    subject = _selectedSubject,
                    xpGained = xp,
                    victory = victory,
                    correctAnswers = _battleCorrect,
                    totalAnswers = _battleTotal,
                    answerLogs = _battleAnswerLogs.ToArray()
                };
            }

            bool syncedOk = false;
            string syncError = null;
            int levelsGained = 0;

            yield return syncManager.SubmitBattleResultOnlineRoutine(_uncommittedBattleResult, (ok, err, gained) =>
            {
                syncedOk = ok;
                syncError = err;
                levelsGained = gained;
            });

            if (!syncedOk)
            {
                _networkGateModal?.Show(syncError ?? "Connection lost while saving battle progress to server.", isConnecting: false);
                yield break;
            }

            int awardedXp = _uncommittedBattleResult.xpGained;
            _uncommittedBattleResult = null;
            _uncommittedStageDef = null;

            if (victory)
            {
                ProgressManager.RecordStageClearedInSession(_stageIndex);
                BattleAudioManager.Instance?.PlayVictoryFanfare();
            }
            else
            {
                BattleAudioManager.Instance?.PlayDefeatStinger();
            }

            State = BattleState.BattleOver;

            var sp = ProgressManager.GetSubjectProgress(_selectedSubject);
            if (levelsGained > 0)
            {
                _activeRunStages = MonsterCatalog.BuildStagesForSubjectAndLevel(_selectedSubject, sp.level, _baseSceneStages);
                player.UpdateStats(ProgressManager.MaxHpForLevel(sp.level), ProgressManager.AttackForLevel(sp.level), fullHeal: true);
            }
            RefreshPlayerInfo();

            int accuracy = _battleTotal > 0 ? Mathf.RoundToInt(100f * _battleCorrect / _battleTotal) : 0;
            string badgeName = SubjectTierSystem.GetBadgeNameForLevel(sp.level);
            int nextXp = SubjectTierSystem.XpToNextSubjectLevel(sp.level);

            string body = $"+{awardedXp} {_selectedSubject} XP  •  Accuracy: {_battleCorrect}/{_battleTotal} ({accuracy}%)\n" +
                          $"Online Subject Standing: LV {sp.level}/100 ({badgeName} Badge • {sp.xp}/{nextXp} XP)";
            if (levelsGained > 0)
            {
                body += $"\n<color=#FFD24D>SUBJECT LEVEL UP! Advanced sequentially to LV {sp.level} ({badgeName} Badge) in {_selectedSubject}!</color>";
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

        private void OnResultButton()
        {
            BattleAudioManager.Instance?.PlayButtonClick();
            if (!EnsureConnectedOrBlock()) return;
            if (State != BattleState.BattleOver) return;

            if (_runFinished || _subjectPoolExhausted)
            {
                _subjectPoolExhausted = false;
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
                2.35f,
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
    }
}

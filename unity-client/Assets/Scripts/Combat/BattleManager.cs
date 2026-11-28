using System;
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
        private bool _isPausePromptActive;
        private BattleState _stateBeforePause;
        private bool _lastDefeatRequiresRestart;
        private bool _isRevivingWithCoins;
        private bool _onlineSignalBannerShown;
        private string _selectedSubject = "Programming";
        private Sprite _heroBaseSprite;
        private GameObject _arenaBackgroundObject;
        private static readonly Color StandaloneAuthScreenColor = new(0.03f, 0.04f, 0.11f, 1f);

        private BattleResultSubmission _uncommittedBattleResult;
        private bool _uncommittedBattleVictory;
        private EnemyDefinition _uncommittedStageDef;

        private BiomeSelectModal _biomeModal;
        private SubjectLobbyModal _subjectLobbyModal;
        private GameMechanicsModal _gameMechanicsModal;
        private BrandIntroModal _brandIntroModal;
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
                SettingsModal.EnsureOnCanvas(canvas);
                _biomeModal = BiomeSelectModal.EnsureOnCanvas(
                    canvas,
                    questionManager,
                    OnSubjectBiomeChosen,
                    OnHudSubjectLobbiesClicked,
                    SwitchFromOfflineToOnlineMode);
                _subjectLobbyModal = SubjectLobbyModal.EnsureOnCanvas(canvas, OnSubjectLobbyLaunchRequested);
                _gameMechanicsModal = GameMechanicsModal.EnsureOnCanvas(canvas);
                _authGateModal = AuthGateModal.EnsureOnCanvas(canvas, OnAuthenticatedSessionReady);
                _networkGateModal = NetworkGateModal.EnsureOnCanvas(canvas, OnRetryOnlineConnection, EnterOfflinePracticeModeFromGate);
                _brandIntroModal = BrandIntroModal.EnsureOnCanvas(canvas);
                _brandIntroModal.PlayIntro();
            }

            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnAuthStateChanged += HandleAuthStateChanged;
            }
            if (syncManager != null)
            {
                syncManager.OnOnlineAvailabilityChanged += HandleOnlineAvailabilityChanged;
                syncManager.OnOnlineSignalRestored += HandleOnlineSignalRestored;
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
            if (syncManager != null)
            {
                syncManager.OnOnlineAvailabilityChanged -= HandleOnlineAvailabilityChanged;
                syncManager.OnOnlineSignalRestored -= HandleOnlineSignalRestored;
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                SaveActiveBattleCheckpointIfInProgress();
            }
        }

        private void OnApplicationQuit()
        {
            SaveActiveBattleCheckpointIfInProgress();
        }

        private void SaveActiveBattleCheckpointIfInProgress()
        {
            bool battleInProgress = State == BattleState.AwaitingAnswer ||
                                    State == BattleState.AwaitingContinue ||
                                    State == BattleState.Intro;
            if (!battleInProgress || _isPausePromptActive || player == null || enemy == null || player.IsDead || enemy.IsDead)
            {
                return;
            }
            SaveActiveBattleCheckpoint();
        }

        private void SaveActiveBattleCheckpoint()
        {
            if (player == null || enemy == null || player.IsDead || enemy.IsDead) return;
            int subjLv = ProgressManager.GetSubjectLevel(_selectedSubject);
            var checkpoint = new SubjectBattleCheckpoint
            {
                subject = _selectedSubject,
                biomeTitle = biomeName,
                subjectLevel = subjLv,
                stageIndex = _stageIndex,
                playerHp = player.CurrentHp,
                playerMaxHp = player.MaxHp,
                enemyHp = enemy.CurrentHp,
                enemyMaxHp = enemy.MaxHp,
                turnType = (int)_turn,
                streak = _streak,
                battleCorrect = _battleCorrect,
                battleTotal = _battleTotal,
                battleXp = _battleXp,
                currentQuestionId = _currentQuestion?.id,
                hasCheckpoint = true
            };
            SubjectCheckpointManager.SaveCheckpoint(checkpoint);
            if (!OfflinePracticeManager.IsOfflinePracticeMode)
            {
                syncManager?.SaveSubjectCheckpointOnline(
                    _selectedSubject,
                    _stageIndex,
                    player.CurrentHp,
                    enemy.CurrentHp,
                    true);
            }
        }

        private void HandleOnlineAvailabilityChanged(bool online, string reason)
        {
            if (online || OfflinePracticeManager.IsOfflinePracticeMode || _networkGateModal == null) return;
            _networkGateModal.Show(reason, isConnecting: false);
        }

        private void HandleOnlineSignalRestored()
        {
            if (!OfflinePracticeManager.IsOfflinePracticeMode) return;
            if (!_onlineSignalBannerShown)
            {
                _onlineSignalBannerShown = true;
                StartCoroutine(ShowOnlineSignalRestoredBannerRoutine());
            }
            UpdateStageHeaderText();
        }

        private IEnumerator ShowOnlineSignalRestoredBannerRoutine()
        {
            if (ui != null && State != BattleState.Idle)
            {
                yield return ui.ShowBanner(
                    "ONLINE SIGNAL RESTORED",
                    "Open Subject Lobbies or finish this stage to switch to Live Online Mode.",
                    victoryColor);
            }
        }

        private void EnterOfflinePracticeModeFromGate()
        {
            StopAllCoroutines();
            OfflinePracticeManager.EnterOfflinePracticeMode();
            _onlineSignalBannerShown = false;
            syncManager?.ResetSignalRestoredNotification();

            bool loaded = OfflinePracticeManager.TryLoadOfflinePracticeQuestions(questionManager, out _);
            if (_networkGateModal != null) _networkGateModal.Hide();
            if (_authGateModal != null) _authGateModal.Hide();

            if (!loaded)
            {
                OfflinePracticeManager.ExitOfflinePracticeMode();
                _networkGateModal?.Show("No offline practice questions are cached yet. Connect once online to download questions.", isConnecting: false);
                return;
            }

            _sessionBootstrapped = true;
            SetGameplayActive(true);
            RefreshPlayerInfo();
            if (_biomeModal != null)
            {
                _biomeModal.Show(allowClose: false);
            }
            else
            {
                StartSubjectMonsterExpedition(biomeName, _selectedSubject, resumeCheckpoint: false);
            }
        }

        private void SwitchFromOfflineToOnlineMode()
        {
            BattleAudioManager.Instance?.PlayButtonClick();
            StopAllCoroutines();
            OfflinePracticeManager.ExitOfflinePracticeMode();
            _onlineSignalBannerShown = false;
            _sessionBootstrapped = false;
            _isPausePromptActive = false;
            ui.HideResult();
            ui.HideQuestionPanel();
            if (_biomeModal != null) _biomeModal.Hide();
            if (_subjectLobbyModal != null) _subjectLobbyModal.Hide();
            State = BattleState.Idle;
            SetGameplayActive(false);
            StartCoroutine(BootstrapOnlineSessionRoutine());
        }

        private void OnRetryOnlineConnection()
        {
            OfflinePracticeManager.ExitOfflinePracticeMode();
            _onlineSignalBannerShown = false;
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
                    string expiryMsg = AuthManager.Instance.LastInactivityExpiryMessage;
                    var page = !string.IsNullOrWhiteSpace(expiryMsg)
                        ? AuthGateModal.AuthPage.Login
                        : AuthGateModal.AuthPage.Login;
                    _authGateModal?.Show(page, expiryMsg);
                    yield break;
                }
            }
            else
            {
                if (_networkGateModal != null) _networkGateModal.Hide();
                string expiryMsg = AuthManager.Instance?.LastInactivityExpiryMessage;
                _authGateModal?.Show(
                    !string.IsNullOrWhiteSpace(expiryMsg) ? AuthGateModal.AuthPage.Login : AuthGateModal.AuthPage.Register,
                    expiryMsg);
                yield break;
            }

            if (_networkGateModal != null) _networkGateModal.Hide();
            if (_authGateModal != null) _authGateModal.Hide();

            if (_uncommittedBattleResult != null)
            {
                yield return CommitBattleResultOnlineRoutine(_uncommittedBattleVictory);
                yield break;
            }

            if (!_sessionBootstrapped)
            {
                _sessionBootstrapped = true;
                StartSubjectMonsterExpedition(biomeName, _selectedSubject, resumeCheckpoint: false);
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

            if (_authGateModal != null) _authGateModal.Hide();
            _sessionBootstrapped = true;
            StartSubjectMonsterExpedition(biomeName, _selectedSubject, resumeCheckpoint: false);
            if (showBiomeSelectOnStart && _biomeModal != null)
            {
                _biomeModal.Show(allowClose: true);
            }
        }

        private void HandleAuthStateChanged()
        {
            RefreshPlayerInfo();
            if (AuthManager.Instance != null && !AuthManager.Instance.IsAuthenticated &&
                !OfflinePracticeManager.IsOfflinePracticeMode && _authGateModal != null)
            {
                StopAllCoroutines();
                _sessionBootstrapped = false;
                _uncommittedBattleResult = null;
                _isPausePromptActive = false;
                State = BattleState.Idle;
                SetGameplayActive(false);
                if (_biomeModal != null) _biomeModal.Hide();
                if (_subjectLobbyModal != null) _subjectLobbyModal.Hide();
                string expiryMsg = AuthManager.Instance.LastInactivityExpiryMessage;
                _authGateModal.Show(
                    !string.IsNullOrWhiteSpace(expiryMsg) ? AuthGateModal.AuthPage.Login : AuthGateModal.AuthPage.Register,
                    expiryMsg);
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

        private void OnHudSubjectLobbiesClicked()
        {
            BattleAudioManager.Instance?.PlayButtonClick();
            AuthManager.Instance?.RecordUserActivity();

            bool battleInProgress = State == BattleState.AwaitingAnswer ||
                                    State == BattleState.AwaitingContinue ||
                                    State == BattleState.Intro;

            if (!battleInProgress || _isPausePromptActive || player.IsDead || enemy.IsDead)
            {
                _biomeModal?.Show(allowClose: true);
                return;
            }

            _stateBeforePause = State;
            _isPausePromptActive = true;
            State = BattleState.Idle;

            string modeNotice = OfflinePracticeManager.IsOfflinePracticeMode
                ? "Offline Practice Mode: Choose RESUME BATTLE to continue, or QUIT TO SUBJECT LOBBIES to save your offline stage progress (kept separate from Online Mode) so you can return anytime."
                : "Choose RESUME BATTLE to continue, or QUIT TO SUBJECT LOBBIES to save your stage progress so you can switch subjects and return later.";

            ui.ShowResult(
                "PAUSE EXPEDITION",
                $"Currently on Stage {_stageIndex + 1}/{_activeRunStages.Count} in {_selectedSubject} ({biomeName}).\n\n{modeNotice}",
                "RESUME BATTLE",
                victoryColor,
                "QUIT TO SUBJECT LOBBIES",
                QuitCurrentBattleToLobbies);
        }

        private void ResumePausedBattle()
        {
            BattleAudioManager.Instance?.PlayButtonClick();
            AuthManager.Instance?.RecordUserActivity();
            _isPausePromptActive = false;
            ui.HideResult();
            State = _stateBeforePause;
            if (State == BattleState.Intro)
            {
                NextTurn();
            }
        }

        private void QuitCurrentBattleToLobbies()
        {
            BattleAudioManager.Instance?.PlayButtonClick();
            AuthManager.Instance?.RecordUserActivity();
            _isPausePromptActive = false;
            StopAllCoroutines();

            if (!player.IsDead && !enemy.IsDead)
            {
                SaveActiveBattleCheckpoint();
            }

            State = BattleState.Idle;
            ui.HideResult();
            ui.HideQuestionPanel();
            _biomeModal?.Show(allowClose: true);
        }

        private void OnSubjectBiomeChosen(string selectedBiomeTitle, string subjectFilter)
        {
            if (!EnsureConnectedOrBlock()) return;
            AuthManager.Instance?.RecordUserActivity();

            biomeName = !string.IsNullOrWhiteSpace(selectedBiomeTitle) ? selectedBiomeTitle : "Citadel of Code";
            _selectedSubject = !string.IsNullOrWhiteSpace(subjectFilter) ? subjectFilter : "Programming";

            if (_subjectLobbyModal != null)
            {
                _subjectLobbyModal.ShowForSubject(biomeName, _selectedSubject);
            }
            else
            {
                bool shouldResume = SubjectCheckpointManager.TryGetCheckpoint(_selectedSubject, out _);
                StartSubjectMonsterExpedition(biomeName, _selectedSubject, shouldResume);
            }
        }

        private void OnSubjectLobbyLaunchRequested(
            string selectedBiomeTitle,
            string subjectFilter,
            bool resumeCheckpoint)
        {
            if (!EnsureConnectedOrBlock()) return;
            AuthManager.Instance?.RecordUserActivity();

            if (_gameMechanicsModal != null)
            {
                _gameMechanicsModal.ShowForSubject(
                    selectedBiomeTitle,
                    subjectFilter,
                    resumeCheckpoint,
                    () => StartSubjectMonsterExpedition(selectedBiomeTitle, subjectFilter, resumeCheckpoint),
                    () => _subjectLobbyModal?.ShowForSubject(selectedBiomeTitle, subjectFilter));
            }
            else
            {
                StartSubjectMonsterExpedition(selectedBiomeTitle, subjectFilter, resumeCheckpoint);
            }
        }

        private void StartSubjectMonsterExpedition(
            string selectedBiomeTitle,
            string subjectFilter,
            bool resumeCheckpoint = false)
        {
            if (!EnsureConnectedOrBlock()) return;
            AuthManager.Instance?.RecordUserActivity();

            StopAllCoroutines();
            _isPausePromptActive = false;
            SetGameplayActive(true);
            biomeName = !string.IsNullOrWhiteSpace(selectedBiomeTitle) ? selectedBiomeTitle : "Citadel of Code";
            _selectedSubject = !string.IsNullOrWhiteSpace(subjectFilter) ? subjectFilter : "Programming";

            int subjectLevel = ProgressManager.GetSubjectLevel(_selectedSubject);
            _activeRunStages = MonsterCatalog.BuildStagesForSubjectAndLevel(_selectedSubject, subjectLevel, _baseSceneStages);

            ApplySubjectThemeAndAudio(isBoss: false);
            ReviveAndSetupPlayer();

            if (resumeCheckpoint &&
                SubjectCheckpointManager.TryGetCheckpoint(_selectedSubject, out var cp) &&
                cp != null)
            {
                ResumeStageFromCheckpoint(cp);
                return;
            }

            SubjectCheckpointManager.ClearCheckpoint(_selectedSubject);
            if (!OfflinePracticeManager.IsOfflinePracticeMode)
            {
                syncManager?.SaveSubjectCheckpointOnline(_selectedSubject, 0, 0, 0, false);
            }
            StartStage(0);
        }

        private void ResumeStageFromCheckpoint(SubjectBattleCheckpoint cp)
        {
            _stageIndex = Mathf.Clamp(cp.stageIndex, 0, _activeRunStages.Count - 1);
            _runFinished = false;
            _subjectPoolExhausted = false;
            _streak = Mathf.Max(0, cp.streak);
            _battleCorrect = Mathf.Max(0, cp.battleCorrect);
            _battleTotal = Mathf.Max(0, cp.battleTotal);
            _battleXp = Mathf.Max(0, cp.battleXp);
            _battleAnswerLogs.Clear();
            _turn = cp.turnType == 1 ? TurnType.Defend : TurnType.Attack;

            if (cp.playerHp > 0)
            {
                player.SetCurrentHp(cp.playerHp);
            }

            var def = CurrentStage;
            ApplySubjectThemeAndAudio(isBoss: def.isBoss);
            enemy.Setup(def.displayName, def.maxHp, def.attack, def.sprite, def.tint, def.worldHeight, def.attackGesture);
            if (cp.enemyHp > 0 && cp.enemyHp <= def.maxHp)
            {
                enemy.SetCurrentHp(cp.enemyHp);
            }

            ui.SetEnemyName(def.displayName, def.isBoss);
            UpdateStageHeaderText();
            ui.SetStreak(_streak);
            ui.HideResult();

            var restoredQuestion = !string.IsNullOrWhiteSpace(cp.currentQuestionId)
                ? questionManager.GetQuestionById(cp.currentQuestionId)
                : null;

            if (restoredQuestion != null)
            {
                _currentQuestion = restoredQuestion;
                ui.SetTurn(_turn);
                ui.ShowQuestion(_currentQuestion);
                State = BattleState.AwaitingAnswer;
            }
            else
            {
                StartCoroutine(IntroRoutine(def));
            }
        }

        private bool EnsureConnectedOrBlock()
        {
            if (OfflinePracticeManager.IsOfflinePracticeMode)
            {
                return true;
            }
            if (syncManager != null && !syncManager.IsOnline)
            {
                syncManager.ReportConnectionLost("Internet data connection required. Reconnect to continue playing or use Offline Practice Mode.");
                return false;
            }
            return true;
        }

        private void UpdateStageHeaderText()
        {
            int subjLv = ProgressManager.GetSubjectLevel(_selectedSubject);
            string badgeName = SubjectTierSystem.GetBadgeNameForLevel(subjLv);
            if (OfflinePracticeManager.IsOfflinePracticeMode)
            {
                string signalTag = syncManager != null && syncManager.IsServerConnected ? "SIGNAL READY • " : "";
                ui.SetStage($"OFFLINE PRACTICE ({signalTag}{biomeName})  •  Stage {_stageIndex + 1}/{_activeRunStages.Count}");
            }
            else
            {
                ui.SetStage($"{biomeName} ({badgeName} Lv {subjLv})  •  Stage {_stageIndex + 1}/{_activeRunStages.Count}");
            }
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
            UpdateStageHeaderText();

            ui.SetStreak(0);
            ui.HideResult();
            StartCoroutine(IntroRoutine(def));
        }

        private IEnumerator IntroRoutine(EnemyDefinition def)
        {
            State = BattleState.Intro;
            ui.HideQuestionPanel();
            ui.SpawnEmote(player, CombatEmoteType.Triumph, "READY!");
            ui.SpawnEmote(enemy, def.isBoss ? CombatEmoteType.BossRoar : CombatEmoteType.MonsterTaunt, def.isBoss ? "ROAR!" : "GRRR!");
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
            bool isOffline = OfflinePracticeManager.IsOfflinePracticeMode;

            _currentQuestion = questionManager.GetQuestionForSubjectLevelAndStage(
                effectiveSubject,
                subjectLevel,
                _stageIndex,
                isOffline);
            if (_currentQuestion == null)
            {
                if (isOffline)
                {
                    _subjectPoolExhausted = true;
                    State = BattleState.BattleOver;
                    ui.ShowResult(
                        "OFFLINE PRACTICE COMPLETE",
                        $"You practiced all cached questions for {effectiveSubject} at this tier.",
                        "OPEN SUBJECT LOBBY",
                        victoryColor);
                    return;
                }
                StartCoroutine(ReplenishAndRetryTurnRoutine(effectiveSubject, subjectLevel));
                return;
            }

            PresentCurrentQuestion(effectiveSubject, subjectLevel);
        }

        private void PresentCurrentQuestion(string effectiveSubject, int subjectLevel)
        {
            if (!OfflinePracticeManager.IsOfflinePracticeMode)
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
            }

            ui.SetTurn(_turn);
            ui.ShowQuestion(_currentQuestion);
            State = BattleState.AwaitingAnswer;
            SaveActiveBattleCheckpoint();
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
            if (State != BattleState.AwaitingAnswer || player.IsDead || enemy.IsDead) return;
            if (!EnsureConnectedOrBlock()) return;

            AuthManager.Instance?.RecordUserActivity();
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
                        ui.SpawnEmote(player, isCrit ? CombatEmoteType.CritFury : CombatEmoteType.Triumph, isCrit ? "CRITICAL!" : "STRIKE!");
                        if (!enemy.IsDead) ui.SpawnEmote(enemy, CombatEmoteType.MonsterShock, "ARGH!");
                    });
                    headline = isCrit ? $"Correct! Combo x{_streak}!" : "Correct! Direct hit!";
                }
                else
                {
                    BattleAudioManager.Instance?.PlayMissWhoosh();
                    yield return player.AttackRoutine(enemy, () =>
                    {
                        ui.SpawnPopup(enemy.PopupAnchor, "MISS", missColor, 1f);
                        ui.SpawnEmote(player, CombatEmoteType.SweatMiss, "OOPS!");
                        ui.SpawnEmote(enemy, CombatEmoteType.MonsterTaunt, "HAHA!");
                    });
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
                        ui.SpawnEmote(player, CombatEmoteType.ShieldBlock, "GUARD!");
                        ui.SpawnEmote(enemy, CombatEmoteType.MonsterShock, "BLOCKED!");
                    }
                    else
                    {
                        BattleAudioManager.Instance?.PlayHitImpact();
                        player.TakeDamage(damage);
                        cameraShake?.Shake(0.2f, 0.14f);
                        ui.SpawnPopup(player.PopupAnchor, damage.ToString(), damageColor, 1.2f);
                        ui.SpawnEmote(enemy, def.isBoss ? CombatEmoteType.BossRoar : CombatEmoteType.MonsterTaunt, def.isBoss ? "BURN!" : "TAKE THIS!");
                        if (!player.IsDead) ui.SpawnEmote(player, CombatEmoteType.SweatMiss, "OUCH!");
                    }
                });
                headline = correct ? "Correct! Attack blocked!" : "Wrong! You took a hit.";
            }

            if (enemy.IsDead)
            {
                ui.SpawnEmote(player, CombatEmoteType.Triumph, "VICTORY!");
                ui.SpawnEmote(enemy, CombatEmoteType.DefeatDizzy, "KO!");
                yield return enemy.DeathRoutine();
            }
            if (player.IsDead)
            {
                ui.SpawnEmote(enemy, CombatEmoteType.MonsterTaunt, "VICTORY!");
                ui.SpawnEmote(player, CombatEmoteType.DefeatDizzy, "KO!");
                yield return player.DeathRoutine();
            }

            ui.ShowFeedback(correct, headline, q.explanation);
            State = BattleState.AwaitingContinue;
        }

        private void OnContinue()
        {
            if (State != BattleState.AwaitingContinue) return;
            if (!EnsureConnectedOrBlock()) return;

            AuthManager.Instance?.RecordUserActivity();
            BattleAudioManager.Instance?.PlayButtonClick();

            if (OfflinePracticeManager.IsOfflinePracticeMode)
            {
                if (enemy.IsDead)
                {
                    ShowOfflinePracticeResult(victory: true);
                    return;
                }
                if (player.IsDead)
                {
                    ShowOfflinePracticeResult(victory: false);
                    return;
                }
                _turn = _turn == TurnType.Attack ? TurnType.Defend : TurnType.Attack;
                NextTurn();
                return;
            }

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

        private void ShowOfflinePracticeResult(bool victory)
        {
            ui.HideQuestionPanel();
            State = BattleState.BattleOver;
            var def = CurrentStage;
            int accuracy = _battleTotal > 0 ? Mathf.RoundToInt(100f * _battleCorrect / _battleTotal) : 0;
            bool signalReady = syncManager != null && syncManager.IsServerConnected;
            string syncNote = signalReady
                ? "\nInternet signal is restored! You can switch back to Live Online Mode now."
                : "\nOffline Practice Mode: 0 XP and 0 Coins awarded until you play online.";

            if (victory)
            {
                BattleAudioManager.Instance?.PlayVictoryFanfare();
                bool lastStage = _stageIndex >= _activeRunStages.Count - 1;
                _runFinished = lastStage;
                _lastDefeatRequiresRestart = false;

                if (lastStage)
                {
                    SubjectCheckpointManager.ClearCheckpoint(_selectedSubject, isOfflineOverride: true);
                }
                else
                {
                    int nextStageHp = Mathf.Min(player.MaxHp, player.CurrentHp + Mathf.RoundToInt(player.MaxHp * healBetweenStages));
                    SubjectCheckpointManager.SaveCheckpoint(new SubjectBattleCheckpoint
                    {
                        subject = _selectedSubject,
                        biomeTitle = biomeName,
                        subjectLevel = ProgressManager.GetSubjectLevel(_selectedSubject),
                        stageIndex = _stageIndex + 1,
                        playerHp = nextStageHp,
                        playerMaxHp = player.MaxHp,
                        hasCheckpoint = true
                    }, isOfflineOverride: true);
                }

                ui.ShowResult(
                    lastStage ? "OFFLINE PRACTICE CLEARED" : "PRACTICE STAGE CLEARED",
                    $"{def.displayName} defeated in Practice Mode.\nAccuracy: {_battleCorrect}/{_battleTotal} ({accuracy}%){syncNote}",
                    lastStage ? "OPEN SUBJECT LOBBY" : "NEXT MONSTER",
                    victoryColor,
                    signalReady ? "SWITCH TO LIVE ONLINE MODE" : null,
                    signalReady ? SwitchFromOfflineToOnlineMode : (Action)null);
            }
            else
            {
                BattleAudioManager.Instance?.PlayDefeatStinger();
                _lastDefeatRequiresRestart = true;
                SubjectCheckpointManager.ClearCheckpoint(_selectedSubject, isOfflineOverride: true);
                ui.ShowResult(
                    "PRACTICE DEFEATED",
                    $"{def.displayName} defeated you in Practice Mode.\nAccuracy: {_battleCorrect}/{_battleTotal} ({accuracy}%){syncNote}",
                    "RESTART EXPEDITION STAGE 1",
                    defeatColor,
                    signalReady ? "SWITCH TO LIVE ONLINE MODE" : "OPEN SUBJECT LOBBY",
                    signalReady ? SwitchFromOfflineToOnlineMode : OpenCurrentSubjectLobbyAfterResult);
            }
        }

        private void OpenCurrentSubjectLobbyAfterResult()
        {
            BattleAudioManager.Instance?.PlayButtonClick();
            ui.HideResult();
            ReviveAndSetupPlayer();
            if (_subjectLobbyModal != null)
            {
                _subjectLobbyModal.ShowForSubject(biomeName, _selectedSubject);
            }
            else if (_biomeModal != null)
            {
                _biomeModal.Show(allowClose: true);
            }
        }

        private IEnumerator CommitBattleResultOnlineRoutine(bool victory)
        {
            State = BattleState.Resolving;
            ui.HideQuestionPanel();

            var def = _uncommittedStageDef ?? CurrentStage;
            bool isFinalStageWin = victory && _stageIndex >= _activeRunStages.Count - 1;
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
                    isBoss = def.isBoss,
                    stageIndex = _stageIndex,
                    clearCheckpoint = isFinalStageWin,
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
                if (victory)
                {
                    player.UpdateStats(ProgressManager.MaxHpForLevel(sp.level), ProgressManager.AttackForLevel(sp.level), fullHeal: true);
                }
            }
            RefreshPlayerInfo();

            int accuracy = _battleTotal > 0 ? Mathf.RoundToInt(100f * _battleCorrect / _battleTotal) : 0;
            string badgeName = SubjectTierSystem.GetBadgeNameForLevel(sp.level);
            int nextXp = SubjectTierSystem.XpToNextSubjectLevel(sp.level);
            int coinsEarned = victory ? SubjectTierSystem.GetStageWinCoinReward(def.isBoss) : 0;
            int totalCoins = ProgressManager.Current.coins;

            string body = $"+{awardedXp} {_selectedSubject} XP  •  Accuracy: {_battleCorrect}/{_battleTotal} ({accuracy}%)\n" +
                          $"Coins Earned: +{coinsEarned} Coins (Balance: {totalCoins} Coins)\n" +
                          $"Online Subject Standing: LV {sp.level}/100 ({badgeName} Badge • {sp.xp}/{nextXp} XP)";

            if (levelsGained > 0)
            {
                body += $"\n<color=#FFD24D>SUBJECT LEVEL UP! Advanced sequentially to LV {sp.level} ({badgeName} Badge) in {_selectedSubject}!</color>";
            }
            if (sp.level >= SubjectTierSystem.MaxLevel)
            {
                body += $"\n<color=#FFD24D>EXPERT LEVEL 100 MASTERED! Stage victories continue awarding +{SubjectTierSystem.StageWinCoinReward} Coins (+{SubjectTierSystem.BossWinCoinReward} on Boss), Subject Points, and Leaderboard XP.</color>";
            }

            if (victory)
            {
                bool lastStage = _stageIndex >= _activeRunStages.Count - 1;
                _runFinished = lastStage;
                _lastDefeatRequiresRestart = false;

                if (lastStage)
                {
                    SubjectCheckpointManager.ClearCheckpoint(_selectedSubject);
                }
                else
                {
                    SubjectCheckpointManager.SaveCheckpoint(new SubjectBattleCheckpoint
                    {
                        subject = _selectedSubject,
                        biomeTitle = biomeName,
                        subjectLevel = sp.level,
                        stageIndex = _stageIndex + 1,
                        playerHp = player.CurrentHp,
                        playerMaxHp = player.MaxHp,
                        hasCheckpoint = true
                    });
                }

                ui.ShowResult(
                    lastStage ? "SUBJECT BIOME CLEARED!" : "VICTORY!",
                    $"{def.displayName} was defeated.\n{body}",
                    lastStage ? "OPEN SUBJECT LOBBY" : "NEXT MONSTER",
                    victoryColor);
            }
            else
            {
                int reviveCost = SubjectTierSystem.GetReviveCoinCostForLevel(sp.level);
                bool canAffordRevive = totalCoins >= reviveCost;
                _lastDefeatRequiresRestart = !canAffordRevive;

                if (canAffordRevive)
                {
                    ui.ShowResult(
                        "DEFEATED",
                        $"{def.displayName} defeated you on Stage {_stageIndex + 1}/{_activeRunStages.Count}.\n{body}\n" +
                        $"Spend {reviveCost} Coins ({badgeName} Tier) to revive on Stage {_stageIndex + 1}, or restart from Stage 1.",
                        $"PAY {reviveCost} COINS TO REVIVE STAGE {_stageIndex + 1}",
                        defeatColor,
                        "RESTART EXPEDITION STAGE 1",
                        RestartExpeditionFromStage1);
                }
                else
                {
                    ui.ShowResult(
                        "DEFEATED",
                        $"{def.displayName} defeated you on Stage {_stageIndex + 1}/{_activeRunStages.Count}.\n{body}\n" +
                        $"Reviving on Stage {_stageIndex + 1} requires {reviveCost} Coins ({badgeName} Tier), and you have {totalCoins} Coins.\n" +
                        "Your expedition will restart from Stage 1/4.",
                        "RESTART EXPEDITION STAGE 1",
                        defeatColor,
                        "OPEN SUBJECT LOBBY",
                        RestartAndOpenLobbyAfterDefeat);
                }
            }
        }

        private void RestartExpeditionFromStage1()
        {
            BattleAudioManager.Instance?.PlayButtonClick();
            AuthManager.Instance?.RecordUserActivity();
            ui.HideResult();
            SubjectCheckpointManager.ClearCheckpoint(_selectedSubject);
            if (!OfflinePracticeManager.IsOfflinePracticeMode)
            {
                syncManager?.SaveSubjectCheckpointOnline(_selectedSubject, 0, 0, 0, false);
            }
            ReviveAndSetupPlayer();
            StartStage(0);
        }

        private void RestartAndOpenLobbyAfterDefeat()
        {
            SubjectCheckpointManager.ClearCheckpoint(_selectedSubject);
            if (!OfflinePracticeManager.IsOfflinePracticeMode)
            {
                syncManager?.SaveSubjectCheckpointOnline(_selectedSubject, 0, 0, 0, false);
            }
            OpenCurrentSubjectLobbyAfterResult();
        }

        private IEnumerator ReviveWithCoinsRoutine(int reviveCost)
        {
            if (_isRevivingWithCoins) yield break;
            _isRevivingWithCoins = true;

            bool revivedOk = false;
            string reviveErr = null;
            int remainingCoins = ProgressManager.Current.coins;

            yield return syncManager.SpendCoinsForReviveOnlineRoutine(
                _selectedSubject,
                _stageIndex,
                (ok, err, remaining) =>
                {
                    revivedOk = ok;
                    reviveErr = err;
                    remainingCoins = remaining;
                });

            _isRevivingWithCoins = false;

            if (!revivedOk)
            {
                if (syncManager != null && !syncManager.IsOnline)
                {
                    _networkGateModal?.Show(reviveErr, isConnecting: false);
                    yield break;
                }

                _lastDefeatRequiresRestart = true;
                ui.ShowResult(
                    "CANNOT REVIVE",
                    $"{reviveErr}\nYour expedition must restart from Stage 1/4.",
                    "RESTART EXPEDITION STAGE 1",
                    defeatColor);
                yield break;
            }

            ProgressManager.SetCoins(remainingCoins);
            ui.HideResult();
            ReviveAndSetupPlayer();
            StartStage(_stageIndex);
        }

        private void OnResultButton()
        {
            if (_isPausePromptActive)
            {
                ResumePausedBattle();
                return;
            }

            BattleAudioManager.Instance?.PlayButtonClick();
            AuthManager.Instance?.RecordUserActivity();
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
                    StartSubjectMonsterExpedition(biomeName, _selectedSubject, resumeCheckpoint: false);
                }
            }
            else if (player.IsDead)
            {
                if (_lastDefeatRequiresRestart || OfflinePracticeManager.IsOfflinePracticeMode)
                {
                    RestartExpeditionFromStage1();
                }
                else
                {
                    int reviveCost = SubjectTierSystem.GetReviveCoinCostForLevel(ProgressManager.GetSubjectLevel(_selectedSubject));
                    StartCoroutine(ReviveWithCoinsRoutine(reviveCost));
                }
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

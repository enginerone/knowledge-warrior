using System;
using System.Collections.Generic;
using KnowledgeWarrior.Progress;
using UnityEngine;

namespace KnowledgeWarrior.Audio
{
    /// <summary>
    /// Synthesizes calm, focus-oriented, vibration-free background music per subject biome and badge tier,
    /// along with gentle harmonic sound effects and persistent Music / Sound Effects settings.
    /// All generated waveforms are high-passed above 210 Hz and low-passed at 3.4 kHz so smartphone
    /// speakers never rattle, buzz, or cause listening fatigue during question reading.
    /// </summary>
    public class BattleAudioManager : MonoBehaviour
    {
        private const string MusicEnabledPrefKey = "kw_settings_music_enabled";
        private const string SfxEnabledPrefKey = "kw_settings_sfx_enabled";
        private const string MusicVolumePrefKey = "kw_settings_music_volume";
        private const string SfxVolumePrefKey = "kw_settings_sfx_volume";

        public const float DefaultMusicVolume = 0.18f;
        public const float DefaultSfxVolume = 0.45f;

        private const int SampleRate = 44100;

        public static BattleAudioManager Instance { get; private set; }

        [Header("Default Volume Controls")]
        [Range(0f, 1f)] [SerializeField] private float musicVolume = DefaultMusicVolume;
        [Range(0f, 1f)] [SerializeField] private float sfxVolume = DefaultSfxVolume;

        private AudioSource _bgmSource;
        private AudioSource _sfxSource;
        private readonly Dictionary<string, AudioClip> _bgmCache = new();

        private AudioClip _heroSlashClip;
        private AudioClip _heroCritClip;
        private AudioClip _heroBlockClip;
        private AudioClip _monsterAttackClip;
        private AudioClip _bossRoarClip;
        private AudioClip _hitImpactClip;
        private AudioClip _missWhooshClip;
        private AudioClip _victoryClip;
        private AudioClip _defeatClip;
        private AudioClip _clickClip;

        public bool MusicEnabled { get; private set; } = true;
        public bool SfxEnabled { get; private set; } = true;
        public float MusicVolume => musicVolume;
        public float SfxVolume => sfxVolume;
        public bool IsMuted => !MusicEnabled && !SfxEnabled;

        public event Action OnAudioSettingsChanged;

        public static BattleAudioManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("BattleAudioManager");
            DontDestroyOnLoad(go);
            return go.AddComponent<BattleAudioManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _bgmSource = gameObject.AddComponent<AudioSource>();
            _bgmSource.loop = true;
            _bgmSource.playOnAwake = false;

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.loop = false;
            _sfxSource.playOnAwake = false;

            LoadSavedAudioSettings();
            ApplyAudioSettingsToSources();
            BuildCombatSfxClips();
        }

        public void SetMusicEnabled(bool enabled)
        {
            MusicEnabled = enabled;
            PlayerPrefs.SetInt(MusicEnabledPrefKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
            ApplyAudioSettingsToSources();
            OnAudioSettingsChanged?.Invoke();
        }

        public void ToggleMusic() => SetMusicEnabled(!MusicEnabled);

        public void SetSfxEnabled(bool enabled)
        {
            SfxEnabled = enabled;
            PlayerPrefs.SetInt(SfxEnabledPrefKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
            ApplyAudioSettingsToSources();
            OnAudioSettingsChanged?.Invoke();
        }

        public void ToggleSfx() => SetSfxEnabled(!SfxEnabled);

        public void SetMusicVolume(float volume)
        {
            musicVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(MusicVolumePrefKey, musicVolume);
            if (musicVolume > 0.005f && !MusicEnabled)
            {
                MusicEnabled = true;
                PlayerPrefs.SetInt(MusicEnabledPrefKey, 1);
            }
            PlayerPrefs.Save();
            ApplyAudioSettingsToSources();
            OnAudioSettingsChanged?.Invoke();
        }

        public void SetSfxVolume(float volume)
        {
            sfxVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(SfxVolumePrefKey, sfxVolume);
            if (sfxVolume > 0.005f && !SfxEnabled)
            {
                SfxEnabled = true;
                PlayerPrefs.SetInt(SfxEnabledPrefKey, 1);
            }
            PlayerPrefs.Save();
            ApplyAudioSettingsToSources();
            OnAudioSettingsChanged?.Invoke();
        }

        public void SetAllAudioEnabled(bool enabled)
        {
            MusicEnabled = enabled;
            SfxEnabled = enabled;
            PlayerPrefs.SetInt(MusicEnabledPrefKey, enabled ? 1 : 0);
            PlayerPrefs.SetInt(SfxEnabledPrefKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
            ApplyAudioSettingsToSources();
            OnAudioSettingsChanged?.Invoke();
        }

        public void ResetToDefaults()
        {
            MusicEnabled = true;
            SfxEnabled = true;
            musicVolume = DefaultMusicVolume;
            sfxVolume = DefaultSfxVolume;
            PlayerPrefs.SetInt(MusicEnabledPrefKey, 1);
            PlayerPrefs.SetInt(SfxEnabledPrefKey, 1);
            PlayerPrefs.SetFloat(MusicVolumePrefKey, musicVolume);
            PlayerPrefs.SetFloat(SfxVolumePrefKey, sfxVolume);
            PlayerPrefs.Save();
            ApplyAudioSettingsToSources();
            OnAudioSettingsChanged?.Invoke();
        }

        public void ToggleMute()
        {
            bool shouldEnable = IsMuted;
            SetAllAudioEnabled(shouldEnable);
        }

        public void StopMusic()
        {
            if (_bgmSource != null && _bgmSource.isPlaying)
            {
                _bgmSource.Stop();
            }
        }

        public void PlaySubjectThemeMusic(string subject, BadgeTier tier, bool isBoss = false)
        {
            string key = $"{(subject ?? "default").Trim().ToLowerInvariant()}_{tier}_{isBoss}_v2";
            if (!_bgmCache.TryGetValue(key, out var clip) || clip == null)
            {
                clip = SynthesizeCalmBiomeLoop(subject, tier, isBoss);
                _bgmCache[key] = clip;
            }

            if (_bgmSource.clip != clip)
            {
                _bgmSource.clip = clip;
            }

            _bgmSource.volume = musicVolume;
            _bgmSource.mute = !MusicEnabled;
            if (!_bgmSource.isPlaying)
            {
                _bgmSource.Play();
            }
        }

        public void PlayHeroAttack(bool isCrit)
        {
            PlayOneShot(isCrit ? _heroCritClip : _heroSlashClip, isCrit ? 0.85f : 0.72f);
        }

        public void PlayHeroBlock() => PlayOneShot(_heroBlockClip, 0.75f);

        public void PlayMonsterAttack(bool isBoss)
        {
            PlayOneShot(isBoss ? _bossRoarClip : _monsterAttackClip, isBoss ? 0.78f : 0.68f);
        }

        public void PlayBossEntranceRoar() => PlayOneShot(_bossRoarClip, 0.82f);

        public void PlayHitImpact() => PlayOneShot(_hitImpactClip, 0.65f);

        public void PlayMissWhoosh() => PlayOneShot(_missWhooshClip, 0.55f);

        public void PlayVictoryFanfare() => PlayOneShot(_victoryClip, 0.85f);

        public void PlayDefeatStinger() => PlayOneShot(_defeatClip, 0.75f);

        public void PlayButtonClick() => PlayOneShot(_clickClip, 0.38f);

        private void LoadSavedAudioSettings()
        {
            MusicEnabled = PlayerPrefs.GetInt(MusicEnabledPrefKey, 1) == 1;
            SfxEnabled = PlayerPrefs.GetInt(SfxEnabledPrefKey, 1) == 1;
            musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumePrefKey, DefaultMusicVolume));
            sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumePrefKey, DefaultSfxVolume));
        }

        private void ApplyAudioSettingsToSources()
        {
            if (_bgmSource != null)
            {
                _bgmSource.volume = musicVolume;
                _bgmSource.mute = !MusicEnabled;
            }
            if (_sfxSource != null)
            {
                _sfxSource.volume = sfxVolume;
                _sfxSource.mute = !SfxEnabled;
            }
        }

        private void PlayOneShot(AudioClip clip, float scale)
        {
            if (!SfxEnabled || sfxVolume <= 0.001f || clip == null || _sfxSource == null) return;
            _sfxSource.PlayOneShot(clip, sfxVolume * scale);
        }

        private void BuildCombatSfxClips()
        {
            _clickClip = CreatePluckBellClip("SoftMarimbaTap", 0.045f, new[] { 587.33f, 880.0f }, new[] { 0.75f, 0.25f }, 38f);
            _heroSlashClip = CreateAirChimeClip("HeroArcaneSlash", 0.24f, 440.0f, 659.25f, 0.10f);
            _heroCritClip = CreateArpeggioBellClip("HeroCritChime", new[] { 523.25f, 659.25f, 783.99f, 1174.66f }, 0.065f, 0.26f);
            _heroBlockClip = CreatePluckBellClip("ArcaneWardBell", 0.30f, new[] { 440.0f, 659.25f, 880.0f }, new[] { 0.55f, 0.32f, 0.13f }, 9.5f);
            _monsterAttackClip = CreateSoftPercussionSweep("MonsterStrike", 0.19f, 330.0f, 220.0f, 0.08f);
            _bossRoarClip = CreateWarmBrassSwellClip("BossPresenceSwell", 0.48f, 220.0f, 329.63f, 440.0f);
            _hitImpactClip = CreateSoftPercussionSweep("SoftImpact", 0.14f, 290.0f, 210.0f, 0.06f);
            _missWhooshClip = CreateAirChimeClip("SoftBreezeMiss", 0.18f, 392.0f, 329.63f, 0.12f);
            _victoryClip = CreateArpeggioBellClip("VictoryCelesta", new[] { 523.25f, 659.25f, 783.99f, 1046.50f }, 0.13f, 0.42f);
            _defeatClip = CreateArpeggioBellClip("DefeatCadence", new[] { 440.00f, 392.00f, 349.23f, 329.63f }, 0.16f, 0.40f);
        }

        private static AudioClip SynthesizeCalmBiomeLoop(string subject, BadgeTier tier, bool isBoss)
        {
            BiomeMusicProfile profile = GetBiomeMusicProfile(subject);

            float bpm = profile.baseBpm + tier switch
            {
                BadgeTier.Beginner => 0f,
                BadgeTier.Intermediate => 3f,
                BadgeTier.Advanced => 6f,
                BadgeTier.Expert => 9f,
                _ => 0f
            };
            if (isBoss)
            {
                bpm = Mathf.Max(bpm + 12f, 92f);
            }

            float stepDuration = 60f / bpm * 0.5f;
            const int totalSteps = 32;
            float totalDuration = stepDuration * totalSteps;
            int sampleCount = Mathf.Max(4096, Mathf.RoundToInt(totalDuration * SampleRate));
            float[] raw = new float[sampleCount];

            int[] melodySteps = isBoss ? profile.bossMelody : profile.calmMelody;

            for (int step = 0; step < totalSteps; step++)
            {
                int chordBar = (step / 8) % 4;
                if (step % 8 == 0)
                {
                    int rootIdx = profile.chordRoots[chordBar] % profile.scale.Length;
                    int thirdIdx = (rootIdx + 2) % profile.scale.Length;
                    int fifthIdx = (rootIdx + 4) % profile.scale.Length;

                    float padDuration = stepDuration * 8f;
                    AddWarmPadChord(
                        raw,
                        Mathf.RoundToInt(step * stepDuration * SampleRate),
                        Mathf.RoundToInt(padDuration * SampleRate),
                        profile.scale[rootIdx],
                        profile.scale[thirdIdx],
                        profile.scale[fifthIdx],
                        isBoss ? 0.085f : 0.072f);
                }

                int noteDeg = melodySteps[step % melodySteps.Length];
                if (noteDeg >= 0)
                {
                    float noteFreq = profile.scale[noteDeg % profile.scale.Length];
                    if (noteDeg >= profile.scale.Length)
                    {
                        noteFreq *= 2f;
                    }

                    float pluckLen = stepDuration * (isBoss ? 1.55f : 2.15f);
                    float velocity = (step % 4 == 0) ? 0.20f : 0.145f;
                    AddHarpPluckNote(
                        raw,
                        Mathf.RoundToInt(step * stepDuration * SampleRate),
                        Mathf.RoundToInt(pluckLen * SampleRate),
                        noteFreq,
                        velocity,
                        profile.overtoneMix);

                    int echoStart = Mathf.RoundToInt((step + 1.5f) * stepDuration * SampleRate) % sampleCount;
                    AddHarpPluckNote(
                        raw,
                        echoStart,
                        Mathf.RoundToInt(stepDuration * 1.35f * SampleRate),
                        noteFreq,
                        velocity * 0.28f,
                        profile.overtoneMix * 0.5f);
                }
            }

            ApplyMobileSpeakerConditioning(raw, highPassHz: 215f, lowPassHz: 3300f, maxPeak: 0.55f);
            ApplySeamlessLoopCrossfade(raw, Mathf.RoundToInt(0.06f * SampleRate));

            var clip = AudioClip.Create($"BGM_{subject}_{tier}_{isBoss}", sampleCount, 1, SampleRate, false);
            clip.SetData(raw, 0);
            return clip;
        }

        private static void AddWarmPadChord(
            float[] buffer,
            int startSample,
            int lengthSamples,
            float f1,
            float f2,
            float f3,
            float gain)
        {
            int total = buffer.Length;
            if (total == 0 || lengthSamples <= 0) return;

            for (int i = 0; i < lengthSamples; i++)
            {
                float k = (float)i / lengthSamples;
                float t = (float)i / SampleRate;
                float env = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI);
                env *= env;

                float sample =
                    Mathf.Sin(2f * Mathf.PI * f1 * t) * 0.42f +
                    Mathf.Sin(2f * Mathf.PI * f2 * t) * 0.32f +
                    Mathf.Sin(2f * Mathf.PI * f3 * t) * 0.26f;

                int idx = (startSample + i) % total;
                buffer[idx] += sample * env * gain;
            }
        }

        private static void AddHarpPluckNote(
            float[] buffer,
            int startSample,
            int lengthSamples,
            float freq,
            float velocity,
            float overtoneMix)
        {
            int total = buffer.Length;
            if (total == 0 || lengthSamples <= 0) return;

            int attackSamples = Mathf.Max(32, Mathf.RoundToInt(0.018f * SampleRate));
            int releaseSamples = Mathf.Max(64, Mathf.RoundToInt(0.035f * SampleRate));

            for (int i = 0; i < lengthSamples; i++)
            {
                float t = (float)i / SampleRate;
                float attack = i < attackSamples
                    ? 0.5f * (1f - Mathf.Cos(Mathf.PI * i / attackSamples))
                    : 1f;
                float decay = Mathf.Exp(-t * 3.8f);
                float tail = i > lengthSamples - releaseSamples
                    ? (float)(lengthSamples - i) / releaseSamples
                    : 1f;

                float fundamental = Mathf.Sin(2f * Mathf.PI * freq * t);
                float secondHarmonic = Mathf.Sin(2f * Mathf.PI * (freq * 2f) * t) * Mathf.Exp(-t * 6.5f);
                float thirdHarmonic = Mathf.Sin(2f * Mathf.PI * (freq * 3f) * t) * Mathf.Exp(-t * 9.5f);

                float wave = fundamental * (1f - overtoneMix * 0.35f)
                           + secondHarmonic * overtoneMix
                           + thirdHarmonic * (overtoneMix * 0.22f);

                int idx = (startSample + i) % total;
                buffer[idx] += wave * attack * decay * tail * velocity;
            }
        }

        private struct BiomeMusicProfile
        {
            public float[] scale;
            public int[] chordRoots;
            public int[] calmMelody;
            public int[] bossMelody;
            public float baseBpm;
            public float overtoneMix;
        }

        private static BiomeMusicProfile GetBiomeMusicProfile(string subject)
        {
            string key = (subject ?? string.Empty).Trim().ToLowerInvariant();
            return key switch
            {
                "programming" => new BiomeMusicProfile
                {
                    scale = new[] { 261.63f, 293.66f, 329.63f, 369.99f, 392.00f, 440.00f, 493.88f, 523.25f },
                    chordRoots = new[] { 0, 4, 5, 1 },
                    calmMelody = new[] { 0, -1, 2, 4, -1, 3, 2, -1, 4, -1, 5, 7, -1, 4, 2, -1, 5, -1, 4, 2, -1, 1, 0, -1, 1, 2, 4, -1, 3, -1, 2, -1 },
                    bossMelody = new[] { 0, 4, 2, 4, 7, 4, 3, 2, 4, 7, 5, 4, 2, 4, 1, -1, 5, 2, 4, 5, 7, 5, 4, 2, 1, 3, 4, 5, 4, 2, 1, -1 },
                    baseBpm = 76f,
                    overtoneMix = 0.26f
                },
                "web development" => new BiomeMusicProfile
                {
                    scale = new[] { 329.63f, 369.99f, 415.30f, 440.00f, 493.88f, 554.37f, 622.25f, 659.25f },
                    chordRoots = new[] { 0, 4, 5, 3 },
                    calmMelody = new[] { 0, 2, -1, 4, -1, 2, 1, -1, 4, -1, 5, 4, -1, 2, -1, -1, 5, 4, -1, 2, 0, -1, 1, -1, 3, -1, 4, 2, -1, 1, 0, -1 },
                    bossMelody = new[] { 0, 2, 4, 2, 5, 4, 2, 1, 4, 5, 7, 5, 4, 2, 4, -1, 5, 2, 4, 5, 4, 2, 1, 2, 3, 4, 5, 4, 2, 1, 0, -1 },
                    baseBpm = 78f,
                    overtoneMix = 0.22f
                },
                "networking" => new BiomeMusicProfile
                {
                    scale = new[] { 293.66f, 329.63f, 349.23f, 392.00f, 440.00f, 493.88f, 523.25f, 587.33f },
                    chordRoots = new[] { 0, 3, 5, 4 },
                    calmMelody = new[] { 0, -1, 4, -1, 2, 3, -1, -1, 3, -1, 5, 4, -1, 2, -1, -1, 5, -1, 7, -1, 5, 4, 2, -1, 4, -1, 3, 2, -1, 1, 0, -1 },
                    bossMelody = new[] { 0, 4, 3, 4, 6, 4, 2, 0, 3, 5, 7, 5, 4, 3, 2, -1, 5, 4, 2, 4, 6, 5, 4, 2, 4, 2, 3, 4, 2, 1, 0, -1 },
                    baseBpm = 80f,
                    overtoneMix = 0.30f
                },
                "databases" => new BiomeMusicProfile
                {
                    scale = new[] { 246.94f, 277.18f, 293.66f, 329.63f, 369.99f, 392.00f, 440.00f, 493.88f },
                    chordRoots = new[] { 0, 5, 2, 4 },
                    calmMelody = new[] { 0, 2, 4, -1, 2, -1, 0, -1, 5, -1, 4, 2, -1, 3, -1, -1, 2, 4, 6, -1, 4, 2, -1, -1, 4, -1, 3, 2, -1, 1, 0, -1 },
                    bossMelody = new[] { 0, 2, 4, 6, 4, 2, 3, 4, 5, 4, 2, 5, 4, 3, 2, -1, 2, 4, 6, 7, 6, 4, 2, 0, 4, 3, 2, 4, 3, 1, 0, -1 },
                    baseBpm = 72f,
                    overtoneMix = 0.20f
                },
                "cybersecurity" => new BiomeMusicProfile
                {
                    scale = new[] { 293.66f, 329.63f, 349.23f, 392.00f, 440.00f, 466.16f, 523.25f, 587.33f },
                    chordRoots = new[] { 0, 2, 4, 3 },
                    calmMelody = new[] { 0, -1, 3, -1, 2, -1, 4, -1, 2, -1, 5, 4, -1, 3, -1, -1, 4, -1, 6, 5, -1, 4, 3, -1, 3, -1, 2, 1, -1, 0, -1, -1 },
                    bossMelody = new[] { 0, 3, 2, 4, 5, 4, 3, 2, 2, 5, 4, 6, 5, 4, 3, -1, 4, 6, 5, 4, 3, 2, 4, 3, 3, 2, 1, 3, 2, 1, 0, -1 },
                    baseBpm = 82f,
                    overtoneMix = 0.24f
                },
                "computer systems" => new BiomeMusicProfile
                {
                    scale = new[] { 261.63f, 293.66f, 311.13f, 349.23f, 392.00f, 415.30f, 466.16f, 523.25f },
                    chordRoots = new[] { 0, 3, 4, 2 },
                    calmMelody = new[] { 0, 2, -1, 4, -1, 3, 2, -1, 3, -1, 5, 4, -1, 2, -1, -1, 4, 6, -1, 4, 3, -1, 2, -1, 2, -1, 4, 3, -1, 2, 0, -1 },
                    bossMelody = new[] { 0, 2, 4, 3, 4, 6, 4, 2, 3, 5, 6, 5, 4, 3, 2, -1, 4, 6, 7, 6, 4, 3, 2, 4, 2, 4, 3, 2, 1, 2, 0, -1 },
                    baseBpm = 76f,
                    overtoneMix = 0.25f
                },
                _ => new BiomeMusicProfile
                {
                    scale = new[] { 261.63f, 293.66f, 329.63f, 392.00f, 440.00f, 523.25f, 587.33f, 659.25f },
                    chordRoots = new[] { 0, 3, 4, 2 },
                    calmMelody = new[] { 0, -1, 2, 3, -1, 4, 2, -1, 3, -1, 5, 4, -1, 3, -1, -1, 4, -1, 5, 3, -1, 2, 1, -1, 2, -1, 3, 2, -1, 1, 0, -1 },
                    bossMelody = new[] { 0, 2, 3, 4, 5, 4, 3, 2, 3, 4, 5, 6, 5, 4, 3, -1, 4, 5, 6, 5, 4, 3, 2, 3, 2, 3, 4, 3, 2, 1, 0, -1 },
                    baseBpm = 76f,
                    overtoneMix = 0.24f
                }
            };
        }

        private static AudioClip CreatePluckBellClip(string name, float duration, float[] freqs, float[] weights, float decayRate)
        {
            int samples = Mathf.Max(256, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[samples];
            int attackSamples = Mathf.Max(24, Mathf.RoundToInt(0.008f * SampleRate));

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float attack = i < attackSamples ? 0.5f * (1f - Mathf.Cos(Mathf.PI * i / attackSamples)) : 1f;
                float env = attack * Mathf.Exp(-t * decayRate);

                float sum = 0f;
                for (int f = 0; f < freqs.Length; f++)
                {
                    float w = f < weights.Length ? weights[f] : 0.3f;
                    sum += Mathf.Sin(2f * Mathf.PI * freqs[f] * t) * w;
                }
                data[i] = sum * env;
            }

            ApplyMobileSpeakerConditioning(data, highPassHz: 220f, lowPassHz: 3600f, maxPeak: 0.72f);
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateAirChimeClip(string name, float duration, float fStart, float fEnd, float airMix)
        {
            int samples = Mathf.Max(256, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[samples];
            var rng = new System.Random(name.GetHashCode());
            float phase = 0f;
            float smoothNoise = 0f;

            for (int i = 0; i < samples; i++)
            {
                float k = (float)i / samples;
                float env = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * Mathf.Pow(1f - k, 0.8f);
                float freq = Mathf.Lerp(fStart, fEnd, k);
                phase += freq / SampleRate;

                float chime = Mathf.Sin(2f * Mathf.PI * phase) * 0.72f +
                              Mathf.Sin(4f * Mathf.PI * phase) * 0.22f;
                float white = (float)rng.NextDouble() * 2f - 1f;
                smoothNoise = Mathf.Lerp(smoothNoise, white, 0.18f);

                data[i] = (chime * (1f - airMix) + smoothNoise * airMix) * env;
            }

            ApplyMobileSpeakerConditioning(data, highPassHz: 230f, lowPassHz: 3400f, maxPeak: 0.70f);
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateSoftPercussionSweep(string name, float duration, float startFreq, float endFreq, float softAirMix)
        {
            int samples = Mathf.Max(256, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[samples];
            var rng = new System.Random(name.GetHashCode());
            float phase = 0f;
            float smoothNoise = 0f;
            int attackSamples = Mathf.Max(24, Mathf.RoundToInt(0.010f * SampleRate));

            for (int i = 0; i < samples; i++)
            {
                float k = (float)i / samples;
                float attack = i < attackSamples ? 0.5f * (1f - Mathf.Cos(Mathf.PI * i / attackSamples)) : 1f;
                float env = attack * Mathf.Pow(1f - k, 2.1f);
                float freq = Mathf.Lerp(startFreq, endFreq, k);
                phase += freq / SampleRate;

                float drumTone = Mathf.Sin(2f * Mathf.PI * phase);
                float white = (float)rng.NextDouble() * 2f - 1f;
                smoothNoise = Mathf.Lerp(smoothNoise, white, 0.14f);

                data[i] = (drumTone * (1f - softAirMix) + smoothNoise * softAirMix) * env;
            }

            ApplyMobileSpeakerConditioning(data, highPassHz: 215f, lowPassHz: 2800f, maxPeak: 0.68f);
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateWarmBrassSwellClip(string name, float duration, float fRoot, float fFifth, float fOctave)
        {
            int samples = Mathf.Max(512, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float k = (float)i / samples;
                float env = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI);

                float chord =
                    Mathf.Sin(2f * Mathf.PI * fRoot * t) * 0.45f +
                    Mathf.Sin(2f * Mathf.PI * fFifth * t) * 0.35f +
                    Mathf.Sin(2f * Mathf.PI * fOctave * t) * 0.20f;

                data[i] = chord * env;
            }

            ApplyMobileSpeakerConditioning(data, highPassHz: 215f, lowPassHz: 2600f, maxPeak: 0.68f);
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateArpeggioBellClip(string name, float[] notes, float stepSeconds, float noteTailSeconds)
        {
            float totalDuration = stepSeconds * notes.Length + noteTailSeconds;
            int totalSamples = Mathf.Max(512, Mathf.RoundToInt(totalDuration * SampleRate));
            float[] data = new float[totalSamples];

            for (int n = 0; n < notes.Length; n++)
            {
                int startSample = Mathf.RoundToInt(n * stepSeconds * SampleRate);
                int lengthSamples = Mathf.Min(totalSamples - startSample, Mathf.RoundToInt((stepSeconds + noteTailSeconds) * SampleRate));
                AddHarpPluckNote(data, startSample, lengthSamples, notes[n], 0.34f, 0.22f);
            }

            ApplyMobileSpeakerConditioning(data, highPassHz: 220f, lowPassHz: 3500f, maxPeak: 0.72f);
            var clip = AudioClip.Create(name, totalSamples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static void ApplyMobileSpeakerConditioning(float[] samples, float highPassHz, float lowPassHz, float maxPeak)
        {
            if (samples == null || samples.Length == 0) return;

            float dt = 1f / SampleRate;
            float rcHigh = 1f / (2f * Mathf.PI * highPassHz);
            float alphaHigh = rcHigh / (rcHigh + dt);

            float prevIn = samples[0];
            float prevHp = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float x = samples[i];
                float hp = alphaHigh * (prevHp + x - prevIn);
                prevIn = x;
                prevHp = hp;
                samples[i] = hp;
            }

            float rcLow = 1f / (2f * Mathf.PI * lowPassHz);
            float alphaLow = dt / (rcLow + dt);
            float lp = samples[0];
            float peak = 0f;

            for (int i = 0; i < samples.Length; i++)
            {
                lp += alphaLow * (samples[i] - lp);
                samples[i] = lp;
                float abs = Mathf.Abs(lp);
                if (abs > peak) peak = abs;
            }

            if (peak > maxPeak && peak > 0.0001f)
            {
                float scale = maxPeak / peak;
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] *= scale;
                }
            }
        }

        private static void ApplySeamlessLoopCrossfade(float[] samples, int fadeSamples)
        {
            if (samples == null || samples.Length < fadeSamples * 4 || fadeSamples <= 0) return;

            int n = samples.Length;
            for (int i = 0; i < fadeSamples; i++)
            {
                float k = (float)i / fadeSamples;
                int tailIdx = n - fadeSamples + i;
                float blended = Mathf.Lerp(samples[tailIdx], samples[i], k);
                samples[i] = blended;
                samples[tailIdx] = Mathf.Lerp(samples[tailIdx], samples[0], k * 0.5f);
            }
        }
    }
}

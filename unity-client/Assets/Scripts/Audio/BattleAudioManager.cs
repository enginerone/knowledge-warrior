using System.Collections.Generic;
using KnowledgeWarrior.Progress;
using UnityEngine;

namespace KnowledgeWarrior.Audio
{
    /// <summary>
    /// Synthesizes and plays thematic background music per subject and tier,
    /// plus combat sound effects for hero attacks, monster gestures, blocks, and UI events.
    /// </summary>
    public class BattleAudioManager : MonoBehaviour
    {
        public static BattleAudioManager Instance { get; private set; }

        [Header("Volume Controls")]
        [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.24f;
        [Range(0f, 1f)] [SerializeField] private float sfxVolume = 0.55f;

        private const int SampleRate = 22050;

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

        public bool IsMuted { get; private set; }

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
            _bgmSource.volume = musicVolume;

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.loop = false;
            _sfxSource.playOnAwake = false;
            _sfxSource.volume = sfxVolume;

            BuildCombatSfxClips();
        }

        public void ToggleMute()
        {
            IsMuted = !IsMuted;
            _bgmSource.mute = IsMuted;
            _sfxSource.mute = IsMuted;
        }

        public void PlaySubjectThemeMusic(string subject, BadgeTier tier, bool isBossOrPvp = false)
        {
            string key = $"{(subject ?? "default").ToLowerInvariant()}_{tier}_{isBossOrPvp}";
            if (!_bgmCache.TryGetValue(key, out var clip) || clip == null)
            {
                clip = SynthesizeThemeLoop(subject, tier, isBossOrPvp);
                _bgmCache[key] = clip;
            }

            if (_bgmSource.clip == clip && _bgmSource.isPlaying) return;
            _bgmSource.clip = clip;
            _bgmSource.volume = musicVolume;
            _bgmSource.Play();
        }

        public void PlayHeroAttack(bool isCrit)
        {
            PlayOneShot(isCrit ? _heroCritClip : _heroSlashClip, isCrit ? 1.0f : 0.85f);
        }

        public void PlayHeroBlock() => PlayOneShot(_heroBlockClip, 0.9f);

        public void PlayMonsterAttack(bool isBoss)
        {
            PlayOneShot(isBoss ? _bossRoarClip : _monsterAttackClip, isBoss ? 1.0f : 0.85f);
        }

        public void PlayBossEntranceRoar() => PlayOneShot(_bossRoarClip, 1.0f);

        public void PlayHitImpact() => PlayOneShot(_hitImpactClip, 0.9f);

        public void PlayMissWhoosh() => PlayOneShot(_missWhooshClip, 0.7f);

        public void PlayVictoryFanfare() => PlayOneShot(_victoryClip, 0.95f);

        public void PlayDefeatStinger() => PlayOneShot(_defeatClip, 0.9f);

        public void PlayButtonClick() => PlayOneShot(_clickClip, 0.5f);

        private void PlayOneShot(AudioClip clip, float scale)
        {
            if (IsMuted || clip == null || _sfxSource == null) return;
            _sfxSource.PlayOneShot(clip, sfxVolume * scale);
        }

        private void BuildCombatSfxClips()
        {
            _heroSlashClip = CreateSweepClip("HeroSlash", 0.24f, 520f, 180f, 0.45f, true);
            _heroCritClip = CreateChordBurstClip("HeroCrit", 0.36f, new[] { 440f, 660f, 880f, 1174f });
            _heroBlockClip = CreateMetallicClangClip("HeroBlock", 0.28f, 780f, 1120f);
            _monsterAttackClip = CreateSweepClip("MonsterSwipe", 0.28f, 240f, 95f, 0.55f, true);
            _bossRoarClip = CreateBossRoarClip("BossRoar", 0.55f);
            _hitImpactClip = CreateSweepClip("HitImpact", 0.18f, 190f, 55f, 0.7f, true);
            _missWhooshClip = CreateSweepClip("MissWhoosh", 0.20f, 340f, 620f, 0.25f, true);
            _victoryClip = CreateArpeggioClip("VictoryFanfare", new[] { 523.25f, 659.25f, 783.99f, 1046.50f }, 0.14f);
            _defeatClip = CreateArpeggioClip("DefeatStinger", new[] { 392.00f, 349.23f, 311.13f, 261.63f }, 0.18f);
            _clickClip = CreateSweepClip("ButtonClick", 0.05f, 880f, 1320f, 0.1f, false);
        }

        private static AudioClip SynthesizeThemeLoop(string subject, BadgeTier tier, bool isBossOrPvp)
        {
            float[] scale = GetSubjectScaleFrequencies(subject);
            float tempoMultiplier = tier switch
            {
                BadgeTier.Beginner => 1.0f,
                BadgeTier.Intermediate => 1.12f,
                BadgeTier.Advanced => 1.24f,
                BadgeTier.Expert => 1.38f,
                _ => 1.0f
            };
            if (isBossOrPvp) tempoMultiplier *= 1.15f;

            float stepDuration = 0.24f / tempoMultiplier;
            int totalSteps = 16;
            float duration = stepDuration * totalSteps;
            int sampleCount = Mathf.Max(1024, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[sampleCount];

            int[] pattern = isBossOrPvp
                ? new[] { 0, 3, 2, 4, 1, 4, 3, 5, 0, 3, 4, 5, 2, 4, 1, 0 }
                : new[] { 0, 2, 4, 2, 1, 3, 4, 3, 0, 2, 5, 4, 3, 2, 1, 2 };

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / SampleRate;
                int step = Mathf.Min(totalSteps - 1, Mathf.FloorToInt(t / stepDuration));
                float localT = (t - step * stepDuration) / stepDuration;
                float env = Mathf.Sin(Mathf.Clamp01(localT) * Mathf.PI);

                float leadFreq = scale[pattern[step] % scale.Length];
                float bassFreq = scale[0] * 0.5f;

                float lead = Mathf.Sin(2f * Mathf.PI * leadFreq * t) * 0.28f;
                float harmonic = Mathf.Sin(2f * Mathf.PI * (leadFreq * 1.5f) * t) * (tier >= BadgeTier.Advanced ? 0.14f : 0.06f);
                float bass = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * bassFreq * t)) * 0.12f;

                data[i] = Mathf.Clamp((lead + harmonic) * env + bass * 0.6f, -0.8f, 0.8f);
            }

            var clip = AudioClip.Create($"BGM_{subject}_{tier}", sampleCount, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float[] GetSubjectScaleFrequencies(string subject)
        {
            string key = (subject ?? string.Empty).Trim().ToLowerInvariant();
            return key switch
            {
                "programming" => new[] { 261.63f, 293.66f, 329.63f, 392.00f, 440.00f, 523.25f },
                "databases" => new[] { 220.00f, 261.63f, 293.66f, 329.63f, 392.00f, 440.00f },
                "networking" => new[] { 293.66f, 329.63f, 369.99f, 440.00f, 493.88f, 587.33f },
                "web development" => new[] { 329.63f, 369.99f, 415.30f, 493.88f, 554.37f, 659.25f },
                "cybersecurity" => new[] { 196.00f, 233.08f, 261.63f, 277.18f, 311.13f, 392.00f },
                "computer systems" => new[] { 246.94f, 277.18f, 329.63f, 369.99f, 440.00f, 493.88f },
                _ => new[] { 261.63f, 311.13f, 349.23f, 392.00f, 466.16f, 523.25f }
            };
        }

        private static AudioClip CreateSweepClip(string name, float duration, float startFreq, float endFreq, float noiseMix, bool punch)
        {
            int samples = Mathf.Max(256, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[samples];
            float phase = 0f;
            var rng = new System.Random(name.GetHashCode());

            for (int i = 0; i < samples; i++)
            {
                float k = (float)i / samples;
                float env = punch ? Mathf.Pow(1f - k, 1.6f) : Mathf.Sin(k * Mathf.PI);
                float freq = Mathf.Lerp(startFreq, endFreq, k);
                phase += freq / SampleRate;
                float tone = Mathf.Sin(phase * 2f * Mathf.PI);
                float noise = ((float)rng.NextDouble() * 2f - 1f);
                data[i] = Mathf.Clamp((tone * (1f - noiseMix) + noise * noiseMix) * env, -0.95f, 0.95f);
            }

            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateChordBurstClip(string name, float duration, float[] freqs)
        {
            int samples = Mathf.Max(256, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float k = (float)i / samples;
                float env = Mathf.Pow(1f - k, 1.3f);
                float sum = 0f;
                for (int f = 0; f < freqs.Length; f++)
                {
                    sum += Mathf.Sin(2f * Mathf.PI * freqs[f] * t);
                }
                data[i] = Mathf.Clamp((sum / freqs.Length) * env * 0.9f, -0.95f, 0.95f);
            }
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateMetallicClangClip(string name, float duration, float f1, float f2)
        {
            int samples = Mathf.Max(256, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float k = (float)i / samples;
                float env = Mathf.Pow(1f - k, 2.0f);
                float s = (Mathf.Sin(2f * Mathf.PI * f1 * t) + 0.7f * Mathf.Sin(2f * Mathf.PI * f2 * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * (f1 * 1.41f) * t)) * 0.45f;
                data[i] = Mathf.Clamp(s * env, -0.95f, 0.95f);
            }
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateBossRoarClip(string name, float duration)
        {
            int samples = Mathf.Max(512, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[samples];
            var rng = new System.Random(1337);
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float k = (float)i / samples;
                float env = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI);
                float growl = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(115f, 68f, k) * t) +
                              0.5f * Mathf.Sin(2f * Mathf.PI * 142f * t);
                float grit = ((float)rng.NextDouble() * 2f - 1f) * 0.35f;
                data[i] = Mathf.Clamp((growl * 0.55f + grit) * env, -0.95f, 0.95f);
            }
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateArpeggioClip(string name, float[] notes, float noteLength)
        {
            int noteSamples = Mathf.Max(128, Mathf.RoundToInt(noteLength * SampleRate));
            int totalSamples = noteSamples * notes.Length;
            float[] data = new float[totalSamples];

            for (int n = 0; n < notes.Length; n++)
            {
                float freq = notes[n];
                for (int i = 0; i < noteSamples; i++)
                {
                    float t = (float)i / SampleRate;
                    float k = (float)i / noteSamples;
                    float env = Mathf.Pow(1f - k * 0.7f, 1.2f);
                    float wave = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.65f +
                                 Mathf.Sin(2f * Mathf.PI * (freq * 2f) * t) * 0.25f;
                    data[n * noteSamples + i] = Mathf.Clamp(wave * env, -0.95f, 0.95f);
                }
            }

            var clip = AudioClip.Create(name, totalSamples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}


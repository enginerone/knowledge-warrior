using System;
using System.Collections;
using UnityEngine;

namespace KnowledgeWarrior.Combat
{
    /// <summary>
    /// A fighter on the battlefield (Hero, Monster, Boss, or 1v1 Real-User Challenger).
    /// Performs code-driven action postures, attack gestures, slash/projectile visual effects,
    /// shield blocks, hit recoils, and death animations.
    /// </summary>
    public class CombatUnit : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SpriteRenderer spriteRenderer;

        [Header("Feel")]
        [SerializeField] private float idleBobAmplitude = 0.09f;
        [SerializeField] private float idleBobSpeed = 2.4f;
        [SerializeField] private float lungeDistance = 2.3f;
        [SerializeField] private Color hitFlashColor = new(1f, 0.35f, 0.35f, 1f);
        [SerializeField] private Color blockFlashColor = new(0.4f, 0.85f, 1f, 1f);

        public string DisplayName { get; private set; }
        public int MaxHp { get; private set; }
        public int CurrentHp { get; private set; }
        public int Attack { get; private set; }
        public AttackGestureType GestureType { get; private set; } = AttackGestureType.HeroBladeSlash;
        public bool IsDead => CurrentHp <= 0;

        /// <summary>Fires with (currentHp, maxHp) whenever health changes.</summary>
        public event Action<int, int> OnHealthChanged;

        private Transform _visual;
        private Vector3 _homePosition;
        private Vector3 _visualBaseScale;
        private Color _baseColor = Color.white;
        private float _shakeOffset;
        private float _bobTime;
        private bool _idle = true;
        private LineRenderer _gestureFxLine;

        public SpriteRenderer Renderer => spriteRenderer;

        /// <summary>World position slightly above the sprite for floating damage popups.</summary>
        public Vector3 PopupAnchor => spriteRenderer != null
            ? new Vector3(spriteRenderer.bounds.center.x, spriteRenderer.bounds.max.y, 0f)
            : transform.position + Vector3.up * 2f;

        private void Awake()
        {
            if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            _visual = spriteRenderer != null ? spriteRenderer.transform : transform;
            _visualBaseScale = _visual.localScale;
            _homePosition = transform.position;
            _bobTime = UnityEngine.Random.value * 10f;
            EnsureGestureFxRenderer();
        }

        public void Setup(
            string displayName,
            int maxHp,
            int attack,
            Sprite sprite = null,
            Color? tint = null,
            float worldHeight = 0f,
            AttackGestureType gestureType = AttackGestureType.HeroBladeSlash)
        {
            StopAllCoroutines();
            DisplayName = displayName;
            MaxHp = Mathf.Max(1, maxHp);
            CurrentHp = MaxHp;
            Attack = attack;
            GestureType = gestureType;

            if (spriteRenderer != null)
            {
                if (sprite != null) spriteRenderer.sprite = sprite;
                _baseColor = tint ?? Color.white;
                spriteRenderer.color = _baseColor;

                var current = spriteRenderer.sprite;
                if (worldHeight > 0f && current != null && current.bounds.size.y > 0f)
                {
                    _visualBaseScale = Vector3.one * (worldHeight / current.bounds.size.y);
                }
            }

            transform.position = _homePosition;
            _visual.localScale = _visualBaseScale;
            _visual.localRotation = Quaternion.identity;
            _visual.gameObject.SetActive(true);
            if (_gestureFxLine != null) _gestureFxLine.enabled = false;
            _shakeOffset = 0f;
            _idle = true;
            OnHealthChanged?.Invoke(CurrentHp, MaxHp);
        }

        /// <summary>Updates stats during a run after a level up while preserving the current health ratio.</summary>
        public void UpdateStats(int maxHp, int attack, bool fullHeal)
        {
            float ratio = MaxHp > 0 ? (float)CurrentHp / MaxHp : 1f;
            MaxHp = Mathf.Max(1, maxHp);
            Attack = attack;
            CurrentHp = fullHeal ? MaxHp : Mathf.Max(1, Mathf.RoundToInt(MaxHp * ratio));
            OnHealthChanged?.Invoke(CurrentHp, MaxHp);
        }

        public void Heal(int amount)
        {
            if (IsDead || amount <= 0) return;
            CurrentHp = Mathf.Min(MaxHp, CurrentHp + amount);
            OnHealthChanged?.Invoke(CurrentHp, MaxHp);
        }

        public void TakeDamage(int amount)
        {
            if (IsDead) return;
            CurrentHp = Mathf.Max(0, CurrentHp - Mathf.Max(0, amount));
            OnHealthChanged?.Invoke(CurrentHp, MaxHp);
            StartCoroutine(FlashRoutine(hitFlashColor, 0.25f));
            StartCoroutine(ShakeRoutine(0.32f, 0.20f));
            StartCoroutine(HitRecoilGestureRoutine());
        }

        public void PlayBlock()
        {
            StartCoroutine(FlashRoutine(blockFlashColor, 0.35f));
            StartCoroutine(PunchScaleRoutine(1.12f, 0.26f));
            StartCoroutine(PlayShieldBarrierEffectRoutine());
        }

        /// <summary>
        /// Performs the fighter's distinct wind-up posture, attack gesture, weapon/spell visual arc,
        /// calls <paramref name="onImpact"/> at the impact frame, and returns to the ready stance.
        /// </summary>
        public IEnumerator AttackRoutine(CombatUnit target, Action onImpact)
        {
            _idle = false;
            Vector3 start = _homePosition;
            float dir = target != null ? Mathf.Sign(target.transform.position.x - start.x) : 1f;
            Vector3 targetPos = target != null ? target.transform.position : start + Vector3.right * (dir * 3.5f);

            Vector3 windup = start + new Vector3(-dir * 0.45f, 0.08f, 0f);
            Vector3 strike = start + new Vector3(dir * lungeDistance, 0f, 0f);

            // 1. Wind-up gesture (lean back and gather energy)
            yield return MoveAndPose(
                start,
                windup,
                0f,
                dir * 14f,
                _visualBaseScale,
                new Vector3(_visualBaseScale.x * 0.92f, _visualBaseScale.y * 1.10f, 1f),
                0.14f,
                EaseOutQuad);

            // 2. Execute gesture-specific lunge or leap
            if (GestureType == AttackGestureType.MonsterCrushSlam)
            {
                Vector3 apex = Vector3.Lerp(windup, strike, 0.55f) + Vector3.up * 1.35f;
                yield return MoveAndPose(
                    windup,
                    apex,
                    dir * 14f,
                    -dir * 8f,
                    _visual.localScale,
                    _visualBaseScale * 1.16f,
                    0.12f,
                    EaseOutQuad);
                yield return MoveAndPose(
                    apex,
                    strike,
                    -dir * 8f,
                    -dir * 22f,
                    _visual.localScale,
                    new Vector3(_visualBaseScale.x * 1.18f, _visualBaseScale.y * 0.86f, 1f),
                    0.09f,
                    EaseInQuad);
            }
            else if (GestureType == AttackGestureType.BossInfernoBreath)
            {
                Vector3 roarPos = start + new Vector3(dir * 0.85f, 0.35f, 0f);
                yield return MoveAndPose(
                    windup,
                    roarPos,
                    dir * 14f,
                    -dir * 16f,
                    _visual.localScale,
                    _visualBaseScale * 1.22f,
                    0.14f,
                    EaseInQuad);
                strike = roarPos;
            }
            else
            {
                yield return MoveAndPose(
                    windup,
                    strike,
                    dir * 14f,
                    -dir * 20f,
                    _visual.localScale,
                    new Vector3(_visualBaseScale.x * 1.14f, _visualBaseScale.y * 0.94f, 1f),
                    0.10f,
                    EaseInQuad);
            }

            // 3. Spawn slash / projectile / breath visual gesture effect and trigger impact
            StartCoroutine(PlayAttackSlashOrBeamRoutine(strike, targetPos, dir));
            onImpact?.Invoke();
            yield return new WaitForSeconds(0.11f);

            // 4. Recover to ready stance
            yield return MoveAndPose(
                strike,
                start,
                -dir * 20f,
                0f,
                _visual.localScale,
                _visualBaseScale,
                0.24f,
                EaseOutQuad);

            _visual.localRotation = Quaternion.identity;
            _visual.localScale = _visualBaseScale;
            _idle = true;
        }

        public IEnumerator DeathRoutine()
        {
            _idle = false;
            float t = 0f;
            const float duration = 0.6f;
            Vector3 startScale = _visual.localScale;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                if (spriteRenderer != null)
                {
                    spriteRenderer.color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, 1f - k);
                }
                _visual.localScale = Vector3.Lerp(startScale, startScale * 0.55f, EaseInQuad(k));
                _visual.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 25f, k));
                yield return null;
            }
            _visual.localRotation = Quaternion.identity;
            _visual.gameObject.SetActive(false);
        }

        private void Update()
        {
            float bob = 0f;
            float swayAngle = 0f;
            if (_idle)
            {
                _bobTime += Time.deltaTime * idleBobSpeed;
                bob = Mathf.Sin(_bobTime) * idleBobAmplitude;
                swayAngle = Mathf.Cos(_bobTime * 0.8f) * 2.2f;
                float breathe = 1f + Mathf.Sin(_bobTime) * 0.025f;
                _visual.localScale = new Vector3(_visualBaseScale.x * breathe, _visualBaseScale.y / breathe, 1f);
                _visual.localRotation = Quaternion.Euler(0f, 0f, swayAngle);
            }
            var lp = _visual.localPosition;
            _visual.localPosition = new Vector3(_shakeOffset, Mathf.Lerp(lp.y, bob, 0.5f), lp.z);
        }

        private void EnsureGestureFxRenderer()
        {
            if (_gestureFxLine != null) return;
            var fxGo = new GameObject("GestureFX");
            fxGo.transform.SetParent(transform, false);
            _gestureFxLine = fxGo.AddComponent<LineRenderer>();
            _gestureFxLine.useWorldSpace = true;
            _gestureFxLine.positionCount = 5;
            _gestureFxLine.startWidth = 0.18f;
            _gestureFxLine.endWidth = 0.04f;
            _gestureFxLine.sortingOrder = 25;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _gestureFxLine.material = new Material(shader);
            }
            _gestureFxLine.enabled = false;
        }

        private IEnumerator PlayAttackSlashOrBeamRoutine(Vector3 fromWorld, Vector3 targetWorld, float dir)
        {
            EnsureGestureFxRenderer();
            if (_gestureFxLine == null) yield break;

            Color fxColor = GestureType switch
            {
                AttackGestureType.HeroBladeSlash => new Color(0.30f, 0.90f, 1.00f, 0.95f),
                AttackGestureType.HeroArcaneBurst => new Color(1.00f, 0.84f, 0.30f, 0.95f),
                AttackGestureType.BossInfernoBreath => new Color(1.00f, 0.38f, 0.24f, 0.95f),
                AttackGestureType.MonsterVenomBolt => new Color(0.68f, 0.42f, 1.00f, 0.95f),
                AttackGestureType.RivalWarriorStrike => new Color(1.00f, 0.36f, 0.68f, 0.95f),
                _ => new Color(1.00f, 0.58f, 0.32f, 0.95f)
            };

            _gestureFxLine.startColor = fxColor;
            _gestureFxLine.endColor = new Color(fxColor.r, fxColor.g, fxColor.b, 0.15f);
            _gestureFxLine.enabled = true;

            Vector3 impactCenter = targetWorld + Vector3.up * 1.35f;
            if (GestureType == AttackGestureType.BossInfernoBreath || GestureType == AttackGestureType.MonsterVenomBolt)
            {
                Vector3 origin = fromWorld + new Vector3(dir * 0.6f, 1.5f, 0f);
                for (int i = 0; i < 5; i++)
                {
                    float k = i / 4f;
                    Vector3 pt = Vector3.Lerp(origin, impactCenter, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.25f);
                    _gestureFxLine.SetPosition(i, pt);
                }
            }
            else
            {
                // Curved weapon or claw slash arc across the target
                for (int i = 0; i < 5; i++)
                {
                    float angle = Mathf.Lerp(-65f, 65f, i / 4f) * Mathf.Deg2Rad;
                    Vector3 offset = new(Mathf.Cos(angle) * dir * 0.85f, Mathf.Sin(angle) * 1.05f, 0f);
                    _gestureFxLine.SetPosition(i, impactCenter + offset);
                }
            }

            yield return new WaitForSeconds(0.14f);
            _gestureFxLine.enabled = false;
        }

        private IEnumerator PlayShieldBarrierEffectRoutine()
        {
            EnsureGestureFxRenderer();
            if (_gestureFxLine == null) yield break;

            float dir = transform.position.x < 0f ? 1f : -1f;
            Vector3 center = transform.position + new Vector3(dir * 0.85f, 1.4f, 0f);
            _gestureFxLine.startColor = blockFlashColor;
            _gestureFxLine.endColor = blockFlashColor;
            _gestureFxLine.enabled = true;

            for (int i = 0; i < 5; i++)
            {
                float angle = Mathf.Lerp(-70f, 70f, i / 4f) * Mathf.Deg2Rad;
                Vector3 pt = center + new Vector3(Mathf.Cos(angle) * dir * 0.55f, Mathf.Sin(angle) * 1.15f, 0f);
                _gestureFxLine.SetPosition(i, pt);
            }

            yield return new WaitForSeconds(0.22f);
            _gestureFxLine.enabled = false;
        }

        private IEnumerator HitRecoilGestureRoutine()
        {
            float dir = transform.position.x < 0f ? -1f : 1f;
            float t = 0f;
            const float duration = 0.22f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);
                _visual.localRotation = Quaternion.Euler(0f, 0f, dir * 14f * k);
                yield return null;
            }
            _visual.localRotation = Quaternion.identity;
        }

        private IEnumerator MoveAndPose(
            Vector3 fromPos,
            Vector3 toPos,
            float fromAngleZ,
            float toAngleZ,
            Vector3 fromScale,
            Vector3 toScale,
            float duration,
            Func<float, float> ease)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = ease(Mathf.Clamp01(t / duration));
                transform.position = Vector3.LerpUnclamped(fromPos, toPos, k);
                _visual.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(fromAngleZ, toAngleZ, k));
                _visual.localScale = Vector3.LerpUnclamped(fromScale, toScale, k);
                yield return null;
            }
            transform.position = toPos;
            _visual.localRotation = Quaternion.Euler(0f, 0f, toAngleZ);
            _visual.localScale = toScale;
        }

        private IEnumerator FlashRoutine(Color flash, float duration)
        {
            if (spriteRenderer == null) yield break;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                spriteRenderer.color = Color.Lerp(flash, _baseColor, t / duration);
                yield return null;
            }
            spriteRenderer.color = _baseColor;
        }

        private IEnumerator ShakeRoutine(float duration, float strength)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float damper = 1f - t / duration;
                _shakeOffset = (Mathf.PerlinNoise(t * 40f, 0f) - 0.5f) * 2f * strength * damper;
                yield return null;
            }
            _shakeOffset = 0f;
        }

        private IEnumerator PunchScaleRoutine(float punch, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);
                _visual.localScale = _visualBaseScale * Mathf.Lerp(1f, punch, k);
                yield return null;
            }
            _visual.localScale = _visualBaseScale;
        }

        private static float EaseInQuad(float x) => x * x;
        private static float EaseOutQuad(float x) => 1f - (1f - x) * (1f - x);
    }
}

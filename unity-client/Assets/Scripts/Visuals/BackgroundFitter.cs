using UnityEngine;

namespace KnowledgeWarrior.Visuals
{
    /// <summary>
    /// Scales and positions a background sprite so it covers the full orthographic camera view
    /// while aligning the painted ground line of the image with the world ground Y coordinate
    /// across Portrait mobile aspect ratios (9:16, 9:19.5, 9:20).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    public class BackgroundFitter : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [Tooltip("Where the ground line sits inside the sprite (0 = bottom edge, 1 = top edge).")]
        [Range(0.05f, 0.95f)] [SerializeField] private float groundInImage = 0.42f;
        [Tooltip("World Y coordinate where character feet stand.")]
        [SerializeField] private float groundWorldY = 1.05f;
        [SerializeField] private float overscan = 1.04f;

        private SpriteRenderer _renderer;
        private float _lastAspect = -1f;
        private float _lastOrtho = -1f;

        public void ConfigureGround(float groundRatioInImage, float worldGroundY)
        {
            groundInImage = Mathf.Clamp(groundRatioInImage, 0.05f, 0.95f);
            groundWorldY = worldGroundY;
            _lastAspect = -1f;
            FitNow();
        }

        private void OnEnable()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _lastAspect = -1f;
            FitNow();
        }

        private void LateUpdate()
        {
            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null || _renderer == null || _renderer.sprite == null || !cam.orthographic) return;
            if (Mathf.Approximately(cam.aspect, _lastAspect) && Mathf.Approximately(cam.orthographicSize, _lastOrtho)) return;
            FitNow();
        }

        public void FitNow()
        {
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null || _renderer == null || _renderer.sprite == null || !cam.orthographic) return;

            _lastAspect = cam.aspect;
            _lastOrtho = cam.orthographicSize;

            Vector2 spriteSize = _renderer.sprite.bounds.size;
            if (spriteSize.x <= 0.001f || spriteSize.y <= 0.001f) return;

            float camHeight = cam.orthographicSize * 2f;
            float camWidth = camHeight * cam.aspect;
            float camTop = cam.transform.position.y + cam.orthographicSize;
            float camBottom = cam.transform.position.y - cam.orthographicSize;

            float scaleForWidth = camWidth * overscan / spriteSize.x;
            float scaleForTop = Mathf.Max(0.1f, camTop - groundWorldY) * overscan / ((1f - groundInImage) * spriteSize.y);
            float scaleForBottom = Mathf.Max(0.1f, groundWorldY - camBottom) * overscan / (groundInImage * spriteSize.y);
            float scale = Mathf.Max(scaleForWidth, Mathf.Max(scaleForTop, scaleForBottom));

            transform.localScale = new Vector3(scale, scale, 1f);

            float height = spriteSize.y * scale;
            float centerY = groundWorldY - groundInImage * height + height * 0.5f;
            transform.position = new Vector3(cam.transform.position.x, centerY, transform.position.z);
        }
    }
}

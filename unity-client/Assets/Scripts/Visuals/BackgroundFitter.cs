using UnityEngine;

namespace KnowledgeWarrior.Visuals
{
    /// <summary>
    /// Scales/positions a background sprite so it always covers the camera width and the
    /// painted "ground line" of the image lines up with where characters stand,
    /// regardless of the phone's aspect ratio (16:9, 19.5:9, 4:3 tablets...).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    public class BackgroundFitter : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [Tooltip("Where the ground is in the image, 0 = bottom edge, 1 = top edge.")]
        [Range(0f, 1f)] [SerializeField] private float groundInImage = 0.2f;
        [Tooltip("World Y the image's ground line should sit at (character feet).")]
        [SerializeField] private float groundWorldY = -1.0f;
        [SerializeField] private float overscan = 1.04f;

        private SpriteRenderer _renderer;
        private float _lastAspect = -1f;
        private float _lastOrtho = -1f;

        private void OnEnable()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _lastAspect = -1f;
        }

        private void LateUpdate()
        {
            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null || _renderer == null || _renderer.sprite == null || !cam.orthographic) return;
            if (Mathf.Approximately(cam.aspect, _lastAspect) && Mathf.Approximately(cam.orthographicSize, _lastOrtho)) return;
            _lastAspect = cam.aspect;
            _lastOrtho = cam.orthographicSize;

            Vector2 spriteSize = _renderer.sprite.bounds.size;
            float camHeight = cam.orthographicSize * 2f;
            float camWidth = camHeight * cam.aspect;
            float camTop = cam.transform.position.y + cam.orthographicSize;

            // Must cover the full width, and from the ground line up to the top of the screen.
            float scaleForWidth = camWidth * overscan / spriteSize.x;
            float scaleForTop = (camTop - groundWorldY) * overscan / ((1f - groundInImage) * spriteSize.y);
            float scale = Mathf.Max(scaleForWidth, scaleForTop);
            transform.localScale = new Vector3(scale, scale, 1f);

            float height = spriteSize.y * scale;
            float centerY = groundWorldY - groundInImage * height + height * 0.5f;
            transform.position = new Vector3(cam.transform.position.x, centerY, transform.position.z);
        }
    }
}

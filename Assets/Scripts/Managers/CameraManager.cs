using UnityEngine;

[DefaultExecutionOrder(100)]
public class CamManager : MonoBehaviour, ILateTick
{
    #region Singleton
    public static CamManager instance { get; private set; }
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }
    #endregion

    #region Unity
    private void Start()
    {
        if (cam == null) cam = Camera.main;
    }

    private void OnEnable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Register((ILateTick)this);
    }

    private void OnDisable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Unregister((ILateTick)this);
    }
    #endregion

    #region Camera
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float ZoomValue;
    [SerializeField] private Vector2 Offset;

    private Camera cam;
    private PlayerManager pm => PlayerManager.instance;
    private Vector2 ZoomLimit => new Vector2(4f, pm.attribute.SIGHT_Current / 2f);

    // Camera follow runs as a LateTick (after gameplay Tick) so it reads
    // the player's already-moved position this frame instead of lagging
    // a frame behind, same reasoning as Unity's Update/LateUpdate split.
    public void LateTick(float delta)
    {
        if (!CanMove()) return;
        MoveCamera(delta);
    }

    private void MoveCamera(float delta)
    {
        Vector3 step = Vector3.Lerp(cam.transform.position, pm.Controlling.transform.position + (Vector3)Offset, moveSpeed * delta);
        step.z = -10f;
        cam.transform.position = step;
    }

    bool CanMove()
    {
        if (pm == null) return false;
        if (pm.Controlling == null) return false;
        if (cam == null) return false;
        if ((pm.Controlling.transform.position + (Vector3)Offset) == cam.transform.position) return false;
        return true;
    }

    #region Zoom
    public void ZoomIn(float v)
    {
        if (cam == null) return;
        ZoomValue = cam.orthographicSize;
        ZoomValue += v;
        Zoom();
    }
    public void ZoomOut(float v)
    {
        if (cam == null) return;
        ZoomValue = cam.orthographicSize;
        ZoomValue -= v;
        Zoom();
    }
    private void Zoom()
    {
        if (cam == null) return;
        ZoomValue = Mathf.Clamp(ZoomValue, ZoomLimit.x, ZoomLimit.y);
        cam.orthographicSize = ZoomValue;
    }
    #endregion
    #endregion
}
using UnityEngine;

[DefaultExecutionOrder(300)]
public class CorpseEmitter : MonoBehaviour
{
    #region Properties
    public GameObject prefab;

    [Header("Settings")]
    [SerializeField] private float corpseLifetime = 120f;
    [SerializeField] private float defaultSlideForce = 3f;
    #endregion

    #region Cache
    private HurtBox owner;
    private bool isCreated;
    #endregion

    #region Functions
    public void CreateEmitter(HurtBox o)
    {
        if (o == null) return;
        if (owner != o) owner = o;
        isCreated = true;
    }

    public void ResetEmitter()
    {
        isCreated = false;
        owner = null;
    }

    /// <summary>Spawns a corpse at the given position, sliding/tilting toward deathDirection.</summary>
    public void SpawnCorpse(Vector2 position, Vector2 deathDirection = default, float slideForce = -1f)
    {
        if (!isCreated) return;
        if (prefab == null) return;

        GameObject instance = PoolingSystem.instance.GetFromPool(prefab);
        if (instance == null) return;

        instance.transform.position = position;

        if (!instance.TryGetComponent<Corpse>(out Corpse c))
        {
            Debug.LogWarning($"Pooled corpse prefab '{prefab.name}' has no Corpse component!");
            return;
        }

        float force = slideForce >= 0f ? slideForce : defaultSlideForce;
        c.StartCorrupt(corpseLifetime, owner.Access().Media.Corpse,owner.Access().Media.Blood, deathDirection, force);
    }
    #endregion
}
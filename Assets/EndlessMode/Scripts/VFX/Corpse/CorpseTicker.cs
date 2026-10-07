using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central tick driver for all active corpses, mirroring how HiveBrain
/// drives zombies. Corpses register themselves here on StartCorrupt() and
/// unregister on Corrupt() (return to pool) - see Corpse.cs.
///
/// Why this exists instead of each Corpse running its own Update(): with
/// corpseLifetime defaulting to 120s, dead zombies can pile up into the
/// dozens on screen at once, and Unity's per-MonoBehaviour Update() dispatch
/// has real fixed overhead per object. Ticking them all from one place
/// removes that overhead the same way HiveBrain does for zombies.
/// </summary>
[DefaultExecutionOrder(600)]
public class CorpseTicker : MonoBehaviour
{
    public static CorpseTicker instance { get; private set; }

    [SerializeField] private List<Corpse> corpses = new List<Corpse>(64);

    #region Singleton
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            // Destroy() is deferred to end-of-frame; disable first so this
            // duplicate's OnEnable() never runs and double-registers.
            enabled = false;
            Destroy(gameObject);
            return;
        }
        instance = this;
    }
    #endregion

    private void Update()
    {
        if (corpses.Count == 0) return;

        float dt = Time.deltaTime;

        for (int i = corpses.Count - 1; i >= 0; i--)
        {
            var c = corpses[i];
            if (c == null)
            {
                corpses.RemoveAt(i); // clean up dead references instead of just skipping them
                continue;
            }
            c.Tick(dt);
        }
    }

    public void Register(Corpse c)
    {
        if (c == null) return;
        if (!corpses.Contains(c)) corpses.Add(c);
    }

    public void Unregister(Corpse c)
    {
        if (c == null) return;
        corpses.Remove(c);
    }
}
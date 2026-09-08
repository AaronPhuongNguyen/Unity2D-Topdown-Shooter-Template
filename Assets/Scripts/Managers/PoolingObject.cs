using System.Collections.Generic;
using UnityEngine;

public class PoolingSystem : MonoBehaviour
{
    public static PoolingSystem instance { get; private set; }
    private Dictionary<string, Queue<GameObject>> poolDict = new Dictionary<string, Queue<GameObject>>();
    private Dictionary<GameObject, string> objNameMap = new Dictionary<GameObject, string>();
    private HashSet<GameObject> inUseObjects = new HashSet<GameObject>();

    [Header("Auto-Return Check")]
    [SerializeField] private float checkInterval = 0.5f;
    private float checkTimer;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            enabled = false;
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    private void OnEnable()
    {
        // Tied to OnGameRestart (not OnPlayerRespawn) so this fires as part
        // of the same restart tier as HiveBrain.Reboot(), rather than
        // mid-way through PlayerManager.Init() before HiveBrain has had a
        // chance to return the previous wave's leftover zombies to the pool.
        EventBus.OnGameRestart += Reboot;
    }
    private void OnDisable()
    {
        EventBus.OnGameRestart -= Reboot;
    }

    private void Update()
    {
        checkTimer -= Time.deltaTime;
        if (checkTimer > 0f) return;
        checkTimer = checkInterval;
        CheckForDisabledObjects();
    }

    private void CheckForDisabledObjects()
    {
        if (inUseObjects.Count == 0) return;
        List<GameObject> snapshot = new List<GameObject>(inUseObjects);
        foreach (GameObject obj in snapshot)
        {
            if (obj == null)
            {
                inUseObjects.Remove(obj);
                continue;
            }
            if (!obj.activeSelf)
            {
                RemoveToPool(obj);
            }
        }
    }

    private void Reboot()
    {
        Debug.Log("Pool reboot detected");
    }

    public GameObject GetFromPool(GameObject prefab)
    {
        if (prefab == null) return null;
        string key = prefab.name;
        if (!poolDict.ContainsKey(key))
        {
            poolDict[key] = new Queue<GameObject>();
        }
        GameObject obj;
        if (poolDict[key].Count > 0)
        {
            obj = poolDict[key].Dequeue();
        }
        else
        {
            obj = Instantiate(prefab);
            obj.name = key;
            if (!objNameMap.ContainsKey(obj))
            {
                objNameMap.Add(obj, key);
            }
        }
        obj.transform.SetParent(null);
        obj.SetActive(true);
        inUseObjects.Add(obj);
        return obj;
    }

    public void RemoveToPool(GameObject obj)
    {
        if (obj == null) return;
        string key = objNameMap.ContainsKey(obj) ? objNameMap[obj] : obj.name;
        if (!poolDict.ContainsKey(key))
        {
            poolDict[key] = new Queue<GameObject>();
        }
        obj.SetActive(false);
        obj.transform.SetParent(transform);
        poolDict[key].Enqueue(obj);
        inUseObjects.Remove(obj);
    }

    public void DestroyObject(GameObject obj)
    {
        if (obj == null) return;
        string key = objNameMap.TryGetValue(obj, out string mappedKey) ? mappedKey : obj.name;
        objNameMap.Remove(obj);
        inUseObjects.Remove(obj);
        if (poolDict.TryGetValue(key, out Queue<GameObject> queue) && queue.Contains(obj))
        {
            Queue<GameObject> rebuilt = new Queue<GameObject>();
            foreach (var item in queue)
            {
                if (item != obj) rebuilt.Enqueue(item);
            }
            poolDict[key] = rebuilt;
        }
        Destroy(obj);
    }
}
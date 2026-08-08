using TMPro;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class VisualZomCount : MonoBehaviour
{
    public TextMeshProUGUI tmp;
    public TextMeshProUGUI tmp2;

    private DomainManager dm => DomainManager.instance;
    private int zomcount=1;
    private int zomkilled = 1;

    private void Update()
    {
        Zomcount();
        ZomKilled();
    }
    void Zomcount()
    {
        if (tmp == null) return;
        if (zomcount == dm.RemainingEnemy) return;
        zomcount = dm.RemainingEnemy;
        tmp.text = $"{zomcount}";
    }
    void ZomKilled()
    {
        if (tmp2 == null) return;
        if (zomkilled == dm.Killed) return;
        zomkilled = dm.Killed;
        tmp2.text = $"{zomkilled}";
    }
}

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class HealDemo:MonoBehaviour
{
    PlayerManager pm => PlayerManager.instance;

    public const float SkillCooldown = 30f;
    public const float HealValue = 60f;
    public const float HealDuration = 5f;
    public Image UI;

    private float CD;
    private float healDuration;
    
    public void Heal()
    {
        if (pm == null) return;
        if (CD > 0) return;

        CD = SkillCooldown;
        healDuration = HealDuration;

        if (UI != null) UI.fillAmount = 0;
    }
    private void Update()
    {
        if(CD > 0) CD -= Time.deltaTime;
        if (UI != null) UI.fillAmount = CD/SkillCooldown;

        if(healDuration > 0)
        {
            pm.Heal( (HealValue/ HealDuration / 100) * pm.attribute.HP_Max * Time.deltaTime);
            healDuration -= Time.deltaTime;
        }
    }
    private void Awake()
    {
        EventBus.OnGameRestart += Refresh;
    }
    private void OnDestroy()
    {
        EventBus.OnGameRestart -= Refresh;
    }
    void Refresh()
    {
        CD = 0;
        healDuration = 0f;
    }
}
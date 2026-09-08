
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class HealDemo:MonoBehaviour
{
    PlayerManager pm => PlayerManager.instance;

    public const float SkillCooldown = 40f;
    public const float HealValue = 3f;
    public const float HealDuration = 8f;
    public const float Recovery = 0.01f;
    public Image UI;

    private float CD;
    private float healDuration;
    private bool isExpired=true;
    
    public void Heal()
    {
        if (pm == null) return;
        if (CD > 0) return;

        CD = SkillCooldown * (1 - pm.attribute.CooldownReduction_Current);
        healDuration = HealDuration;
        isExpired = false;

        pm.attribute.OnDealDamage += Lifesteal;

        if (UI != null) UI.fillAmount = 0;
    }
    private void RemoveEffect()
    {
        if (isExpired) return;
        isExpired = healDuration<=0;

        if (isExpired) pm.attribute.OnDealDamage -= Lifesteal;

    }
    private void Update()
    {
        if(CD > 0) CD -= Time.deltaTime;
        if (UI != null) UI.fillAmount = CD / (SkillCooldown * (1 - pm.attribute.CooldownReduction_Current));

        if(healDuration > 0 && !isExpired)
        {
            pm.Heal( (HealValue/ HealDuration / 100) * pm.attribute.HP_Max * Time.deltaTime);
            healDuration -= Time.deltaTime;
            RemoveEffect();
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
        isExpired = true;
    }
    private void Lifesteal(float v)
    {
        if (healDuration <= 0f) return;
        pm.Heal(v * Recovery + pm.attribute.HP_Current * Recovery);
    }
}
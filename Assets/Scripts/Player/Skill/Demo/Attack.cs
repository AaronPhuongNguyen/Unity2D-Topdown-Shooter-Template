
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class AttackDemo:MonoBehaviour
{
    PlayerManager pm => PlayerManager.instance;

    private const float SkillCooldown = 90f;
    private const float ASPDValue = -0.25f;
    private const float SpeedValue = 0.1f;
    private const float ArmourPenValue = 0.4f;
    private const float BuffDuration = 12f;
    private const float ShootAcc = 0.25f;
    private const float Protecting = 0.4f;
    public Image UI;

    private float CD;
    private float buffDuration;
    private bool isExpired=true;
    private float ATKValue;

    public void Attack()
    {
        if (pm == null) return;
        if (CD > 0) return;

        CD = SkillCooldown * (1- Mathf.Clamp01(pm.attribute.CooldownReduction_Extra));
        buffDuration = BuffDuration;
        ATKValue = 10 + DomainManager.instance.Killed * 0.005f;

        pm.attribute.DealtDamage_Extra += ATKValue;
        pm.attribute.SPEED_Ampl.TotalBonus += SpeedValue;
        pm.attribute.ASPD_Ampl.TotalBonus += ASPDValue;
        pm.attribute.ArmourPenetration_Perc += ArmourPenValue;
        pm.attribute.DamageReduction_Extra += Protecting;
        pm.clonedPack.ShootAccuracy += ShootAcc;

        isExpired = false;
        if (UI != null) UI.fillAmount = 0;
    }
    public void Remove()
    {
        pm.attribute.DealtDamage_Extra -= ATKValue;
        pm.attribute.SPEED_Ampl.TotalBonus -= SpeedValue;
        pm.attribute.ASPD_Ampl.TotalBonus -= ASPDValue;
        pm.attribute.ArmourPenetration_Perc -= ArmourPenValue;
        pm.attribute.DamageReduction_Extra -= Protecting;
        pm.clonedPack.ShootAccuracy -= ShootAcc;

    }
    private void Update()
    {
        if(CD > 0) CD -= Time.deltaTime;
        if (UI != null) UI.fillAmount = CD/SkillCooldown;

        if(buffDuration > 0) buffDuration -= Time.deltaTime;
        else if(!isExpired && buffDuration <= 0)
        {
            Remove();
            isExpired = true;
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
        buffDuration = 0f;
    }
}
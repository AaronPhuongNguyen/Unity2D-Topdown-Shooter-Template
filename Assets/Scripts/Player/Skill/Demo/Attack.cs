
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class AttackDemo:MonoBehaviour
{
    PlayerManager pm => PlayerManager.instance;

    public const float SkillCooldown = 120f;
    public float ASPDValue = -0.5f;
    public float ATKValue = 0.8f;
    public float SpeedValue = 0.3f;
    public float BuffDuration = 12f;
    public Image UI;

    private float CD;
    private float buffDuration;
    private bool isExpired=true;
    
    public void Attack()
    {
        if (pm == null) return;
        if (CD > 0) return;

        CD = SkillCooldown;
        buffDuration = BuffDuration;

        pm.attribute.ATK_Ampl.TotalBonus += ATKValue;
        pm.attribute.SPEED_Ampl.TotalBonus += SpeedValue;
        pm.attribute.ASPD_Ampl.TotalBonus += ASPDValue;
        pm.clonedPack.ShootAccuracy += 0.5f;

        isExpired = false;
        if (UI != null) UI.fillAmount = 0;
    }
    public void Remove()
    {
        pm.attribute.ATK_Ampl.TotalBonus -= ATKValue;
        pm.attribute.SPEED_Ampl.TotalBonus -= SpeedValue;
        pm.attribute.ASPD_Ampl.TotalBonus -= ASPDValue;
        pm.clonedPack.ShootAccuracy -= 0.5f;

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
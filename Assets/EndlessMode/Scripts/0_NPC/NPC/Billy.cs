using Server;
using UnityEngine;

/// <summary>
/// Billy: dual-wielding NPC. His two ARMS are the turrets.
///  - Each arm has its OWN target and rotates toward it (limited by armTurnSpeed).
///    Left arm = nearest enemy. Right arm = nearest OTHER enemy inside a 180 degree arc
///    centred on the left arm's target. Each arm shoots 40% ATK, only when aimed.
///  - Normal skill (6s CD): charge 1s (both arms aim forward), then a heavy piercing
///    shot: 300 flat + 300% ATK, camera shake 2.
///  - Ultimate (20s CD): 3s slow slide forward while spinning and firing 50 shots/s.
///    Arms aim in SYMMETRY: right arm at angle A, left arm at A + 180
///    (right 90 -> left -90). Each shot = 20 flat + 25% ATK + 0.75% target max HP.
///    Cannot move, dodge or cancel meanwhile, gains 60% damage reduction.
///  - After the Heavy shot and after the Ultimate: RECOVERY for a random
///    0.75 to 2s. He cannot shoot or cast, but he can move (and dodge).
///  - Recovers Dodge faster (energy regen and dodge cooldown).
/// Put this on the prefab INSTEAD of NpcBrain.
/// Hierarchy: the muzzles should be CHILDREN of their arm transforms.
/// </summary>
public class BillyBrain : NpcBrain
{
    private enum Skill { None, Charging, Ultimate, Recovery }

    #region Inspector
    [Header("Arms (rotate toward their own target)")]
    [Tooltip("Left arm transform. It rotates; put the left muzzle under it.")]
    [SerializeField] private Transform armLeft;
    [Tooltip("Right arm transform. It rotates; put the right muzzle under it.")]
    [SerializeField] private Transform armRight;
    [SerializeField] private Transform muzzleLeft;
    [SerializeField] private Transform muzzleRight;
    [Tooltip("Degrees per second an arm can turn.")]
    [SerializeField] private float armTurnSpeed = 540f;
    [Tooltip("Added to the arm's rotation if the arm sprite does not point RIGHT (+X).")]
    [SerializeField] private float armAngleOffset = 0f;
    [Tooltip("An arm only fires when its aim is within this many degrees of its target.")]
    [SerializeField] private float aimTolerance = 8f;

    [Header("Turrets")]
    [Tooltip("Damage per arm shot as a fraction of ATK (0.4 = 40%).")]
    [SerializeField] private float turretDamagePercent = 0.4f;
    [Tooltip("Right arm only picks targets inside this arc (degrees), centred on the left arm's target.")]
    [SerializeField] private float turretArc = 180f;
    [Tooltip("If only one enemy is in range, both arms shoot it.")]
    [SerializeField] private bool shareTargetWhenAlone = true;
    [SerializeField] private float turretScanInterval = 0.15f;
    [SerializeField] private float turretMinCooldown = 0.1f;
    [SerializeField] private float turretMaxCooldown = 3f;
    [SerializeField] private float turretSpread = 2f;

    [Header("Normal Skill - Heavy Shot")]
    [SerializeField] private float normalCooldown = 6f;
    [SerializeField] private float chargeTime = 1f;
    [SerializeField] private float heavyFlatDamage = 300f;
    [Tooltip("3 = 300% ATK.")]
    [SerializeField] private float heavyAtkPercent = 3f;
    [SerializeField] private float heavyCameraShake = 2f;
    [SerializeField] private Bullet heavyBullet;
    [SerializeField] private float heavyBulletSpeed = 160f;
    [SerializeField] private AudioClip chargeSound;
    [SerializeField] private AudioClip heavySound;
    [SerializeField] private GameObject chargeEffect;

    [Header("Ultimate - Spin Barrage")]
    [SerializeField] private float ultCooldown = 20f;
    [SerializeField] private float ultDuration = 3f;
    [SerializeField] private float ultSlideSpeed = 2.5f;
    [Tooltip("Degrees per second.")]
    [SerializeField] private float ultSpinSpeed = 720f;
    [SerializeField] private float ultShotsPerSecond = 50f;
    [SerializeField] private float ultFlatDamage = 20f;
    [Tooltip("0.25 = 25% ATK.")]
    [SerializeField] private float ultAtkPercent = 0.25f;
    [Tooltip("0.0075 = 0.75% of the target's max HP.")]
    [SerializeField] private float ultTargetMaxHpPercent = 0.0075f;
    [Tooltip("0.6 = 60% damage reduction while the ultimate runs.")]
    [SerializeField] private float ultDamageReduction = 0.6f;
    [Tooltip("Only every Nth ultimate shot spawns a bullet/sound/muzzle flash (damage is always dealt).")]
    [SerializeField, Min(1)] private int ultVisualEvery = 3;

    [Header("Recovery (after Heavy and Ultimate)")]
    [Tooltip("Random recovery time: he cannot shoot or cast, but he can move.")]
    [SerializeField] private float recoveryTimeMin = 0.75f;
    [SerializeField] private float recoveryTimeMax = 2f;
    [Tooltip("If his normal AI would stand still, he strafes sideways during recovery.")]
    [SerializeField] private bool strafeDuringRecovery = true;

    [Header("Dodge Recovery")]
    [Tooltip("Energy regen and dodge cooldown recover this many times faster.")]
    [SerializeField] private float dodgeRecoveryMultiplier = 2.5f;
    #endregion

    #region Runtime
    private Skill state;
    private float stateTimer;
    private float normalCd, ultCd;
    private float baseEnergyRegen;
    private bool drApplied;

    // Targets: left arm = primary (nearest), right arm = secondary
    private Transform priT, secT;
    private UnitAttribute priU, secU;
    private float scanTimer, leftTimer, rightTimer;
    private Collider2D[] buf = new Collider2D[32];

    // Arms
    private float leftAngle, rightAngle; // world angles in degrees (without armAngleOffset)
    private Vector2 lastFace = Vector2.right;

    // Skill data
    private Vector2 chargeDir = Vector2.right;
    private Vector2 slideDir = Vector2.right;
    private Vector2 recoveryDir;
    private float spinAngle;
    private float shotAcc;
    private int shotCount;
    private bool nextRight;

    private Transform MuzzleL => muzzleLeft != null ? muzzleLeft : (armLeft != null ? armLeft : _t);
    private Transform MuzzleR => muzzleRight != null ? muzzleRight : (armRight != null ? armRight : _t);
    private Vector2 ArmLPos => armLeft != null ? (Vector2)armLeft.position : (Vector2)_t.position;
    private Vector2 ArmRPos => armRight != null ? (Vector2)armRight.position : (Vector2)_t.position;
    #endregion

    #region Lifecycle
    protected override void Awake()
    {
        base.Awake();
        baseEnergyRegen = Energy.RegenPerSecond; // read once, so pooled respawns don't compound
    }

    protected override void OnSpawned()
    {
        CanAttack = false;        // base single-turret attack is replaced by the arms
        InfiniteAmmo = true;      // Billy's shots don't use the magazine
        CanMove = true;

        Energy.RegenPerSecond = baseEnergyRegen * dodgeRecoveryMultiplier;

        state = Skill.None;
        stateTimer = 0f;
        normalCd = 0f;
        ultCd = 0f;
        drApplied = false;

        priT = secT = null;
        priU = secU = null;
        scanTimer = 0f;
        leftTimer = 0f;
        rightTimer = turretMinCooldown * 0.5f; // arms alternate slightly
        shotAcc = 0f;
        shotCount = 0;
        nextRight = false;

        lastFace = _t.right;
        float a = DirAngle(lastFace);
        leftAngle = a;
        rightAngle = a;
        ApplyArm(armLeft, leftAngle);
        ApplyArm(armRight, rightAngle);
    }

    protected override void OnRemoved()
    {
        EndSkill(false); // attribute is still valid here, so the DR is removed correctly
        priT = secT = null;
        priU = secU = null;
    }

    /// <summary>Ends the current skill. goRecovery = enter the random recovery window.</summary>
    private void EndSkill(bool goRecovery)
    {
        if (drApplied && attribute != null)
            attribute.DamageReduction_Ampl.FlatBonus -= ultDamageReduction;
        drApplied = false;

        CanMove = true;

        if (goRecovery)
        {
            state = Skill.Recovery;
            stateTimer = RNG.GetFloat(Mathf.Min(recoveryTimeMin, recoveryTimeMax),
                                      Mathf.Max(recoveryTimeMin, recoveryTimeMax));

            // Strafe: sideways to the nearest enemy, random side.
            Vector2 fwd = DirToPrimary();
            Vector2 perp = new Vector2(-fwd.y, fwd.x);
            recoveryDir = RNG.GetFloat(0f, 1f) < 0.5f ? perp : -perp;
        }
        else
        {
            state = Skill.None;
            stateTimer = 0f;
        }
    }

    /// <summary>No dodging while charging or during the ultimate. Recovery CAN dodge.</summary>
    public override bool NotifyIncomingAttack(Vector2 attackerPos)
    {
        if (state == Skill.Charging || state == Skill.Ultimate) return false;
        return base.NotifyIncomingAttack(attackerPos);
    }
    #endregion

    #region Tick
    public override void Tick(float dt)
    {
        base.Tick(dt);

        if (TickSystem.GameSpeed <= 0) return;
        if (!IsActive || attribute == null || attribute.IsDead || pm == null) return;

        if (normalCd > 0f) normalCd -= dt;
        if (ultCd > 0f) ultCd -= dt;

        // Faster dodge recovery: base already counts down 1x, this adds the extra.
        if (dodgeCooldownTimer > 0f && dodgeRecoveryMultiplier > 1f)
            dodgeCooldownTimer = Mathf.Max(0f, dodgeCooldownTimer - dt * (dodgeRecoveryMultiplier - 1f));

        scanTimer -= dt;
        if (scanTimer <= 0f)
        {
            scanTimer = turretScanInterval;
            ScanTargets();
        }

        switch (state)
        {
            case Skill.Charging:
                TickCharge(dt);
                break;

            case Skill.Ultimate:
                TickUltimate(dt);
                break;

            case Skill.Recovery:
                // Cannot shoot or cast, but the arms still track their targets and he moves.
                TickArms(dt, false);
                stateTimer -= dt;
                if (stateTimer <= 0f) state = Skill.None;
                break;

            default:
                TryStartSkill();
                if (state == Skill.None) TickArms(dt, true);
                break;
        }
    }

    /// <summary>Faces the skill direction, the nearest enemy, or where he walks.</summary>
    protected override Vector2 ShapeFacing(Vector2 desired, float dt)
    {
        Vector2 face;

        if (state == Skill.Ultimate) face = AngleToDir(spinAngle);
        else if (state == Skill.Charging) face = chargeDir;
        else if (IsDodging) face = desired;
        else if (state == Skill.Recovery && desired != Vector2.zero) face = desired;
        else if (Alive(priT, priU)) face = ((Vector2)priT.position - (Vector2)_t.position).normalized;
        else face = desired;

        if (face != Vector2.zero) lastFace = face;
        return face;
    }

    /// <summary>During recovery he keeps moving even if his normal AI would stand still.</summary>
    protected override Vector2 ShapeMovement(Vector2 desired, float dt)
    {
        if (state == Skill.Recovery && strafeDuringRecovery && desired == Vector2.zero)
            return recoveryDir;
        return desired;
    }
    #endregion

    #region Targeting (left arm = nearest, right arm = nearest other in the arc)
    private static bool IsHostile(Collider2D col, out UnitAttribute ua)
    {
        ua = null;
        if (col == null) return false;
        if (!col.TryGetComponent(out HurtBox hurt)) return false;

        ua = hurt.Access()?.attribute;
        if (ua == null || ua.IsDead || ua.IsUntargetable) return false;
        return ua.tag == UnitTag.Aggressive;
    }

    private static bool Alive(Transform t, UnitAttribute u)
        => t != null && u != null && !u.IsDead && !u.IsUntargetable && t.gameObject.activeInHierarchy;

    private bool InRange(Transform t, UnitAttribute u, float range)
    {
        if (!Alive(t, u)) return false;
        return ((Vector2)t.position - (Vector2)_t.position).sqrMagnitude <= range * range;
    }

    private void ScanTargets()
    {
        Vector2 pos = _t.position;
        float sight = attribute.SIGHT_Current;

        int count = Physics2D.OverlapCircleNonAlloc(pos, sight, buf, pm.EnemyMask);
        if (count >= buf.Length)
        {
            buf = new Collider2D[buf.Length * 2];
            count = Physics2D.OverlapCircleNonAlloc(pos, sight, buf, pm.EnemyMask);
        }

        // Primary = nearest enemy. Keep the current one while it stays within sight (+15%).
        float keep = sight * 1.15f;
        bool keepPri = Alive(priT, priU)
                       && ((Vector2)priT.position - pos).sqrMagnitude <= keep * keep;

        if (!keepPri)
        {
            priT = null;
            priU = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (!IsHostile(buf[i], out UnitAttribute ua)) continue;
                float sqr = ((Vector2)buf[i].transform.position - pos).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    priT = buf[i].transform;
                    priU = ua;
                }
            }
        }

        // Secondary = nearest OTHER enemy inside the arc centred on the primary's direction.
        secT = null;
        secU = null;
        if (priT == null) return;

        Vector2 fwd = ((Vector2)priT.position - pos).normalized;
        float cosHalf = Mathf.Cos(turretArc * 0.5f * Mathf.Deg2Rad);
        float best = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            if (!IsHostile(buf[i], out UnitAttribute ua)) continue;
            if (ua == priU) continue;

            Vector2 to = (Vector2)buf[i].transform.position - pos;
            float sqr = to.sqrMagnitude;
            if (sqr > 0.0001f && Vector2.Dot(to / Mathf.Sqrt(sqr), fwd) < cosHalf) continue;

            if (sqr < best)
            {
                best = sqr;
                secT = buf[i].transform;
                secU = ua;
            }
        }
    }
    #endregion

    #region Arms
    private static float DirAngle(Vector2 d) => Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

    private void ApplyArm(Transform arm, float angle)
    {
        if (arm == null) return;
        arm.rotation = Quaternion.Euler(0f, 0f, angle + armAngleOffset);
    }

    /// <summary>Turns an arm toward targetAngle at armTurnSpeed. Returns the remaining error (degrees).</summary>
    private float TurnArm(Transform arm, ref float angle, float targetAngle, float dt)
    {
        angle = Mathf.MoveTowardsAngle(angle, targetAngle, armTurnSpeed * dt);
        ApplyArm(arm, angle);
        return Mathf.Abs(Mathf.DeltaAngle(angle, targetAngle));
    }

    /// <summary>
    /// Both arms track their OWN target. When canFire, each arm shoots once it is aimed.
    /// An arm with no target rests pointing where the body faces.
    /// </summary>
    private void TickArms(float dt, bool canFire)
    {
        leftTimer -= dt;
        rightTimer -= dt;

        float range = attribute.SIGHT_Current;
        float damage = attribute.ATK_Current * turretDamagePercent;

        bool priOk = InRange(priT, priU, range);
        bool secOk = InRange(secT, secU, range);

        Transform leftTarget = priOk ? priT : null;
        Transform rightTarget = secOk ? secT : (shareTargetWhenAlone && priOk ? priT : null);

        float restAngle = DirAngle(lastFace);

        // LEFT arm
        float leftWanted = leftTarget != null
            ? DirAngle((Vector2)leftTarget.position - ArmLPos)
            : restAngle;
        float leftErr = TurnArm(armLeft, ref leftAngle, leftWanted, dt);

        if (canFire && leftTarget != null && leftTimer <= 0f && leftErr <= aimTolerance)
        {
            leftTimer = TurretCooldown;
            FireArm(MuzzleL, leftAngle, damage, range);
        }

        // RIGHT arm
        float rightWanted = rightTarget != null
            ? DirAngle((Vector2)rightTarget.position - ArmRPos)
            : restAngle;
        float rightErr = TurnArm(armRight, ref rightAngle, rightWanted, dt);

        if (canFire && rightTarget != null && rightTimer <= 0f && rightErr <= aimTolerance)
        {
            rightTimer = TurretCooldown;
            FireArm(MuzzleR, rightAngle, damage, range);
        }
    }

    private float TurretCooldown
        => Mathf.Clamp(0.1f / attribute.ASPD_Current, turretMinCooldown, turretMaxCooldown);

    /// <summary>The shot goes where the arm really points (plus a little spread).</summary>
    private void FireArm(Transform muzzle, float armAngle, float damage, float range)
    {
        Vector2 dir = AngleToDir(armAngle);
        if (turretSpread > 0f) dir = Rotate(dir, RNG.GetFloat(-turretSpread, turretSpread));

        ShootFrom(muzzle, dir, damage, range, true, bulletPrefab, bulletSpeed);
    }
    #endregion

    #region Normal Skill: Heavy Shot
    private void TryStartSkill()
    {
        if (IsDodging) return;
        if (!InRange(priT, priU, attribute.SIGHT_Current)) return;

        if (ultCd <= 0f) StartUltimate();
        else if (normalCd <= 0f) StartCharge();
    }

    private void StartCharge()
    {
        MarkInCombat();

        state = Skill.Charging;
        stateTimer = chargeTime;
        normalCd = normalCooldown;
        chargeDir = DirToPrimary();
        CanMove = false;

        PlaySound(chargeSound);
        SpawnFx(chargeEffect, _t.position, chargeDir);
    }

    private void TickCharge(float dt)
    {
        // Keep aiming at the enemy while charging; both arms swing forward.
        if (Alive(priT, priU)) chargeDir = DirToPrimary();

        float a = DirAngle(chargeDir);
        TurnArm(armLeft, ref leftAngle, a, dt);
        TurnArm(armRight, ref rightAngle, a, dt);

        stateTimer -= dt;
        if (stateTimer > 0f) return;

        FireHeavy();
        EndSkill(true); // recovery: cannot shoot, can move
    }

    private void FireHeavy()
    {
        float range = attribute.SIGHT_Current;
        float damage = heavyFlatDamage + heavyAtkPercent * attribute.ATK_Current;

        // Piercing shot: hits every enemy on the line.
        AttackResult[] hits = Attack.ShootPierce(gameObject, damage, attribute, range, chargeDir, pm.EnemyMask);

        Vector2 from = MuzzleR.position;
        Vector2 end = (Vector2)_t.position + chargeDir * range;
        for (int i = 0; i < hits.Length; i++)
        {
            ShowHit(hits[i]);
            if (i == hits.Length - 1) end = hits[i].HitPoint;
        }

        SpawnBullet(heavyBullet != null ? heavyBullet : bulletPrefab, from, chargeDir, end,
            heavyBulletSpeed, hits.Length > 0);
        SpawnFx(shootEffect, from, chargeDir);
        PlaySound(heavySound);

        if (heavyCameraShake > 0f && CamManager.instance != null)
            CamManager.instance.Shake(heavyCameraShake);
    }
    #endregion

    #region Ultimate: Spin Barrage
    private void StartUltimate()
    {
        MarkInCombat();

        state = Skill.Ultimate;
        stateTimer = ultDuration;
        ultCd = ultCooldown;

        slideDir = DirToPrimary();
        spinAngle = DirAngle(slideDir);
        shotAcc = 0f;
        shotCount = 0;
        nextRight = false;
        CanMove = false;

        attribute.DamageReduction_Ampl.FlatBonus += ultDamageReduction;
        drApplied = true;
    }

    private void TickUltimate(float dt)
    {
        stateTimer -= dt;

        // Spin
        spinAngle += ultSpinSpeed * dt;

        // Arms in SYMMETRY: right arm at A, left arm at A + 180 (right 90 -> left -90).
        rightAngle = spinAngle;
        leftAngle = spinAngle + 180f;
        ApplyArm(armRight, rightAngle);
        ApplyArm(armLeft, leftAngle);

        // Slow slide forward (locked direction at cast)
        Vector2 p = (Vector2)_t.position + slideDir * ultSlideSpeed * dt;
        if (dm != null) p = dm.ClampToMap(p);
        _t.position = new Vector3(p.x, p.y, _t.position.z);

        // Fire, alternating right and left arm
        float range = attribute.SIGHT_Current;
        float interval = 1f / Mathf.Max(1f, ultShotsPerSecond);
        shotAcc += dt;

        int guard = 0;
        while (shotAcc >= interval && guard++ < 10)
        {
            shotAcc -= interval;
            FireUltShot(range);
        }

        if (stateTimer <= 0f) EndSkill(true); // recovery: cannot shoot, can move
    }

    private void FireUltShot(float range)
    {
        Transform muzzle = nextRight ? MuzzleR : MuzzleL;
        Vector2 dir = AngleToDir(nextRight ? rightAngle : leftAngle);
        nextRight = !nextRight;

        // Damage depends on the target's max HP, so peek at what the ray will hit first.
        float damage = ultFlatDamage + ultAtkPercent * attribute.ATK_Current;
        RaycastHit2D h = Physics2D.Raycast(muzzle.position, dir, range, pm.EnemyMask);
        if (h.collider != null && h.collider.TryGetComponent(out HurtBox hb))
        {
            UnitAttribute u = hb.Access()?.attribute;
            if (u != null && !u.IsDead && u.tag == UnitTag.Aggressive)
                damage += ultTargetMaxHpPercent * u.HP_Max;
        }

        bool visual = (shotCount++ % ultVisualEvery) == 0;
        ShootFrom(muzzle, dir, damage, range, visual, bulletPrefab, bulletSpeed);
    }
    #endregion

    #region Shot helpers
    private AttackResult ShootFrom(Transform muzzle, Vector2 dir, float damage, float range,
        bool visual, Bullet bullet, float speed)
    {
        MarkInCombat();

        // Attack.Shoot raycasts from o.transform.position, so the muzzle object is the origin.
        AttackResult r = Attack.Shoot(muzzle.gameObject, damage, attribute, range, dir, pm.EnemyMask);

        if (visual)
        {
            Vector2 from = muzzle.position;
            Vector2 end = r.DidHit ? r.HitPoint : from + dir * range;
            SpawnFx(shootEffect, from, dir);
            SpawnBullet(bullet, from, dir, end, speed, r.DidHit);
            PlaySound(package?.Media?.Audio?.GetAttackSound());
        }

        if (r.DidHit) ShowHit(r);
        return r;
    }

    private void ShowHit(AttackResult r)
    {
        DamagePopupManager.Show(r.HitPoint, r.Damage, r.IsCrit);

        GameObject fx = package?.Media?.HitEffect;
        if (fx == null || PoolingSystem.instance == null) return;
        GameObject o = PoolingSystem.instance.GetFromPool(fx);
        if (o != null) o.transform.position = r.HitPoint;
    }

    private void SpawnBullet(Bullet prefab, Vector2 from, Vector2 dir, Vector2 end, float speed, bool didHit)
    {
        if (prefab == null || PoolingSystem.instance == null) return;
        GameObject o = PoolingSystem.instance.GetFromPool(prefab.gameObject);
        if (o == null) return;

        o.transform.position = from;
        o.transform.rotation = Quaternion.Euler(0f, 0f, DirAngle(dir));
        if (o.TryGetComponent(out Bullet b)) b.Launch(end, speed, bulletLifetime, didHit);
    }

    private void SpawnFx(GameObject prefab, Vector2 pos, Vector2 dir)
    {
        if (prefab == null || PoolingSystem.instance == null) return;
        GameObject o = PoolingSystem.instance.GetFromPool(prefab);
        if (o == null) return;
        o.transform.position = pos;
        o.transform.rotation = Quaternion.Euler(0f, 0f, DirAngle(dir));
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip == null || AudioManager.instance == null) return;
        AudioManager.instance.PlayAudio(clip, _t.position);
    }

    private Vector2 DirToPrimary()
    {
        if (!Alive(priT, priU)) return chargeDir.sqrMagnitude > 0.0001f ? chargeDir : Vector2.right;
        Vector2 d = (Vector2)priT.position - (Vector2)_t.position;
        return d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.right;
    }

    private static Vector2 AngleToDir(float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
    }

    private static Vector2 Rotate(Vector2 d, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
    }
    #endregion
}
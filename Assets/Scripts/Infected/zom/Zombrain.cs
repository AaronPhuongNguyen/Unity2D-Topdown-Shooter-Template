using Server;
using UnityEngine;

public class Zombrain : HurtBox, ITick
{
    public virtual HiveBrain hb => HiveBrain.instance;
    public virtual DomainManager dm => DomainManager.instance;

    #region Properties
    public ZomPackage package;
    public UnitAttribute attribute;
    public override UnitPackage Access() => package;

    public Animator anim;
    public Transform Target;
    public Vector2 Direction;

    public bool CanAttack = true;
    public bool CanMove = true;
    public bool isAttacking => attackStagger > Time.time;
    public bool isMoving => Target != null && Direction != Vector2.zero;

    [SerializeField] protected float DistanceToTarget;
    [SerializeField] protected float SpeedHelper;
     #endregion

    #region Cache
    protected CorpseEmitter ce;
    protected Rigidbody2D rb;
    protected float rotateInterval;
    protected float attackInterval;
    protected float attackStagger;
    protected float findPreyInterval;
    protected PlayerManager pm => PlayerManager.instance;

    // True while this instance is spawned/active and subscribed to events.
    // Prevents Despawn()/Reboot() from running their cleanup twice on the
    // same object (which was causing HiveBrain to fail removing an already
    // -removed Zombrain after a restart).
    protected bool isActive;

    [Header("Separation")]
    [SerializeField] protected float separationRadius = 1f;
    [SerializeField] protected float separationStrength = 1.5f;
    protected float separationInterval;
    protected Vector2 cachedSeparation;

    private static readonly int AttackHash = Animator.StringToHash("IsAttacking");
    private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");
    #endregion

    #region Lifecycle
    public virtual void Tick(float dt)
    {
        FindPrey();
        DefineDirection();
        Move();
        TryAttack();
        UpdateAnimator();
    }

    /// <returns>True if this instance was newly spawned/counted; false if it
    /// was rejected (bad package) or was already active. HiveBrain uses this
    /// to decide whether to start tracking the object in its `zoms` list -
    /// tracking it after a failed/duplicate spawn would let it be ticked
    /// and eventually despawned without ever having been counted.</returns>
    public virtual bool SpawnObject(ZomPackage zp)
    {
        if (!CanSpawn(zp)) return false;
        if (isActive)
        {
            Debug.LogWarning($"[Zombrain] SpawnObject blocked on {gameObject.name} - isActive was already true (this instance was never rebooted/despawned).");
            return false;
        }

        SetRigidBody();

        if (package == null) package = Instantiate(zp);

        attribute = package.attribute.Clone();
        attribute.Reset();
        attribute.OnDeath += Despawn;
        attribute.OnTakeDamage += OnHit;
        isActive = true;

        package.attribute = attribute;

        Target = pm.Controlling == null ? null : pm.Controlling.transform;

        if (dm != null) dm.RemainingEnemy++;
        if (ce == null && gameObject.TryGetComponent(out CorpseEmitter cet))
        {
            ce = cet;
            ce.CreateEmitter(this);
        }

        return true;
    }

    // Normal death: plays FX/sound, spawns a corpse, counts as a kill.
    protected virtual void Despawn()
    {
        if (!Unsubscribe()) return;

        PlayDeathSound();
        ce?.SpawnCorpse(transform.position, Direction);

        if (dm != null)
        {
            dm.RemainingEnemy--;
            dm.Killed++;
        }

        CleanUpAndReturnToHive();
    }

    /// <summary>
    /// Called directly by HiveBrain.Reboot() during its bulk game-restart
    /// clear - not self-subscribed to EventBus.OnGameRestart anymore, so
    /// HiveBrain is the single owner of "clear everything on restart"
    /// instead of every zombie racing to remove itself independently.
    /// Unlike Despawn(): no death FX/sound, no kill credit, and it does NOT
    /// call hb.DespawnZom itself - the caller (HiveBrain) owns pooling and
    /// clearing its own `zoms` list as part of the same bulk operation.
    ///
    /// Deliberately does NOT touch dm.RemainingEnemy: DomainManager.Reboot()
    /// already resets it to 0 as part of the same restart sequence, so
    /// decrementing here too would double-count leftover zombies and drive
    /// the counter negative (which then silently resurfaces as an
    /// artificially low RemainingEnemy once the next wave spawns on top of
    /// it). Counter resets belong to DomainManager alone; this only tears
    /// down this instance's own subscriptions/state.
    /// </summary>
    public virtual void Reboot()
    {
        if (!Unsubscribe()) return;

        Target = null;
        package = null;
        attribute = null;
        ce = null;
    }

    // Unsubscribes everything and returns false if this instance was
    // already inactive, so callers can bail out instead of double-cleaning.
    private bool Unsubscribe()
    {
        if (!isActive) return false;
        isActive = false;

        attribute.OnDeath -= Despawn;
        attribute.OnTakeDamage -= OnHit;
        return true;
    }

    private void CleanUpAndReturnToHive()
    {
        Target = null;
        package = null;
        attribute = null;
        ce = null;

        hb.DespawnZom(gameObject);
    }
    #endregion

    #region Setup
    protected virtual bool CanSpawn(ZomPackage zp)
    {
        if (zp == null)
        {
            Debug.LogWarning("No Package available, check before spawning this Zom");
            return false;
        }
        if (zp.attribute == null)
        {
            Debug.LogWarning("No Valid Attribute, check");
            return false;
        }
        return true;
    }

    protected virtual void SetRigidBody()
    {
        if (!TryGetComponent(out rb)) rb = gameObject.AddComponent<Rigidbody2D>();

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.constraints = RigidbodyConstraints2D.FreezePosition;
        rb.freezeRotation = true;
    }
    #endregion

    #region Movement
    protected virtual void DefineDirection()
    {
        if (Target == null) return;
        if (Time.time < rotateInterval) return;

        Direction = (Target.position - transform.position).normalized;
        Rotate();

        rotateInterval = Time.time + RNG.GetFloat(0.1f, 0.3f);
    }

    protected virtual float DefineDistance()
    {
        if (Target == null) return 0f;

        DistanceToTarget = Vector2.Distance(Target.position, transform.position);
        return DistanceToTarget;
    }

    protected virtual void Rotate()
    {
        if (Direction == Vector2.zero) return;
        if (isAttacking) return;
        if (Time.time < rotateInterval) return;

        UnitMotion.RotateThisObject(gameObject, Direction);
    }

    protected virtual void Move()
    {
        if (!CanMove) return;
        if (Target == null) return;
        if (Direction == Vector2.zero) return;
        if (isAttacking) return;

        if (Time.time >= separationInterval)
        {
            cachedSeparation = UnitMotion.GetSeparationForce(
                transform.position,
                hb.GetNearbyZoms(this, separationRadius),
                separationRadius,
                separationStrength
            );
            separationInterval = Time.time + 0.2f;
        }

        if (DistanceToTarget > pm.attribute.SIGHT_Current * 2f)
            SpeedHelper = 20f;
        else SpeedHelper = 0f;
        attribute.SPEED_Ampl.FlatBonus = SpeedHelper;
        
        Vector2 finalDir = (Direction + cachedSeparation).normalized;
        UnitMotion.MoveThisObject(attribute, gameObject, finalDir);
    }

    protected virtual void UpdateAnimator()
    {
        if (anim == null) return;
        anim.SetBool(IsWalkingHash, CanMove && isMoving && !isAttacking);
    }
    #endregion

    #region Combat
    protected virtual bool TargetInRange()
    {
        return DefineDistance() <= attribute.SIGHT_Current;
    }

    protected virtual void TryAttack()
    {
        if (Target == null) return;
        if (!CanAttack) return;
        if (Time.time < attackInterval) return;

        attackInterval = Time.time + RNG.GetFloat(attribute.ASPD_Current, 1f);

        if (Time.time < attackStagger) return;
        if (!TargetInRange()) return;

        PerformAttack();
    }

    protected virtual void PerformAttack()
    {
        if (package == null) return;

        PlayAttackSound();

        if (RNG.GetFloat(0, 1f) < package.BiteAccuracy)
            Attack.Shoot(gameObject, attribute, attribute.SIGHT_Current, Direction, hb.enemyMask);

        if (anim != null) anim.SetBool(AttackHash,CanAttack && isAttacking);

        attackStagger = Time.time + RNG.GetFloat(attribute.ASPD_Current, 2f);
    }

    protected virtual void OnHit(float damage)
    {
        PlayHitSound();
    }
    #endregion

    #region Sense
    protected virtual void FindPrey()
    {
        if (Target != null) return;
        if (Time.time < findPreyInterval) return;
        findPreyInterval = Time.time + RNG.GetFloat(0.3f, 1f);

        if (pm.Controlling == null) return;
        Target = pm.Controlling.transform;
    }

    protected virtual void PlayAttackSound()
    {
        if (AudioManager.instance == null || package == null) return;
        AudioManager.instance.PlayAudio(package.Media.Audio.GetAttackSound(), transform.position);
    }

    protected virtual void PlayHitSound()
    {
        if (AudioManager.instance == null || package == null) return;
        AudioManager.instance.PlayAudio(package.Media.Audio.GetHitSound(), transform.position);
    }

    protected virtual void PlayDeathSound()
    {
        if (AudioManager.instance == null || package == null) return;
        AudioManager.instance.PlayAudio(package.Media.Audio.GetDeathSound(), transform.position);
    }

    protected virtual void PlayMiscSound()
    {
        if (AudioManager.instance == null || package == null) return;
        AudioManager.instance.PlayAudio(package.Media.Audio.GetMiscSound(), transform.position);
    }
    #endregion
}
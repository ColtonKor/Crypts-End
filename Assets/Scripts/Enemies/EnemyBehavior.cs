using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class EnemyBehavior : MonoBehaviour
{
    public enum EnemyType { Melee, Ranged } // Define types of enemies
    public EnemyType enemyType; // Choose Melee or Ranged in the Inspector

    public Transform[] waypoints;
    public float patrolSpeed = 2f;
    public float chaseSpeed = 4f;
    public float detectionRange = 5f;
    public float chaseStopRange = 6f;

    public bool blindMode; // Enable blind mode in the Inspector
    public float sprintDetectionBoost = 10f; // Extra detection range when the player is sprinting
    private bool isPlayerSprinting = false;
    private bool isPlayerRolling = false;

    // Ranged-specific properties
    public float attackRange = 8f;
    public float attackCooldown = 2f;
    public GameObject projectilePrefab;
    public Transform projectileSpawnPoint;

    // Melee-specific properties
    public float meleeAttackRange = 1.5f;
    public float meleeBlindAttackRange = 1.2f;
    public float meleeAttackCooldown = 1f;
    public float meleeDamage = 20f;

    public Transform player;

    private int currentWaypointIndex = 0;
    private bool isChasing = false;

    private Rigidbody rb;
    private CapsuleCollider capsuleCollider;
    private float lastAttackTime = 0f;

    public float obstacleDetectionDistance = 1.5f; // Distance to check for obstacles
    public float jumpForce = 5f; // Force applied to the enemy's Rigidbody for jumping
    private EnemyStun enemyStun;
    private Animator anim;

    // Random wandering variables
    private Vector3 randomTarget;
    private float timeToNextRandomMove = 0f;
    public bool attackReady = true;

    public delegate void TakeDamage(float damage);
    public static event TakeDamage HitPlayer;

    public delegate void HumanBonked();
    public static event HumanBonked playerSound;
    public static event HumanBonked fire;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        capsuleCollider = GetComponent<CapsuleCollider>();

        rb.useGravity = true; // Ensure gravity is enabled
        rb.freezeRotation = true; // Prevent physics-induced rotation
        enemyStun = GetComponent<EnemyStun>();
        anim = GetComponent<Animator>();
        if (anim == null)
        {
            anim = GetComponentInChildren<Animator>();
        }
        anim.SetBool("Walk", true);
        player = FindAnyObjectByType<PlayerMovement>().gameObject.transform;
    }

    private void Update()
    {
        if (enemyStun.isStunned)
        {
            rb.linearVelocity = Vector3.zero; // Stop all movement while stunned
            return;
        }

        // Check player's sprinting state
        PlayerMovement playerMovement = player.GetComponent<PlayerMovement>();
        if (playerMovement != null)
        {
            isPlayerSprinting = playerMovement.movement.isSprinting;
            isPlayerRolling = playerMovement.isRolling;
        }

        if (blindMode){
            HandleBlindModeLogic();
        } else if(enemyType == EnemyType.Ranged){
            HandleRangedLogic();
        } else {
            HandleNormalLogic();
        }
    }

    private void HandleBlindModeLogic()
    {
        float effectiveDetectionRange = meleeAttackRange;
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        

        if (distanceToPlayer <= meleeBlindAttackRange){
            // Blind enemy is dangerous in melee range or if the player is sprinting nearby
            AttackMelee();
        } else if (distanceToPlayer <= sprintDetectionBoost && (isPlayerSprinting || isPlayerRolling)){
            ChasePlayer();
        } else if (distanceToPlayer > sprintDetectionBoost){
            PatrolOrWander();
        }
    }


    private void HandleRangedLogic()
    {
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (distanceToPlayer <= attackRange)
        {
            RotateTowards(player.position);
            AttackRanged();
        } else if (distanceToPlayer > sprintDetectionBoost){
            PatrolOrWander();
        }
    }

    private void HandleNormalLogic()
    {
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (distanceToPlayer <= meleeAttackRange)
        {
            // Melee enemy attacks if within melee range
            AttackMelee();
        }
        else if (distanceToPlayer <= detectionRange)
        {
            // Start chasing the player
            isChasing = true;
        }
        else if (isChasing && distanceToPlayer > chaseStopRange)
        {
            // Stop chasing if the player is out of range
            isChasing = false;
        }

        if (isChasing)
        {
            ChasePlayer();
        }
        else if (distanceToPlayer > attackRange)
        {
            PatrolOrWander();
        }
    }

    private void PatrolOrWander()
    {
        if (waypoints.Length > 0)
        {
            Patrol();
        }
        else
        {
            WanderRandomly();
        }
    }

    private void Patrol()
    {
        Transform targetWaypoint = waypoints[currentWaypointIndex];
        MoveTowards(targetWaypoint.position, patrolSpeed);
        RotateTowards(targetWaypoint.position);

        if (Vector3.Distance(transform.position, targetWaypoint.position) < 2f)
        {
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
        }
    }

    private void WanderRandomly()
    {
        if (timeToNextRandomMove <= 0f || Vector3.Distance(transform.position, randomTarget) < 1f)
        {
            randomTarget = new Vector3(
                transform.position.x + Random.Range(-10f, 10f),
                transform.position.y,
                transform.position.z + Random.Range(-10f, 10f)
            );

            timeToNextRandomMove = Random.Range(2f, 5f);
        }

        MoveTowards(randomTarget, patrolSpeed);
        RotateTowards(randomTarget);
        timeToNextRandomMove -= Time.deltaTime;
    }

    private void ChasePlayer()
    {
        MoveTowards(player.position, chaseSpeed);
        RotateTowards(player.position);
    }

    private void MoveTowards(Vector3 target, float speed)
    {
        anim.SetBool("Walk", true);
        Vector3 direction = (target - transform.position).normalized;
        Vector3 velocity = direction * speed;
        velocity.y = rb.linearVelocity.y;
        rb.linearVelocity = velocity;
    }

    private void RotateTowards(Vector3 targetPosition)
    {
        Vector3 direction = (targetPosition - transform.position).normalized;
        direction.y = 0;

        Quaternion lookRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 5f);
    }

    private void AttackRanged()
    {
        anim.SetBool("Walk", false);
        if (attackReady){
            GameObject projectile = Instantiate(projectilePrefab, projectileSpawnPoint.position, Quaternion.identity);
            EnemyProjectile projectileScript = projectile.GetComponent<EnemyProjectile>();

            if (projectileScript != null){
                // adjust position bc player root is at their feet
                Vector3 playerPos = new Vector3(player.position.x, player.position.y + 1, player.position.z);
                Vector3 direction = (playerPos - projectileSpawnPoint.position).normalized;
                projectileScript.Initialize(direction);
                Debug.Log("Projectile initialized with direction.");
                anim.SetTrigger("Attack");
                fire.Invoke();
                StartCoroutine(AttackCooldown());
            }
        }
    }

    public IEnumerator AttackCooldown(){
        attackReady = false;
        yield return new WaitForSeconds(attackCooldown);
        attackReady = true;
    }

    private void AttackMelee()
    {
        if (attackReady)
        {
            anim.SetTrigger("Attack");
            lastAttackTime = Time.time;
            Debug.Log($"Melee enemy attacked the player for {meleeDamage} damage!");
            HitPlayer.Invoke(meleeDamage);
            playerSound.Invoke();
            StartCoroutine(AttackCooldown());
            // Optionally, apply damage directly to the player's health script (if implemented)
        }
    }
}

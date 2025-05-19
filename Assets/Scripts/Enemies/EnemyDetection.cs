using UnityEngine;

public class EnemyDetection : MonoBehaviour
{
    public float detectionDistance = 15f;
    public float viewAngle = 30f;
    private Camera playerCamera;
    private EnemyBehavior enemyBehavior;

    void Start()
    {
        playerCamera = Camera.main;
        enemyBehavior = GetComponent<EnemyBehavior>();
    }

    void Update()
    {
        if (IsPlayerLookingAtMe())
        {
            enemyBehavior.SetSeen(true);
        }
        else
        {
            enemyBehavior.SetSeen(false);
        }
    }

    private bool IsPlayerLookingAtMe()
    {
        Vector3 toEnemy = transform.position - playerCamera.transform.position;
        float angleToEnemy = Vector3.Angle(playerCamera.transform.forward, toEnemy);

        if (angleToEnemy < viewAngle && toEnemy.magnitude < detectionDistance)
        {
            // Raycast to check visibility (no walls between)
            Ray ray = new Ray(playerCamera.transform.position, toEnemy.normalized);
            if (Physics.Raycast(ray, out RaycastHit hit, detectionDistance))
            {
                return true;
            }
        }

        return false;
    }
}


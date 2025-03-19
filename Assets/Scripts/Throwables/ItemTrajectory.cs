using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ItemTrajectory : MonoBehaviour
{
    private LineRenderer lineRenderer;
    public GameObject Grenade;
    private Rigidbody rb;
    private GrenadeThrow grenadeThrow;
    public int numPoints = 20;
    private float timeStep = 0.1f;
    

    private void Awake(){
        lineRenderer = GetComponent<LineRenderer>();
        grenadeThrow = Grenade.GetComponentInParent<GrenadeThrow>();
        rb = grenadeThrow.GetComponent<Rigidbody>();
    }

    private void OnEnable(){
        lineRenderer.positionCount = numPoints;
    }
    

    void Update() {
        float customGravity = grenadeThrow.GetCustomGravity();
        Vector3 gravity = new Vector3(0, customGravity, 0);
        
        // Fetch the latest throw direction and speed each frame
        Vector3 direction = grenadeThrow.GetDirection();
        float throwForce = grenadeThrow.GetThrowSpeed();
        Vector3 launchVelocity = direction * throwForce;

        Vector3 position = Grenade.transform.position;
        lineRenderer.SetPosition(0, position);

        Vector3 nextVel = launchVelocity;
        Vector3 nextPos = position;

        int currentPoint = 1;

        for (int i = 1; i < numPoints; i++) {
            nextVel += gravity * timeStep;
            nextVel *= (1 - rb.linearDamping * timeStep);
            nextPos += nextVel * timeStep;

            RaycastHit hit;
            if (Physics.Linecast(position, nextPos, out hit)) {
                if (i < lineRenderer.positionCount) {
                    lineRenderer.SetPosition(i, hit.point);
                }
                currentPoint = i + 1;
                break;
            }

            if (i < lineRenderer.positionCount) {
                lineRenderer.SetPosition(i, nextPos);
            }
            position = nextPos;
            currentPoint = i + 1;
        }
        lineRenderer.positionCount = currentPoint;
    }


    private void OnDisable(){
        lineRenderer.positionCount = 0;//Reset the line.
    }
}

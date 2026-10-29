using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class ShipTarget : MonoBehaviour
{
    public static int ShipsDestroyed { get; private set; }
    public static event Action<ShipTarget> ShipDestroyedEvent;
    public static event Action<ShipTarget> ShipExitedEvent;

    private static readonly List<ShipTarget> ActiveShips = new List<ShipTarget>();
    private static Rigidbody2D PlayerBody;
    [SerializeField, Min(0.1f)] private float moveSpeed = 0.8f;
    [SerializeField, Min(0.1f)] private float neighborRadius = 3.5f;
    [SerializeField, Min(0.1f)] private float separationRadius = 0.8f;
    [SerializeField, Min(0.1f)] private float playerAvoidanceRadius = 1.8f;
    private bool destroyed;
    private Rigidbody2D body;
    private Vector2 wanderDirection;
    private float nextWanderChange;
    private float playerAvoidanceSide;

    public static void RegisterPlayer(Rigidbody2D playerBody)
    {
        PlayerBody = playerBody;
    }

    public static void UnregisterPlayer(Rigidbody2D playerBody)
    {
        if (PlayerBody == playerBody)
        {
            PlayerBody = null;
        }
    }

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;

        CircleCollider2D hitbox = GetComponent<CircleCollider2D>();
        hitbox.radius = 0.35f;
        wanderDirection = UnityEngine.Random.insideUnitCircle.normalized;
        if (wanderDirection == Vector2.zero)
        {
            wanderDirection = Vector2.right;
        }
        playerAvoidanceSide = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        nextWanderChange = Time.time + UnityEngine.Random.Range(1.5f, 3f);
        ActiveShips.Add(this);
    }

    public void InitializeMovement(Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector2.left;
        }

        wanderDirection = direction.normalized;
        body.linearVelocity = wanderDirection * moveSpeed;
        FaceDirection(wanderDirection);
    }

    void FixedUpdate()
    {
        if (Time.time >= nextWanderChange)
        {
            wanderDirection = (wanderDirection + UnityEngine.Random.insideUnitCircle * 0.25f).normalized;
            nextWanderChange = Time.time + UnityEngine.Random.Range(2.5f, 4f);
        }

        Vector2 alignment = Vector2.zero;
        Vector2 cohesion = Vector2.zero;
        Vector2 separation = Vector2.zero;
        int neighborCount = 0;
        int separationCount = 0;

        foreach (ShipTarget other in ActiveShips)
        {
            if (other == null || other == this || other.destroyed)
            {
                continue;
            }

            Vector2 offset = (Vector2)other.transform.position - body.position;
            float distance = offset.magnitude;
            if (distance > neighborRadius)
            {
                continue;
            }

            alignment += other.body.linearVelocity;
            cohesion += (Vector2)other.transform.position;
            neighborCount++;

            if (distance < separationRadius && distance > 0.001f)
            {
                separation -= offset / distance;
                separationCount++;
            }
        }

        Vector2 steering = wanderDirection;
        if (PlayerBody != null)
        {
            Vector2 offsetToPlayer = PlayerBody.position - body.position;
            float distanceToPlayer = offsetToPlayer.magnitude;
            if (distanceToPlayer <= neighborRadius)
            {
                alignment += PlayerBody.linearVelocity;
                cohesion += PlayerBody.position;
                neighborCount++;
            }

            if (distanceToPlayer < playerAvoidanceRadius && distanceToPlayer > 0.001f)
            {
                Vector2 awayFromPlayer = -offsetToPlayer / distanceToPlayer;
                Vector2 tangent = new Vector2(-awayFromPlayer.y, awayFromPlayer.x);
                tangent *= playerAvoidanceSide;

                float avoidance = 1f - distanceToPlayer / playerAvoidanceRadius;
                steering += awayFromPlayer * (0.8f + avoidance * 1.5f)
                    + tangent * avoidance * 1.5f;
            }
        }

        if (neighborCount > 0)
        {
            alignment /= neighborCount;
            cohesion = cohesion / neighborCount - body.position;
            steering += alignment.normalized * 1.1f + cohesion.normalized * 0.9f;
        }

        if (separationCount > 0)
        {
            steering += (separation / separationCount).normalized * 1.5f;
        }

        Camera camera = Camera.main;
        if (camera != null && camera.orthographic)
        {
            Vector3 viewportPosition = camera.WorldToViewportPoint(body.position);
            if (viewportPosition.x <= 0f)
            {
                ExitScreen();
                return;
            }

            if (viewportPosition.x > 0.95f)
            {
                steering.x = Mathf.Min(steering.x, -0.5f);
            }

            if (viewportPosition.y < 0.15f)
            {
                steering.y = Mathf.Abs(steering.y) + 0.5f;
            }
            else if (viewportPosition.y > 0.85f)
            {
                steering.y = -Mathf.Abs(steering.y) - 0.5f;
            }

            float cameraDistance = Mathf.Abs(camera.transform.position.z - transform.position.z);
            Vector3 lowerBound = camera.ViewportToWorldPoint(new Vector3(0f, 0.05f, cameraDistance));
            Vector3 upperBound = camera.ViewportToWorldPoint(new Vector3(0.95f, 0.95f, cameraDistance));
            Vector2 boundedPosition = new Vector2(
                Mathf.Min(body.position.x, upperBound.x),
                Mathf.Clamp(body.position.y, lowerBound.y, upperBound.y));
            body.position = boundedPosition;
        }

        steering.x = Mathf.Min(steering.x, -0.2f);
        if (steering.sqrMagnitude > 0.001f)
        {
            Vector2 velocity = steering.normalized * moveSpeed;
            body.linearVelocity = velocity;
            FaceDirection(velocity);
        }
    }

    void ExitScreen()
    {
        if (destroyed)
        {
            return;
        }

        destroyed = true;
        ShipExitedEvent?.Invoke(this);
        Destroy(gameObject);
    }

    void FaceDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude > 0.001f)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            body.MoveRotation(angle);
        }
    }

    public void DestroyShip()
    {
        if (destroyed)
        {
            return;
        }

        destroyed = true;
        ShipsDestroyed++;
        Debug.Log($"Ships destroyed: {ShipsDestroyed}");
        ShipDestroyedEvent?.Invoke(this);
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        ActiveShips.Remove(this);
    }

}

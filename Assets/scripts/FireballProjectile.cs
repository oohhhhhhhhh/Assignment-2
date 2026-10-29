using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class FireballProjectile : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float lifetime = 5f;

    private Rigidbody2D body;
    private Camera gameplayCamera;
    private bool initialized;

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.freezeRotation = true;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        CircleCollider2D hitbox = GetComponent<CircleCollider2D>();
        hitbox.isTrigger = true;
        hitbox.radius = 0.08f;
        gameplayCamera = Camera.main;
    }

    public void Initialize(Vector2 direction, float speed)
    {
        body.linearVelocity = direction.normalized * speed;
        initialized = true;
        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        if (gameplayCamera == null)
        {
            gameplayCamera = Camera.main;
        }

        if (gameplayCamera == null)
        {
            return;
        }

        Vector3 viewportPosition = gameplayCamera.WorldToViewportPoint(transform.position);
        if (viewportPosition.z < 0f || viewportPosition.x < 0f || viewportPosition.x > 1f
            || viewportPosition.y < 0f || viewportPosition.y > 1f)
        {
            Destroy(gameObject);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!initialized)
        {
            return;
        }

        ShipTarget hitShip = other.GetComponentInParent<ShipTarget>();
        if (hitShip != null)
        {
            hitShip.DestroyShip();
            Destroy(gameObject);
        }
    }
}

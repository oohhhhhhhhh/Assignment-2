using System.Collections;
using Mediapipe;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.HandLandmarker;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    private Rigidbody2D rb;
    [SerializeField] private float moveSpeed = 2;
    [SerializeField] private bool useMediaPipe = true;
    [SerializeField] private bool mirrorCameraMovement;
    [SerializeField, Range(0.05f, 0.4f)] private float handDeadzone = 0.12f;
    [SerializeField, Range(1, 30)] private int handDetectionFps = 15;
    [SerializeField] private GameObject redFireballPrefab;
    [SerializeField] private GameObject ship1Prefab;
    [SerializeField, Min(1)] private int flockSize = 5;
    [SerializeField, Min(0.1f)] private float fireballSpeed = 8f;
    [SerializeField, Min(0.05f)] private float fireballCooldown = 0.3f;
    private const float WarningInactivitySeconds = 15f;
    private const float TerminationInactivitySeconds = 30f;
    private Vector2 playerDirection;
    private bool hasTrackedHand;
    private Vector2 handDirection;
    private bool handFistClosed;
    private bool handFistWasClosed;
    private WebCamTexture webcam;
    private Texture2D frameTexture;
    private Color32[] cameraPixels;
    private HandLandmarker handLandmarker;
    private HandLandmarkerResult handLandmarkerResult;
    private float nextFireTime;
    private bool warnedAboutMissingFireball;
    private bool warnedAboutMissingShipPrefab;
    private bool shipFleetInitialized;
    private Camera gameplayCamera;
    private AudioSource lossAudioSource;
    private AudioClip lossSound;
    private AudioClip laserSound;
    private AudioClip explosionSound;
    private const float InitialExplosionVolume = 0.4f;
    private const float ExplosionVolumePerShip = 0.06f;
    private GUIStyle inactivityTextStyle;
    private int observedShipsDestroyed;
    private float lastShipDestroyedTime;
    private bool showingInactivityWarning;
    private bool terminated;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        gameplayCamera = Camera.main;
        lossAudioSource = gameObject.AddComponent<AudioSource>();
        lossAudioSource.playOnAwake = false;
        lossAudioSource.loop = false;
        lossAudioSource.volume = 1f;
        lossAudioSource.spatialBlend = 0f;
        lossAudioSource.ignoreListenerPause = true;
    }

    void OnEnable()
    {
        ShipTarget.ShipDestroyedEvent += OnShipDestroyed;
        ShipTarget.ShipExitedEvent += OnShipExited;
        ShipTarget.RegisterPlayer(rb);
        if (useMediaPipe)
        {
            StartCoroutine(ReadMediaPipeInput());
        }
    }

    void Start()
    {
        observedShipsDestroyed = ShipTarget.ShipsDestroyed;
        lastShipDestroyedTime = Time.unscaledTime;
        lossSound = Resources.Load<AudioClip>("Sounds/loss");
        if (lossSound == null)
        {
            Debug.LogError("Loss sound not found at Resources/Sounds/loss.mp3.", this);
        }
        laserSound = Resources.Load<AudioClip>("Sounds/laser");
        if (laserSound == null)
        {
            Debug.LogError("Laser sound not found at Resources/Sounds/laser.mp3.", this);
        }
        explosionSound = Resources.Load<AudioClip>("Sounds/exp");
        if (explosionSound == null)
        {
            Debug.LogError("Explosion sound not found at Resources/Sounds/exp.mp3.", this);
        }

        PlacePlayerOnLeftSide();
        shipFleetInitialized = true;
        if (ship1Prefab == null)
        {
            if (!warnedAboutMissingShipPrefab)
            {
                Debug.LogError("Assign the ship1 prefab on PlayerController to spawn the flock.", this);
                warnedAboutMissingShipPrefab = true;
            }
            return;
        }

        while (CountActiveShips() < flockSize)
        {
            SpawnShip1();
        }

    }

    void Update()
    {
        UpdateInactivityState();
        if (terminated)
        {
            return;
        }

        Vector2 direction = Vector2.zero;

        if (hasTrackedHand)
        {
            direction = handDirection;
        }
        else
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) direction.x -= 1;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) direction.x += 1;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) direction.y -= 1;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) direction.y += 1;
            }
        }

        Gamepad gamepad = Gamepad.current;
        if (!hasTrackedHand && direction.sqrMagnitude == 0 && gamepad != null)
        {
            direction = gamepad.leftStick.ReadValue();
        }

        playerDirection = direction.normalized;
        if (WasFirePressed())
        {
            ShootAtTarget();
        }
    }

    void UpdateInactivityState()
    {
        int destroyedCount = ShipTarget.ShipsDestroyed;
        if (destroyedCount > observedShipsDestroyed)
        {
            observedShipsDestroyed = destroyedCount;
            lastShipDestroyedTime = Time.unscaledTime;
            showingInactivityWarning = false;
        }

        float inactivityDuration = Time.unscaledTime - lastShipDestroyedTime;
        showingInactivityWarning = inactivityDuration >= WarningInactivitySeconds;
        if (inactivityDuration >= TerminationInactivitySeconds)
        {
            terminated = true;
            playerDirection = Vector2.zero;
            if (lossSound != null)
            {
                lossAudioSource.PlayOneShot(lossSound);
            }
            Time.timeScale = 0f;
        }
    }

    void OnGUI()
    {
        if (!showingInactivityWarning && !terminated)
        {
            return;
        }

        UnityEngine.Color previousColor = GUI.color;
        GUI.color = terminated
            ? UnityEngine.Color.black
            : new UnityEngine.Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new UnityEngine.Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);

        GUI.color = UnityEngine.Color.red;
        if (inactivityTextStyle == null)
        {
            inactivityTextStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
        }

        inactivityTextStyle.fontSize = Mathf.Clamp(
            Mathf.Min(Screen.height / 7, Screen.width / 14),
            24,
            180);
        string message = terminated
            ? "YOU HAVE BEEN TERMINATED"
            : "COMPLETE YOUR TASK OR YOU WILL BE TERMINATED";
        GUI.Label(
            new UnityEngine.Rect(Screen.width * 0.04f, Screen.height * 0.1f, Screen.width * 0.92f, Screen.height * 0.8f),
            message,
            inactivityTextStyle);
        GUI.color = previousColor;
    }

    void FixedUpdate()
    {
        rb.linearVelocity = playerDirection * moveSpeed;
        if (playerDirection.sqrMagnitude > 0.001f)
        {
            float angle = Mathf.Atan2(playerDirection.y, playerDirection.x) * Mathf.Rad2Deg - 90f;
            rb.MoveRotation(angle);
        }
    }

    IEnumerator ReadMediaPipeInput()
    {
        TextAsset model = Resources.Load<TextAsset>("MediaPipe/hand_landmarker");
        if (model == null)
        {
            Debug.LogError("MediaPipe hand model not found at Resources/MediaPipe/hand_landmarker.bytes.");
            yield break;
        }

        WebCamDevice[] cameras = WebCamTexture.devices;
        if (cameras.Length == 0)
        {
            Debug.LogWarning("No webcam found. Use keyboard or gamepad input instead.");
            yield break;
        }

        HandLandmarkerOptions options = new HandLandmarkerOptions(
            new Mediapipe.Tasks.Core.BaseOptions(
                Mediapipe.Tasks.Core.BaseOptions.Delegate.CPU,
                modelAssetBuffer: model.bytes),
            runningMode: Mediapipe.Tasks.Vision.Core.RunningMode.VIDEO,
            numHands: 1);
        handLandmarker = HandLandmarker.CreateFromOptions(options);
        handLandmarkerResult = HandLandmarkerResult.Alloc(1);

        webcam = new WebCamTexture(cameras[0].name, 640, 480, handDetectionFps);
        webcam.Play();

        float cameraStartTime = Time.realtimeSinceStartup;
        while (webcam.width <= 16 && Time.realtimeSinceStartup - cameraStartTime < 10f)
        {
            yield return null;
        }

        if (webcam.width <= 16)
        {
            Debug.LogError("The webcam did not start. Check camera permissions and try again.");
            webcam.Stop();
            webcam = null;
            ((System.IDisposable)handLandmarker).Dispose();
            handLandmarker = null;
            yield break;
        }

        frameTexture = new Texture2D(webcam.width, webcam.height, TextureFormat.RGBA32, false);
        cameraPixels = new Color32[webcam.width * webcam.height];

        var waitForNextFrame = new WaitForSeconds(1f / handDetectionFps);
        while (isActiveAndEnabled && webcam != null)
        {
            yield return waitForNextFrame;
            if (!webcam.didUpdateThisFrame)
            {
                continue;
            }

            webcam.GetPixels32(cameraPixels);
            frameTexture.SetPixels32(cameraPixels);
            frameTexture.Apply(false);

            using (var image = new Image(frameTexture))
            {
                bool detected = handLandmarker.TryDetectForVideo(
                    image,
                    (long)(Time.realtimeSinceStartupAsDouble * 1000),
                    null,
                    ref handLandmarkerResult);

                if (detected && handLandmarkerResult.handLandmarks != null && handLandmarkerResult.handLandmarks.Count > 0)
                {
                    NormalizedLandmarks hand = handLandmarkerResult.handLandmarks[0];
                    handDirection = GetHandDirection(hand);
                    handFistClosed = IsClosedFist(hand);
                    hasTrackedHand = true;
                }
                else
                {
                    handDirection = Vector2.zero;
                    handFistClosed = false;
                    hasTrackedHand = false;
                }
            }
        }
    }

    Vector2 GetHandDirection(NormalizedLandmarks hand)
    {
        if (hand.landmarks == null || hand.landmarks.Count <= 17)
        {
            return Vector2.zero;
        }

        float x = (hand.landmarks[0].x + hand.landmarks[5].x + hand.landmarks[9].x
            + hand.landmarks[13].x + hand.landmarks[17].x) / 5f;
        float y = (hand.landmarks[0].y + hand.landmarks[5].y + hand.landmarks[9].y
            + hand.landmarks[13].y + hand.landmarks[17].y) / 5f;
        float horizontal = mirrorCameraMovement ? 0.5f - x : x - 0.5f;
        Vector2 offsetFromCenter = new Vector2(horizontal, 0.5f - y);

        return offsetFromCenter.magnitude < handDeadzone
            ? Vector2.zero
            : offsetFromCenter.normalized;
    }

    bool IsClosedFist(NormalizedLandmarks hand)
    {
        if (hand.landmarks == null || hand.landmarks.Count <= 20)
        {
            return false;
        }

        Vector2 wrist = new Vector2(hand.landmarks[0].x, hand.landmarks[0].y);
        int[] pipIndices = { 6, 10, 14, 18 };
        int[] tipIndices = { 8, 12, 16, 20 };
        int curledFingers = 0;

        for (int i = 0; i < pipIndices.Length; i++)
        {
            Vector2 pip = new Vector2(hand.landmarks[pipIndices[i]].x, hand.landmarks[pipIndices[i]].y);
            Vector2 tip = new Vector2(hand.landmarks[tipIndices[i]].x, hand.landmarks[tipIndices[i]].y);
            if (Vector2.Distance(wrist, tip) < Vector2.Distance(wrist, pip) * 1.1f)
            {
                curledFingers++;
            }
        }

        return curledFingers >= 3;
    }

    bool WasFirePressed()
    {
        Keyboard keyboard = Keyboard.current;
        Gamepad gamepad = Gamepad.current;
        bool fistJustClosed = hasTrackedHand && handFistClosed && !handFistWasClosed;
        handFistWasClosed = hasTrackedHand && handFistClosed;
        return fistJustClosed
            || (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            || (gamepad != null && gamepad.rightShoulder.wasPressedThisFrame);
    }

    void ShootAtTarget()
    {
        if (Time.time < nextFireTime)
        {
            return;
        }

        if (redFireballPrefab == null)
        {
            if (!warnedAboutMissingFireball)
            {
                Debug.LogError("Assign the red fireball prefab on PlayerController to enable shooting.", this);
                warnedAboutMissingFireball = true;
            }
            return;
        }

        Vector2 direction = transform.up;
        Vector2 spawnPosition = rb.position + direction * 0.35f;
        GameObject fireball = Instantiate(redFireballPrefab, spawnPosition, Quaternion.FromToRotation(Vector3.up, direction));
        SpriteRenderer fireballRenderer = fireball.GetComponent<SpriteRenderer>();
        if (fireballRenderer != null)
        {
            fireballRenderer.sortingLayerName = "Background";
            fireballRenderer.sortingOrder = 1;
        }

        FireballProjectile projectile = fireball.GetComponent<FireballProjectile>();
        if (projectile == null)
        {
            projectile = fireball.AddComponent<FireballProjectile>();
        }

        projectile.Initialize(direction, fireballSpeed);
        if (laserSound != null)
        {
            lossAudioSource.PlayOneShot(laserSound);
        }
        nextFireTime = Time.time + fireballCooldown;
    }

    void PlacePlayerOnLeftSide()
    {
        if (gameplayCamera == null)
        {
            gameplayCamera = Camera.main;
        }

        if (gameplayCamera == null || !gameplayCamera.orthographic)
        {
            return;
        }

        float distance = Mathf.Abs(gameplayCamera.transform.position.z - transform.position.z);
        Vector3 startPosition = gameplayCamera.ViewportToWorldPoint(new Vector3(0.15f, 0.5f, distance));
        startPosition.z = transform.position.z;
        rb.position = startPosition;
        rb.rotation = -90f;
    }

    int CountActiveShips()
    {
        ShipTarget[] ships = FindObjectsByType<ShipTarget>();
        int count = 0;
        foreach (ShipTarget ship in ships)
        {
            if (ship != null && ship.isActiveAndEnabled)
            {
                count++;
            }
        }
        return count;
    }

    void SpawnShip1()
    {
        if (ship1Prefab == null)
        {
            return;
        }

        Vector3 spawnPosition;
        if (gameplayCamera != null && gameplayCamera.orthographic)
        {
            float cameraDistance = Mathf.Abs(gameplayCamera.transform.position.z - transform.position.z);
            spawnPosition = gameplayCamera.ViewportToWorldPoint(new Vector3(1.05f, Random.Range(0.15f, 0.85f), cameraDistance));
            spawnPosition.z = transform.position.z;
        }
        else
        {
            spawnPosition = transform.position + Vector3.right * 5f
                + Vector3.up * Random.Range(-2f, 2f);
        }

        GameObject shipObject = Instantiate(ship1Prefab, spawnPosition, Quaternion.identity);
        shipObject.name = "ship1";
        ShipTarget ship = shipObject.GetComponent<ShipTarget>();
        if (ship == null)
        {
            ship = shipObject.AddComponent<ShipTarget>();
        }
        ship.InitializeMovement(Vector2.left);
    }

    void OnShipDestroyed(ShipTarget destroyedShip)
    {
        if (explosionSound != null)
        {
            float explosionVolume = Mathf.Clamp01(
                InitialExplosionVolume + (ShipTarget.ShipsDestroyed - 1) * ExplosionVolumePerShip);
            lossAudioSource.PlayOneShot(explosionSound, explosionVolume);
        }

        if (shipFleetInitialized && ship1Prefab != null)
        {
            SpawnShip1();
        }
    }

    void OnShipExited(ShipTarget exitedShip)
    {
        if (shipFleetInitialized && ship1Prefab != null)
        {
            SpawnShip1();
        }
    }

    void OnDisable()
    {
        if (terminated)
        {
            Time.timeScale = 1f;
        }

        ShipTarget.ShipDestroyedEvent -= OnShipDestroyed;
        ShipTarget.ShipExitedEvent -= OnShipExited;
        ShipTarget.UnregisterPlayer(rb);
        hasTrackedHand = false;
        handDirection = Vector2.zero;
        handFistClosed = false;
        handFistWasClosed = false;
        if (handLandmarker != null)
        {
            ((System.IDisposable)handLandmarker).Dispose();
        }
        handLandmarker = null;

        if (webcam != null)
        {
            webcam.Stop();
            webcam = null;
        }

        if (frameTexture != null)
        {
            Destroy(frameTexture);
            frameTexture = null;
        }
    }
}

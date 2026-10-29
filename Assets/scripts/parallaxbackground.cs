using UnityEngine;

public class parallaxbackground : MonoBehaviour
{
    [SerializeField] private float moveSpeed;
    float backgroundWidth;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Sprite sprite = GetComponent<SpriteRenderer>().sprite;
        backgroundWidth = sprite.texture.width / sprite.pixelsPerUnit;
    }

    // Update is called once per frame
    void Update()
    {
        float moveX = moveSpeed * Time.deltaTime;
        transform.position += new Vector3(moveX, 0f, 0f);
        if (Mathf.Abs(transform.position.x) - backgroundWidth > 0)
        {
            transform.position = new Vector3(0f, transform.position.y, 0f);
        }
    }
}

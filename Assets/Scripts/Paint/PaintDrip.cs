using UnityEngine;

// جزيء دهان واحد بيسقط ويرسم على اللوحة
public class PaintDrip : MonoBehaviour
{
    [Header("=== خصائص الجزيء ===")]
    public float gravity = 9.81f;
    public float airResistance = 0.02f;
    public float lifeTime = 3f;
    public float splashRadius = 0.1f;

    private Vector3 velocity;
    private Color paintColor;
    private CanvasPainter canvasPainter;
    private float age = 0f;
    private bool hasSplashed = false;

    public void Initialize(Vector3 startVelocity, Color color, CanvasPainter painter)
    {
        velocity = startVelocity;
        paintColor = color;
        canvasPainter = painter;

        // لوّن الجزيء
        var renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = color;
        }
    }

    void Update()
    {
        if (hasSplashed) return;

        age += Time.deltaTime;

        if (age >= lifeTime)
        {
            Destroy(gameObject);
            return;
        }

        // فيزياء السقوط
        StepPhysics(Time.deltaTime);

        // تحقق من اصطدام باللوحة
        CheckCanvasCollision();
    }

    void StepPhysics(float dt)
    {
        // جاذبية + مقاومة هواء
        velocity.y -= gravity * dt;
        velocity *= (1f - airResistance * dt);
        transform.position += velocity * dt;
    }

    void CheckCanvasCollision()
    {
        // رمي شعاع للأسفل
        Ray ray = new Ray(transform.position, Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, 0.1f))
        {
            if (hit.collider.CompareTag("Canvas"))
            {
                Splash(hit.point, hit.textureCoord);
            }
        }
    }

    void Splash(Vector3 worldPos, Vector2 texCoord)
    {
        hasSplashed = true;

        // ارسم على اللوحة
        if (canvasPainter != null)
        {
            float size = splashRadius * (1f + velocity.magnitude * 0.1f);
            canvasPainter.Paint(texCoord, paintColor, size);
        }

        // اخفي الجزيء
        gameObject.SetActive(false);
        Destroy(gameObject, 0.1f);
    }
}
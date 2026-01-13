using UnityEngine;
using UnityEngine.InputSystem;

public class ZacsMovement : MonoBehaviour
{
    private float horizontal;
    public float speed = 5f;

    // Update is called once per frame
    void Update()
    {
        horizontal =
            (Keyboard.current.dKey.isPressed ? 1f : 0f) + 
            (Keyboard.current.aKey.isPressed ? -1f : 0f);
        transform.position += new Vector3(horizontal, 0, 0) * speed * Time.deltaTime;
    }
}

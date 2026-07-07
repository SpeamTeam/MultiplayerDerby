// WARN: this code is only for testing purposes,
// if you find this code in production, please 
// report about it to some of the maintainers


using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody), typeof(PlayerInput))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpForce = 5f;
    [SerializeField] private float groundCheckDistance = 0.6f;
    
    [Header("References")]
    [SerializeField] private LayerMask groundLayer;
    
    private Rigidbody rb;
    private bool isGrounded;
    private Vector2 _moveInput;
    private bool _jumpPressed;
    
    // Input System
    private SampleInputActions inputActions;
    
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        
        // Инициализация Input System
        inputActions = new SampleInputActions();
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (IsOwner)
        {
            inputActions.Enable();
        }
        GetComponent<EventSystem>().enabled = IsOwner;
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        
        if (IsOwner)
        {
            inputActions.Disable();
        }
    }

    public void OnMovement(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        _moveInput = context.ReadValue<Vector2>();
    }

    public void OnJump()
    {
        if (isGrounded)
            _jumpPressed = true;
    }
    
    private void FixedUpdate()
    {
        if (!IsOwner) return;
        
        CheckGround();
        MoveServerRPC(_moveInput);
        
        // Обрабатываем прыжок
        if (_jumpPressed && isGrounded)
        {
            JumpServerRPC();
            _jumpPressed = false;
        }
    }


    [ServerRpc]
    private void MoveServerRPC(Vector2 moveInput)
    {
        // Преобразуем 2D ввод в 3D движение
        Vector3 moveDirection = new Vector3(moveInput.x, 0f, moveInput.y).normalized;
        Vector3 velocity = moveDirection * moveSpeed;
        velocity.y = rb.linearVelocity.y; // Сохраняем вертикальную скорость
        
        rb.linearVelocity = velocity;
    }

    [ServerRpc]
    private void JumpServerRPC()
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    }
    
    private void CheckGround()
    {
        isGrounded = Physics.Raycast(
            gameObject.transform.position - new Vector3(0,0.5f,0), 
            Vector3.down, 
            groundCheckDistance, 
            groundLayer
        );
    }
    
    public override void OnDestroy()
    {
        inputActions?.Dispose();
    }
}

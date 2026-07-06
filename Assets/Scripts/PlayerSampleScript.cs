// WARN: this code is only for testing purposes,
// if you find this code in production, please 
// report about it to some of the maintainers


using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpForce = 5f;
    [SerializeField] private float groundCheckDistance = 0.6f;
    
    [Header("References")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private LayerMask groundLayer;
    
    private Rigidbody rb;
    private bool isGrounded;
    private Vector2 moveInput;
    private bool jumpPressed;
    
    // Input System
    private SampleInputActions inputActions;
    
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        
        // Инициализация Input System
        inputActions = new SampleInputActions();
        
        // Создаём точку проверки земли, если её нет
        if (groundCheck == null)
        {
            GameObject checkObj = new GameObject("GroundCheck");
            checkObj.transform.parent = transform;
            checkObj.transform.localPosition = new Vector3(0, -0.5f, 0);
            groundCheck = checkObj.transform;
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Включаем ввод только для локального игрока
        if (IsOwner)
        {
            inputActions.Enable();
            
            // Подписываемся на события
            inputActions.SamplePlayer.Jump.performed += OnJump;
        }
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        
        if (IsOwner)
        {
            // Отписываемся от событий
            inputActions.SamplePlayer.Jump.performed -= OnJump;
            inputActions.Disable();
        }
    }
    
    private void Update()
    {
        if (!IsOwner) return;
        
        // Читаем движение каждый кадр
        moveInput = inputActions.SamplePlayer.Movement.ReadValue<Vector2>();
    }
    
    private void FixedUpdate()
    {
        if (!IsOwner) return;
        
        CheckGround();
        Move();
        
        // Обрабатываем прыжок
        if (jumpPressed && isGrounded)
        {
            Jump();
            jumpPressed = false;
        }
    }
    
    private void OnJump(InputAction.CallbackContext context)
    {
        if (isGrounded)
        {
            jumpPressed = true;
        }
    }
    
    private void Move()
    {
        // Преобразуем 2D ввод в 3D движение
        Vector3 moveDirection = new Vector3(moveInput.x, 0f, moveInput.y).normalized;
        Vector3 velocity = moveDirection * moveSpeed;
        velocity.y = rb.linearVelocity.y; // Сохраняем вертикальную скорость
        
        rb.linearVelocity = velocity;
    }
    
    private void Jump()
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    }
    
    private void CheckGround()
    {
        isGrounded = Physics.Raycast(
            groundCheck.position, 
            Vector3.down, 
            groundCheckDistance, 
            groundLayer
        );
    }
    
    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Gizmos.DrawLine(
                groundCheck.position, 
                groundCheck.position + Vector3.down * groundCheckDistance
            );
        }
    }
    
    public override void OnDestroy()
    {
        inputActions?.Dispose();
    }
}

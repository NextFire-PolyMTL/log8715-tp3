#define AZERTY

using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class Player : NetworkBehaviour
{
    #region keycodes
#if AZERTY
    private const KeyCode m_UpKeyCode = KeyCode.Z;
    private const KeyCode m_LeftKeyCode = KeyCode.Q;
    private const KeyCode m_DownKeyCode = KeyCode.S;
    private const KeyCode m_RightKeyCode = KeyCode.D;
#else
    private const KeyCode m_UpKeyCode = KeyCode.W;
    private const KeyCode m_LeftKeyCode = KeyCode.A;
    private const KeyCode m_DownKeyCode = KeyCode.S;
    private const KeyCode m_RightKeyCode = KeyCode.D;
#endif
    #endregion

    [SerializeField]
    private float m_Velocity;

    [SerializeField]
    private float m_Size = 1;

    private GameState m_GameState;

    // GameState peut etre nul si l'entite joueur est instanciee avant de charger MainScene
    private GameState GameState
    {
        get
        {
            if (m_GameState == null)
            {
                m_GameState = FindObjectOfType<GameState>();
            }
            return m_GameState;
        }
    }

    private NetworkVariable<Vector2> m_Position = new NetworkVariable<Vector2>();

    private Vector2 m_LocalPosition;
    public Vector2 Position => (IsClient && IsOwner) ? m_LocalPosition : m_Position.Value;

    private Queue<Vector2> m_InputQueue = new Queue<Vector2>();

    private void Awake()
    {
        m_GameState = FindObjectOfType<GameState>();

        if (IsClient && IsOwner)
        {
            m_LocalPosition = new();
        }
    }

    private void FixedUpdate()
    {
        // Si le stun est active, rien n'est mis a jour.
        if (GameState == null || GameState.IsStunned)
        {
            return;
        }

        // Seul le serveur met à jour la position de l'entite.
        if (IsServer)
        {
            m_Position.Value = UpdatedPosition(m_Position.Value);
        }

        // Seul le client qui possede cette entite peut envoyer ses inputs.
        if (IsClient && IsOwner)
        {
            UpdateInputClient();
            m_LocalPosition = UpdatedPosition(m_LocalPosition);
        }
    }

    private Vector2 UpdatedPosition(Vector2 position)
    {
        // Mise a jour de la position selon dernier input reçu, puis consommation de l'input
        if (m_InputQueue.Count > 0)
        {
            var input = m_InputQueue.Dequeue();
            position += input * m_Velocity * Time.deltaTime;

            // Gestion des collisions avec l'exterieur de la zone de simulation
            var size = GameState.GameSize;
            if (position.x - m_Size < -size.x)
            {
                position = new Vector2(-size.x + m_Size, position.y);
            }
            else if (position.x + m_Size > size.x)
            {
                position = new Vector2(size.x - m_Size, position.y);
            }

            if (position.y + m_Size > size.y)
            {
                position = new Vector2(position.x, size.y - m_Size);
            }
            else if (position.y - m_Size < -size.y)
            {
                position = new Vector2(position.x, -size.y + m_Size);
            }
        }
        return position;
    }

    private void UpdateInputClient()
    {
        Vector2 inputDirection = new Vector2(0, 0);
        if (Input.GetKey(m_UpKeyCode))
        {
            inputDirection += Vector2.up;
        }
        if (Input.GetKey(m_LeftKeyCode))
        {
            inputDirection += Vector2.left;
        }
        if (Input.GetKey(m_DownKeyCode))
        {
            inputDirection += Vector2.down;
        }
        if (Input.GetKey(m_RightKeyCode))
        {
            inputDirection += Vector2.right;
        }
        Vector2 input = inputDirection.normalized;
        m_InputQueue.Enqueue(input);
        SendInputServerRpc(input);
    }

    [ServerRpc]
    private void SendInputServerRpc(Vector2 input)
    {
        // On utilise une file pour les inputs pour les cas ou on en recoit plusieurs en meme temps.
        m_InputQueue.Enqueue(input);
    }
}

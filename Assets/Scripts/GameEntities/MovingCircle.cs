using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;


public struct CircleState : INetworkSerializeByMemcpy
{
    public Vector2 position;
    public Vector2 velocity;
    // Basic constructor
    public CircleState(Vector2 pos, Vector2 vel)
    {
        position = pos;
        velocity = vel;
    }
    // Constructor from a tickState
    public CircleState(tickState ts)
    {
        position = ts.position;
        velocity = ts.velocity;
    }
}

public struct tickState : INetworkSerializeByMemcpy
{
    public int tick;
    public Vector2 position;
    public Vector2 velocity;
    //Basic constructor
    public tickState(int t, Vector2 pos, Vector2 vel)
    {
        tick = t;
        position = pos;
        velocity = vel;
    }
    // Constructor using a tick and a circle state
    public tickState(int t, CircleState cs)
    {
        tick = t;
        position = cs.position;
        velocity = cs.velocity;
    }
}


public class MovingCircle : NetworkBehaviour
{
    [SerializeField]
    private float m_Radius = 1;
    private int m_LastTickSimulated = 0;

    private CircleState m_LocalState = new();
    private NetworkVariable<tickState> m_LastKnownState = new();
    public Vector2 Position => (IsClient) ? m_LocalState.position : m_LastKnownState.Value.position;

    public Vector2 Velocity => (IsClient) ? m_LocalState.velocity : m_LastKnownState.Value.velocity;

    public Vector2 InitialPosition, InitialVelocity;
    private Queue<tickState> m_History = new();

    private GameState m_GameState;

    private void Awake()
    {
        m_GameState = FindObjectOfType<GameState>();
    }

    public override void OnNetworkSpawn()
    {
        // Server initializes the circles
        if (IsServer)
        {
            m_LastKnownState.Value = new tickState
            {
                tick = NetworkUtility.GetLocalTick(),
                position = InitialPosition,
                velocity = InitialVelocity
            };
        }
        if (IsClient && IsOwner)
        {
            m_LastKnownState.OnValueChanged += OnLastServerChangeReceived;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsClient && IsOwner)
        {
            m_LastKnownState.OnValueChanged -= OnLastServerChangeReceived;
        }
    }


    private void FixedUpdate()
    {
        // Stun = skip update
        if (m_GameState.IsStunned)
        {
            return;
        }
        // The local client simulates the extra ticks due to latency
        if (IsClient /* && isOwner  */)
        {
            int lastServTick = m_LastKnownState.Value.tick;
            m_LastTickSimulated = Mathf.Max(lastServTick, m_LastTickSimulated);
            int currentTick = NetworkUtility.GetLocalTick();
            int tickDiff = currentTick - m_LastTickSimulated;
            while (tickDiff-- > 0)
            {
                var simu = tickSimulate(m_LocalState);
                m_LocalState.position = simu.position;
                m_LocalState.velocity = simu.velocity;
                m_LastTickSimulated = simu.tick;
            }
        }

        // Server updates the 'true' position of circles
        if (IsServer)
        {
            CircleState currentState = new CircleState(m_LastKnownState.Value.position, m_LastKnownState.Value.velocity);
            var newState = tickSimulate(currentState);
            m_LastKnownState.Value = newState;
        }
    }

    // Simulates only one step of circle physics
    private tickState tickSimulate(CircleState state)
    {
        Vector2 pos = state.position;
        Vector2 vel = state.velocity;
        // Mise a jour de la position du cercle selon sa vitesse
        pos += vel * Time.deltaTime;

        // Screen border collision management
        var size = m_GameState.GameSize;
        // Horizontal boundary
        if (pos.x - m_Radius < -size.x)
        {
            pos.x = -(size.x - m_Radius);
            vel.x *= -1;
        }
        else if (pos.x + m_Radius > size.x)
        {
            pos.x = size.x - m_Radius;
            vel.x *= -1;
        }
        // Vertical boundary
        if (pos.y + m_Radius > size.y)
        {
            pos.y = size.y - m_Radius;
            vel.y *= -1;
        }
        else if (pos.y - m_Radius < -size.y)
        {
            pos.y = -(size.y - m_Radius);
            vel.y *= -1;
        }

        var ret = new tickState(NetworkUtility.GetLocalTick(), pos, vel);

        // Make the client remember this simulation for reconciliation
        if (IsClient && IsOwner)
            m_History.Enqueue(ret);

        return ret;
    }

    private void OnLastServerChangeReceived(tickState previous, tickState current)
    {
        // Ignore older states
        while (m_History.Count > 0 && m_History.Peek().tick < current.tick)
            m_History.Dequeue();
        // Only focus on current tick, if the history isn't empty
        if (m_History.Count > 0 && m_History.Peek().tick == current.tick)
        {
            tickState clientSimu = m_History.Dequeue();
            if (IsOwner)
                // Reconciliate if the states differ
                if (clientSimu.position != current.position ||
                    clientSimu.velocity != current.velocity)
                {
                    Reconciliate(current);
                }
        }
    }

    // Called when m_History.Peek (client side) has the same tick as the currently examinated state (server)
    // and they have conflicting states
    private void Reconciliate(tickState serverSimulation)
    {
        Vector2 servPos = serverSimulation.position;
        Vector2 servVel = serverSimulation.velocity;
        Queue<tickState> correctedHistory = new();
        // Re-simulates the steps taking the server correcting into account
        while (m_History.Count > 0)
        {
            tickState clientSimu = m_History.Dequeue();
            tickState correctSimu = tickSimulate(new CircleState(servPos, servVel));
            servPos = correctSimu.position;
            servVel = correctSimu.velocity;
            correctedHistory.Enqueue(correctSimu);
        }
        // Apply corrections after simulation has been done again
        m_LocalState.position = servPos;
        m_LocalState.velocity = servVel;
        m_History = correctedHistory;
    }
}

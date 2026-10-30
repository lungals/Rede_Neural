using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Calcula a rota de referencia (A* e Dijkstra) entre o SpawnPoint e a chegada.
/// Serve SOMENTE para comparacao: nao controla a Capsule evoluida.
/// Desenha a rota com LineRenderer (visivel no Game/video) e Gizmos (Scene).
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public sealed class ReferenceRoute : MonoBehaviour
{
    [Header("Grid (cubra toda a pista)")]
    [SerializeField] private Vector3 gridCenter = Vector3.zero;
    [SerializeField] private Vector2 gridSize = new Vector2(40f, 40f);
    [SerializeField, Min(0.1f)] private float cellSize = 0.5f;
    [SerializeField] private float sampleHeight = 0.5f;
    [Tooltip("Raio da Capsule (+ folga). Celulas mais proximas que isso de uma parede ficam bloqueadas.")]
    [SerializeField, Min(0.05f)] private float agentRadius = 0.6f;
    [SerializeField] private LayerMask obstacleMask;

    [Header("Algoritmo usado como referencia")]
    [SerializeField] private PathAlgorithm referenceAlgorithm = PathAlgorithm.AStar;
    [SerializeField] private bool drawGridGizmos = false;

    private GridPathfinder grid;
    private LineRenderer line;
    private List<Vector3> lastPath = new();

    public float LastLength { get; private set; }
    public bool LastFound { get; private set; }
    public string LastComparison { get; private set; } = "";

    private void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.widthMultiplier = 0.15f;
        line.positionCount = 0;
        if (line.sharedMaterial == null)
            line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = line.endColor = Color.yellow;
    }

    /// <summary>Refaz o bake (obstaculos podem ter mudado) e calcula A* e Dijkstra.</summary>
    public float Compute(Vector3 start, Vector3 goal)
    {
        grid = new GridPathfinder(gridCenter, gridSize, cellSize, sampleHeight);
        grid.Bake(agentRadius, obstacleMask);

        GridPathfinder.Result astar = grid.FindPath(start, goal, PathAlgorithm.AStar);
        GridPathfinder.Result dijkstra = grid.FindPath(start, goal, PathAlgorithm.Dijkstra);
        GridPathfinder.Result chosen = referenceAlgorithm == PathAlgorithm.AStar ? astar : dijkstra;

        LastFound = chosen.found;
        LastLength = chosen.found ? chosen.length : 0f;
        lastPath = chosen.points;

        line.positionCount = lastPath.Count;
        for (int i = 0; i < lastPath.Count; i++)
            line.SetPosition(i, lastPath[i] + Vector3.up * 0.05f);

        LastComparison =
            $"A*: comprimento {astar.length:F2}, nos expandidos {astar.expandedNodes}, {astar.milliseconds:F2} ms | " +
            $"Dijkstra: comprimento {dijkstra.length:F2}, nos expandidos {dijkstra.expandedNodes}, {dijkstra.milliseconds:F2} ms";

        if (!chosen.found)
            Debug.LogWarning("Rota de referencia NAO encontrada. Verifique se a chegada esta dentro do grid e se o caminho nao esta bloqueado.", this);
        else
        {
            Debug.Log($"[Referencia {referenceAlgorithm}] {lastPath.Count} pontos. {LastComparison}", this);
            Debug.Log("Cantos da rota: " + string.Join(" -> ", lastPath.ConvertAll(p => $"({p.x:F1},{p.z:F1})")), this);
        }
        return LastLength;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
        Gizmos.DrawWireCube(gridCenter + Vector3.up * sampleHeight, new Vector3(gridSize.x, 0.05f, gridSize.y));

        if (drawGridGizmos && grid != null && grid.Walkable != null)
        {
            for (int x = 0; x < grid.Width; x++)
                for (int z = 0; z < grid.Depth; z++)
                    if (!grid.Walkable[x, z])
                    {
                        Gizmos.color = new Color(1f, 0f, 0f, 0.35f);
                        Gizmos.DrawCube(grid.CellCenter(x, z), new Vector3(cellSize, 0.05f, cellSize) * 0.9f);
                    }
        }

        Gizmos.color = Color.yellow;
        for (int i = 1; i < lastPath.Count; i++)
            Gizmos.DrawLine(lastPath[i - 1], lastPath[i]);
    }
}

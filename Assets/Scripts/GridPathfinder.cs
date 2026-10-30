using System.Collections.Generic;
using UnityEngine;

public enum PathAlgorithm { AStar, Dijkstra }

/// <summary>
/// Grid navegavel no plano XZ + busca de menor custo (A* e Dijkstra).
/// Dijkstra = A* com heuristica zero: expande em "ondas" a partir da origem.
/// A* usa a distancia octil ate o alvo para expandir menos nos.
/// Ambos retornam o MESMO custo minimo; muda o numero de nos expandidos.
/// </summary>
public sealed class GridPathfinder
{
    public struct Result
    {
        public bool found;
        public List<Vector3> points;   // inicio -> centros das celulas -> alvo
        public float length;
        public int expandedNodes;
        public float milliseconds;
    }

    private readonly Vector3 origin;   // canto minimo do grid
    private readonly float cellSize;
    private readonly int width, depth;
    private readonly float height;
    private bool[,] walkable;

    public int Width => width;
    public int Depth => depth;
    public bool[,] Walkable => walkable;

    public GridPathfinder(Vector3 center, Vector2 size, float cellSize, float height)
    {
        this.cellSize = Mathf.Max(0.05f, cellSize);
        width = Mathf.Max(1, Mathf.CeilToInt(size.x / this.cellSize));
        depth = Mathf.Max(1, Mathf.CeilToInt(size.y / this.cellSize));
        origin = new Vector3(center.x - size.x * 0.5f, 0f, center.z - size.y * 0.5f);
        this.height = height;
    }

    /// <summary>Marca celulas bloqueadas por colliders da layer de obstaculos.</summary>
    public void Bake(float agentRadius, LayerMask obstacleMask)
    {
        Physics.SyncTransforms();
        walkable = new bool[width, depth];
        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                walkable[x, z] = !Physics.CheckSphere(CellCenter(x, z), agentRadius, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    public Vector3 CellCenter(int x, int z) =>
        new Vector3(origin.x + (x + 0.5f) * cellSize, height, origin.z + (z + 0.5f) * cellSize);

    private Vector2Int WorldToCell(Vector3 p) => new Vector2Int(
        Mathf.Clamp(Mathf.FloorToInt((p.x - origin.x) / cellSize), 0, width - 1),
        Mathf.Clamp(Mathf.FloorToInt((p.z - origin.z) / cellSize), 0, depth - 1));

    private Vector2Int NearestWalkable(Vector3 p)
    {
        Vector2Int c = WorldToCell(p);
        if (walkable[c.x, c.y]) return c;
        Vector2Int best = c; float bestD = float.MaxValue;
        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                if (walkable[x, z])
                {
                    float d = (CellCenter(x, z) - p).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = new Vector2Int(x, z); }
                }
        return best;
    }

    private static readonly Vector2Int[] Dirs =
    {
        new(1,0), new(-1,0), new(0,1), new(0,-1),
        new(1,1), new(1,-1), new(-1,1), new(-1,-1)
    };

    public Result FindPath(Vector3 start, Vector3 goal, PathAlgorithm algorithm)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new Result { points = new List<Vector3>() };
        if (walkable == null) return result;

        Vector2Int s = NearestWalkable(start);
        Vector2Int g = NearestWalkable(goal);

        int n = width * depth;
        var cost = new float[n];
        var parent = new int[n];
        var closed = new bool[n];
        for (int i = 0; i < n; i++) { cost[i] = float.MaxValue; parent[i] = -1; }

        var open = new MinHeap();
        int sIdx = s.x + s.y * width, gIdx = g.x + g.y * width;
        cost[sIdx] = 0f;
        open.Push(sIdx, Heuristic(s, g, algorithm));

        while (open.Count > 0)
        {
            int current = open.Pop();
            if (closed[current]) continue;
            closed[current] = true;
            result.expandedNodes++;
            if (current == gIdx) break;

            int cx = current % width, cz = current / width;
            foreach (Vector2Int d in Dirs)
            {
                int nx = cx + d.x, nz = cz + d.y;
                if (nx < 0 || nz < 0 || nx >= width || nz >= depth || !walkable[nx, nz]) continue;
                // Nao corta quinas de obstaculos na diagonal.
                if (d.x != 0 && d.y != 0 && (!walkable[cx + d.x, cz] || !walkable[cx, cz + d.y])) continue;

                int ni = nx + nz * width;
                if (closed[ni]) continue;
                float step = (d.x != 0 && d.y != 0 ? 1.41421356f : 1f) * cellSize;
                float newCost = cost[current] + step;
                if (newCost < cost[ni])
                {
                    cost[ni] = newCost;
                    parent[ni] = current;
                    open.Push(ni, newCost + Heuristic(new Vector2Int(nx, nz), g, algorithm));
                }
            }
        }

        if (cost[gIdx] < float.MaxValue)
        {
            var cells = new List<Vector3>();
            for (int i = gIdx; i != -1; i = parent[i])
                cells.Add(CellCenter(i % width, i / width));
            cells.Reverse();

            result.points.Add(new Vector3(start.x, height, start.z));
            result.points.AddRange(cells);
            result.points.Add(new Vector3(goal.x, height, goal.z));
            for (int i = 1; i < result.points.Count; i++)
                result.length += Vector3.Distance(result.points[i - 1], result.points[i]);
            result.found = true;
        }

        sw.Stop();
        result.milliseconds = (float)sw.Elapsed.TotalMilliseconds;
        return result;
    }

    private float Heuristic(Vector2Int a, Vector2Int b, PathAlgorithm algorithm)
    {
        if (algorithm == PathAlgorithm.Dijkstra) return 0f; // Dijkstra: sem heuristica
        int dx = Mathf.Abs(a.x - b.x), dz = Mathf.Abs(a.y - b.y);
        return (Mathf.Max(dx, dz) + 0.41421356f * Mathf.Min(dx, dz)) * cellSize; // octil (admissivel)
    }

    /// <summary>Fila de prioridade minima simples (Unity nao possui PriorityQueue).</summary>
    private sealed class MinHeap
    {
        private readonly List<(int node, float priority)> items = new();
        public int Count => items.Count;

        public void Push(int node, float priority)
        {
            items.Add((node, priority));
            int i = items.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (items[p].priority <= items[i].priority) break;
                (items[p], items[i]) = (items[i], items[p]);
                i = p;
            }
        }

        public int Pop()
        {
            int top = items[0].node;
            int last = items.Count - 1;
            items[0] = items[last];
            items.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < items.Count && items[l].priority < items[m].priority) m = l;
                if (r < items.Count && items[r].priority < items[m].priority) m = r;
                if (m == i) break;
                (items[m], items[i]) = (items[i], items[m]);
                i = m;
            }
            return top;
        }
    }
}

using System;

/// <summary>Metricas de um episodio (Parte A).</summary>
[Serializable]
public struct EpisodeMetrics
{
    public bool reachedGoal;
    public bool collided;
    public float time;               // segundos ate o fim do episodio
    public float realDistance;       // soma dos deslocamentos frame a frame
    public float referenceDistance;  // comprimento da rota A* (ou Dijkstra)
    public float fitness;

    /// <summary>
    /// Eficiencia = referencia / real. So e valida se chegou ao alvo e se as
    /// distancias sao positivas. Caso contrario retorna NaN (invalido).
    /// </summary>
    public float Efficiency
    {
        get
        {
            if (!reachedGoal || realDistance <= 0.0001f || referenceDistance <= 0f)
                return float.NaN;
            return referenceDistance / realDistance;
        }
    }

    public string EfficiencyText => float.IsNaN(Efficiency) ? "invalida" : Efficiency.ToString("P1");

    public override string ToString() =>
        $"chegou={reachedGoal} colidiu={collided} tempo={time:F2}s real={realDistance:F2} ref={referenceDistance:F2} eficiencia={EfficiencyText} fitness={fitness:F2}";
}

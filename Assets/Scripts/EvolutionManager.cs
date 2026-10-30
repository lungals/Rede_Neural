using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[Serializable]
public class ValidationScenario
{
    public string name = "Cenario";
    [Tooltip("Objeto vazio marcando onde a 'chegada' deve ficar neste cenario (deixe vazio para manter a posicao).")]
    public Transform goalPosition;
    [Tooltip("Obstaculo opcional que sera deslocado neste cenario.")]
    public Transform obstacle;
    public Vector3 obstacleOffset;
}

public sealed class EvolutionManager : MonoBehaviour
{
    private enum Mode { Training, Replay, Validation, Idle }

    [Header("Cena")]
    [SerializeField] private EvolvingShip shipPrefab;   // prefab da Capsule
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform goal;             // Sphere "chegada"
    [SerializeField] private ReferenceRoute referenceRoute;

    [Header("Evolucao")]
    [SerializeField, Min(4)] private int populationSize = 30;
    [SerializeField, Min(1)] private int eliteCount = 2;
    [SerializeField, Range(0f, 1f)] private float mutationRate = 0.08f;
    [SerializeField, Min(0f)] private float mutationStrength = 0.25f;
    [SerializeField, Min(1f)] private float episodeSeconds = 15f;
    [SerializeField] private int randomSeed = 12345;

    [Header("Fitness")]
    [SerializeField] private float progressWeight = 10f;
    [SerializeField] private float goalBonus = 100f;
    [SerializeField] private float collisionPenalty = -50f;
    [SerializeField] private float timePenalty = 1f;

    [Header("Validacao (Parte B)")]
    [SerializeField] private ValidationScenario[] validationScenarios = new ValidationScenario[3];

    private readonly List<EvolvingShip> ships = new();
    private readonly List<Genome> population = new();
    private System.Random random;
    private float elapsed;
    private int finishedCount;
    private Mode mode = Mode.Training;
    private float referenceDistance;

    // Melhor de todas as geracoes (copia, nao referencia)
    private Genome bestEver;
    private int bestEverGeneration = -1;
    private EpisodeMetrics bestEverMetrics;
    private EpisodeMetrics lastGenerationBest;

    // Replay / validacao
    private Genome evaluationGenome;
    private int validationIndex;
    private readonly List<(string name, EpisodeMetrics m)> validationResults = new();
    private Vector3 originalGoalPos;
    private Vector3 originalObstaclePos;
    private string status = "";
    private string validationTable = "";

    public int Generation { get; private set; }
    public float BestFitness { get; private set; }
    public float AverageFitness { get; private set; }
    public float ProgressWeight => progressWeight;
    public float GoalBonus => goalBonus;
    public float CollisionPenalty => collisionPenalty;
    public float TimePenalty => timePenalty;

    private void Start()
    {
        if (shipPrefab == null || spawnPoint == null || goal == null)
        {
            Debug.LogError("EvolutionManager precisa de Ship Prefab (Capsule), Spawn Point e Goal (chegada).", this);
            enabled = false;
            return;
        }

        referenceDistance = ComputeReference();

        random = new System.Random(randomSeed);
        UnityEngine.Random.InitState(randomSeed);
        for (int i = 0; i < populationSize; i++)
            population.Add(new Genome(NeuralNetwork.WeightCount, random));

        SetStatus(GenomeStorage.HasSave ? "Existe um save. Pressione L para carregar." : "Sem save. Treinando...");
        BeginGeneration();
    }

    private float ComputeReference()
    {
        if (referenceRoute == null)
        {
            Debug.LogWarning("Sem ReferenceRoute: eficiencia sera invalida.", this);
            return 0f;
        }
        return referenceRoute.Compute(spawnPoint.position, goal.position);
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return;
        if (k.sKey.wasPressedThisFrame) SaveBest();
        if (k.lKey.wasPressedThisFrame) LoadBest();
        if (k.dKey.wasPressedThisFrame) DeleteSave();
        if (k.vKey.wasPressedThisFrame) StartValidation();
        if (k.tKey.wasPressedThisFrame) ResumeTraining();
#else
        if (Input.GetKeyDown(KeyCode.S)) SaveBest();
        if (Input.GetKeyDown(KeyCode.L)) LoadBest();
        if (Input.GetKeyDown(KeyCode.D)) DeleteSave();
        if (Input.GetKeyDown(KeyCode.V)) StartValidation();
        if (Input.GetKeyDown(KeyCode.T)) ResumeTraining();
#endif
    }

    private void FixedUpdate()
    {
        if (mode == Mode.Idle) return;
        elapsed += Time.fixedDeltaTime;
        if (elapsed >= episodeSeconds)
        {
            EvolvingShip[] activeShips = ships.Where(ship => ship.IsActive).ToArray();
            foreach (EvolvingShip ship in activeShips)
                ship.Timeout();
        }
    }

    public void ReportFinished(EvolvingShip ship)
    {
        switch (mode)
        {
            case Mode.Training:
                finishedCount++;
                if (finishedCount >= population.Count)
                    Evolve();
                break;

            case Mode.Replay:
                Debug.Log($"[Replay do genoma salvo] {ship.Metrics}");
                SetStatus($"Replay terminou: {ship.Metrics}");
                mode = Mode.Idle;
                break;

            case Mode.Validation:
                validationResults.Add((ScenarioName(validationIndex), ship.Metrics));
                Debug.Log($"[Validacao {ScenarioName(validationIndex)}] {ship.Metrics}");
                RestoreScenario(validationIndex);
                validationIndex++;
                RunNextValidation();
                break;
        }
    }

    // ---------------- Treino ----------------

    private void BeginGeneration()
    {
        mode = Mode.Training;
        elapsed = 0f;
        finishedCount = 0;

        while (ships.Count < population.Count)
            ships.Add(Instantiate(shipPrefab, spawnPoint.position, spawnPoint.rotation, transform));

        for (int i = 0; i < ships.Count; i++)
        {
            if (i < population.Count)
                ships[i].Begin(this, population[i], goal, spawnPoint.position, spawnPoint.rotation, referenceDistance);
            else
                ships[i].Abort();
        }
    }

    private void Evolve()
    {
        for (int i = 0; i < population.Count; i++)
            ships[i].Genome.Fitness = ships[i].Fitness;

        EvolvingShip bestShip = ships.Take(population.Count).OrderByDescending(s => s.Fitness).First();
        lastGenerationBest = bestShip.Metrics;

        if (bestEver == null || bestShip.Fitness > bestEver.Fitness)
        {
            bestEver = bestShip.Genome.Clone();
            bestEverGeneration = Generation;
            bestEverMetrics = bestShip.Metrics;
        }

        population.Sort((a, b) => b.Fitness.CompareTo(a.Fitness));
        BestFitness = population[0].Fitness;
        AverageFitness = population.Average(genome => genome.Fitness);

        int arrived = ships.Take(population.Count).Count(s => s.Metrics.reachedGoal);
        var bestEff = ships.Take(population.Count).Select(s => s.Metrics.Efficiency).Where(e => !float.IsNaN(e)).DefaultIfEmpty(float.NaN).Max();
        Debug.Log($"Geracao {Generation}: melhor {BestFitness:F2}, media {AverageFitness:F2}, chegaram {arrived}/{population.Count}, " +
                  $"maior eficiencia {(float.IsNaN(bestEff) ? "invalida" : bestEff.ToString("P1"))} | melhor individuo: {lastGenerationBest}");

        var next = new List<Genome>(populationSize);
        int protectedCount = Mathf.Min(eliteCount, population.Count);
        for (int i = 0; i < protectedCount; i++)
            next.Add(population[i].Clone());

        while (next.Count < populationSize)
        {
            Genome parentA = TournamentPick();
            Genome parentB = TournamentPick();
            Genome child = Genome.Crossover(parentA, parentB, random);
            child.Mutate(random, mutationRate, mutationStrength);
            next.Add(child);
        }

        population.Clear();
        population.AddRange(next);
        Generation++;
        BeginGeneration();
    }

    private Genome TournamentPick()
    {
        const int tournamentSize = 3;
        Genome best = population[random.Next(population.Count)];
        for (int i = 1; i < tournamentSize; i++)
        {
            Genome contender = population[random.Next(population.Count)];
            if (contender.Fitness > best.Fitness)
                best = contender;
        }
        return best;
    }

    private void StopAllShips()
    {
        foreach (var s in ships) s.Abort();
    }

    private void ResumeTraining()
    {
        if (mode == Mode.Training) return;
        if (mode == Mode.Validation) RestoreScenario(validationIndex);
        referenceDistance = ComputeReference();
        SetStatus("Treino retomado.");
        BeginGeneration();
    }

    // ---------------- Execucao de um unico genoma (sem evoluir) ----------------

    private void RunSingle(Genome g)
    {
        StopAllShips();
        if (ships.Count == 0)
            ships.Add(Instantiate(shipPrefab, spawnPoint.position, spawnPoint.rotation, transform));
        elapsed = 0f;
        ships[0].Begin(this, g, goal, spawnPoint.position, spawnPoint.rotation, referenceDistance);
    }

    // ---------------- Parte C: Save / Load / Delete ----------------

    public void SaveBest()
    {
        Genome g = bestEver ?? evaluationGenome;
        if (g == null)
        {
            SetStatus("Ainda nao ha melhor genoma (espere terminar a primeira geracao).");
            return;
        }

        var snap = new EvolutionSnapshot
        {
            seed = randomSeed,
            generation = bestEver != null ? bestEverGeneration : -1,
            bestFitness = g.Fitness,
            weights = g.Weights, // GenomeStorage.Save faz a copia
            populationSize = populationSize,
            mutationRate = mutationRate,
            mutationStrength = mutationStrength
        };
        GenomeStorage.Save(snap);
        SetStatus($"Melhor genoma SALVO (geracao {snap.generation}, fitness {snap.bestFitness:F2}, seed {snap.seed}).");
    }

    public void LoadBest()
    {
        if (!GenomeStorage.TryLoad(out EvolutionSnapshot snap, out string msg))
        {
            SetStatus(msg, warning: true); // sem excecao, fluxo normal continua
            return;
        }

        evaluationGenome = Genome.FromWeights(snap.weights, snap.bestFitness);
        if (mode == Mode.Validation) RestoreScenario(validationIndex);
        referenceDistance = ComputeReference();
        mode = Mode.Replay;
        SetStatus(msg + " Executando o genoma salvo (sem nova geracao)...");
        RunSingle(evaluationGenome);
    }

    public void DeleteSave()
    {
        bool had = GenomeStorage.HasSave;
        GenomeStorage.Delete();
        SetStatus(had ? $"Save apagado (PlayerPrefs.DeleteKey(\"{GenomeStorage.Key}\"))." : "Nao havia save para apagar.");
    }

    // ---------------- Parte B: Validacao ----------------

    public void StartValidation()
    {
        // Prefere o melhor do treino atual; se nao houver, usa o genoma carregado do save.
        Genome g = bestEver ?? evaluationGenome;
        if (g == null)
        {
            SetStatus("Sem genoma para validar. Treine ou carregue um save (L).", warning: true);
            return;
        }
        if (validationScenarios == null || validationScenarios.Length == 0)
        {
            SetStatus("Configure os cenarios de validacao no Inspector.", warning: true);
            return;
        }

        if (mode == Mode.Validation) RestoreScenario(validationIndex);
        evaluationGenome = g.Clone();
        validationResults.Clear();
        validationIndex = 0;
        mode = Mode.Validation;
        SetStatus("Validando o melhor genoma nos cenarios (sem evoluir)...");
        RunNextValidation();
    }

    private string ScenarioName(int i) =>
        validationScenarios[i] != null && !string.IsNullOrEmpty(validationScenarios[i].name) ? validationScenarios[i].name : $"Cenario {i + 1}";

    private void RunNextValidation()
    {
        if (validationIndex >= validationScenarios.Length)
        {
            FinishValidation();
            return;
        }

        ValidationScenario sc = validationScenarios[validationIndex];
        originalGoalPos = goal.position;
        if (sc != null && sc.goalPosition != null)
            goal.position = new Vector3(sc.goalPosition.position.x, goal.position.y, sc.goalPosition.position.z);
        if (sc != null && sc.obstacle != null)
        {
            originalObstaclePos = sc.obstacle.position;
            sc.obstacle.position += sc.obstacleOffset;
        }
        Physics.SyncTransforms();

        referenceDistance = ComputeReference();
        RunSingle(evaluationGenome);
    }

    private void RestoreScenario(int i)
    {
        if (validationScenarios == null || i < 0 || i >= validationScenarios.Length) return;
        ValidationScenario sc = validationScenarios[i];
        goal.position = originalGoalPos;
        if (sc != null && sc.obstacle != null)
            sc.obstacle.position = originalObstaclePos;
        Physics.SyncTransforms();
    }

    private void FinishValidation()
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Cenario | Chegou | Colidiu | Tempo (s) | Dist. real | Dist. ref | Eficiencia |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        int ok = 0;
        foreach (var (name, m) in validationResults)
        {
            if (m.reachedGoal && !m.collided) ok++;
            sb.AppendLine($"| {name} | {(m.reachedGoal ? "sim" : "nao")} | {(m.collided ? "sim" : "nao")} | {m.time:F2} | {m.realDistance:F2} | {m.referenceDistance:F2} | {m.EfficiencyText} |");
        }
        bool validated = ok >= 2;
        sb.AppendLine();
        sb.AppendLine($"Resultado: {ok}/{validationResults.Count} cenarios com chegada sem colisao -> {(validated ? "VALIDADO" : "NAO VALIDADO")}");
        validationTable = sb.ToString();
        Debug.Log("[Validacao concluida]\n" + validationTable);

        referenceDistance = ComputeReference();
        mode = Mode.Idle;
        SetStatus($"Validacao concluida: {(validated ? "VALIDADO" : "NAO VALIDADO")} ({ok}/{validationResults.Count}). Tabela no Console. T = voltar a treinar.");
    }

    // ---------------- UI ----------------

    private void SetStatus(string msg, bool warning = false)
    {
        status = msg;
        if (warning) Debug.LogWarning(msg, this); else Debug.Log(msg, this);
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 560, Screen.height - 20), GUI.skin.box);
        GUILayout.Label($"Modo: {mode}   Seed: {randomSeed}   Geracao: {Generation}");
        GUILayout.Label($"Melhor fitness (ger.): {BestFitness:F2}   Media: {AverageFitness:F2}");
        GUILayout.Label($"Rota de referencia: {referenceDistance:F2} m");
        if (referenceRoute != null) GUILayout.Label(referenceRoute.LastComparison);
        GUILayout.Label($"Melhor da ultima geracao: {lastGenerationBest}");
        if (bestEver != null)
            GUILayout.Label($"Melhor de todas (ger. {bestEverGeneration}): {bestEverMetrics}");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Salvar melhor (S)")) SaveBest();
        if (GUILayout.Button("Carregar melhor (L)")) LoadBest();
        if (GUILayout.Button("Apagar save (D)")) DeleteSave();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Validar 3 cenarios (V)")) StartValidation();
        if (GUILayout.Button("Voltar a treinar (T)")) ResumeTraining();
        GUILayout.EndHorizontal();

        GUILayout.Label(status);
        if (!string.IsNullOrEmpty(validationTable)) GUILayout.Label(validationTable);
        GUILayout.EndArea();
    }
}

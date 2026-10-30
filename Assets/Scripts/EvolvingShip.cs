using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class EvolvingShip : MonoBehaviour
{
    [Header("Sensores")]
    [SerializeField] private float sensorDistance = 8f;
    [SerializeField] private float sensorAngle = 35f;
    [SerializeField] private LayerMask obstacleMask;

    [Header("Movimento")]
    [SerializeField] private float maxSpeed = 7f;
    [SerializeField] private float turnSpeed = 180f;

    private readonly float[] inputs = new float[NeuralNetwork.InputCount];
    private readonly NeuralNetwork brain = new();
    private Rigidbody body;
    private EvolutionManager manager;
    private Genome genome;
    private Transform goal;
    private float initialDistance;
    private bool active;

    // Metricas (Parte A)
    private Vector3 lastPosition;
    private float traveled;
    private float episodeTime;
    private bool reached;
    private bool collided;
    private float referenceDistance;

    public float Fitness { get; private set; }
    public Genome Genome => genome;
    public bool IsActive => active;

    public EpisodeMetrics Metrics => new EpisodeMetrics
    {
        reachedGoal = reached,
        collided = collided,
        time = episodeTime,
        realDistance = traveled,
        referenceDistance = referenceDistance,
        fitness = Fitness
    };

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    public void Begin(EvolutionManager owner, Genome newGenome, Transform newGoal, Vector3 position, Quaternion rotation, float refDistance)
    {
        manager = owner;
        genome = newGenome;
        goal = newGoal;
        transform.SetPositionAndRotation(position, rotation);
        body.position = position;
        body.rotation = rotation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        initialDistance = Vector3.Distance(position, goal.position);
        Fitness = 0f;

        lastPosition = position;
        traveled = 0f;
        episodeTime = 0f;
        reached = false;
        collided = false;
        referenceDistance = refDistance;

        active = true;
        gameObject.SetActive(true);
    }

    private void FixedUpdate()
    {
        if (!active)
            return;

        // Distancia real: soma dos deslocamentos frame a frame.
        Vector3 pos = body.position;
        traveled += Vector3.Distance(new Vector3(pos.x, 0f, pos.z), new Vector3(lastPosition.x, 0f, lastPosition.z));
        lastPosition = pos;
        episodeTime += Time.fixedDeltaTime;

        ReadSensors();
        brain.Forward(inputs, genome.Weights);

        float turn = brain.GetOutput(0);
        float throttle = Mathf.Clamp01((brain.GetOutput(1) + 1f) * 0.5f);
        Quaternion delta = Quaternion.Euler(0f, turn * turnSpeed * Time.fixedDeltaTime, 0f);
        body.MoveRotation(body.rotation * delta);
        body.linearVelocity = transform.forward * (throttle * maxSpeed);

        // Progresso continuo evita que sobreviver parado seja uma boa estrategia.
        Fitness = (initialDistance - Vector3.Distance(transform.position, goal.position)) * manager.ProgressWeight;
    }

    private void ReadSensors()
    {
        inputs[0] = ReadSensor(transform.forward);
        inputs[1] = ReadSensor(Quaternion.AngleAxis(-sensorAngle, Vector3.up) * transform.forward);
        inputs[2] = ReadSensor(Quaternion.AngleAxis(sensorAngle, Vector3.up) * transform.forward);
    }

    private float ReadSensor(Vector3 direction)
    {
        Vector3 origin = transform.position + Vector3.up * 0.25f;
        if (Physics.Raycast(origin, direction, out RaycastHit hit, sensorDistance, obstacleMask, QueryTriggerInteraction.Ignore))
            return hit.distance / sensorDistance;

        return 1f;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (active && ((1 << collision.gameObject.layer) & obstacleMask.value) != 0)
        {
            collided = true;
            Finish(manager.CollisionPenalty);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (active && other.transform == goal)
        {
            reached = true;
            Finish(manager.GoalBonus);
        }
    }

    public void Timeout()
    {
        if (active)
            Finish(-manager.TimePenalty);
    }

    /// <summary>Desliga a nave sem avisar o manager (usado ao trocar de modo).</summary>
    public void Abort()
    {
        active = false;
        if (body != null) body.linearVelocity = Vector3.zero;
        gameObject.SetActive(false);
    }

    private void Finish(float bonusOrPenalty)
    {
        Fitness += bonusOrPenalty;
        active = false;
        body.linearVelocity = Vector3.zero;
        manager.ReportFinished(this);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 origin = transform.position + Vector3.up * 0.25f;
        Gizmos.DrawRay(origin, transform.forward * sensorDistance);
        Gizmos.DrawRay(origin, Quaternion.AngleAxis(-sensorAngle, Vector3.up) * transform.forward * sensorDistance);
        Gizmos.DrawRay(origin, Quaternion.AngleAxis(sensorAngle, Vector3.up) * transform.forward * sensorDistance);
    }
}

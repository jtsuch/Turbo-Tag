using UnityEngine;

public class VFXController : MonoBehaviour
{
    [Tooltip("If true, forces all particle systems to Local simulation space so transform scale works")]
    public bool forceLocalSimulation = true;

    [Tooltip("Hard upper bound on VFX lifetime (seconds). Prevents looping particle systems from leaking forever.")]
    [SerializeField] private float maxLifetime = 10f;

    private ParticleSystem[] systems;
    private float elapsed;

    private void Awake()
    {
        systems = GetComponentsInChildren<ParticleSystem>();

        if (forceLocalSimulation)
        {
            foreach (var ps in systems)
            {
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
            }
        }
    }

    private void Update()
    {
        elapsed += Time.deltaTime;

        if (elapsed >= maxLifetime)
        {
            Destroy(gameObject);
            return;
        }

        bool allStopped = true;
        foreach (var ps in systems)
        {
            if (ps.IsAlive())
            {
                allStopped = false;
                break;
            }
        }

        if (allStopped)
            Destroy(gameObject);
    }
}

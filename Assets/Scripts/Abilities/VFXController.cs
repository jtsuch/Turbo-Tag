using UnityEngine;

public class VFXController : MonoBehaviour
{
    [Tooltip("If true, forces all particle systems to Local simulation space so transform scale works")]
    public bool forceLocalSimulation = true;

    private ParticleSystem[] systems;

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
        // Check if all particle systems have finished playing
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
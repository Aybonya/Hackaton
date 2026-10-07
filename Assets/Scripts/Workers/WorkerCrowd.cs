using System.Collections.Generic;
using UnityEngine;

// Рабочие с коробками: при запуске создаёт 5–10 человек, которые ходят по цеху
// от процесса к процессу (FactoryProcess) и ненадолго останавливаются у каждого.
// Ходят по центральному проходу (между рядами процессов) и подходят к процессу со стороны прохода.
public class WorkerCrowd : MonoBehaviour
{
    [Tooltip("Модель рабочего с анимацией ходьбы (Technician_Walk_Carry_Box.fbx)")]
    public GameObject workerPrefab;
    [Range(1, 30)] public int minCount = 5;
    [Range(1, 30)] public int maxCount = 10;

    [Header("Движение")]
    public float walkSpeed = 1.15f;           // м/с
    public float stopMin = 2f, stopMax = 5f;  // сколько стоят у процесса, с

    [Header("Проход")]
    [Tooltip("Z центрального прохода цеха")]
    public float aisleZ = 0f;
    [Tooltip("Ширина прохода: у каждого рабочего своя полоса внутри него")]
    public float aisleWidth = 4f;
    [Tooltip("Границы цеха по X")]
    public Vector2 hallX = new Vector2(-74f, 74f);
    [Tooltip("Отступ от края процесса, где рабочий останавливается")]
    public float dockGap = 1.2f;

    readonly List<CarrierWorker> workers = new List<CarrierWorker>();
    FactoryProcess[] processes = new FactoryProcess[0];

    void Start()
    {
        if (workerPrefab == null)
        {
            Debug.LogWarning("WorkerCrowd: не задана модель рабочего (Worker Prefab).");
            return;
        }
        processes = FindObjectsByType<FactoryProcess>();

        int count = Random.Range(Mathf.Min(minCount, maxCount), Mathf.Max(minCount, maxCount) + 1);
        for (int i = 0; i < count; i++)
        {
            GameObject go = Instantiate(workerPrefab, transform);
            go.name = "Worker_" + (i + 1);
            CarrierWorker w = go.AddComponent<CarrierWorker>();
            float lane = aisleZ + Random.Range(-aisleWidth * 0.5f, aisleWidth * 0.5f);
            Vector3 start = new Vector3(Random.Range(hallX.x, hallX.y), 0f, lane);
            w.Init(this, start, lane);
            workers.Add(w);
        }
    }

    // точка у процесса со стороны прохода, куда рабочий приносит коробку
    public bool PickDock(FactoryProcess exclude, out FactoryProcess target, out Vector3 dock)
    {
        target = null;
        dock = Vector3.zero;
        List<FactoryProcess> alive = new List<FactoryProcess>();
        foreach (FactoryProcess p in processes)
        {
            if (p != null && p.isActiveAndEnabled) alive.Add(p);
        }
        if (alive.Count == 0) return false;
        if (alive.Count > 1) alive.Remove(exclude);
        target = alive[Random.Range(0, alive.Count)];

        Bounds wb = WorldBounds(target);
        float side = wb.center.z < aisleZ ? 1f : -1f;   // с какой стороны процесса проход
        float z = side > 0f ? wb.max.z + dockGap : wb.min.z - dockGap;
        float x = wb.center.x + Random.Range(-0.3f, 0.3f) * wb.size.x;
        dock = new Vector3(Mathf.Clamp(x, hallX.x, hallX.y), 0f, z);
        return true;
    }

    // случайная точка в проходе (если процессов в сцене нет)
    public Vector3 RandomAislePoint(float lane)
    {
        return new Vector3(Random.Range(hallX.x, hallX.y), 0f, lane);
    }

    static Bounds WorldBounds(FactoryProcess p)
    {
        Bounds lb = p.LocalBounds;
        Bounds b = new Bounds(p.transform.TransformPoint(lb.center), Vector3.zero);
        Vector3 e = lb.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = lb.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            b.Encapsulate(p.transform.TransformPoint(c));
        }
        return b;
    }

    public float ClampX(float x)
    {
        return Mathf.Clamp(x, hallX.x, hallX.y);
    }
}

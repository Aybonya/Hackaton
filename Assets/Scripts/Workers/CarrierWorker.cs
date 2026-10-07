using System.Collections.Generic;
using UnityEngine;

// Один рабочий с коробкой. Создаётся и управляется WorkerCrowd.
// Маршрут: из текущей точки в свою полосу прохода → по проходу до нужного процесса →
// к процессу → стоит пару секунд → следующий процесс. Анимация ходьбы из FBX идёт на месте,
// а перемещает рабочего этот скрипт.
public class CarrierWorker : MonoBehaviour
{
    const float AnimMetersPerSecond = 1.12f; // скорость, под которую сделан шаг в анимации

    WorkerCrowd crowd;
    float lane;
    float footOffset;           // насколько точка объекта выше подошв
    readonly Queue<Vector3> route = new Queue<Vector3>();
    FactoryProcess current;
    float waitLeft;

    Animation anim;
    AnimationState walk;

    public void Init(WorkerCrowd owner, Vector3 start, float laneZ)
    {
        crowd = owner;
        lane = laneZ;

        transform.position = start;

        anim = GetComponentInChildren<Animation>();
        if (anim != null)
        {
            // по умолчанию Legacy-анимация не играет, если Unity считает модель невидимой;
            // у скиннутой модели рамка видимости часто неверная, поэтому играем всегда
            anim.cullingType = AnimationCullingType.AlwaysAnimate;
            anim.wrapMode = WrapMode.Loop;
            foreach (AnimationState s in anim)
            {
                if (walk == null) walk = s;   // в модели одна анимация — ходьба
                s.wrapMode = WrapMode.Loop;
            }
            if (walk != null)
            {
                anim.Play(walk.name);
                walk.time = Random.Range(0f, walk.length);   // чтобы не шагали в ногу
                anim.Sample();
            }
        }
        else
        {
            Debug.LogWarning(name + ": у модели нет компонента Animation (FBX должен импортироваться как Legacy).");
        }

        ApplyTextures();

        // поставить на пол: точка модели в центре тела, а не у ног
        footOffset = transform.position.y - LowestPoint();
        transform.position = start + Vector3.up * footOffset;

        NextTarget();
    }

    // самая нижняя точка модели в текущей позе (по реальным вершинам, а не по рамке)
    float LowestPoint()
    {
        float min = float.MaxValue;
        Mesh baked = new Mesh();
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            SkinnedMeshRenderer sk = r as SkinnedMeshRenderer;
            if (sk != null)
            {
                sk.updateWhenOffscreen = true;
                sk.BakeMesh(baked, true);
                Vector3[] v = baked.vertices;
                Transform t = sk.transform;
                for (int i = 0; i < v.Length; i++)
                {
                    float y = t.position.y + (t.rotation * v[i]).y;
                    if (y < min) min = y;
                }
            }
            else
            {
                min = Mathf.Min(min, r.bounds.min.y);
            }
        }
        Destroy(baked);
        return min == float.MaxValue ? transform.position.y : min;
    }

    // текстуры вшиты в FBX так, что Unity их не достаёт: берём их из Resources/Technician
    static Texture2D bodyTex, boxTex;

    void ApplyTextures()
    {
        if (bodyTex == null) bodyTex = Resources.Load<Texture2D>("Technician/technician_basecolor");
        if (boxTex == null) boxTex = Resources.Load<Texture2D>("Technician/box_cardboard");

        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            Material[] mats = r.materials;   // копии материалов этого рабочего
            for (int i = 0; i < mats.Length; i++)
            {
                bool box = mats[i].name.ToLower().Contains("cardboard") || r.name.ToLower().Contains("box");
                Texture2D tex = box ? boxTex : bodyTex;
                if (tex == null) continue;
                mats[i].mainTexture = tex;
                mats[i].color = Color.white;
                if (mats[i].HasProperty("_Glossiness")) mats[i].SetFloat("_Glossiness", box ? 0.1f : 0.3f);
                if (mats[i].HasProperty("_Metallic")) mats[i].SetFloat("_Metallic", 0f);
            }
            r.materials = mats;
        }
    }

    void NextTarget()
    {
        route.Clear();
        Vector3 here = Flat(transform.position);
        FactoryProcess target;
        Vector3 dock;
        if (crowd.PickDock(current, out target, out dock))
        {
            current = target;
            route.Enqueue(new Vector3(here.x, 0f, lane));               // выйти в свою полосу прохода
            route.Enqueue(new Vector3(crowd.ClampX(dock.x), 0f, lane)); // пройти по проходу
            route.Enqueue(dock);                                         // подойти к процессу
        }
        else
        {
            route.Enqueue(new Vector3(here.x, 0f, lane));
            route.Enqueue(crowd.RandomAislePoint(lane));
        }
    }

    void Update()
    {
        if (crowd == null) return;
        float dt = Time.deltaTime;

        if (waitLeft > 0f)
        {
            waitLeft -= dt;
            SetWalking(false);
            if (waitLeft <= 0f) NextTarget();
            return;
        }

        if (route.Count == 0)
        {
            // дошёл: постоять у процесса, "сдать" коробку
            waitLeft = Random.Range(crowd.stopMin, crowd.stopMax);
            return;
        }

        Vector3 pos = Flat(transform.position);
        Vector3 goal = route.Peek();
        Vector3 to = goal - pos;
        float dist = to.magnitude;
        if (dist < 0.05f)
        {
            route.Dequeue();
            return;
        }

        SetWalking(true);
        float step = Mathf.Min(dist, crowd.walkSpeed * dt);
        Vector3 dir = to / dist;
        pos += dir * step;
        transform.position = pos + Vector3.up * footOffset;

        Quaternion look = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 360f * dt);
    }

    void SetWalking(bool on)
    {
        if (walk == null) return;
        if (on)
        {
            walk.speed = crowd.walkSpeed / AnimMetersPerSecond;
        }
        else if (walk.speed != 0f)
        {
            // стоит: замираем в нейтральной позе начала шага
            walk.speed = 0f;
            walk.time = 0f;
        }
    }

    static Vector3 Flat(Vector3 v)
    {
        return new Vector3(v.x, 0f, v.z);
    }
}

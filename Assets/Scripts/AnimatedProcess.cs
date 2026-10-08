using UnityEngine;

// Основа для процессов из анимированных FBX (линия колёс, пост стекла и т. д.).
// Вешается на корень модели. Анимация (Legacy) крутится без остановки, один цикл = одно изделие,
// а по времени внутри цикла определяется текущая стадия — её видно в панели статистики.
public abstract class AnimatedProcess : FactoryProcess
{
    [Range(0.25f, 4f)] public float speed = 1f;

    // с какой секунды цикла начинается каждая стадия (по порядку PhaseNames)
    protected abstract float[] PhaseStarts { get; }

    static readonly Color[] Palette =
    {
        new Color(0.94f, 0.7f, 0.14f), new Color(0.75f, 0.85f, 1f), new Color(0.4f, 0.65f, 1f),
        new Color(1f, 0.45f, 0.2f), new Color(0.85f, 0.55f, 1f), new Color(0.25f, 0.78f, 0.45f),
        new Color(0.55f, 0.62f, 0.7f)
    };

    Animation anim;
    AnimationState clip;
    float cycleTime;      // время внутри цикла
    int completed;        // сколько циклов (изделий) прошло
    Bounds bounds;
    bool boundsReady;

    protected virtual void Start()
    {
        anim = GetComponentInChildren<Animation>();
        if (anim == null)
        {
            string how = GetComponentInChildren<Animator>() != null
                ? "модель импортирована не как Legacy (на ней Animator)"
                : "на модели нет компонента Animation";
            Debug.LogWarning(name + ": " + how + ". Выдели FBX в Project → Rig → Animation Type: Legacy → Apply, затем удали объект и добавь заново через меню Allur.");
            return;
        }
        anim.cullingType = AnimationCullingType.AlwaysAnimate;
        anim.wrapMode = WrapMode.Loop;
        foreach (AnimationState s in anim)
        {
            s.wrapMode = WrapMode.Loop;
            if (clip == null) clip = s;   // основной клип не задан — берём первый из списка
        }
        if (anim.clip != null) clip = anim[anim.clip.name];
        if (clip == null)
        {
            Debug.LogWarning(name + ": в компоненте Animation нет ни одного клипа. Проверь FBX: Project → Animation → Import Animation.");
            return;
        }
        anim.Play(clip.name);
    }

    protected virtual void Update()
    {
        if (clip == null) return;
        clip.speed = speed;
        float t = Mathf.Repeat(clip.time, CycleLength);
        if (t + 0.0001f < cycleTime) completed++;   // цикл начался заново: изделие готово
        cycleTime = t;
    }

    protected float CycleLength => clip != null ? Mathf.Max(0.01f, clip.length) : 1f;

    public override int Phase
    {
        get
        {
            float[] starts = PhaseStarts;
            int p = 0;
            for (int i = 0; i < starts.Length; i++)
            {
                if (cycleTime >= starts[i]) p = i;
            }
            return p;
        }
    }

    public override int CompletedUnits => completed;

    public override string LiveDetail =>
        "Цикл  " + cycleTime.ToString("0.0") + " / " + CycleLength.ToString("0") + " с  ·  такт " + (CycleLength / Mathf.Max(0.01f, speed)).ToString("0.0") + " с";

    public override Color PhaseColor => Palette[Phase % Palette.Length];

    // габариты считаются по всем мешам модели (в локальных координатах этого объекта)
    public override Bounds LocalBounds
    {
        get
        {
            if (!boundsReady || !Application.isPlaying)
            {
                bounds = ComputeBounds();
                boundsReady = true;
            }
            return bounds;
        }
    }

    Bounds ComputeBounds()
    {
        Renderer[] rs = GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(Vector3.up, Vector3.one * 2f);
        bool any = false;
        Bounds b = new Bounds();
        foreach (Renderer r in rs)
        {
            Bounds wb = r.bounds;
            Vector3 c = wb.center, e = wb.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                Vector3 local = transform.InverseTransformPoint(corner);
                if (!any)
                {
                    b = new Bounds(local, Vector3.zero);
                    any = true;
                }
                else
                {
                    b.Encapsulate(local);
                }
            }
        }
        return b;
    }

    // у модели и так всё видно, рамку рисуем только когда объект выделен
    protected override void OnDrawGizmos() { }

    void OnDrawGizmosSelected()
    {
        base.OnDrawGizmos();
    }
}

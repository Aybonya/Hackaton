using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Ячейка R-4 «Сварка и окраска кузова»: 4 робота-манипулятора сваривают и красят кузов.
// Вешается на ПУСТОЙ объект. Всё остальное (роботы, конвейер, кузов, искры, свет, камера, панель)
// скрипт строит сам при нажатии Play. Цикл бесконечный: подача → сварка → окраска → сушка → выезд,
// и дальше следующий кузов другого цвета.
// Нужен ещё файл CarBodyData.cs (геометрия кузова), он лежит рядом.
public class RobotCell : FactoryProcess
{
    [Header("Сцена")]
    [Tooltip("Создать пол, свет, туман и тёмный фон. Выключи, если ячейку кладёшь внутрь готовой сцены.")]
    public bool buildEnvironment = true;
    [Tooltip("Управлять Main Camera: вращение мышкой, колесо, автооблёт.")]
    public bool controlCamera = true;
    public bool autoRotate = true;
    public float autoRotateSpeed = 4.2f;
    [Range(0.25f, 4f)] public float speed = 1f;
    public bool showHud = true;

    // ---------- размеры рук (как в исходной 3D-странице) ----------
    const float H = 1.0f, L1 = 1.1f, L2 = 1.0f, TL = 0.3f, PHI = -0.3f;
    const int MW = 384, MH = 130;

    static readonly Vector2[] REAR =
    {
        new Vector2(14, 80), new Vector2(46, 56), new Vector2(82, 76), new Vector2(118, 56), new Vector2(60, 100),
        new Vector2(100, 112), new Vector2(118, 134), new Vector2(142, 148), new Vector2(175, 46), new Vector2(190, 128)
    };
    static readonly Vector2[] FRONT =
    {
        new Vector2(366, 70), new Vector2(336, 56), new Vector2(300, 76), new Vector2(264, 56), new Vector2(320, 102),
        new Vector2(288, 114), new Vector2(268, 134), new Vector2(245, 148), new Vector2(210, 46), new Vector2(290, 98)
    };

    static readonly string[] COLOR_NAMES = { "Красный", "Синий", "Белый", "Жёлтый", "Зелёный", "Графит" };
    static readonly int[] COLOR_HEX = { 0xc81828, 0x1e5cbe, 0xe8e8e2, 0xf0aa0a, 0x188c5c, 0x464c58 };
    static readonly string[] PHASE_NAMES = { "Подача кузова", "Точечная сварка", "Окраска", "Сушка", "Выезд" };

    class Arm
    {
        public int sx, sz;
        public float bx, bz;
        public Vector2[] pts;
        public float r0, r1;
        public Vector3 home, pos;
        public int idx;
        public bool welding;      // true = дуга горит, false = едет к точке
        public bool arc;
        public bool spraying;
        public float timer, acc;
        public Transform yaw, sh, el, wr;
        public GameObject gun, bell, cone, flash;
        public Material sprayMat;
        public Light light;
    }

    class Spot
    {
        public GameObject go;
        public Material mat;
        public float heat;
    }

    readonly List<Arm> arms = new List<Arm>();
    readonly List<Spot> spots = new List<Spot>();
    readonly List<Transform> rollers = new List<Transform>();

    Transform mover, carRoot;
    Material towerMat;
    Light cureLight;
    ParticleSystem sparks;

    // кузов и маска покраски
    Texture2D paintTex;
    float[] mask = new float[MW * MH];
    Color32[] pixels = new Color32[MW * MH];
    bool paintDirty = true;

    // состояние цикла
    int phase = 0;
    float pt = 0f, carX = -9f, fullA = 0f, coverage = 0f, weldEndT = -1f, rollerAngle = 0f;
    int cycle = 1, colorIdx = 0, weldDone = 0, weldTotal = 0;
    bool paused = false;
    Vector3 tmp;

    // сборка мешей
    static Mesh cubeMesh, sphereMesh;
    static readonly Dictionary<string, Mesh> cylCache = new Dictionary<string, Mesh>();
    Material mBlue, mBlueD, mDark, mSteel, mFloor, mConv, mRoller, mYellow, mCopper, mWhite, mWall, mSkid, mCurtain, mBody;
    float lightScale = 1f;

    // камера
    Camera cam;
    Vector3 target;
    float yaw, pitch, dist, yawT, pitchT, distT;

    // панель
    Rect hudRect = new Rect(0, 0, 0, 0);
    GUIStyle sTitle, sText, sSmall, sBtn;

    // ---------- описание процесса для клика, камеры и панели статистики ----------
    public override string Code => "ЯЧЕЙКА R-4";
    public override string Title => "Сварка и окраска кузова";
    public override string UnitName => "кузов";
    public override string UnitNamePlural => "кузовов";
    public override string[] PhaseNames => PHASE_NAMES;
    public override int Phase => phase;
    public override int CompletedUnits => cycle - 1;
    public override string LiveDetail =>
        phase == 1 ? "Точки сварки  " + weldDone + " / " + weldTotal
        : phase == 2 ? "Покрытие  " + Mathf.RoundToInt(coverage * 100f) + " %"
        : phase >= 3 ? "Покрытие  100 %" : "Кузов на конвейере";
    public override Color PhaseColor =>
        phase == 0 ? Hex(0xf0b323) : phase == 1 ? Hex(0x66a6ff) : phase == 2 ? Hex(COLOR_HEX[colorIdx])
        : phase == 3 ? Hex(0xff9a3c) : Hex(0x40c773);
    public override bool HasSwatch => true;
    public override Color SwatchColor => Hex(COLOR_HEX[colorIdx]);
    public override string SwatchName => COLOR_NAMES[colorIdx];
    public override int BasePlanPerDay => 360;
    public override string[] Equipment => new[] { "R-1", "R-2", "R-3", "R-4", "Конвейер" };
    public override string[] Faults => new[]
    {
        "Износ электродов сварочных клещей", "Сбой сервопривода оси 2", "Засор форсунки колокола",
        "Ошибка датчика позиции кузова", "Перегрев трансформатора сварки", "Заклинивание роликовой секции",
        "Потеря связи с PLC", "Утечка в линии подачи краски", "Срабатывание световой завесы",
        "Калибровка TCP после касания"
    };
    public override Bounds LocalBounds => new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(22f, 3f, 8f));

    // живое состояние ячейки (только чтение)
    public int BodyNumber => cycle;
    public int WeldsDone => weldDone;
    public int WeldsTotal => weldTotal;
    public float PaintCoverage => coverage;
    public string PaintColorName => COLOR_NAMES[colorIdx];
    public Color PaintColorValue => Hex(COLOR_HEX[colorIdx]);

    // ======================================================================
    void Start()
    {
        lightScale = GraphicsSettings.currentRenderPipeline != null ? 1f : 0.12f;

        cylCache.Clear();
        cubeMesh = PrimitiveMesh(PrimitiveType.Cube);
        sphereMesh = PrimitiveMesh(PrimitiveType.Sphere);

        MakeMaterials();
        if (buildEnvironment) BuildEnvironment();
        BuildRoom();
        BuildCar();
        BuildArms();
        BuildSparks();
        SetupCamera();

        weldTotal = 0;
        for (int i = 0; i < arms.Count; i++) weldTotal += arms[i].pts.Length;

        ClearMask();
        for (int i = 0; i < arms.Count; i++)
        {
            arms[i].bell.SetActive(false);
            arms[i].sprayMat.color = SprayColor(COLOR_HEX[0]);
        }
        PaintColorChanged();
        RefreshPaint();
    }

    void Update()
    {
        float raw = Mathf.Min(0.05f, Time.deltaTime);
        if (!paused)
        {
            float dt = raw * speed;
            while (dt > 0f)
            {
                float s = Mathf.Min(dt, 0.025f);
                Step(s);
                dt -= s;
            }
        }
        Pose();
        if (paintDirty)
        {
            RefreshPaint();
            paintDirty = false;
        }
    }

    // ======================================================================
    //  Материалы и меши
    // ======================================================================
    static Mesh PrimitiveMesh(PrimitiveType t)
    {
        GameObject go = GameObject.CreatePrimitive(t);
        Mesh m = go.GetComponent<MeshFilter>().sharedMesh;
        Destroy(go);
        return m;
    }

    static Color Hex(int h)
    {
        return new Color32((byte)((h >> 16) & 255), (byte)((h >> 8) & 255), (byte)(h & 255), 255);
    }

    static Color SprayColor(int hex)
    {
        Color c = Hex(hex);
        c.a = 0.3f;
        return c;
    }

    static Material Lit(int hex, float rough, float metal)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        Material m = new Material(sh);
        m.color = Hex(hex);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f - rough);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 1f - rough);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        return m;
    }

    static Material Unlit(Color c)
    {
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        Material m = new Material(sh);
        m.color = c;
        return m;
    }

    void MakeMaterials()
    {
        mBlue = Lit(0x2a5fd6, .42f, .25f);
        mBlueD = Lit(0x173a8f, .5f, .25f);
        mDark = Lit(0x1b2129, .6f, .3f);
        mSteel = Lit(0x5d6a79, .5f, .5f);
        mFloor = Lit(0x2a323b, .92f, .05f);
        mConv = Lit(0x3a4654, .6f, .4f);
        mRoller = Lit(0x7b8898, .35f, .7f);
        mYellow = Lit(0xf0b323, .55f, .1f);
        mCopper = Lit(0xc9803a, .35f, .7f);
        mWhite = Lit(0xe9edf2, .4f, .2f);
        mWall = Lit(0x77818c, .85f, .1f);
        mSkid = Lit(0x4a5868, .6f, .4f);
        mCurtain = Lit(0xc8202c, .35f, 0f);
        mBody = Lit(0xffffff, .5f, .35f);
    }

    GameObject MakePart(Mesh mesh, Material mat, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler)
    {
        GameObject go = new GameObject("part");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    GameObject Box(float w, float h, float d, Material mat, float x, float y, float z, Transform parent)
    {
        return MakePart(cubeMesh, mat, parent, new Vector3(x, y, z), new Vector3(w, h, d), Vector3.zero);
    }

    // цилиндр вдоль оси Y: rTop - верхний радиус, rBot - нижний
    GameObject Cyl(float rTop, float rBot, float h, Material mat, float x, float y, float z, Transform parent, int seg, Vector3 euler)
    {
        return MakePart(CylinderMesh(rTop, rBot, h, seg), mat, parent, new Vector3(x, y, z), Vector3.one, euler);
    }

    GameObject Cyl(float rTop, float rBot, float h, Material mat, float x, float y, float z, Transform parent, int seg)
    {
        return Cyl(rTop, rBot, h, mat, x, y, z, parent, seg, Vector3.zero);
    }

    static void Tri(List<Vector3> v, List<Vector3> n, List<int> t,
                    Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
    {
        int ia = v.Count;
        v.Add(a); n.Add(na);
        v.Add(b); n.Add(nb);
        v.Add(c); n.Add(nc);
        Vector3 hint = na + nb + nc;
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), hint) >= 0f)
        {
            t.Add(ia); t.Add(ia + 1); t.Add(ia + 2);
        }
        else
        {
            t.Add(ia); t.Add(ia + 2); t.Add(ia + 1);
        }
    }

    static Mesh CylinderMesh(float rTop, float rBot, float h, int seg)
    {
        string key = rTop + "|" + rBot + "|" + h + "|" + seg;
        Mesh cached;
        if (cylCache.TryGetValue(key, out cached)) return cached;

        List<Vector3> v = new List<Vector3>();
        List<Vector3> n = new List<Vector3>();
        List<int> t = new List<int>();
        float hh = h * 0.5f;
        float slope = (rBot - rTop) / h;
        bool flat = seg <= 12;

        for (int i = 0; i < seg; i++)
        {
            float a0 = (float)i / seg * Mathf.PI * 2f;
            float a1 = (float)(i + 1) / seg * Mathf.PI * 2f;
            float am = (a0 + a1) * 0.5f;
            Vector3 n0 = new Vector3(Mathf.Cos(flat ? am : a0), slope, Mathf.Sin(flat ? am : a0)).normalized;
            Vector3 n1 = new Vector3(Mathf.Cos(flat ? am : a1), slope, Mathf.Sin(flat ? am : a1)).normalized;
            Vector3 t0 = new Vector3(rTop * Mathf.Cos(a0), hh, rTop * Mathf.Sin(a0));
            Vector3 t1 = new Vector3(rTop * Mathf.Cos(a1), hh, rTop * Mathf.Sin(a1));
            Vector3 b0 = new Vector3(rBot * Mathf.Cos(a0), -hh, rBot * Mathf.Sin(a0));
            Vector3 b1 = new Vector3(rBot * Mathf.Cos(a1), -hh, rBot * Mathf.Sin(a1));
            Tri(v, n, t, t0, t1, b1, n0, n1, n1);
            Tri(v, n, t, t0, b1, b0, n0, n1, n0);
            if (rTop > 0f) Tri(v, n, t, new Vector3(0, hh, 0), t1, t0, Vector3.up, Vector3.up, Vector3.up);
            if (rBot > 0f) Tri(v, n, t, new Vector3(0, -hh, 0), b0, b1, Vector3.down, Vector3.down, Vector3.down);
        }

        Mesh m = new Mesh();
        m.name = "cyl";
        m.SetVertices(v);
        m.SetNormals(n);
        m.SetTriangles(t, 0);
        m.RecalculateBounds();
        cylCache[key] = m;
        return m;
    }

    // конус распыления: вершина у сопла, раскрывается вдоль +X
    static Mesh ConeMesh(float start, float length, float radius, int seg)
    {
        List<Vector3> v = new List<Vector3>();
        List<int> t = new List<int>();
        v.Add(new Vector3(start, 0, 0));
        for (int i = 0; i <= seg; i++)
        {
            float a = (float)i / seg * Mathf.PI * 2f;
            v.Add(new Vector3(start + length, radius * Mathf.Cos(a), radius * Mathf.Sin(a)));
        }
        for (int i = 1; i <= seg; i++)
        {
            t.Add(0); t.Add(i); t.Add(i + 1);
        }
        Mesh m = new Mesh();
        m.name = "spray";
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateBounds();
        return m;
    }

    // ======================================================================
    //  Окружение и помещение
    // ======================================================================
    void BuildEnvironment()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.50f, 0.58f, 0.70f);
        RenderSettings.ambientEquatorColor = new Color(0.30f, 0.34f, 0.40f);
        RenderSettings.ambientGroundColor = new Color(0.10f, 0.12f, 0.14f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Hex(0x0b1016);
        RenderSettings.fogStartDistance = 16f;
        RenderSettings.fogEndDistance = 34f;

        GameObject sunGo = new GameObject("Sun");
        sunGo.transform.SetParent(transform, false);
        sunGo.transform.localPosition = new Vector3(5, 10, -6);
        sunGo.transform.LookAt(transform.position);
        Light sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.1f;
        sun.color = Color.white;
        sun.shadows = LightShadows.Soft;
        sun.shadowBias = 0.03f;

        GameObject fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(transform, false);
        fillGo.transform.localPosition = new Vector3(-6, 4, 5);
        fillGo.transform.LookAt(transform.position);
        Light fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.35f;
        fill.color = Hex(0x7fa6ff);
        fill.shadows = LightShadows.None;
    }

    void BuildRoom()
    {
        Transform root = transform;

        // кольцо жёлто-чёрной разметки вокруг ячейки
        Texture2D hz = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        hz.wrapMode = TextureWrapMode.Repeat;
        Color32 yel = new Color32(0xf0, 0xb3, 0x23, 255);
        Color32 blk = new Color32(0x16, 0x1a, 0x1f, 255);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                hz.SetPixel(x, y, ((x + y) % 32) < 16 ? blk : yel);
        hz.Apply();

        // пол (внутри готового здания свой пол не нужен)
        if (buildEnvironment) Box(60, 0.1f, 60, mFloor, 0, -0.05f, 0, root);
        HazardStrip(hz, 11f, 0, 3.3f, true, root);
        HazardStrip(hz, 11f, 0, -3.3f, true, root);
        HazardStrip(hz, 6.6f, 5.5f, 0, false, root);
        HazardStrip(hz, 6.6f, -5.5f, 0, false, root);

        // конвейер
        Box(22, 0.28f, 1.3f, mConv, 0, 0.14f, 0, root);
        Box(22, 0.06f, 0.08f, mSteel, 0, 0.31f, 0.62f, root);
        Box(22, 0.06f, 0.08f, mSteel, 0, 0.31f, -0.62f, root);
        Mesh rollerMesh = CylinderMesh(0.05f, 0.05f, 1.14f, 10);
        for (float x = -10.6f; x <= 10.61f; x += 0.4f)
        {
            GameObject holder = new GameObject("roller");
            holder.transform.SetParent(root, false);
            holder.transform.localPosition = new Vector3(x, 0.3f, 0);
            holder.transform.localRotation = Quaternion.Euler(90, 0, 0);
            GameObject r = MakePart(rollerMesh, mRoller, holder.transform, Vector3.zero, Vector3.one, Vector3.zero);
            rollers.Add(r.transform);
        }

        // красная шторка позади ячейки
        for (int i = 0; i < 44; i++)
        {
            Box(0.23f, 2.5f, 0.012f, mCurtain, -5.4f + i * 0.25f, 1.35f, -3.8f + ((i % 2 == 1) ? 0.03f : 0f), root);
        }
        Box(11.2f, 0.1f, 0.1f, mWhite, 0, 2.65f, -3.8f, root);
        Cyl(0.06f, 0.06f, 2.7f, mSteel, -5.6f, 1.35f, -3.8f, root, 24);
        Cyl(0.06f, 0.06f, 2.7f, mSteel, 5.6f, 1.35f, -3.8f, root, 24);

        // входной и выходной порталы
        float[] xs = { -6.4f, 6.4f };
        for (int i = 0; i < xs.Length; i++)
        {
            float x = xs[i];
            Box(0.12f, 3.0f, 2.6f, mWall, x, 1.5f, -2.5f, root);
            Box(0.12f, 3.0f, 2.6f, mWall, x, 1.5f, 2.5f, root);
            Box(0.12f, 0.9f, 2.4f, mWall, x, 2.55f, 0, root);
            Box(0.2f, 3.1f, 0.16f, mSteel, x, 1.55f, -1.2f, root);
            Box(0.2f, 3.1f, 0.16f, mSteel, x, 1.55f, 1.2f, root);
        }

        // сигнальная колонна
        Cyl(0.03f, 0.03f, 2.0f, mSteel, 4.6f, 1.0f, 2.9f, root, 24);
        towerMat = Unlit(Hex(0xf0b323));
        MakePart(CylinderMesh(0.09f, 0.09f, 0.26f, 24), towerMat, root, new Vector3(4.6f, 2.1f, 2.9f), Vector3.one, Vector3.zero);

        // тёплая лампа сушки
        GameObject cl = new GameObject("CureLight");
        cl.transform.SetParent(root, false);
        cl.transform.localPosition = new Vector3(0, 4.2f, 0);
        cureLight = cl.AddComponent<Light>();
        cureLight.type = LightType.Point;
        cureLight.color = Hex(0xff9a3c);
        cureLight.range = 12f;
        cureLight.intensity = 0f;
        cureLight.shadows = LightShadows.None;
        cureLight.enabled = false;

        // подвижная часть (кузов едет по конвейеру)
        GameObject mv = new GameObject("Mover");
        mv.transform.SetParent(root, false);
        mover = mv.transform;
    }

    void HazardStrip(Texture2D tex, float len, float x, float z, bool alongX, Transform parent)
    {
        Material m = Lit(0xffffff, .8f, 0f);
        m.mainTexture = tex;
        m.mainTextureScale = alongX ? new Vector2(len / 0.3f, 1f) : new Vector2(1f, len / 0.3f);
        Vector3 size = alongX ? new Vector3(len, 0.01f, 0.18f) : new Vector3(0.18f, 0.01f, len);
        MakePart(cubeMesh, m, parent, new Vector3(x, 0.006f, z), size, Vector3.zero);
    }

    // ======================================================================
    //  Кузов
    // ======================================================================
    void BuildCar()
    {
        GameObject car = new GameObject("Car");
        carRoot = car.transform;
        carRoot.SetParent(mover, false);
        carRoot.localPosition = new Vector3(-1.9f, 0.17f, -0.8f);

        // меш из CarBodyData
        float[] vf = CarBodyData.Vertices;
        float[] nf = CarBodyData.Normals;
        int vc = vf.Length / 3;
        Vector3[] verts = new Vector3[vc];
        Vector3[] norms = new Vector3[vc];
        Vector2[] uvs = new Vector2[vc];
        for (int i = 0; i < vc; i++)
        {
            verts[i] = new Vector3(vf[i * 3], vf[i * 3 + 1], vf[i * 3 + 2]);
            norms[i] = new Vector3(nf[i * 3], nf[i * 3 + 1], nf[i * 3 + 2]);
            uvs[i] = new Vector2(verts[i].x * 100f / MW, (verts[i].y * 100f - 30f) / MH);
        }
        Mesh mesh = new Mesh();
        mesh.name = "CarBody";
        mesh.vertices = verts;
        mesh.normals = norms;
        mesh.uv = uvs;
        mesh.triangles = CarBodyData.Triangles;
        mesh.RecalculateBounds();

        paintTex = new Texture2D(MW, MH, TextureFormat.RGBA32, false);
        paintTex.wrapMode = TextureWrapMode.Clamp;
        paintTex.filterMode = FilterMode.Bilinear;
        mBody.mainTexture = paintTex;

        MakePart(mesh, mBody, carRoot, Vector3.zero, Vector3.one, Vector3.zero);

        // тёмное «нутро», чтобы арки читались как колёсные ниши
        Box(3.5f, 0.54f, 1.2f, mDark, 1.9f, 0.68f, 0.8f, carRoot);

        // технологическая платформа (скид)
        Box(3.6f, 0.08f, 0.10f, mSkid, 1.9f, 0.22f, 0.40f, carRoot);
        Box(3.6f, 0.08f, 0.10f, mSkid, 1.9f, 0.22f, 1.20f, carRoot);
        float[] px = { 50f, 150f, 232f, 330f };
        for (int i = 0; i < px.Length; i++)
        {
            Box(0.08f, 0.14f, 0.08f, mSkid, px[i] * 0.01f, 0.33f, 0.40f, carRoot);
            Box(0.08f, 0.14f, 0.08f, mSkid, px[i] * 0.01f, 0.33f, 1.20f, carRoot);
        }
        Box(0.10f, 0.06f, 0.96f, mSkid, 0.40f, 0.18f, 0.80f, carRoot);
        Box(0.10f, 0.06f, 0.96f, mSkid, 3.40f, 0.18f, 0.80f, carRoot);
    }

    void ClearMask()
    {
        for (int i = 0; i < mask.Length; i++) mask[i] = 0f;
        paintDirty = true;
    }

    static float Sstep(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    Color paintColor = Color.red;

    void PaintColorChanged()
    {
        paintColor = Hex(COLOR_HEX[colorIdx]);
        paintDirty = true;
    }

    void RefreshPaint()
    {
        Color grey = new Color(0x9a / 255f, 0xa5 / 255f, 0xb1 / 255f);
        for (int i = 0; i < mask.Length; i++)
        {
            float pm = Sstep(0.12f, 0.7f, Mathf.Max(mask[i], fullA));
            pixels[i] = Color.Lerp(grey, paintColor, pm);
        }
        paintTex.SetPixels32(pixels);
        paintTex.Apply(false);
    }

    // мягкая кисть краски
    void Brush(float cx, float cy, float dt)
    {
        float ga = Mathf.Min(1f, dt * 14f);
        const float R = 34f;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - R));
        int x1 = Mathf.Min(MW - 1, Mathf.CeilToInt(cx + R));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - R));
        int y1 = Mathf.Min(MH - 1, Mathf.CeilToInt(cy + R));
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / R;
                if (d >= 1f) continue;
                float g = d < 0.6f ? Mathf.Lerp(0.9f, 0.45f, d / 0.6f) : Mathf.Lerp(0.45f, 0f, (d - 0.6f) / 0.4f);
                int i = y * MW + x;
                mask[i] += (1f - mask[i]) * g * ga;
            }
        }
        paintDirty = true;
    }

    // ======================================================================
    //  Роботы
    // ======================================================================
    void BuildArms()
    {
        int[,] q = { { -1, 1 }, { -1, -1 }, { 1, 1 }, { 1, -1 } };
        for (int k = 0; k < 4; k++)
        {
            Arm a = new Arm();
            a.sx = q[k, 0];
            a.sz = q[k, 1];
            a.bx = a.sx * 0.95f;
            a.bz = a.sz * 2.3f;
            a.pts = a.sx < 0 ? REAR : FRONT;
            a.r0 = a.sx < 0 ? 0f : 186f;
            a.r1 = a.sx < 0 ? 194f : 380f;
            a.home = new Vector3(a.bx * 0.72f, 2.25f, a.bz * 0.62f);
            a.pos = a.home;
            a.idx = 0;
            a.welding = false;

            GameObject rootGo = new GameObject("Robot_" + k);
            Transform root = rootGo.transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(a.bx, 0, a.bz);

            Cyl(0.34f, 0.4f, 0.12f, mDark, 0, 0.06f, 0, root, 24);
            Cyl(0.26f, 0.3f, 0.5f, mBlueD, 0, 0.37f, 0, root, 24);

            a.yaw = new GameObject("yaw").transform;
            a.yaw.SetParent(root, false);
            Cyl(0.28f, 0.28f, 0.2f, mBlue, 0, 0.72f, 0, a.yaw, 24);
            Box(0.34f, 0.36f, 0.34f, mBlue, 0, 0.95f, 0, a.yaw);

            a.sh = new GameObject("shoulder").transform;
            a.sh.SetParent(a.yaw, false);
            a.sh.localPosition = new Vector3(0, H, 0);
            Cyl(0.2f, 0.2f, 0.46f, mDark, 0, 0, 0, a.sh, 24, new Vector3(90, 0, 0));
            Box(L1, 0.24f, 0.22f, mBlue, L1 / 2f, 0, 0, a.sh);
            Cyl(0.03f, 0.03f, L1 * 0.9f, mYellow, L1 / 2f, 0.17f, 0.06f, a.sh, 8, new Vector3(0, 0, 90));

            a.el = new GameObject("elbow").transform;
            a.el.SetParent(a.sh, false);
            a.el.localPosition = new Vector3(L1, 0, 0);
            Cyl(0.15f, 0.15f, 0.36f, mDark, 0, 0, 0, a.el, 24, new Vector3(90, 0, 0));
            Box(L2, 0.17f, 0.16f, mBlue, L2 / 2f, 0, 0, a.el);
            Box(0.3f, 0.2f, 0.2f, mBlueD, 0.12f, 0.02f, 0, a.el);
            Cyl(0.025f, 0.025f, L2 * 0.8f, mYellow, L2 / 2f, 0.12f, 0.05f, a.el, 8, new Vector3(0, 0, 90));

            a.wr = new GameObject("wrist").transform;
            a.wr.SetParent(a.el, false);
            a.wr.localPosition = new Vector3(L2, 0, 0);
            Cyl(0.09f, 0.09f, 0.24f, mDark, 0, 0, 0, a.wr, 24, new Vector3(90, 0, 0));
            Cyl(0.055f, 0.055f, TL - 0.1f, mSteel, (TL - 0.1f) / 2f, 0, 0, a.wr, 12, new Vector3(0, 0, 90));

            // сварочные клещи
            a.gun = new GameObject("gun");
            a.gun.transform.SetParent(a.wr, false);
            Cyl(0.018f, 0.03f, 0.12f, mCopper, TL - 0.06f, 0, 0, a.gun.transform, 10, new Vector3(0, 0, -90));
            Box(0.1f, 0.14f, 0.05f, mSteel, TL - 0.15f, 0, 0, a.gun.transform);

            // колокол краскопульта
            a.bell = new GameObject("bell");
            a.bell.transform.SetParent(a.wr, false);
            Cyl(0.075f, 0.04f, 0.12f, mWhite, TL - 0.06f, 0, 0, a.bell.transform, 16, new Vector3(0, 0, -90));

            // конус распыления
            a.sprayMat = Unlit(SprayColor(COLOR_HEX[0]));
            a.cone = new GameObject("spray");
            a.cone.transform.SetParent(a.wr, false);
            a.cone.AddComponent<MeshFilter>().sharedMesh = ConeMesh(TL, 0.42f, 0.26f, 20);
            a.cone.AddComponent<MeshRenderer>().sharedMaterial = a.sprayMat;
            a.cone.SetActive(false);

            // свет дуги и вспышка
            GameObject lg = new GameObject("arcLight");
            lg.transform.SetParent(transform, false);
            a.light = lg.AddComponent<Light>();
            a.light.type = LightType.Point;
            a.light.color = Hex(0x9fc4ff);
            a.light.range = 5f;
            a.light.intensity = 0f;
            a.light.shadows = LightShadows.None;
            a.light.enabled = false;

            a.flash = new GameObject("flash");
            a.flash.transform.SetParent(transform, false);
            a.flash.AddComponent<MeshFilter>().sharedMesh = sphereMesh;
            a.flash.AddComponent<MeshRenderer>().sharedMaterial = Unlit(Color.white);
            a.flash.SetActive(false);

            arms.Add(a);
        }
    }

    // обратная кинематика: рука достаёт до точки a.pos
    void Solve(Arm a)
    {
        float dx = a.pos.x - a.bx, dz = a.pos.z - a.bz;
        float r = Mathf.Sqrt(dx * dx + dz * dz);
        a.yaw.localRotation = Quaternion.Euler(0f, Mathf.Atan2(-dz, dx) * Mathf.Rad2Deg, 0f);

        float wr = r - TL * Mathf.Cos(PHI);
        float wy = a.pos.y - H - TL * Mathf.Sin(PHI);
        float d = Mathf.Sqrt(wr * wr + wy * wy);
        d = Mathf.Max(Mathf.Abs(L1 - L2) + 0.05f, Mathf.Min(L1 + L2 - 0.01f, d));
        float baseA = Mathf.Atan2(wy, wr);
        float c1 = (L1 * L1 + d * d - L2 * L2) / (2f * L1 * d);
        float c2 = (L1 * L1 + L2 * L2 - d * d) / (2f * L1 * L2);
        float a1 = baseA + Mathf.Acos(Mathf.Clamp(c1, -1f, 1f));
        float a2 = -(Mathf.PI - Mathf.Acos(Mathf.Clamp(c2, -1f, 1f)));
        a.sh.localRotation = Quaternion.Euler(0f, 0f, a1 * Mathf.Rad2Deg);
        a.el.localRotation = Quaternion.Euler(0f, 0f, a2 * Mathf.Rad2Deg);
        a.wr.localRotation = Quaternion.Euler(0f, 0f, (PHI - a1 - a2) * Mathf.Rad2Deg);
    }

    // ======================================================================
    //  Искры
    // ======================================================================
    void BuildSparks()
    {
        GameObject go = new GameObject("Sparks");
        go.transform.SetParent(transform, false);
        sparks = go.AddComponent<ParticleSystem>();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = sparks.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 700;
        main.gravityModifier = 0.66f;
        main.startLifetime = 0.7f;
        main.startSize = 0.045f;
        main.startSpeed = 0f;

        ParticleSystem.EmissionModule em = sparks.emission;
        em.enabled = false;
        ParticleSystem.ShapeModule sh = sparks.shape;
        sh.enabled = false;

        ParticleSystemRenderer psr = go.GetComponent<ParticleSystemRenderer>();
        psr.material = Unlit(new Color(1f, 0.78f, 0.35f, 1f));
        psr.renderMode = ParticleSystemRenderMode.Stretch;
        psr.lengthScale = 1f;
        psr.velocityScale = 0.04f;
        psr.cameraVelocityScale = 0f;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;

        sparks.Play();
    }

    void Spark(Vector3 p, float sz)
    {
        float an = Random.value * 6.283f;
        float v = 0.6f + Random.value * 2.4f;
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        ep.position = p;
        ep.velocity = new Vector3(Mathf.Cos(an) * v, Random.value * 2.6f - 0.4f, Mathf.Sin(an) * v * 0.6f + sz * (0.8f + Random.value * 1.6f));
        ep.startLifetime = 0.4f + Random.value * 0.6f;
        ep.startSize = 0.03f + Random.value * 0.03f;
        ep.startColor = new Color(1f, 0.7f + Random.value * 0.25f, 0.3f + Random.value * 0.2f, 1f);
        sparks.Emit(ep, 1);
    }

    // ======================================================================
    //  Цикл
    // ======================================================================
    Vector3 ToWorld(Arm a, float px, float py, float off)
    {
        bool cab = py > 106f;
        return new Vector3(carX - 1.9f + px * 0.01f, 0.17f + py * 0.01f, a.sz * ((cab ? 0.71f : 0.84f) + off));
    }

    void Go(Arm a, Vector3 t, float dt, float k)
    {
        a.pos = Vector3.Lerp(a.pos, t, 1f - Mathf.Exp(-dt * k));
    }

    void Roll(float dx)
    {
        rollerAngle -= dx / 0.05f * Mathf.Rad2Deg;
    }

    void ResetCar()
    {
        for (int i = 0; i < spots.Count; i++)
        {
            Destroy(spots[i].go);
            Destroy(spots[i].mat);
        }
        spots.Clear();
        fullA = 0f;
        coverage = 0f;
        weldDone = 0;
        weldEndT = -1f;
        ClearMask();
        PaintColorChanged();
        for (int i = 0; i < arms.Count; i++)
        {
            arms[i].idx = 0;
            arms[i].welding = false;
            arms[i].sprayMat.color = SprayColor(COLOR_HEX[colorIdx]);
        }
    }

    void SetPhase(int p)
    {
        phase = p;
        pt = 0f;
        if (p == 0)
        {
            cycle++;
            colorIdx = (colorIdx + 1) % COLOR_HEX.Length;
            ResetCar();
        }
        bool paintTool = p >= 2;
        for (int i = 0; i < arms.Count; i++)
        {
            arms[i].gun.SetActive(!paintTool);
            arms[i].bell.SetActive(paintTool);
        }
    }

    void AddSpot(Arm a, Vector2 p)
    {
        float z = a.sz > 0 ? (p.y > 106f ? 151f : 161f) : (p.y > 106f ? 9f : -1f);
        Spot s = new Spot();
        s.mat = Unlit(Hex(0xffd27a));
        s.go = new GameObject("weld");
        s.go.transform.SetParent(carRoot, false);
        s.go.transform.localPosition = new Vector3(p.x * 0.01f, p.y * 0.01f, z * 0.01f);
        s.go.transform.localScale = Vector3.one * 0.052f;
        s.go.AddComponent<MeshFilter>().sharedMesh = sphereMesh;
        s.go.AddComponent<MeshRenderer>().sharedMaterial = s.mat;
        s.heat = 1f;
        spots.Add(s);
    }

    void Step(float dt)
    {
        pt += dt;
        for (int i = 0; i < arms.Count; i++)
        {
            arms[i].arc = false;
            arms[i].spraying = false;
        }

        if (phase == 0)
        {
            float u = Mathf.Min(1f, pt / 2.6f);
            float e = 1f - Mathf.Pow(1f - u, 3f);
            float nx = -9f + 9f * e;
            Roll(nx - carX);
            carX = nx;
            for (int i = 0; i < arms.Count; i++) Go(arms[i], arms[i].home, dt, 4f);
            if (u >= 1f) SetPhase(1);
        }
        else if (phase == 1)
        {
            bool all = true;
            for (int i = 0; i < arms.Count; i++)
            {
                Arm a = arms[i];
                if (a.idx < a.pts.Length)
                {
                    all = false;
                    Vector2 p = a.pts[a.idx];
                    tmp = ToWorld(a, p.x, p.y, 0f);
                    Go(a, tmp, dt, 7f);
                    if (!a.welding)
                    {
                        if (Vector3.Distance(a.pos, tmp) < 0.03f)
                        {
                            a.welding = true;
                            a.timer = 0.38f;
                            weldDone++;
                            AddSpot(a, p);
                        }
                    }
                    else
                    {
                        a.arc = true;
                        a.acc += dt * 120f;
                        while (a.acc > 1f)
                        {
                            a.acc -= 1f;
                            Spark(a.pos, a.sz);
                        }
                        a.timer -= dt;
                        if (a.timer <= 0f)
                        {
                            a.idx++;
                            a.welding = false;
                        }
                    }
                }
                else
                {
                    Go(a, a.home, dt, 4f);
                }
            }
            if (all)
            {
                if (weldEndT < 0f) weldEndT = pt;
                if (pt - weldEndT > 0.8f) SetPhase(2);
            }
        }
        else if (phase == 2)
        {
            const float TT = 8f;
            float u = Mathf.Min(1f, pt / TT);
            coverage = u;
            for (int i = 0; i < arms.Count; i++)
            {
                Arm a = arms[i];
                const int rows = 4;
                float v = Mathf.Min(rows - 0.0001f, u * rows);
                int row = Mathf.FloorToInt(v);
                float f = v - row;
                if (row % 2 == 1) f = 1f - f;
                if (a.sx > 0) f = 1f - f;
                float px = a.r0 + (a.r1 - a.r0) * f;
                float py = 148f - (row + 0.5f) / rows * 104f;
                tmp = ToWorld(a, px, Mathf.Min(py, 128f), 0.34f);
                tmp.z = a.sz * 1.2f;
                Go(a, tmp, dt, 6f);
                if (pt > 0.6f)
                {
                    a.spraying = true;
                    Brush(px, py - 30f, dt);
                }
            }
            if (u >= 1f) SetPhase(3);
        }
        else if (phase == 3)
        {
            coverage = 1f;
            float before = fullA;
            fullA = Mathf.Min(1f, pt / 0.9f);
            if (fullA != before) paintDirty = true;
            for (int i = 0; i < arms.Count; i++) Go(arms[i], arms[i].home, dt, 3.5f);
            if (pt > 2.2f) SetPhase(4);
        }
        else
        {
            if (fullA != 1f)
            {
                fullA = 1f;
                paintDirty = true;
            }
            float u = Mathf.Min(1f, pt / 2.6f);
            float nx = 9f * u * u * u;
            Roll(nx - carX);
            carX = nx;
            for (int i = 0; i < arms.Count; i++) Go(arms[i], arms[i].home, dt, 3.5f);
            if (u >= 1f) SetPhase(0);
        }

        for (int i = 0; i < spots.Count; i++)
        {
            Spot s = spots[i];
            if (s.heat > 0f)
            {
                s.heat = Mathf.Max(0f, s.heat - dt * 0.8f);
                float h = s.heat;
                s.mat.color = new Color(0.2f + 0.8f * h, 0.22f + 0.55f * h * h, 0.26f + 0.1f * h, 1f);
            }
        }
    }

    // применяем состояние к объектам сцены (раз за кадр)
    void Pose()
    {
        mover.localPosition = new Vector3(carX, 0, 0);

        Quaternion rq = Quaternion.Euler(0f, rollerAngle, 0f);
        for (int i = 0; i < rollers.Count; i++) rollers[i].localRotation = rq;

        for (int i = 0; i < arms.Count; i++)
        {
            Arm a = arms[i];
            Solve(a);
            a.cone.SetActive(a.spraying);
            if (a.spraying)
            {
                Color c = a.sprayMat.color;
                c.a = 0.22f + Random.value * 0.14f;
                a.sprayMat.color = c;
            }
            a.light.enabled = a.arc;
            a.light.intensity = a.arc ? (5f + Random.value * 8f) * lightScale : 0f;
            a.light.transform.localPosition = a.pos;
            a.flash.SetActive(a.arc);
            if (a.arc)
            {
                a.flash.transform.localPosition = a.pos;
                a.flash.transform.localScale = Vector3.one * (0.14f * (0.6f + Random.value * 0.9f));
            }
        }

        float cure = phase == 3 ? 2.2f * Mathf.Min(1f, pt / 0.5f) : 0f;
        cureLight.enabled = cure > 0f;
        cureLight.intensity = cure * 18f * lightScale;

        Color tc;
        if (phase == 2) tc = Hex(COLOR_HEX[colorIdx]);
        else if (phase == 0) tc = Hex(0xf0b323);
        else if (phase == 1) tc = Hex(0x4d8dff);
        else if (phase == 3) tc = Hex(0xff9a3c);
        else tc = Hex(0x35c46a);
        towerMat.color = tc;
    }

    // ======================================================================
    //  Камера
    // ======================================================================
    void SetupCamera()
    {
        cam = Camera.main;
        if (cam == null)
        {
            GameObject go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
        }
        if (!controlCamera) return;

        cam.fieldOfView = 42f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 80f;
        if (buildEnvironment)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex(0x0b1016);
        }

        target = transform.position + new Vector3(0, 1, 0);
        Vector3 off = new Vector3(6.2f, 4.0f, -7.2f);
        dist = distT = off.magnitude;
        yaw = yawT = Mathf.Atan2(off.x, off.z);
        pitch = pitchT = Mathf.Asin(off.y / dist);
        ApplyCamera();
    }

    void ApplyCamera()
    {
        Vector3 dir = new Vector3(Mathf.Cos(pitch) * Mathf.Sin(yaw), Mathf.Sin(pitch), Mathf.Cos(pitch) * Mathf.Cos(yaw));
        cam.transform.position = target + dir * dist;
        cam.transform.LookAt(target);
    }

    void LateUpdate()
    {
        if (!controlCamera || cam == null) return;

        float dt = Time.deltaTime;
        bool dragging = false;
        Vector2 pointer = PointerGui();
        bool overHud = showHud && hudRect.Contains(pointer);

        if (PointerHeld() && !overHud)
        {
            Vector2 d = PointerDelta();
            float k = Mathf.PI * 2f / Mathf.Max(1f, Screen.height);
            yawT -= d.x * k;
            pitchT -= d.y * k;
            dragging = true;
        }

        float scroll = ScrollDelta();
        if (!overHud && Mathf.Abs(scroll) > 0.001f)
        {
            distT *= Mathf.Pow(0.9f, scroll);
        }

        if (autoRotate && !dragging) yawT += autoRotateSpeed * Mathf.Deg2Rad * dt;

        pitchT = Mathf.Clamp(pitchT, 5f * Mathf.Deg2Rad, 85f * Mathf.Deg2Rad);
        distT = Mathf.Clamp(distT, 4.5f, 16f);

        float s = 1f - Mathf.Exp(-dt * 10f);
        yaw = Mathf.Lerp(yaw, yawT, s);
        pitch = Mathf.Lerp(pitch, pitchT, s);
        dist = Mathf.Lerp(dist, distT, s);
        ApplyCamera();
    }

    static bool PointerHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
        return Input.GetMouseButton(0);
#endif
    }

    static Vector2 PointerDelta()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 12f;
#endif
    }

    static float ScrollDelta()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null) return 0f;
        float v = Mouse.current.scroll.ReadValue().y;
        return Mathf.Abs(v) > 10f ? v / 120f : v;
#else
        return Input.mouseScrollDelta.y;
#endif
    }

    static Vector2 PointerGui()
    {
#if ENABLE_INPUT_SYSTEM
        Vector2 p = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        Vector2 p = Input.mousePosition;
#endif
        return new Vector2(p.x, Screen.height - p.y);
    }

    // ======================================================================
    //  Панель
    // ======================================================================
    void OnGUI()
    {
        if (!showHud) return;

        if (sTitle == null)
        {
            sTitle = new GUIStyle(GUI.skin.label);
            sTitle.fontSize = 20;
            sTitle.fontStyle = FontStyle.Bold;
            sTitle.normal.textColor = Color.white;

            sText = new GUIStyle(GUI.skin.label);
            sText.fontSize = 16;
            sText.normal.textColor = new Color(0.86f, 0.89f, 0.93f);

            sSmall = new GUIStyle(GUI.skin.label);
            sSmall.fontSize = 13;
            sSmall.normal.textColor = new Color(0.55f, 0.62f, 0.70f);

            sBtn = new GUIStyle(GUI.skin.button);
            sBtn.fontSize = 15;
        }

        float s = Screen.height / 900f;
        Matrix4x4 old = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

        Rect panel = new Rect(20, 20, 300, 470);
        hudRect = new Rect(panel.x * s, panel.y * s, panel.width * s, panel.height * s);

        GUI.color = new Color(0.04f, 0.06f, 0.09f, 0.82f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = Color.white;

        float x = panel.x + 18, y = panel.y + 14;
        GUI.Label(new Rect(x, y, 280, 20), "Ячейка R-4 · 4 манипулятора", sSmall);
        y += 22;
        GUI.Label(new Rect(x, y, 280, 34), "Сварка и окраска кузова", sTitle);
        y += 46;

        for (int i = 0; i < PHASE_NAMES.Length; i++)
        {
            bool on = i == phase;
            GUI.color = on ? Hex(0xf0b323) : new Color(1, 1, 1, 0.35f);
            GUI.Label(new Rect(x, y, 260, 24), (on ? "●  " : "○  ") + (i + 1) + ". " + PHASE_NAMES[i], sText);
            y += 26;
        }
        GUI.color = Color.white;
        y += 12;

        GUI.Label(new Rect(x, y, 140, 22), "Кузов №", sSmall);
        GUI.Label(new Rect(x + 140, y - 3, 120, 24), cycle.ToString(), sText);
        y += 26;
        GUI.Label(new Rect(x, y, 140, 22), "Точек сварки", sSmall);
        GUI.Label(new Rect(x + 140, y - 3, 120, 24), weldDone + " / " + weldTotal, sText);
        y += 26;
        GUI.Label(new Rect(x, y, 140, 22), "Покрытие", sSmall);
        GUI.Label(new Rect(x + 140, y - 3, 120, 24), Mathf.RoundToInt(coverage * 100f) + " %", sText);
        y += 26;
        GUI.Label(new Rect(x, y, 140, 22), "Цвет", sSmall);
        GUI.color = Hex(COLOR_HEX[colorIdx]);
        GUI.DrawTexture(new Rect(x + 140, y + 2, 16, 16), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(x + 162, y - 3, 110, 24), COLOR_NAMES[colorIdx], sText);
        y += 40;

        if (GUI.Button(new Rect(x, y, 120, 34), paused ? "Пуск" : "Пауза", sBtn)) paused = !paused;
        if (GUI.Button(new Rect(x + 130, y, 120, 34), autoRotate ? "Облёт: вкл" : "Облёт: выкл", sBtn)) autoRotate = !autoRotate;
        y += 44;
        GUI.Label(new Rect(x, y + 4, 60, 24), "Скорость", sSmall);
        float[] sp = { 1f, 2f, 4f };
        for (int i = 0; i < sp.Length; i++)
        {
            bool cur = Mathf.Approximately(speed, sp[i]);
            GUI.color = cur ? Hex(0xf0b323) : Color.white;
            if (GUI.Button(new Rect(x + 80 + i * 56, y, 50, 30), "×" + sp[i], sBtn)) speed = sp[i];
        }
        GUI.color = Color.white;

        GUI.Label(new Rect(20, 900 - 36, 700, 24), "Тяни мышкой, чтобы повернуть · колесо для приближения", sSmall);

        GUI.matrix = old;
    }
}

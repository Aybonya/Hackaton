using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Вешается на камеру (Main Camera).
// Что делает:
//  1. Ставит камеру у входа и создаёт над заводом парящую плашку "Нажмите, чтобы начать работу".
//  2. По клику на плашку: крыша поднимается и исчезает, камера плавно взлетает к виду сверху.
//  3. Кнопка "Назад" (или клавиша Esc) возвращает всё обратно.
// Работает и со старой, и с новой системой ввода, Canvas и EventSystem не нужны.
[RequireComponent(typeof(Camera))]
public class AllurExperience : MonoBehaviour
{
    [Header("Плашка")]
    [Tooltip("Перетащи сюда картинку AllurPlate")]
    public Texture2D plateTexture;
    public Vector3 platePosition = new Vector3(0f, 30f, -40f);
    [Tooltip("Ширина плашки в метрах")]
    public float plateWidth = 50f;

    [Header("Вид у входа")]
    public Vector3 entrancePosition = new Vector3(0f, 16f, -125f);
    public Vector3 entranceEuler = new Vector3(7f, 0f, 0f);

    [Header("Вид сверху")]
    public Vector3 overviewPosition = new Vector3(0f, 95f, -55f);
    public Vector3 overviewEuler = new Vector3(60f, 0f, 0f);
    public float flightTime = 2.4f;
    [Tooltip("Объект, который камера ставит в центр кадра (если не найден — берётся Overview Position)")]
    public string focusName = "Hall_Floor";
    [Tooltip("Запас по краям: 1 = цех впритык к краям экрана")]
    public float focusPadding = 1.1f;

    [Header("Крыша")]
    [Tooltip("Имена объектов крыши в Hierarchy")]
    public string[] roofNames = { "Hall_Roof", "Hall_Skylights", "Hall_RoofBeams" };
    public float roofLift = 45f;
    public float roofTime = 1.6f;

    [Header("Скрыть в цехе")]
    [Tooltip("Объекты здания, которые выключаются при запуске (колонны и балки внутри цеха)")]
    public string[] hiddenNames = { "Hall_Columns", "Hall_RoofBeams" };

    [Header("Вывеска")]
    [Tooltip("Блочные буквы из модели, вместо них показывается логотип Assets/Resources/AllurLogo.png")]
    public string signLettersName = "Sign_ALLUR";
    [Tooltip("Белая доска вывески, логотип ставится по её центру")]
    public string signBoardName = "Sign_ALLUR_Board";
    [Tooltip("Высота логотипа в долях от высоты доски")]
    [Range(0.3f, 1f)] public float logoHeight = 0.68f;

    [Header("Осмотр процесса")]
    [Tooltip("Расстояния камеры в долях от размера процесса (длины его большей стороны)")]
    public float orbitStartFactor = 0.75f;
    public float orbitMinFactor = 0.25f;
    public float orbitMaxFactor = 1.4f;
    [Tooltip("Медленный автооблёт, пока не крутишь мышкой (градусов в секунду)")]
    public float cellAutoRotate = 6f;
    public float cellFlightTime = 1.6f;

    enum State { Exterior, Flying, Overview, Cell }
    State state = State.Exterior;

    Camera cam;
    Transform plateRoot;
    Transform plateVisual;
    SpriteRenderer plateRenderer;
    BoxCollider plateCollider;

    float appear;        // 0..1, появление плашки
    bool plateHidden;    // плашка спрятана, пока мы наверху
    float hoverT;        // 0..1, плавное наведение мыши
    float punch;         // 1..0, "нажатие"

    class RoofPart
    {
        public Transform t;
        public Vector3 home;
    }
    readonly List<RoofPart> roof = new List<RoofPart>();

    GUIStyle buttonStyle;
    Rect backRect;

    // осмотр ячейки: камера летает по сфере вокруг цели
    Vector3 orbitTarget;
    float orbitYaw, orbitPitch, orbitDist;
    float orbitYawT, orbitPitchT, orbitDistT;

    // правая панель статистики
    readonly CellSidebar sidebar = new CellSidebar();
    FactoryProcess activeCell;
    float activeSize = 20f;
    bool panelOpen;
    float panelT;   // 0..1, выезд панели

    void Start()
    {
        cam = GetComponent<Camera>();
        transform.position = entrancePosition;
        transform.rotation = Quaternion.Euler(entranceEuler);

        foreach (string n in hiddenNames)
        {
            GameObject go = GameObject.Find(n);
            if (go != null)
            {
                go.SetActive(false);
            }
        }

        foreach (string n in roofNames)
        {
            if (System.Array.IndexOf(hiddenNames, n) >= 0)
            {
                continue; // скрытое не поднимаем вместе с крышей, иначе оно включится обратно
            }
            GameObject go = GameObject.Find(n);
            if (go != null)
            {
                RoofPart part = new RoofPart();
                part.t = go.transform;
                part.home = go.transform.position;
                roof.Add(part);
            }
            else
            {
                Debug.LogWarning("AllurExperience: в сцене нет объекта крыши \"" + n + "\"");
            }
        }

        BuildPlate();
        PlaceLogo();
    }

    // фирменный логотип на вывеске вместо блочных букв из модели
    void PlaceLogo()
    {
        Texture2D logo = Resources.Load<Texture2D>("AllurLogo");
        GameObject board = GameObject.Find(signBoardName);
        Renderer boardRenderer = board != null ? board.GetComponentInChildren<Renderer>() : null;
        if (logo == null || boardRenderer == null)
        {
            Debug.LogWarning("AllurExperience: логотип не поставлен (нет AllurLogo.png в Resources или доски " + signBoardName + ")");
            return;
        }

        GameObject letters = GameObject.Find(signLettersName);
        if (letters != null)
        {
            letters.SetActive(false);
        }

        // доска смотрит на улицу (к камере у входа, в сторону -Z): логотип ставим чуть перед ней
        Bounds b = boardRenderer.bounds;
        float h = b.size.y * logoHeight;
        float w = h * logo.width / logo.height;
        if (w > b.size.x * 0.9f)
        {
            w = b.size.x * 0.9f;
            h = w * logo.height / logo.width;
        }

        logo.wrapMode = TextureWrapMode.Clamp;
        Shader sh = Shader.Find("Sprites/Default");
        Material mat = new Material(sh);
        mat.mainTexture = logo;

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "AllurLogo";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.position = new Vector3(b.center.x, b.center.y, b.min.z - 0.03f);
        quad.transform.rotation = Quaternion.identity;   // лицевая сторона Quad смотрит в -Z
        quad.transform.localScale = new Vector3(w, h, 1f);
        MeshRenderer mr = quad.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    void BuildPlate()
    {
        if (plateTexture == null)
        {
            Debug.LogError("AllurExperience: перетащи картинку AllurPlate в поле Plate Texture на камере.");
            return;
        }

        plateRoot = new GameObject("EnterPlate").transform;
        plateRoot.position = platePosition;

        GameObject visual = new GameObject("Visual");
        plateVisual = visual.transform;
        plateVisual.SetParent(plateRoot, false);

        float pixelsPerUnit = plateTexture.width / plateWidth;
        Sprite sprite = Sprite.Create(
            plateTexture,
            new Rect(0f, 0f, plateTexture.width, plateTexture.height),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit,
            0,
            SpriteMeshType.FullRect);

        plateRenderer = visual.AddComponent<SpriteRenderer>();
        plateRenderer.sprite = sprite;
        plateRenderer.sortingOrder = 100;

        plateCollider = visual.AddComponent<BoxCollider>();
        float plateHeight = plateWidth * plateTexture.height / plateTexture.width;
        plateCollider.center = Vector3.zero;
        plateCollider.size = new Vector3(plateWidth, plateHeight, 1f);

        visual.SetActive(false);
    }

    void Update()
    {
        if (plateRoot != null)
        {
            AnimatePlate();
        }

        // панель выезжает справа, а 3D-вид сужается до оставшейся части экрана
        panelT = Mathf.MoveTowards(panelT, panelOpen ? 1f : 0f, Time.deltaTime / 0.45f);
        float slide = Smooth(panelT);
        cam.rect = new Rect(0f, 0f, 1f - CellSidebar.PixelWidth() / Screen.width * slide, 1f);

        if (state == State.Overview)
        {
            if (EscPressed())
            {
                StartCoroutine(ExitRoutine());
            }
            else if (ClickPressed() && !PointerOverBack())
            {
                FactoryProcess cell = CellUnderPointer();
                if (cell != null)
                {
                    StartCoroutine(EnterCellRoutine(cell));
                }
            }
        }
        else if (state == State.Cell)
        {
            if (EscPressed())
            {
                StartCoroutine(ExitCellRoutine());
            }
            else
            {
                Orbit();
            }
        }
    }

    // ---------- осмотр ячейки ----------

    // процесс под курсором (луч против его габаритов, коллайдеры не нужны)
    FactoryProcess CellUnderPointer()
    {
        Ray ray = cam.ScreenPointToRay(PointerPosition());
        FactoryProcess[] cells = FindObjectsByType<FactoryProcess>();
        FactoryProcess best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < cells.Length; i++)
        {
            Transform t = cells[i].transform;
            Ray local = new Ray(t.InverseTransformPoint(ray.origin), t.InverseTransformDirection(ray.direction));
            float d;
            if (cells[i].LocalBounds.IntersectRay(local, out d))
            {
                // расстояние сравниваем в мировых единицах (у объектов может быть разный масштаб)
                float wd = Vector3.Distance(ray.origin, t.TransformPoint(local.GetPoint(d)));
                if (wd < bestDist)
                {
                    bestDist = wd;
                    best = cells[i];
                }
            }
        }
        return best;
    }

    IEnumerator EnterCellRoutine(FactoryProcess cell)
    {
        state = State.Flying;
        activeCell = cell;
        sidebar.Open(activeCell);
        panelOpen = true;

        // цель и расстояние подбираем по габаритам процесса
        Bounds b = cell.LocalBounds;
        Vector3 worldSize = Vector3.Scale(b.size, cell.transform.lossyScale);
        activeSize = Mathf.Max(2f, Mathf.Max(worldSize.x, worldSize.z));
        orbitTarget = cell.transform.TransformPoint(b.center);

        // начинаем спереди-сбоку, с учётом поворота процесса
        orbitYaw = orbitYawT = (cell.transform.eulerAngles.y + 180f + 35f) * Mathf.Deg2Rad;
        orbitPitch = orbitPitchT = 28f * Mathf.Deg2Rad;
        orbitDist = orbitDistT = activeSize * orbitStartFactor;

        Vector3 pos = OrbitPosition();
        yield return Fly(pos, Quaternion.LookRotation(orbitTarget - pos), cellFlightTime);
        state = State.Cell;
    }

    IEnumerator ExitCellRoutine()
    {
        state = State.Flying;
        panelOpen = false;
        yield return Fly(OverviewPoint(), Quaternion.Euler(overviewEuler), cellFlightTime);
        state = State.Overview;
    }

    void Orbit()
    {
        float dt = Time.deltaTime;
        bool dragging = false;

        bool overPanel = PointerOverSidebar();
        if (PointerHeld() && !PointerOverBack() && !overPanel)
        {
            Vector2 d = PointerDelta();
            float k = Mathf.PI * 2f / Mathf.Max(1f, Screen.height);
            orbitYawT -= d.x * k;
            orbitPitchT -= d.y * k;
            dragging = true;
        }

        float scroll = ScrollDelta();
        if (!overPanel && Mathf.Abs(scroll) > 0.001f)
        {
            orbitDistT *= Mathf.Pow(0.88f, scroll);
        }

        if (!dragging)
        {
            orbitYawT += cellAutoRotate * Mathf.Deg2Rad * dt;
        }

        orbitPitchT = Mathf.Clamp(orbitPitchT, 4f * Mathf.Deg2Rad, 85f * Mathf.Deg2Rad);
        orbitDistT = Mathf.Clamp(orbitDistT, activeSize * orbitMinFactor, activeSize * orbitMaxFactor);

        float s = 1f - Mathf.Exp(-dt * 10f);
        orbitYaw = Mathf.Lerp(orbitYaw, orbitYawT, s);
        orbitPitch = Mathf.Lerp(orbitPitch, orbitPitchT, s);
        orbitDist = Mathf.Lerp(orbitDist, orbitDistT, s);

        transform.position = OrbitPosition();
        transform.LookAt(orbitTarget);
    }

    Vector3 OrbitPosition()
    {
        Vector3 dir = new Vector3(
            Mathf.Cos(orbitPitch) * Mathf.Sin(orbitYaw),
            Mathf.Sin(orbitPitch),
            Mathf.Cos(orbitPitch) * Mathf.Cos(orbitYaw));
        return orbitTarget + dir * orbitDist;
    }

    bool PointerOverSidebar()
    {
        return panelT > 0f && PointerPosition().x > Screen.width - CellSidebar.PixelWidth() * Smooth(panelT);
    }

    bool PointerOverBack()
    {
        Vector2 p = PointerPosition();
        return backRect.Contains(new Vector2(p.x, Screen.height - p.y));
    }

    void AnimatePlate()
    {
        float dt = Time.deltaTime;

        // появление и исчезновение
        float target = plateHidden ? 0f : 1f;
        float speed = plateHidden ? 1f / 0.45f : 1f / 1.3f;
        appear = Mathf.MoveTowards(appear, target, dt * speed);

        bool visible = appear > 0.001f;
        plateVisual.gameObject.SetActive(visible);
        if (!visible)
        {
            return;
        }

        // наведение мыши и клик
        bool hovered = false;
        if (state == State.Exterior && !plateHidden)
        {
            Ray ray = cam.ScreenPointToRay(PointerPosition());
            RaycastHit[] hits = Physics.RaycastAll(ray, 1000f);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == plateCollider)
                {
                    hovered = true;
                    break;
                }
            }
        }
        hoverT = Mathf.MoveTowards(hoverT, hovered ? 1f : 0f, dt * 6f);

        if (hovered && ClickPressed())
        {
            punch = 1f;
            StartCoroutine(EnterRoutine());
        }
        punch = Mathf.MoveTowards(punch, 0f, dt * 3f);

        // парение
        float t = Time.time;
        float bob = Mathf.Sin(t * 1.7f) * 1.0f + Mathf.Sin(t * 0.63f) * 0.4f;
        float sway = Mathf.Sin(t * 0.9f) * 1.6f;
        float roll = Mathf.Sin(t * 1.15f) * 2.2f;
        plateRoot.position = platePosition + new Vector3(sway, bob, 0f);

        // плашка всегда повёрнута лицом к камере
        Vector3 away = plateRoot.position - cam.transform.position;
        if (away.sqrMagnitude > 0.01f)
        {
            plateVisual.rotation = Quaternion.LookRotation(away, Vector3.up) * Quaternion.Euler(0f, 0f, roll);
        }

        // размер: выскакивает при появлении, "дышит", увеличивается при наведении, сжимается при клике
        float pop = EaseOutBack(appear);
        float breathe = 1f + Mathf.Sin(t * 2.4f) * 0.015f;
        float hoverScale = 1f + hoverT * 0.08f;
        float press = 1f - Mathf.Sin(punch * Mathf.PI) * 0.12f;
        plateVisual.localScale = Vector3.one * (pop * breathe * hoverScale * press);

        Color c = plateRenderer.color;
        c.a = Mathf.Clamp01(appear * 1.5f);
        plateRenderer.color = c;
    }

    IEnumerator EnterRoutine()
    {
        state = State.Flying;
        plateHidden = true;
        StartCoroutine(MoveRoof(true));
        yield return Fly(OverviewPoint(), Quaternion.Euler(overviewEuler), flightTime);
        state = State.Overview;
    }

    // точка, из которой цех виден по центру и целиком
    Vector3 OverviewPoint()
    {
        GameObject focus = GameObject.Find(focusName);
        Renderer r = focus != null ? focus.GetComponentInChildren<Renderer>() : null;
        if (r == null)
        {
            return overviewPosition;
        }

        Bounds b = r.bounds;
        Quaternion rot = Quaternion.Euler(overviewEuler);
        float pitch = overviewEuler.x * Mathf.Deg2Rad;

        float halfV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfH = halfV * Screen.width / Mathf.Max(1f, Screen.height);

        // половина размера цеха в кадре по ширине и по высоте
        float halfWidth = b.extents.x;
        float halfDepth = b.extents.z * Mathf.Sin(pitch);

        float dist = Mathf.Max(halfWidth / halfH, halfDepth / halfV) * focusPadding;
        return b.center - rot * Vector3.forward * dist;
    }

    IEnumerator ExitRoutine()
    {
        state = State.Flying;
        StartCoroutine(MoveRoof(false));
        yield return Fly(entrancePosition, Quaternion.Euler(entranceEuler), flightTime);
        plateHidden = false;
        state = State.Exterior;
    }

    // плавный перелёт камеры с небольшой дугой
    IEnumerator Fly(Vector3 toPos, Quaternion toRot, float duration)
    {
        Vector3 fromPos = transform.position;
        Quaternion fromRot = transform.rotation;

        for (float k = 0f; k < 1f; k += Time.deltaTime / duration)
        {
            float e = Smooth(k);
            Vector3 p = Vector3.Lerp(fromPos, toPos, e);
            p.y += Mathf.Sin(e * Mathf.PI) * 6f;
            transform.SetPositionAndRotation(p, Quaternion.Slerp(fromRot, toRot, e));
            yield return null;
        }
        transform.SetPositionAndRotation(toPos, toRot);
    }

    // крыша плавно поднимается вверх и выключается (или возвращается на место)
    IEnumerator MoveRoof(bool up)
    {
        for (int i = 0; i < roof.Count; i++)
        {
            roof[i].t.gameObject.SetActive(true);
        }

        for (float k = 0f; k < 1f; k += Time.deltaTime / roofTime)
        {
            float e = Smooth(k);
            float h = up ? e : 1f - e;
            for (int i = 0; i < roof.Count; i++)
            {
                roof[i].t.position = roof[i].home + Vector3.up * (roofLift * h);
            }
            yield return null;
        }

        for (int i = 0; i < roof.Count; i++)
        {
            roof[i].t.position = roof[i].home + Vector3.up * (up ? roofLift : 0f);
            if (up)
            {
                roof[i].t.gameObject.SetActive(false);
            }
        }
    }

    // кнопка "Назад" в режиме наблюдения и при осмотре ячейки
    void OnGUI()
    {
        if (panelT > 0f && activeCell != null)
        {
            sidebar.Draw(Smooth(panelT), activeCell);
        }

        if (state != State.Overview && state != State.Cell)
        {
            return;
        }

        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 15;
            buttonStyle.fontStyle = FontStyle.Bold;
        }

        backRect = new Rect(16f, Screen.height - 52f, 110f, 36f);
        if (GUI.Button(backRect, "←  Назад", buttonStyle))
        {
            StartCoroutine(state == State.Cell ? ExitCellRoutine() : ExitRoutine());
        }
    }

    static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * x * (x * (x * 6f - 15f) + 10f);
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float p = x - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    static bool ClickPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
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

    static Vector2 PointerPosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    static bool EscPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }
}

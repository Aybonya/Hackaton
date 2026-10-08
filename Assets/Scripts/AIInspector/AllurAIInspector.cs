using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Главный контроллер ИИ-Инспектора автозавода Allur.
/// Выполняет анализ технологических процессов в реальном времени,
/// формирует экспертные наставления и рекомендации.
/// Интегрирован с OpenAI API (GPT-4o-mini) с полной поддержкой автономного
/// режима симуляции при отсутствии или невалидности API-ключа.
/// </summary>
[DisallowMultipleComponent]
public class AllurAIInspector : MonoBehaviour
{
    public static AllurAIInspector Instance { get; private set; }

    [Header("OpenAI Configuration")]
    [Tooltip("OpenAI API Ключ (sk-...).")]
    [SerializeField] private string apiKey = OpenAIClient.DefaultApiKey;
    [SerializeField] private string model = "gpt-4o-mini";
    [SerializeField] private int requestTimeoutSeconds = 15;
    [SerializeField] private bool forceSimulationMode = false;

    [Header("Мониторинг")]
    [SerializeField] private bool autoAnalyze = true;
    [SerializeField] private float autoAnalyzeIntervalSeconds = 45f;
    [SerializeField] private KeyCode toggleHotkey = KeyCode.I;

    [Header("Интерфейс")]
    public bool isWindowOpen = false;

    // Состояние анализа
    public bool IsAnalyzing { get; private set; }
    public bool IsBottleneckAnalyzing { get; private set; }
    public bool IsMaintenanceAnalyzing { get; private set; }
    public bool IsChatResponding { get; private set; }

    public AIInspectorEngine.AuditReport LatestReport { get; private set; }
    public AIInspectorEngine.BottleneckReport LatestBottleneckReport { get; private set; }
    public AIInspectorEngine.MaintenancePlanReport LatestMaintenanceReport { get; private set; }
    public string LastApiNotice { get; private set; }
    public float FactoryHealthScore => LatestReport != null ? LatestReport.factoryHealthScore : 92.5f;

    [Serializable]
    public class ChatMessage
    {
        public enum SenderType { Operator, AI, System }
        public SenderType sender;
        public string text;
        public DateTime timestamp;
        public bool isPending;
    }

    public List<ChatMessage> ChatHistory { get; private set; } = new List<ChatMessage>();

    // Внутренние переменные UI
    private float autoTimer = 0f;
    private string customQuestionInput = "";
    private string apiKeyInput = "";
    private int activeTab = 0; // 0 - Сводка, 1 - Узкие места, 2 - План ТО, 3 - Чат, 4 - Настройки
    private Vector2 reportScroll = Vector2.zero;
    private Vector2 bottleneckScroll = Vector2.zero;
    private Vector2 maintenanceScroll = Vector2.zero;
    private Vector2 chatScroll = Vector2.zero;
    private Vector2 settingsScroll = Vector2.zero;
    private Rect windowRect;
    private bool stylesInitialized = false;

    // Переменные свободного позиционирования и перетаскивания окна мышью
    private Vector2 windowPos = Vector2.zero;
    private bool hasCustomPos = false;
    private bool isDraggingHeader = false;
    private Vector2 dragMouseOffset = Vector2.zero;

    public string ApiKey
    {
        get => apiKey;
        set
        {
            apiKey = value;
            OpenAIClient.SaveApiKey(apiKey);
        }
    }

    public string Model
    {
        get => model;
        set => model = value;
    }

    public bool ForceSimulationMode
    {
        get => forceSimulationMode;
        set => forceSimulationMode = value;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoEnsureInstance()
    {
        EnsureInstanceExists();
    }

    public static AllurAIInspector EnsureInstanceExists()
    {
        if (Instance != null) return Instance;

        AllurAIInspector existing = FindAnyObjectByType<AllurAIInspector>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject go = new GameObject("AllurAIInspector_Manager");
        existing = go.AddComponent<AllurAIInspector>();
        DontDestroyOnLoad(go);
        Debug.Log("[AllurAIInspector] Автоматически создан синглтон ИИ-Инспектора Allur в сцене.");
        return existing;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // Загрузка сохраненного ключа из PlayerPrefs или дефолтного
        apiKey = OpenAIClient.LoadApiKey(apiKey);
        if (OpenAIClient.IsPlaceholderKey(apiKey) || apiKey.Contains("E0X6Qr"))
        {
            apiKey = OpenAIClient.DefaultApiKey;
        }
        OpenAIClient.SaveApiKey(apiKey);
        apiKeyInput = apiKey;

        whiteTex = new Texture2D(1, 1);
        whiteTex.SetPixel(0, 0, Color.white);
        whiteTex.Apply();
    }

    private void Start()
    {
        // Инициализация первичных отчетов, чтобы ни одна вкладка не была пустой
        var snap = FactoryTelemetryCollector.CollectSnapshot();
        LatestReport = AIInspectorEngine.GenerateSimulatedAudit(snap);
        LatestBottleneckReport = AIInspectorEngine.GenerateBottleneckReport(snap);
        LatestMaintenanceReport = AIInspectorEngine.GenerateMaintenancePlanReport(snap);

        if (ChatHistory.Count == 0)
        {
            ChatHistory.Add(new ChatMessage
            {
                sender = ChatMessage.SenderType.AI,
                text = "👋 Здравствуйте! Я Цифровой ИИ-Инспектор автозавода Allur.\n" +
                       "Я непрерывно отслеживаю телеметрию технологических постов (сварка кузовов RobotCell, вклейка стекол, сборка колес, сход-развал, цеховая логистика).\n\n" +
                       "Задайте мне любой вопрос о текущем состоянии цеха, рисках срыва плана, причинах простоев или рекомендациях по оборудованию.",
                timestamp = DateTime.Now,
                isPending = false
            });
        }

        // Первичный экспресс-аудит завода при старте
        RunFactoryAudit(null);
    }

    private void Update()
    {
        // Проверка горячей клавиши (I или Tab)
        bool hotkeyPressed = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && (Keyboard.current.iKey.wasPressedThisFrame || Keyboard.current.tabKey.wasPressedThisFrame))
        {
            hotkeyPressed = true;
        }
#else
        if (Input.GetKeyDown(toggleHotkey) || Input.GetKeyDown(KeyCode.Tab))
        {
            hotkeyPressed = true;
        }
#endif
        if (hotkeyPressed)
        {
            ToggleWindow();
        }

        // Фоновый периодический аудит
        if (autoAnalyze)
        {
            autoTimer += Time.deltaTime;
            if (autoTimer >= autoAnalyzeIntervalSeconds)
            {
                autoTimer = 0f;
                if (!IsAnalyzing)
                {
                    RunFactoryAudit(null);
                }
            }
        }
    }

    public void ToggleWindow()
    {
        isWindowOpen = !isWindowOpen;
    }

    /// <summary>
    /// Запуск аудита всего завода (онлайн через OpenAI или офлайн симуляция).
    /// </summary>
    public void RunFactoryAudit(string customQuery = null)
    {
        if (IsAnalyzing) return;
        StartCoroutine(ExecuteAuditRoutine(null, customQuery));
    }

    /// <summary>
    /// Запуск аудита конкретного технологического процесса.
    /// </summary>
    public void RunProcessAudit(FactoryProcess process, string customQuery = null)
    {
        if (IsAnalyzing) return;
        StartCoroutine(ExecuteAuditRoutine(process, customQuery));
    }

    private IEnumerator ExecuteAuditRoutine(FactoryProcess targetProcess, string customQuery)
    {
        IsAnalyzing = true;
        LastApiNotice = null;

        var snap = FactoryTelemetryCollector.CollectSnapshot();

        bool useSimulation = forceSimulationMode || OpenAIClient.IsPlaceholderKey(apiKey);

        if (useSimulation)
        {
            // Небольшая задержка для реалистичного UX анализа
            yield return new WaitForSeconds(0.4f);

            if (targetProcess != null)
            {
                LatestReport = AIInspectorEngine.GenerateProcessAudit(targetProcess, customQuery);
            }
            else
            {
                LatestReport = AIInspectorEngine.GenerateSimulatedAudit(snap, customQuery);
            }

            LastApiNotice = "Режим автономной симуляции ИИ (для GPT укажите реальный ключ в настройках)";
            IsAnalyzing = false;
            yield break;
        }

        // Подготовка промпта для реального OpenAI
        string promptText = FactoryTelemetryCollector.BuildTelemetryPromptText(snap, customQuery);
        string systemPrompt = AIInspectorEngine.SystemRolePrompt;

        if (targetProcess != null)
        {
            promptText += $"\nСФОКУСИРУЙТЕСЬ НА УЧАСТКЕ: {targetProcess.Title} ({targetProcess.Code}).";
        }

        bool requestFinished = false;
        string responseContent = null;
        string errorMessage = null;

        yield return OpenAIClient.SendChatRequest(
            apiKey,
            model,
            systemPrompt,
            promptText,
            requestTimeoutSeconds,
            onSuccess: (text) =>
            {
                responseContent = text;
                requestFinished = true;
            },
            onError: (err) =>
            {
                errorMessage = err;
                requestFinished = true;
            }
        );

        if (!string.IsNullOrEmpty(responseContent))
        {
            // Успешный ответ от OpenAI — очищаем от markdown и выполняем глубокий парсинг
            responseContent = AIInspectorEngine.CleanMarkdown(responseContent);
            LatestReport = AIInspectorEngine.ParseOpenAIReport(responseContent, snap, model);
            LastApiNotice = "✅ Экспертный аудит успешно сформирован моделью " + model;
        }
        else
        {
            // Ошибка API (401, 429 quota, timeout) -> БЕЗОПАСНЫЙ FALLBACK
            Debug.LogWarning("[AllurAIInspector] OpenAI API вернул ошибку: " + errorMessage + ". Переключение на локальный симулятор инспектора.");
            if (!string.IsNullOrEmpty(errorMessage) && (errorMessage.Contains("Баланс") || errorMessage.Contains("credits")))
            {
                LastApiNotice = errorMessage;
            }
            else
            {
                LastApiNotice = "⚠️ OpenAI API (" + errorMessage + "). Активирован локальный экспертный движок.";
            }

            if (targetProcess != null)
            {
                LatestReport = AIInspectorEngine.GenerateProcessAudit(targetProcess, customQuery);
            }
            else
            {
                LatestReport = AIInspectorEngine.GenerateSimulatedAudit(snap, customQuery);
            }
            LatestReport.sourceName += " (Fallback)";
        }

        IsAnalyzing = false;
    }

    /// <summary>
    /// Запуск углубленного анализа узких мест и такта линии цеха.
    /// </summary>
    public void RunBottleneckAnalysis()
    {
        if (IsBottleneckAnalyzing) return;
        StartCoroutine(ExecuteBottleneckRoutine());
    }

    private IEnumerator ExecuteBottleneckRoutine()
    {
        IsBottleneckAnalyzing = true;
        LastApiNotice = "🔍 Запущен углубленный анализ узких мест цеха...";

        var snap = FactoryTelemetryCollector.CollectSnapshot();
        bool useSimulation = forceSimulationMode || OpenAIClient.IsPlaceholderKey(apiKey);

        if (useSimulation)
        {
            yield return new WaitForSeconds(0.4f);
            LatestBottleneckReport = AIInspectorEngine.GenerateBottleneckReport(snap);
            LastApiNotice = "✅ Отчет по узким местам сформирован: «" + LatestBottleneckReport.bottleneckStation + "»";
            IsBottleneckAnalyzing = false;
            yield break;
        }

        string promptText = FactoryTelemetryCollector.BuildTelemetryPromptText(snap,
            "СФОКУСИРУЙТЕСЬ НА УЗКИХ МЕСТАХ (BOTTLENECK ANALYSIS). Проанализируйте лимитирующий участок, разницу с целевым тактом (45 сек), потери сменного выпуска, состояние входного/выходного буфера, первопричины простоя и пошаговый регламент устранения затора. ВАЖНО: пишите чистым текстом без символов markdown # и **.");
        string systemPrompt = AIInspectorEngine.SystemRolePrompt;

        string responseContent = null;
        string errorMessage = null;

        yield return OpenAIClient.SendChatRequest(
            apiKey,
            model,
            systemPrompt,
            promptText,
            requestTimeoutSeconds,
            onSuccess: (text) => responseContent = text,
            onError: (err) => errorMessage = err
        );

        if (!string.IsNullOrEmpty(responseContent))
        {
            responseContent = AIInspectorEngine.CleanMarkdown(responseContent);
            LatestBottleneckReport = AIInspectorEngine.GenerateBottleneckReport(snap, responseContent, model);
            LastApiNotice = "✅ Отчет по узким местам успешно сформирован моделью " + model;
        }
        else
        {
            LatestBottleneckReport = AIInspectorEngine.GenerateBottleneckReport(snap);
            LastApiNotice = "⚠️ (Локальный эксперт): Узкое место цеха — «" + LatestBottleneckReport.bottleneckStation + "»";
        }

        IsBottleneckAnalyzing = false;
    }

    /// <summary>
    /// Запуск формирования графика превентивного техобслуживания узлов.
    /// </summary>
    public void RunMaintenancePlanAnalysis()
    {
        if (IsMaintenanceAnalyzing) return;
        StartCoroutine(ExecuteMaintenanceRoutine());
    }

    private IEnumerator ExecuteMaintenanceRoutine()
    {
        IsMaintenanceAnalyzing = true;
        LastApiNotice = "🛠️ Формирование графика превентивного техобслуживания узлов...";

        var snap = FactoryTelemetryCollector.CollectSnapshot();
        bool useSimulation = forceSimulationMode || OpenAIClient.IsPlaceholderKey(apiKey);

        if (useSimulation)
        {
            yield return new WaitForSeconds(0.4f);
            LatestMaintenanceReport = AIInspectorEngine.GenerateMaintenancePlanReport(snap);
            LastApiNotice = $"✅ График ТО узлов обновлен ({LatestMaintenanceReport.criticalCount} срочных)";
            IsMaintenanceAnalyzing = false;
            yield break;
        }

        string promptText = FactoryTelemetryCollector.BuildTelemetryPromptText(snap,
            "СФОКУСИРУЙТЕСЬ НА ПРЕВЕНТИВНОМ ТЕХОБСЛУЖИВАНИИ УЗЛОВ (PREDICTIVE MAINTENANCE). Сформируйте график превентивного ТО, распределите регламентные работы между службами СГМ, РТК и ЭТЛ, укажите требуемый ЗИП, смазки и технологическое окно для обслуживания без остановки потока. ВАЖНО: пишите чистым текстом без символов markdown # и **.");
        string systemPrompt = AIInspectorEngine.SystemRolePrompt;

        string responseContent = null;
        string errorMessage = null;

        yield return OpenAIClient.SendChatRequest(
            apiKey,
            model,
            systemPrompt,
            promptText,
            requestTimeoutSeconds,
            onSuccess: (text) => responseContent = text,
            onError: (err) => errorMessage = err
        );

        if (!string.IsNullOrEmpty(responseContent))
        {
            responseContent = AIInspectorEngine.CleanMarkdown(responseContent);
            LatestMaintenanceReport = AIInspectorEngine.GenerateMaintenancePlanReport(snap, responseContent, model);
            LastApiNotice = "✅ План ТО узлов успешно сформирован моделью " + model;
        }
        else
        {
            LatestMaintenanceReport = AIInspectorEngine.GenerateMaintenancePlanReport(snap);
            LastApiNotice = $"⚠️ (Локальный эксперт): План ТО обновлен ({LatestMaintenanceReport.criticalCount} срочных)";
        }

        IsMaintenanceAnalyzing = false;
    }

    /// <summary>
    /// Отправка вопроса в интерактивный диалог с ИИ-Инспектором (без редиректа на первую страницу).
    /// </summary>
    public void AskChatQuestion(string question)
    {
        if (string.IsNullOrWhiteSpace(question)) return;
        if (IsChatResponding) return;

        string q = question.Trim();
        customQuestionInput = "";
        activeTab = 3; // Переключаемся на вкладку диалога с ИИ

        // Добавляем вопрос пользователя в диалог
        ChatHistory.Add(new ChatMessage
        {
            sender = ChatMessage.SenderType.Operator,
            text = q,
            timestamp = DateTime.Now,
            isPending = false
        });

        // Создаем временное сообщение ожидания ответа
        var pendingMsg = new ChatMessage
        {
            sender = ChatMessage.SenderType.AI,
            text = "⏳ Анализирую телеметрию завода и формулирую ответ...",
            timestamp = DateTime.Now,
            isPending = true
        };
        ChatHistory.Add(pendingMsg);

        // Прокрутка вниз
        chatScroll.y = 99999f;

        StartCoroutine(ExecuteChatRoutine(q, pendingMsg));
    }

    private IEnumerator ExecuteChatRoutine(string question, ChatMessage pendingMsg)
    {
        IsChatResponding = true;
        var snap = FactoryTelemetryCollector.CollectSnapshot();

        bool useSimulation = forceSimulationMode || OpenAIClient.IsPlaceholderKey(apiKey);

        if (useSimulation)
        {
            yield return new WaitForSeconds(0.4f);
            pendingMsg.text = AIInspectorEngine.GenerateDirectChatAnswer(question, snap);
            pendingMsg.isPending = false;
            IsChatResponding = false;
            chatScroll.y = 99999f;
            yield break;
        }

        string systemPrompt = AIInspectorEngine.SystemRolePrompt +
            "\nВы ведете прямой профессиональный диалог с оператором/начальником цеха автозавода Allur. Ответьте емко, конкретно и структурированно на его вопрос, опираясь на текущую телеметрию завода." +
            "\nКАТЕГОРИЧЕСКИ ВАЖНО: Отвечайте ЧИСТЫМ ОБЫЧНЫМ ТЕКСТОМ без markdown (#, ##, ###, **, *). НЕ используйте жирный шрифт со звездочками. Разделяйте абзацы пустыми строками, а пункты списка — дефисами (-).";

        string userPrompt = $"=== ТЕКУЩИЙ СТАТУС ЗАВОДА ===\n" +
            $"Выпуск: {snap.totalFact}/{snap.totalPlan} авто. OEE: {Mathf.RoundToInt(snap.averageOee * 100f)}%. " +
            $"Простои: {snap.totalDowntimeMin} мин. Узкое место: {snap.bottleneckProcess}. Брак: {snap.totalDefects}.\n\n" +
            $"ВОПРОС ОПЕРАТОРА: {question}\n\n" +
            $"Напоминание: ответ должен быть без звездочек и без решеток, простым чистым текстом.";

        string responseContent = null;
        string errorMessage = null;

        yield return OpenAIClient.SendChatRequest(
            apiKey,
            model,
            systemPrompt,
            userPrompt,
            requestTimeoutSeconds,
            onSuccess: (text) => responseContent = text,
            onError: (err) => errorMessage = err
        );

        if (!string.IsNullOrEmpty(responseContent))
        {
            pendingMsg.text = AIInspectorEngine.CleanMarkdown(responseContent);
            pendingMsg.isPending = false;
        }
        else
        {
            string fallback = AIInspectorEngine.GenerateDirectChatAnswer(question, snap);
            pendingMsg.text = AIInspectorEngine.CleanMarkdown(fallback);
            pendingMsg.isPending = false;
            if (!string.IsNullOrEmpty(errorMessage))
            {
                LastApiNotice = "⚠️ OpenAI API: " + errorMessage;
            }
        }

        IsChatResponding = false;
        chatScroll.y = 99999f;
    }

    private float GetTextHeight(string text, GUIStyle style, float width)
    {
        if (string.IsNullOrEmpty(text)) return 20f;
        return Mathf.Max(20f, style.CalcHeight(new GUIContent(text), width));
    }

    private GUIStyle titleStyle, sectionHeaderStyle, subTitleStyle, bodyStyle, boldBodyStyle;
    private GUIStyle buttonStyle, primaryButtonStyle, tabStyle, activeTabStyle;
    private GUIStyle badgeStyle, kpiValStyle, kpiLabelStyle, inputStyle;
    private Texture2D whiteTex;
    private int lastScreenWidth = 0;
    private int lastScreenHeight = 0;

    private void InitStyles()
    {
        if (stylesInitialized && titleStyle != null && lastScreenWidth == Screen.width && lastScreenHeight == Screen.height) return;
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        bool isFullHD = Screen.height >= 900;

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = isFullHD ? 17 : 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white }
        };

        sectionHeaderStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = isFullHD ? 14 : 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.22f, 0.74f, 0.97f) } // #38BDF8 Sky Cyan
        };

        subTitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = isFullHD ? 12 : 11,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.55f, 0.65f, 0.78f) }
        };

        bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = isFullHD ? 13 : 12,
            fontStyle = FontStyle.Normal,
            wordWrap = true,
            richText = true,
            normal = { textColor = new Color(0.86f, 0.91f, 0.96f) }
        };

        boldBodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = isFullHD ? 13 : 12,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            richText = true,
            normal = { textColor = Color.white }
        };

        kpiValStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = isFullHD ? 22 : 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };

        kpiLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = isFullHD ? 11 : 10,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.58f, 0.68f, 0.82f) }
        };

        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = isFullHD ? 12 : 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        primaryButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = isFullHD ? 13 : 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        tabStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = isFullHD ? 13 : 11,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleCenter
        };

        activeTabStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = isFullHD ? 13 : 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        badgeStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = isFullHD ? 11 : 10,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };

        inputStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = isFullHD ? 13 : 12,
            alignment = TextAnchor.MiddleLeft
        };

        stylesInitialized = true;
    }

    private void OnGUI()
    {
        // Повышенный приоритет отрисовки IMGUI (окно всегда поверх боковых панелей и элементов сцены)
        GUI.depth = -100;
        InitStyles();

        // 1. Кнопка вызова инспектора в HUD (показываем только когда главное окно закрыто)
        if (!isWindowOpen)
        {
            DrawHudTriggerButton();
            return;
        }

        // 2. Если окно открыто — определяем доступную область экрана с учетом CellSidebar
        float sidebarPx = 0f;
        Camera mainCam = Camera.main;
        if (mainCam != null && mainCam.rect.width < 0.98f)
        {
            sidebarPx = Screen.width * (1f - mainCam.rect.width);
        }

        // Если открыта панель ячейки и остается комфортное место, центрируем инспектор в свободной левой части
        float availW = (sidebarPx > 50f && (Screen.width - sidebarPx) >= 640f)
            ? (Screen.width - sidebarPx)
            : Screen.width;

        bool isFullHD = Screen.height >= 900;

        // Размеры окна, оптимизированные для Full HD (1920x1080) и пропорциональные экрану
        float targetW = isFullHD ? (sidebarPx > 50f ? 1160f : 1220f) : 840f;
        float targetH = isFullHD ? 880f : 710f;

        float w = Mathf.Clamp(Mathf.Min(targetW, availW - 32f), 580f, 1260f);
        float h = Mathf.Clamp(Mathf.Min(targetH, Screen.height * 0.92f), 520f, 960f);

        float defaultX = Mathf.Max(12f, (availW - w) * 0.5f);
        float defaultY = Mathf.Max(12f, (Screen.height - h) * 0.5f);

        if (!hasCustomPos)
        {
            windowPos = new Vector2(defaultX, defaultY);
        }
        else
        {
            // Ограничиваем окно границами экрана, чтобы не улетало за пределы
            windowPos.x = Mathf.Clamp(windowPos.x, 4f, Mathf.Max(4f, Screen.width - w - 4f));
            windowPos.y = Mathf.Clamp(windowPos.y, 4f, Mathf.Max(4f, Screen.height - h - 4f));
        }

        windowRect = new Rect(windowPos.x, windowPos.y, w, h);
        DrawInspectorWindow(windowRect);
    }

    private void DrawHudTriggerButton()
    {
        bool isFullHD = Screen.height >= 900;
        float bw = isFullHD ? 240f : 200f;
        float bh = isFullHD ? 42f : 36f;
        float bx = isFullHD ? 20f : 16f;
        float by = isFullHD ? 20f : 16f;

        Rect btnRect = new Rect(bx, by, bw, bh);

        Color badgeColor = FactoryHealthScore >= 88f ? new Color(0.18f, 0.8f, 0.45f)
                         : FactoryHealthScore >= 75f ? new Color(0.96f, 0.72f, 0.15f)
                         : new Color(0.94f, 0.28f, 0.28f);

        DrawBox(btnRect, new Color(0.06f, 0.09f, 0.14f, 0.94f), new Color(0.2f, 0.35f, 0.55f, 0.5f));

        // Индикатор здоровья
        DrawBox(new Rect(bx + 12, by + (bh - 14) * 0.5f, 14, 14), badgeColor, Color.clear);

        string label = IsAnalyzing ? "  ИИ: Анализирую..." : $"  ИИ-Инспектор ({FactoryHealthScore:F0}%)";
        if (GUI.Button(btnRect, label, buttonStyle))
        {
            ToggleWindow();
        }
    }

    private void DrawInspectorWindow(Rect r)
    {
        bool isFullHD = Screen.height >= 900;

        // Перетаскивание окна мышью за шапку (drag bar)
        Rect dragBarRect = new Rect(r.x, r.y, r.width - 90f, isFullHD ? 42f : 36f);
        Event e = Event.current;
        if (e != null)
        {
            if (e.type == EventType.MouseDown && dragBarRect.Contains(e.mousePosition) && e.button == 0)
            {
                if (e.clickCount == 2)
                {
                    // Двойной клик сбрасывает позицию в центр экрана
                    hasCustomPos = false;
                    e.Use();
                }
                else
                {
                    isDraggingHeader = true;
                    dragMouseOffset = e.mousePosition - windowPos;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag && isDraggingHeader)
            {
                windowPos = e.mousePosition - dragMouseOffset;
                hasCustomPos = true;
                e.Use();
            }
            else if (e.rawType == EventType.MouseUp && isDraggingHeader)
            {
                isDraggingHeader = false;
                e.Use();
            }
        }

        // Подложка окна в стиле темного индустриального центра управления
        DrawBox(r, new Color(0.06f, 0.08f, 0.12f, 0.98f), new Color(0.2f, 0.35f, 0.55f, 0.6f), 2f);

        // Верхняя декоративная акцентная линия
        DrawBox(new Rect(r.x, r.y, r.width, 3), new Color(0.0f, 0.75f, 0.95f), Color.clear);

        float p = isFullHD ? 20f : 16f;
        float cw = r.width - p * 2;
        float curY = r.y + p;

        // --- ШАПКА ОКНА ---
        GUI.Label(new Rect(r.x + p, curY, Mathf.Max(260f, r.width - 320f), 26), "🤖 ЦИФРОВОЙ ИИ-ИНСПЕКТОР ALLUR", titleStyle);

        // Кнопка закрытия
        if (GUI.Button(new Rect(r.x + r.width - 44, curY - 2, 30, 26), "✕", buttonStyle))
        {
            isWindowOpen = false;
        }

        // Кнопка сброса позиции / центрирования
        if (GUI.Button(new Rect(r.x + r.width - 80, curY - 2, 32, 26), "⤢", buttonStyle))
        {
            hasCustomPos = false;
        }

        // Правый бейдж здоровья завода
        float score = FactoryHealthScore;
        Color scoreColor = score >= 88f ? new Color(0.18f, 0.8f, 0.45f)
                         : score >= 75f ? new Color(0.96f, 0.72f, 0.15f)
                         : new Color(0.94f, 0.28f, 0.28f);

        Rect scorePill = new Rect(r.x + r.width - 275, curY - 2, 188, 26);
        DrawBox(scorePill, new Color(scoreColor.r, scoreColor.g, scoreColor.b, 0.16f), scoreColor);
        GUI.Label(scorePill, $"🛡️ Индекс цеха: {score:F1}%", badgeStyle);

        curY += isFullHD ? 30 : 24;

        // Подзаголовок: статус нейросети
        string aiBadge = (LatestReport != null && !LatestReport.isSimulated)
            ? "<color=#34D399>● GPT-4o-mini (Онлайн)</color>"
            : "<color=#38BDF8>● Автономный эксперт Allur</color>";
        GUI.Label(new Rect(r.x + p, curY, cw - 240, 20), $"{aiBadge}  ·  Горячая клавиша: [ I ] / [ Tab ]", bodyStyle);

        curY += isFullHD ? 28 : 24;

        // --- НАВИГАЦИОННЫЕ ВКЛАДКИ ---
        float tabW = cw / 5f;
        float tabH = isFullHD ? 34f : 30f;
        string[] tabs = { "📊 Сводка", "🔍 Узкие места", "🛠️ План ТО", "💬 Чат с ИИ", "⚙️ Настройки" };
        for (int i = 0; i < tabs.Length; i++)
        {
            Rect tr = new Rect(r.x + p + i * tabW, curY, tabW - 4, tabH);
            if (GUI.Button(tr, tabs[i], i == activeTab ? activeTabStyle : tabStyle))
            {
                activeTab = i;
            }
        }
        curY += tabH + 6f;

        DrawLine(new Rect(r.x + p, curY, cw, 1), new Color(1f, 1f, 1f, 0.08f));
        curY += 8;

        // Уведомление API (если есть)
        if (!string.IsNullOrEmpty(LastApiNotice))
        {
            Rect noticeRect = new Rect(r.x + p, curY, cw, 24);
            Color notColor = LastApiNotice.StartsWith("✅") ? new Color(0.18f, 0.8f, 0.45f, 0.15f) : new Color(0.96f, 0.72f, 0.15f, 0.15f);
            DrawBox(noticeRect, notColor, Color.clear);
            GUI.Label(new Rect(noticeRect.x + 10, noticeRect.y + 2, cw - 20, 20), LastApiNotice, subTitleStyle);
            curY += 28;
        }

        // --- СОДЕРЖИМОЕ АКТИВНОЙ ВКЛАДКИ ---
        float footerH = isFullHD ? 40f : 34f;
        float footerY = r.y + r.height - (footerH + 12f);
        float contentHeight = footerY - curY - 10f;
        Rect contentRect = new Rect(r.x + p, curY, cw, contentHeight);

        switch (activeTab)
        {
            case 0: DrawDashboardTab(contentRect); break;
            case 1: DrawBottleneckTab(contentRect); break;
            case 2: DrawMaintenanceTab(contentRect); break;
            case 3: DrawChatTab(contentRect); break;
            case 4: DrawSettingsTab(contentRect); break;
        }

        // --- ПОДВАЛ ОКНА: КНОПКИ ДЕЙСТВИЯ ---
        DrawLine(new Rect(r.x + p, footerY - 6, cw, 1), new Color(1f, 1f, 1f, 0.08f));

        float btnW = (cw - 16) / 3f;
        if (GUI.Button(new Rect(r.x + p, footerY, btnW, footerH), IsAnalyzing ? "Анализирую..." : "⚡ Комплексный аудит (GPT)", primaryButtonStyle))
        {
            activeTab = 0;
            RunFactoryAudit(null);
        }

        if (GUI.Button(new Rect(r.x + p + btnW + 8, footerY, btnW, footerH), IsBottleneckAnalyzing ? "Анализирую..." : "🔍 Анализ узких мест", buttonStyle))
        {
            activeTab = 1;
            RunBottleneckAnalysis();
        }

        if (GUI.Button(new Rect(r.x + p + (btnW + 8) * 2, footerY, btnW, footerH), IsMaintenanceAnalyzing ? "Формирую план..." : "🛠️ План ТО узлов", buttonStyle))
        {
            activeTab = 2;
            RunMaintenancePlanAnalysis();
        }
    }

    /// <summary>
    /// Главная вкладка 0: Информативная панель руководителя (KPI, статус, ключевые алерты, топ-рекомендации).
    /// </summary>
    private void DrawDashboardTab(Rect r)
    {
        if (LatestReport == null)
        {
            GUI.Label(r, "Нажмите «Комплексный аудит (GPT)» для сбора телеметрии и анализа.", bodyStyle);
            return;
        }

        bool isFullHD = Screen.height >= 900;
        var snap = FactoryTelemetryCollector.CollectSnapshot();
        reportScroll = GUI.BeginScrollView(r, reportScroll, new Rect(0, 0, r.width - 18, isFullHD ? 980 : 920));
        float y = 2;

        // 1. KPI КАРТОЧКИ ЦЕХА (4 плитки в ряд)
        float kpiW = (r.width - 24 - 18) / 4f;
        float kpiH = isFullHD ? 68f : 58f;

        // KPI 1: OEE
        Rect kpi1 = new Rect(0, y, kpiW, kpiH);
        DrawBox(kpi1, new Color(0.08f, 0.12f, 0.18f), new Color(0.2f, 0.35f, 0.5f, 0.4f));
        GUI.Label(new Rect(kpi1.x, kpi1.y + 6, kpiW, 16), "OEE ЦЕХА", kpiLabelStyle);
        GUI.Label(new Rect(kpi1.x, kpi1.y + (isFullHD ? 22 : 20), kpiW, isFullHD ? 26 : 22), $"{Mathf.RoundToInt(snap.averageOee * 100f)}%", kpiValStyle);
        GUI.Label(new Rect(kpi1.x, kpi1.y + (isFullHD ? 46 : 40), kpiW, 16), "Норма ≥ 85%", kpiLabelStyle);

        // KPI 2: Выпуск
        Rect kpi2 = new Rect(kpiW + 6, y, kpiW, kpiH);
        DrawBox(kpi2, new Color(0.08f, 0.12f, 0.18f), new Color(0.2f, 0.35f, 0.5f, 0.4f));
        GUI.Label(new Rect(kpi2.x, kpi2.y + 6, kpiW, 16), "ВЫПУСК АВТО", kpiLabelStyle);
        GUI.Label(new Rect(kpi2.x, kpi2.y + (isFullHD ? 22 : 20), kpiW, isFullHD ? 26 : 22), $"{snap.totalFact}", kpiValStyle);
        GUI.Label(new Rect(kpi2.x, kpi2.y + (isFullHD ? 46 : 40), kpiW, 16), $"План: {snap.totalPlan} авто", kpiLabelStyle);

        // KPI 3: Простои
        Rect kpi3 = new Rect((kpiW + 6) * 2, y, kpiW, kpiH);
        DrawBox(kpi3, new Color(0.08f, 0.12f, 0.18f), new Color(0.2f, 0.35f, 0.5f, 0.4f));
        GUI.Label(new Rect(kpi3.x, kpi3.y + 6, kpiW, 16), "ПРОСТОИ ОБОРУДОВАНИЯ", kpiLabelStyle);
        Color dtColor = snap.totalDowntimeMin > 60 ? new Color(0.96f, 0.5f, 0.2f) : Color.white;
        GUI.Label(new Rect(kpi3.x, kpi3.y + (isFullHD ? 22 : 20), kpiW, isFullHD ? 26 : 22), $"<color=#{ColorUtility.ToHtmlStringRGB(dtColor)}>{snap.totalDowntimeMin} мин</color>", kpiValStyle);
        GUI.Label(new Rect(kpi3.x, kpi3.y + (isFullHD ? 46 : 40), kpiW, 16), "За смену", kpiLabelStyle);

        // KPI 4: Узкое место
        Rect kpi4 = new Rect((kpiW + 6) * 3, y, kpiW, kpiH);
        DrawBox(kpi4, new Color(0.08f, 0.12f, 0.18f), new Color(0.2f, 0.35f, 0.5f, 0.4f));
        GUI.Label(new Rect(kpi4.x, kpi4.y + 6, kpiW, 16), "УЗКОЕ МЕСТО (BOTTLENECK)", kpiLabelStyle);
        string shortBottle = !string.IsNullOrEmpty(snap.bottleneckProcess) ? snap.bottleneckProcess : "Не выявлено";
        int maxBottleChars = isFullHD ? 26 : 18;
        if (shortBottle.Length > maxBottleChars) shortBottle = shortBottle.Substring(0, maxBottleChars - 2) + "..";
        GUI.Label(new Rect(kpi4.x, kpi4.y + (isFullHD ? 22 : 20), kpiW, isFullHD ? 26 : 22), $"<color=#38BDF8>{shortBottle}</color>", kpiValStyle);
        GUI.Label(new Rect(kpi4.x, kpi4.y + (isFullHD ? 46 : 40), kpiW, 16), "Требует внимания", kpiLabelStyle);

        y += kpiH + 14;

        // 2. СТАТУС-ВЕРДИКТ ИНСПЕКЦИИ
        Color sevColor = LatestReport.severity == AIInspectorEngine.AlertSeverity.Normal ? new Color(0.18f, 0.8f, 0.45f)
                       : LatestReport.severity == AIInspectorEngine.AlertSeverity.Warning ? new Color(0.96f, 0.72f, 0.15f)
                       : new Color(0.94f, 0.28f, 0.28f);

        float statusH = isFullHD ? 66f : 60f;
        Rect statusBox = new Rect(0, y, r.width - 24, statusH);
        DrawBox(statusBox, new Color(sevColor.r, sevColor.g, sevColor.b, 0.12f), sevColor);

        Rect sevBadge = new Rect(statusBox.x + 12, y + 10, isFullHD ? 160 : 140, isFullHD ? 24 : 20);
        DrawBox(sevBadge, sevColor, Color.clear);
        GUI.Label(sevBadge, LatestReport.severityText, badgeStyle);

        GUI.Label(new Rect(statusBox.x + (isFullHD ? 186 : 162), y + 10, statusBox.width - (isFullHD ? 200 : 174), 46), LatestReport.summaryTitle, boldBodyStyle);
        y += statusH + 12;

        // 3. СЕКЦИЯ: ТОП-ПРЕДУПРЕЖДЕНИЯ
        GUI.Label(new Rect(0, y, r.width - 24, 20), "⚠️ КЛЮЧЕВЫЕ ТЕХНОЛОГИЧЕСКИЕ АНОМАЛИИ", sectionHeaderStyle);
        y += 24;

        if (LatestReport.findings != null && LatestReport.findings.Count > 0)
        {
            int showCount = Mathf.Min(4, LatestReport.findings.Count);
            for (int i = 0; i < showCount; i++)
            {
                string f = LatestReport.findings[i];
                Rect alertRow = new Rect(0, y, r.width - 24, isFullHD ? 38f : 34f);
                Color alertBg = f.Contains("🔴") ? new Color(0.94f, 0.28f, 0.28f, 0.12f)
                              : f.Contains("🟡") ? new Color(0.96f, 0.72f, 0.15f, 0.12f)
                              : new Color(1f, 1f, 1f, 0.04f);
                Color alertBorder = f.Contains("🔴") ? new Color(0.94f, 0.28f, 0.28f, 0.35f)
                                  : f.Contains("🟡") ? new Color(0.96f, 0.72f, 0.15f, 0.35f)
                                  : new Color(1f, 1f, 1f, 0.08f);

                DrawBox(alertRow, alertBg, alertBorder);
                GUI.Label(new Rect(10, y + (isFullHD ? 9 : 8), alertRow.width - 100, 22), f, bodyStyle);

                string btnLbl = f.Contains("узк") || f.Contains("Bottleneck") ? "Узкие →" : "ТО →";
                if (GUI.Button(new Rect(alertRow.width - (isFullHD ? 88 : 76), y + (isFullHD ? 6 : 5), isFullHD ? 80 : 70, isFullHD ? 26 : 24), btnLbl, buttonStyle))
                {
                    activeTab = f.Contains("узк") || f.Contains("Bottleneck") ? 1 : 2;
                }
                y += isFullHD ? 44f : 40f;
            }
        }
        else
        {
            Rect emptyRow = new Rect(0, y, r.width - 24, 30);
            DrawBox(emptyRow, new Color(1f, 1f, 1f, 0.03f), Color.clear);
            GUI.Label(new Rect(10, y + 6, emptyRow.width - 20, 20), "Аномалий не выявлено. Оборудование функционирует в допуске.", subTitleStyle);
            y += 34;
        }

        y += 8;

        // 4. СЕКЦИЯ: ТОП-3 ПРЕДПИСАНИЯ
        GUI.Label(new Rect(0, y, r.width - 24, 20), "💡 ОПЕРАТИВНЫЕ ПРЕДПИСАНИЯ И РЕКОМЕНДАЦИИ ИИ", sectionHeaderStyle);
        y += 24;

        if (LatestReport.recommendations != null && LatestReport.recommendations.Count > 0)
        {
            int recShow = Mathf.Min(3, LatestReport.recommendations.Count);
            for (int i = 0; i < recShow; i++)
            {
                Rect recBox = new Rect(0, y, r.width - 24, isFullHD ? 44f : 40f);
                DrawBox(recBox, new Color(0.08f, 0.14f, 0.24f), new Color(0.2f, 0.45f, 0.75f, 0.35f));

                // Бейдж номера
                Rect numPill = new Rect(8, y + 8, isFullHD ? 26 : 24, isFullHD ? 26 : 24);
                DrawBox(numPill, new Color(0.2f, 0.5f, 0.9f, 0.3f), Color.clear);
                GUI.Label(numPill, $"{i + 1}", badgeStyle);

                // Текст предписания
                GUI.Label(new Rect(42, y + 6, recBox.width - 54, 32), LatestReport.recommendations[i], bodyStyle);
                y += isFullHD ? 48f : 44f;
            }

            if (LatestReport.recommendations.Count > 3)
            {
                if (GUI.Button(new Rect(0, y, r.width - 24, 28), "Перейти к полному графику превентивного ТО и предписаниям →", buttonStyle))
                {
                    activeTab = 2; // Переход во вкладку плана ТО
                }
                y += 34;
            }
        }

        y += 8;

        // 5. СЕКЦИЯ: ПОЛНЫЙ ТЕКСТ ОТЧЕТА
        GUI.Label(new Rect(0, y, r.width - 24, 20), "📋 АНАЛИТИЧЕСКАЯ ЗАПИСКА НЕЙРОСЕТИ", sectionHeaderStyle);
        y += 24;

        float memoH = GetTextHeight(LatestReport.mainAnalysis, bodyStyle, r.width - 48);
        Rect memoBox = new Rect(0, y, r.width - 24, memoH + 20f);
        DrawBox(memoBox, new Color(0.05f, 0.07f, 0.1f), new Color(1f, 1f, 1f, 0.08f));
        GUI.Label(new Rect(12, y + 8, memoBox.width - 24, memoH), LatestReport.mainAnalysis, bodyStyle);
        y += memoH + 30f;

        GUI.EndScrollView();
    }

    /// <summary>
    /// Вкладка 1: Детальный отчет по узким местам (Bottleneck Analysis) и балансировке такта цеха.
    /// </summary>
    private void DrawBottleneckTab(Rect r)
    {
        if (LatestBottleneckReport == null)
        {
            var snap = FactoryTelemetryCollector.CollectSnapshot();
            LatestBottleneckReport = AIInspectorEngine.GenerateBottleneckReport(snap);
        }

        bool isFullHD = Screen.height >= 900;
        var report = LatestBottleneckReport;
        float memoH = GetTextHeight(report.rawAiAnalysis, bodyStyle, r.width - 48);
        float totalH = 480f + report.rootCauses.Count * 46f + report.actionSteps.Count * 46f + memoH;

        bottleneckScroll = GUI.BeginScrollView(r, bottleneckScroll, new Rect(0, 0, r.width - 18, totalH));
        float y = 2;

        // Заголовок вкладки и кнопка обновления
        GUI.Label(new Rect(0, y, r.width - 200, 24), "🔍 ДЕТАЛЬНЫЙ АНАЛИЗ УЗКИХ МЕСТ И ТАКТА (BOTTLENECK)", titleStyle);
        if (GUI.Button(new Rect(r.width - 190, y, 166, isFullHD ? 28 : 26), IsBottleneckAnalyzing ? "Анализирую..." : "🔄 Обновить анализ", primaryButtonStyle))
        {
            RunBottleneckAnalysis();
        }
        y += 30;

        GUI.Label(new Rect(0, y, r.width - 24, 20), $"Анализ сформирован: {report.timestamp:HH:mm:ss}  ·  Источник: {report.sourceName}", subTitleStyle);
        y += 24;

        if (IsBottleneckAnalyzing)
        {
            Rect loadingBox = new Rect(0, y, r.width - 24, 30);
            DrawBox(loadingBox, new Color(0.96f, 0.72f, 0.15f, 0.15f), new Color(0.96f, 0.72f, 0.15f, 0.5f));
            GUI.Label(new Rect(loadingBox.x + 8, loadingBox.y + 4, loadingBox.width - 16, 22), "⏳ Нейросеть Allur рассчитывает такт и выявляет скрытые заторы линии...", subTitleStyle);
            y += 36;
        }

        // Hero Card: Главное узкое место
        float heroH = isFullHD ? 74f : 68f;
        Rect heroCard = new Rect(0, y, r.width - 24, heroH);
        DrawBox(heroCard, new Color(0.20f, 0.08f, 0.08f, 0.95f), new Color(0.94f, 0.35f, 0.35f, 0.8f), 2f);
        GUI.Label(new Rect(heroCard.x + 12, heroCard.y + 8, heroCard.width - 24, 24), 
            $"🚨 ГЛАВНОЕ УЗКОЕ МЕСТО: «{report.bottleneckStation.ToUpper()}» ({report.bottleneckCode})", boldBodyStyle);
        GUI.Label(new Rect(heroCard.x + 12, heroCard.y + (isFullHD ? 38 : 34), heroCard.width - 24, 24), 
            $"OEE: <color=#F87171>{Mathf.RoundToInt(report.stationOee * 100f)}%</color>  ·  Простой за смену: <color=#FBBF24>{report.downtimeMin} мин</color>  ·  Выпуск: {report.fact} из {report.plan} авто", bodyStyle);
        y += heroH + 12;

        // 3 Плитки влияния на поток
        float tileW = (r.width - 36) / 3f;
        float tileH = isFullHD ? 68f : 62f;

        Rect t1 = new Rect(0, y, tileW, tileH);
        DrawBox(t1, new Color(0.08f, 0.12f, 0.18f), new Color(0.2f, 0.35f, 0.5f, 0.4f));
        GUI.Label(new Rect(t1.x, t1.y + 6, tileW, 16), "ТАКТ СТАНЦИИ", kpiLabelStyle);
        GUI.Label(new Rect(t1.x, t1.y + (isFullHD ? 22 : 20), tileW, isFullHD ? 26 : 22), $"{report.cycleTimeSec:F0} сек <color=#F87171>(+{report.taktLagSec:F0}с)</color>", kpiValStyle);
        GUI.Label(new Rect(t1.x, t1.y + (isFullHD ? 46 : 42), tileW, 16), $"Целевой такт: {report.taktTargetSec:F0} сек", kpiLabelStyle);

        Rect t2 = new Rect(tileW + 6, y, tileW, tileH);
        DrawBox(t2, new Color(0.08f, 0.12f, 0.18f), new Color(0.2f, 0.35f, 0.5f, 0.4f));
        GUI.Label(new Rect(t2.x, t2.y + 6, tileW, 16), "ПОТЕРИ ВЫПУСКА", kpiLabelStyle);
        GUI.Label(new Rect(t2.x, t2.y + (isFullHD ? 22 : 20), tileW, isFullHD ? 26 : 22), $"~{report.lostCarsEstimate} авто", kpiValStyle);
        GUI.Label(new Rect(t2.x, t2.y + (isFullHD ? 46 : 42), tileW, 16), "Из-за задержек смены", kpiLabelStyle);

        Rect t3 = new Rect((tileW + 6) * 2, y, tileW, tileH);
        DrawBox(t3, new Color(0.08f, 0.12f, 0.18f), new Color(0.2f, 0.35f, 0.5f, 0.4f));
        GUI.Label(new Rect(t3.x, t3.y + 6, tileW, 16), "МЕЖОПЕРАЦИОННЫЕ БУФЕРЫ", kpiLabelStyle);
        GUI.Label(new Rect(t3.x, t3.y + (isFullHD ? 22 : 20), tileW, isFullHD ? 26 : 22), "<color=#F87171>3/3</color> ➔ <color=#FBBF24>0/3</color>", kpiValStyle);
        GUI.Label(new Rect(t3.x, t3.y + (isFullHD ? 46 : 42), tileW, 16), "Переполнение / Голодание", kpiLabelStyle);
        y += tileH + 14;

        // Буферные статусы
        Rect bufBox = new Rect(0, y, r.width - 24, 40);
        DrawBox(bufBox, new Color(1f, 1f, 1f, 0.03f), new Color(1f, 1f, 1f, 0.08f));
        GUI.Label(new Rect(10, y + 2, bufBox.width - 20, 18), report.bufferUpstream, subTitleStyle);
        GUI.Label(new Rect(10, y + 20, bufBox.width - 20, 18), report.bufferDownstream, subTitleStyle);
        y += 50;

        // Первопричины
        GUI.Label(new Rect(0, y, r.width - 24, 20), "⚠️ ВЫЯВЛЕННЫЕ ПЕРВОПРИЧИНЫ ЗАТОРА (ROOT CAUSES):", sectionHeaderStyle);
        y += 24;

        foreach (var cause in report.rootCauses)
        {
            Rect causeBox = new Rect(0, y, r.width - 24, 38);
            DrawBox(causeBox, new Color(0.12f, 0.08f, 0.08f, 0.5f), new Color(0.94f, 0.35f, 0.35f, 0.3f));
            GUI.Label(new Rect(12, y + 8, causeBox.width - 24, 22), cause, bodyStyle);
            y += 44;
        }
        y += 8;

        // План действий
        GUI.Label(new Rect(0, y, r.width - 24, 20), "🛠️ ПОШАГОВЫЙ ПЛАН ИИ ПО ЛИКВИДАЦИИ УЗКОГО МЕСТА:", sectionHeaderStyle);
        y += 24;

        foreach (var step in report.actionSteps)
        {
            Rect stepBox = new Rect(0, y, r.width - 24, 38);
            DrawBox(stepBox, new Color(0.06f, 0.14f, 0.22f), new Color(0.2f, 0.45f, 0.75f, 0.4f));
            GUI.Label(new Rect(12, y + 8, stepBox.width - 24, 22), step, bodyStyle);
            y += 44;
        }
        y += 8;

        // Аналитическая записка
        GUI.Label(new Rect(0, y, r.width - 24, 20), "📋 ПОЛНЫЙ ТЕКСТ АНАЛИТИЧЕСКОЙ ЗАПИСКИ НЕЙРОСЕТИ:", sectionHeaderStyle);
        y += 24;

        Rect rawBox = new Rect(0, y, r.width - 24, memoH + 20f);
        DrawBox(rawBox, new Color(0.05f, 0.07f, 0.1f), new Color(1f, 1f, 1f, 0.08f));
        GUI.Label(new Rect(12, y + 10, rawBox.width - 24, memoH), report.rawAiAnalysis, bodyStyle);
        y += memoH + 28f;

        GUI.EndScrollView();
    }

    /// <summary>
    /// Вкладка 2: График и детальный план превентивного ТО узлов оборудования цеха.
    /// Оптимизирован для Full HD (1920x1080) с широкими карточками и наглядными индикаторами износа.
    /// </summary>
    private void DrawMaintenanceTab(Rect r)
    {
        if (LatestMaintenanceReport == null)
        {
            var snap = FactoryTelemetryCollector.CollectSnapshot();
            LatestMaintenanceReport = AIInspectorEngine.GenerateMaintenancePlanReport(snap);
        }

        bool isFullHD = Screen.height >= 900;
        var report = LatestMaintenanceReport;
        float memoH = GetTextHeight(report.rawAiAnalysis, bodyStyle, r.width - 48);
        float cardStepH = isFullHD ? 82f : 78f;
        float totalH = (isFullHD ? 290f : 260f) + report.tasks.Count * cardStepH + report.sparePartsSummary.Count * 32f + memoH;

        maintenanceScroll = GUI.BeginScrollView(r, maintenanceScroll, new Rect(0, 0, r.width - 18, totalH));
        float y = 2;

        // Заголовок вкладки и кнопка обновления
        GUI.Label(new Rect(0, y, r.width - 220, 24), "🛠️ ГРАФИК ПРЕВЕНТИВНОГО ТЕХОБСЛУЖИВАНИЯ (ПЛАН ТО)", titleStyle);
        if (GUI.Button(new Rect(r.width - 210, y, 186, isFullHD ? 28 : 26), IsMaintenanceAnalyzing ? "Формирую..." : "🔄 Обновить график ТО", primaryButtonStyle))
        {
            RunMaintenancePlanAnalysis();
        }
        y += 30;

        GUI.Label(new Rect(0, y, r.width - 24, 20), $"График сформирован: {report.timestamp:HH:mm:ss}  ·  {report.sourceName}", subTitleStyle);
        y += 24;

        if (IsMaintenanceAnalyzing)
        {
            Rect loadingBox = new Rect(0, y, r.width - 24, 30);
            DrawBox(loadingBox, new Color(0.18f, 0.74f, 0.97f, 0.15f), new Color(0.18f, 0.74f, 0.97f, 0.5f));
            GUI.Label(new Rect(loadingBox.x + 8, loadingBox.y + 4, loadingBox.width - 16, 22), "⏳ Нейросеть производит расчет наработки узлов и формирует предписания ТО...", subTitleStyle);
            y += 36;
        }

        // 3 Бейджа срочности
        float pillW = (r.width - 36) / 3f;
        float pillH = isFullHD ? 40f : 36f;
        Rect p1 = new Rect(0, y, pillW, pillH);
        DrawBox(p1, new Color(0.94f, 0.28f, 0.28f, 0.15f), new Color(0.94f, 0.28f, 0.28f, 0.6f));
        GUI.Label(p1, $"🔴 СРОЧНО (<24ч): {report.criticalCount} узлов", badgeStyle);

        Rect p2 = new Rect(pillW + 6, y, pillW, pillH);
        DrawBox(p2, new Color(0.96f, 0.72f, 0.15f, 0.15f), new Color(0.96f, 0.72f, 0.15f, 0.6f));
        GUI.Label(p2, $"🟡 ВНИМАНИЕ (<72ч): {report.warningCount} узлов", badgeStyle);

        Rect p3 = new Rect((pillW + 6) * 2, y, pillW, pillH);
        DrawBox(p3, new Color(0.18f, 0.8f, 0.45f, 0.15f), new Color(0.18f, 0.8f, 0.45f, 0.6f));
        GUI.Label(p3, $"🟢 ПЛАНОВО (>72ч): {report.scheduledCount} узлов", badgeStyle);
        y += pillH + 12;

        // Список задач оборудования
        GUI.Label(new Rect(0, y, r.width - 24, 20), "📋 ПЕРЕЧЕНЬ УЗЛОВ И РАСПРЕДЕЛЕНИЕ РАБОТ ПО СЛУЖБАМ [СГМ / РТК / ЭТЛ]:", sectionHeaderStyle);
        y += 26;

        float cardH = isFullHD ? 76f : 72f;
        foreach (var task in report.tasks)
        {
            Rect card = new Rect(0, y, r.width - 24, cardH);
            DrawBox(card, new Color(0.07f, 0.10f, 0.16f), task.urgencyColor * 0.45f);

            // Бейдж срочности
            float badgeW = isFullHD ? 130f : 120f;
            Rect urgBadge = new Rect(card.x + 8, card.y + 6, badgeW, isFullHD ? 20 : 18);
            DrawBox(urgBadge, task.urgencyColor * 0.2f, task.urgencyColor);
            GUI.Label(urgBadge, task.urgencyText, badgeStyle);

            // Название поста и узла
            float titleX = card.x + badgeW + 14f;
            float titleW = isFullHD ? 340f : 260f;
            GUI.Label(new Rect(titleX, card.y + 6, titleW, 20), $"🏭 {task.stationName} — {task.unitName}", boldBodyStyle);

            // Температура и наработка
            Color tc = task.temperature > 55f ? new Color(0.94f, 0.28f, 0.28f) : task.temperature > 48f ? new Color(0.96f, 0.72f, 0.15f) : new Color(0.18f, 0.8f, 0.45f);
            float tempX = titleX + titleW + 10f;
            GUI.Label(new Rect(tempX, card.y + 6, isFullHD ? 220 : 175, 20), $"t = <color=#{ColorUtility.ToHtmlStringRGB(tc)}>{task.temperature:F1}°C</color>  ·  ТО: {task.hoursToService}ч", bodyStyle);

            // Прогресс-бар здоровья
            float barW = isFullHD ? 140f : 110f;
            float barH = isFullHD ? 12f : 10f;
            Rect bar = new Rect(card.width - barW - 14f, card.y + 10f, barW, barH);
            Color hc = task.health < 0.65f ? Color.red : task.health < 0.80f ? Color.yellow : Color.green;
            DrawProgressBar(bar, task.health, hc, new Color(1f, 1f, 1f, 0.1f));

            // Процент здоровья
            GUI.Label(new Rect(bar.x - 52f, card.y + 6, 48, 18), $"{Mathf.RoundToInt(task.health * 100f)}%", subTitleStyle);

            // Детализация работ
            GUI.Label(new Rect(card.x + 8, card.y + (isFullHD ? 30 : 28), card.width - 16, 20), 
                $"<color=#38BDF8>{task.department}</color>  ·  🔧 Регламент: {task.procedure}", bodyStyle);
            GUI.Label(new Rect(card.x + 8, card.y + (isFullHD ? 52 : 48), card.width - 16, 20), 
                $"📦 ЗИП / материалы: {task.partsAndConsumables}", subTitleStyle);

            y += cardH + 6;
        }
        y += 8;

        // Потребность в ЗИП
        GUI.Label(new Rect(0, y, r.width - 24, 20), "📦 СВОДНАЯ ПОТРЕБНОСТЬ В РАСХОДНЫХ МАТЕРИАЛАХ И ЗИП:", sectionHeaderStyle);
        y += 24;

        foreach (var item in report.sparePartsSummary)
        {
            Rect spBox = new Rect(0, y, r.width - 24, isFullHD ? 28 : 26);
            DrawBox(spBox, new Color(1f, 1f, 1f, 0.03f), Color.clear);
            GUI.Label(new Rect(10, y + 4, spBox.width - 20, 20), item, bodyStyle);
            y += isFullHD ? 32 : 30;
        }
        y += 8;

        // Регламент проведения работ
        GUI.Label(new Rect(0, y, r.width - 24, 20), "🛡️ РЕГЛАМЕНТ БЕЗОПАСНОСТИ И ПОРЯДОК ПРИЕМКИ ОТК:", sectionHeaderStyle);
        y += 24;

        Rect gBox = new Rect(0, y, r.width - 24, memoH + 20f);
        DrawBox(gBox, new Color(0.05f, 0.07f, 0.1f), new Color(1f, 1f, 1f, 0.08f));
        GUI.Label(new Rect(12, y + 10, gBox.width - 24, memoH), report.rawAiAnalysis, bodyStyle);
        y += memoH + 26f;

        GUI.EndScrollView();
    }

    /// <summary>
    /// Вкладка 3: Интерактивный диалог с ИИ-Инспектором без редиректов и с отображением истории Q&A.
    /// Все координаты строго привязаны к рабочей области вкладки (r.x, r.y).
    /// Оптимизирован для Full HD (1920x1080) с комфортной высотой поля ввода и чипов.
    /// </summary>
    private void DrawChatTab(Rect r)
    {
        bool isFullHD = Screen.height >= 900;

        // 1. Верхняя панель управления чатом
        GUI.Label(new Rect(r.x, r.y, r.width - 130, 24), "💬 ОПЕРАТИВНЫЙ ДИАЛОГ С ИИ-ИНСПЕКТОРОМ ALLUR", titleStyle);
        float clearBtnW = isFullHD ? 120f : 110f;
        float clearBtnH = isFullHD ? 26f : 24f;
        if (GUI.Button(new Rect(r.x + r.width - clearBtnW, r.y, clearBtnW, clearBtnH), "🗑️ Очистить", buttonStyle))
        {
            ChatHistory.Clear();
            ChatHistory.Add(new ChatMessage
            {
                sender = ChatMessage.SenderType.AI,
                text = "Диалог очищен. Задайте любой технический вопрос по телеметрии автозавода Allur.",
                timestamp = DateTime.Now,
                isPending = false
            });
        }

        GUI.Label(new Rect(r.x, r.y + (isFullHD ? 26 : 24), r.width - 24, 20), 
            "Задавайте любые вопросы по участкам цеха, причинам простоев, такту или оборудованию.", subTitleStyle);

        float headerH = isFullHD ? 52f : 46f;
        float bottomBarH = isFullHD ? 86f : 74f;
        float chatAreaH = Mathf.Max(120f, r.height - headerH - bottomBarH);

        // Расчет высоты диалога
        float bubblePadding = isFullHD ? 42f : 34f;
        float totalChatH = 20f;
        for (int i = 0; i < ChatHistory.Count; i++)
        {
            string cleanText = AIInspectorEngine.CleanMarkdown(ChatHistory[i].text);
            float textH = GetTextHeight(cleanText, bodyStyle, r.width - 56);
            totalChatH += textH + bubblePadding + 10f;
        }

        // 2. Скролл истории сообщений
        Rect chatViewRect = new Rect(r.x, r.y + headerH, r.width, chatAreaH);
        DrawBox(chatViewRect, new Color(0.04f, 0.06f, 0.09f, 0.75f), new Color(1f, 1f, 1f, 0.06f));

        chatScroll = GUI.BeginScrollView(chatViewRect, chatScroll, new Rect(0, 0, r.width - 18, Mathf.Max(totalChatH, chatAreaH)));

        float curY = 8f;
        for (int i = 0; i < ChatHistory.Count; i++)
        {
            var msg = ChatHistory[i];
            string cleanText = AIInspectorEngine.CleanMarkdown(msg.text);
            float textH = GetTextHeight(cleanText, bodyStyle, r.width - 56);
            float bubbleH = textH + bubblePadding;
            Rect bubbleRect = new Rect(6, curY, r.width - 30, bubbleH);

            if (msg.sender == ChatMessage.SenderType.Operator)
            {
                // Сообщение оператора
                DrawBox(bubbleRect, new Color(0.10f, 0.18f, 0.28f, 0.95f), new Color(0.25f, 0.50f, 0.85f, 0.6f));
                GUI.Label(new Rect(bubbleRect.x + 12, bubbleRect.y + 6, bubbleRect.width - 24, 18), 
                    $"🧑‍💻 Оператор цеха  ·  {msg.timestamp:HH:mm:ss}", subTitleStyle);
                GUI.Label(new Rect(bubbleRect.x + 12, bubbleRect.y + 26, bubbleRect.width - 24, textH + 4), 
                    cleanText, boldBodyStyle);
            }
            else
            {
                // Сообщение ИИ
                Color borderC = msg.isPending ? new Color(0.96f, 0.72f, 0.15f, 0.6f) : new Color(0.18f, 0.74f, 0.97f, 0.6f);
                DrawBox(bubbleRect, new Color(0.05f, 0.11f, 0.16f, 0.95f), borderC);

                string header = msg.isPending 
                    ? $"⏳ Цифровой Инспектор (анализирую телеметрию...)  ·  {msg.timestamp:HH:mm:ss}"
                    : $"🤖 Цифровой ИИ-Инспектор Allur  ·  {msg.timestamp:HH:mm:ss}";
                GUI.Label(new Rect(bubbleRect.x + 12, bubbleRect.y + 6, bubbleRect.width - 24, 18), 
                    header, msg.isPending ? subTitleStyle : sectionHeaderStyle);
                GUI.Label(new Rect(bubbleRect.x + 12, bubbleRect.y + 26, bubbleRect.width - 24, textH + 4), 
                    cleanText, bodyStyle);
            }

            curY += bubbleH + 10f;
        }

        GUI.EndScrollView();

        // 3. Нижняя панель: Быстрые сценарии и поле ввода
        float inputSectionY = r.y + r.height - bottomBarH + 4f;

        // Быстрые чипы-сценарии
        string[] quickChips = { "⚡ OEE цеха", "🔍 Узкое место", "🛠️ График ТО", "🌡️ Нагрев узлов", "📦 Срыв плана" };
        float chipSpacing = isFullHD ? 6f : 4f;
        float chipH = isFullHD ? 28f : 24f;
        float chipW = (r.width - (quickChips.Length - 1) * chipSpacing) / quickChips.Length;
        for (int i = 0; i < quickChips.Length; i++)
        {
            Rect cr = new Rect(r.x + i * (chipW + chipSpacing), inputSectionY, chipW, chipH);
            if (GUI.Button(cr, quickChips[i], tabStyle))
            {
                if (quickChips[i].Contains("OEE")) AskChatQuestion("Как повысить текущий OEE завода на 5%?");
                else if (quickChips[i].Contains("Узкое")) AskChatQuestion("Определи главное узкое место завода и риски задержки сменного такта.");
                else if (quickChips[i].Contains("ТО")) AskChatQuestion("Сформируй график превентивного техобслуживания наиболее изношенных узлов.");
                else if (quickChips[i].Contains("Нагрев")) AskChatQuestion("Какие роботы и приводы имеют повышенную температуру?");
                else AskChatQuestion("Оцени риск срыва сменного плана выпуска авто.");
            }
        }

        // Поле ввода и кнопка
        float fieldY = inputSectionY + chipH + 6f;
        float sendBtnW = isFullHD ? 110f : 96f;
        float fieldH = isFullHD ? 36f : 32f;
        GUI.SetNextControlName("ChatInputField");
        Rect inputRect = new Rect(r.x, fieldY, r.width - sendBtnW - 8f, fieldH);
        customQuestionInput = GUI.TextField(inputRect, customQuestionInput, inputStyle);

        // Обработка клавиши Enter
        if (Event.current.type == EventType.KeyDown && 
            (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && 
            GUI.GetNameOfFocusedControl() == "ChatInputField")
        {
            if (!string.IsNullOrWhiteSpace(customQuestionInput))
            {
                AskChatQuestion(customQuestionInput);
                Event.current.Use();
            }
        }

        if (GUI.Button(new Rect(r.x + r.width - sendBtnW, fieldY, sendBtnW, fieldH), IsChatResponding ? "..." : "Спросить", primaryButtonStyle))
        {
            if (!string.IsNullOrWhiteSpace(customQuestionInput))
            {
                AskChatQuestion(customQuestionInput);
            }
        }
    }

    /// <summary>
    /// Вкладка 4: Настройки OpenAI API и параметров инспектора.
    /// Оптимизирован для Full HD (1920x1080).
    /// </summary>
    private void DrawSettingsTab(Rect r)
    {
        bool isFullHD = Screen.height >= 900;
        settingsScroll = GUI.BeginScrollView(r, settingsScroll, new Rect(0, 0, r.width - 18, isFullHD ? 520 : 460));
        float y = 2;

        GUI.Label(new Rect(0, y, r.width - 24, 24), "🔧 НАСТРОЙКИ OPENAI API:", titleStyle);
        y += 28;

        float labelW = 140f;
        float rowH = isFullHD ? 28f : 26f;
        GUI.Label(new Rect(0, y, labelW, rowH), "API Key:", bodyStyle);
        apiKeyInput = GUI.TextField(new Rect(labelW, y, r.width - 260, rowH), apiKeyInput, inputStyle);

        if (GUI.Button(new Rect(r.width - 105, y, 86, rowH), "Сохранить", buttonStyle))
        {
            ApiKey = apiKeyInput;
            LastApiNotice = "✅ Ключ успешно обновлен и сохранен!";
        }
        y += rowH + 12;

        GUI.Label(new Rect(0, y, labelW, rowH), "Модель нейросети:", bodyStyle);
        string[] models = { "gpt-4o-mini", "gpt-4o" };
        float modelBtnW = isFullHD ? 120f : 105f;
        for (int i = 0; i < models.Length; i++)
        {
            Rect mr = new Rect(labelW + i * (modelBtnW + 8), y, modelBtnW, rowH);
            if (GUI.Button(mr, models[i], model == models[i] ? activeTabStyle : tabStyle))
            {
                model = models[i];
            }
        }
        y += rowH + 14;

        forceSimulationMode = GUI.Toggle(new Rect(0, y, r.width - 24, 24), forceSimulationMode, " Автономный режим симуляции (без запросов в сеть)");
        y += 30;

        autoAnalyze = GUI.Toggle(new Rect(0, y, r.width - 24, 24), autoAnalyze, $" Автоматический фоновый аудит (каждые {autoAnalyzeIntervalSeconds:F0} сек)");
        y += 38;

        DrawLine(new Rect(0, y, r.width - 24, 1), new Color(1f, 1f, 1f, 0.08f));
        y += 18;

        GUI.Label(new Rect(0, y, r.width - 24, 24), "ℹ️ ДИАГНОСТИКА ПОДКЛЮЧЕНИЯ:", titleStyle);
        y += 28;

        bool hasValidKey = !OpenAIClient.IsPlaceholderKey(apiKey);
        string keyStatus = hasValidKey ? "<color=#34D399>Подключен (sk-...)</color>" : "<color=#F87171>Не установлен / Заглушка</color>";
        GUI.Label(new Rect(0, y, r.width - 24, 22), $"Статус ключа: {keyStatus}", bodyStyle);
        y += 24;

        GUI.Label(new Rect(0, y, r.width - 24, 22), $"Активная модель: <color=#38BDF8>{model}</color> (Таймаут: {requestTimeoutSeconds}с)", bodyStyle);
        y += 24;

        GUI.Label(new Rect(0, y, r.width - 24, 22), $"Режим работы: {(forceSimulationMode ? "Автономный эксперт Allur" : "Онлайн нейросеть OpenAI с резервным симулятором")}", bodyStyle);
        y += 32;

        GUI.EndScrollView();
    }

    private void DrawBox(Rect r, Color bg, Color border, float borderWidth = 1f)
    {
        Color old = GUI.color;
        GUI.color = bg;
        GUI.DrawTexture(r, whiteTex);

        if (border != Color.clear)
        {
            GUI.color = border;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, borderWidth), whiteTex);
            GUI.DrawTexture(new Rect(r.x, r.y + r.height - borderWidth, r.width, borderWidth), whiteTex);
            GUI.DrawTexture(new Rect(r.x, r.y, borderWidth, r.height), whiteTex);
            GUI.DrawTexture(new Rect(r.x + r.width - borderWidth, r.y, borderWidth, r.height), whiteTex);
        }
        GUI.color = old;
    }

    private void DrawProgressBar(Rect r, float progress, Color fill, Color bg)
    {
        DrawBox(r, bg, Color.clear);
        float pw = Mathf.Clamp01(progress) * r.width;
        if (pw > 0)
        {
            DrawBox(new Rect(r.x, r.y, pw, r.height), fill, Color.clear);
        }
    }

    private void DrawLine(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, whiteTex);
        GUI.color = old;
    }

    private void OnDestroy()
    {
        if (whiteTex != null)
        {
            Destroy(whiteTex);
        }
    }
}

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
public partial class AllurAIInspector : MonoBehaviour
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
            // Успешный ответ от OpenAI — выполняем глубокий парсинг структуры
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
            "СФОКУСИРУЙТЕСЬ НА УЗКИХ МЕСТАХ (BOTTLENECK ANALYSIS). Проанализируйте лимитирующий участок, разницу с целевым тактом (45 сек), потери сменного выпуска, состояние входного/выходного буфера, первопричины простоя и пошаговый регламент устранения затора.");
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
            "СФОКУСИРУЙТЕСЬ НА ПРЕВЕНТИВНОМ ТЕХОБСЛУЖИВАНИИ УЗЛОВ (PREDICTIVE MAINTENANCE). Сформируйте график превентивного ТО, распределите регламентные работы между службами СГМ, РТК и ЭТЛ, укажите требуемый ЗИП, смазки и технологическое окно для обслуживания без остановки потока.");
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
            "\nВы ведете прямой профессиональный диалог с оператором/начальником цеха автозавода Allur. Ответьте емко, конкретно и структурированно на его вопрос, опираясь на текущую телеметрию завода.";

        string userPrompt = $"=== ТЕКУЩИЙ СТАТУС ЗАВОДА ===\n" +
            $"Выпуск: {snap.totalFact}/{snap.totalPlan} авто. OEE: {Mathf.RoundToInt(snap.averageOee * 100f)}%. " +
            $"Простои: {snap.totalDowntimeMin} мин. Узкое место: {snap.bottleneckProcess}. Брак: {snap.totalDefects}.\n\n" +
            $"ВОПРОС ОПЕРАТОРА: {question}";

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
            pendingMsg.text = responseContent;
            pendingMsg.isPending = false;
        }
        else
        {
            string fallback = AIInspectorEngine.GenerateDirectChatAnswer(question, snap);
            pendingMsg.text = fallback;
            pendingMsg.isPending = false;
            if (!string.IsNullOrEmpty(errorMessage))
            {
                LastApiNotice = "⚠️ OpenAI API: " + errorMessage;
            }
        }

        IsChatResponding = false;
        chatScroll.y = 99999f;
    }

    // интерфейс окна — в AllurAIInspector.UI.cs

    private void OnDestroy()
    {
        if (whiteTex != null)
        {
            Destroy(whiteTex);
        }
    }
}

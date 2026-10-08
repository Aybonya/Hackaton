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
    public AIInspectorEngine.AuditReport LatestReport { get; private set; }
    public string LastApiNotice { get; private set; }
    public float FactoryHealthScore => LatestReport != null ? LatestReport.factoryHealthScore : 92.5f;

    // Внутренние переменные UI
    private float autoTimer = 0f;
    private string customQuestionInput = "";
    private string apiKeyInput = "";
    private int activeTab = 0; // 0 - Аудит, 1 - Диалог, 2 - Узлы/Телеметрия, 3 - Настройки
    private Vector2 reportScroll = Vector2.zero;
    private Vector2 unitsScroll = Vector2.zero;
    private Rect windowRect;
    private bool stylesInitialized = false;

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

    private GUIStyle titleStyle, sectionHeaderStyle, subTitleStyle, bodyStyle, boldBodyStyle;
    private GUIStyle buttonStyle, primaryButtonStyle, tabStyle, activeTabStyle;
    private GUIStyle badgeStyle, kpiValStyle, kpiLabelStyle, inputStyle;
    private Texture2D whiteTex;

    private void InitStyles()
    {
        if (stylesInitialized && titleStyle != null) return;

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white }
        };

        sectionHeaderStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.22f, 0.74f, 0.97f) } // #38BDF8 Sky Cyan
        };

        subTitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.55f, 0.65f, 0.78f) }
        };

        bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Normal,
            wordWrap = true,
            richText = true,
            normal = { textColor = new Color(0.86f, 0.91f, 0.96f) }
        };

        boldBodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            richText = true,
            normal = { textColor = Color.white }
        };

        kpiValStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };

        kpiLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.58f, 0.68f, 0.82f) }
        };

        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        primaryButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        tabStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleCenter
        };

        activeTabStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        badgeStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };

        inputStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft
        };

        stylesInitialized = true;
    }

    private void OnGUI()
    {
        InitStyles();

        // 1. Кнопка вызова инспектора в HUD
        DrawHudTriggerButton();

        // 2. Если окно открыто — рисуем главную панель
        if (isWindowOpen)
        {
            float w = Mathf.Min(840f, Screen.width * 0.95f);
            float h = Mathf.Min(710f, Screen.height * 0.94f);
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            windowRect = new Rect(x, y, w, h);
            DrawInspectorWindow(windowRect);
        }
    }

    private void DrawHudTriggerButton()
    {
        float bw = 220f;
        float bh = 38f;
        float bx = 16f; // Верхний левый угол
        float by = 16f;

        Rect btnRect = new Rect(bx, by, bw, bh);

        Color badgeColor = FactoryHealthScore >= 88f ? new Color(0.18f, 0.8f, 0.45f)
                         : FactoryHealthScore >= 75f ? new Color(0.96f, 0.72f, 0.15f)
                         : new Color(0.94f, 0.28f, 0.28f);

        DrawBox(btnRect, new Color(0.06f, 0.09f, 0.14f, 0.94f), new Color(0.2f, 0.35f, 0.55f, 0.5f));

        // Индикатор здоровья
        DrawBox(new Rect(bx + 12, by + 12, 14, 14), badgeColor, Color.clear);

        string label = IsAnalyzing ? "  ИИ: Анализирую..." : $"  ИИ-Инспектор ({FactoryHealthScore:F0}%)";
        if (GUI.Button(btnRect, label, buttonStyle))
        {
            ToggleWindow();
        }
    }

    private void DrawInspectorWindow(Rect r)
    {
        // Подложка окна в стиле темного индустриального центра управления
        DrawBox(r, new Color(0.06f, 0.08f, 0.12f, 0.98f), new Color(0.2f, 0.35f, 0.55f, 0.6f), 2f);

        // Верхняя декоративная акцентная линия
        DrawBox(new Rect(r.x, r.y, r.width, 3), new Color(0.0f, 0.75f, 0.95f), Color.clear);

        float p = 16f;
        float cw = r.width - p * 2;
        float curY = r.y + p;

        // --- ШАПКА ОКНА ---
        GUI.Label(new Rect(r.x + p, curY, 380, 22), "🤖 ЦИФРОВОЙ ИИ-ИНСПЕКТОР ALLUR", titleStyle);

        // Кнопка закрытия
        if (GUI.Button(new Rect(r.x + r.width - 44, curY - 2, 28, 24), "✕", buttonStyle))
        {
            isWindowOpen = false;
        }

        // Правый бейдж здоровья завода
        float score = FactoryHealthScore;
        Color scoreColor = score >= 88f ? new Color(0.18f, 0.8f, 0.45f)
                         : score >= 75f ? new Color(0.96f, 0.72f, 0.15f)
                         : new Color(0.94f, 0.28f, 0.28f);

        Rect scorePill = new Rect(r.x + r.width - 225, curY - 2, 172, 24);
        DrawBox(scorePill, new Color(scoreColor.r, scoreColor.g, scoreColor.b, 0.16f), scoreColor);
        GUI.Label(scorePill, $"🛡️ Индекс цеха: {score:F1}%", badgeStyle);

        curY += 24;

        // Подзаголовок: статус нейросети
        string aiBadge = (LatestReport != null && !LatestReport.isSimulated)
            ? "<color=#34D399>● GPT-4o-mini (Онлайн)</color>"
            : "<color=#38BDF8>● Автономный эксперт Allur</color>";
        GUI.Label(new Rect(r.x + p, curY, cw - 240, 18), $"{aiBadge}  ·  Горячая клавиша: [ I ] / [ Tab ]", bodyStyle);

        curY += 24;

        // --- НАВИГАЦИОННЫЕ ВКЛАДКИ ---
        float tabW = cw / 4f;
        string[] tabs = { "📊 Сводка цеха", "⚠️ Узлы и Алерты", "🛠️ Все рекомендации", "💬 Чат и Настройки" };
        for (int i = 0; i < tabs.Length; i++)
        {
            Rect tr = new Rect(r.x + p + i * tabW, curY, tabW - 4, 30);
            if (GUI.Button(tr, tabs[i], i == activeTab ? activeTabStyle : tabStyle))
            {
                activeTab = i;
            }
        }
        curY += 36;

        DrawLine(new Rect(r.x + p, curY, cw, 1), new Color(1f, 1f, 1f, 0.08f));
        curY += 8;

        // Уведомление API (если есть)
        if (!string.IsNullOrEmpty(LastApiNotice))
        {
            Rect noticeRect = new Rect(r.x + p, curY, cw, 22);
            Color notColor = LastApiNotice.StartsWith("✅") ? new Color(0.18f, 0.8f, 0.45f, 0.15f) : new Color(0.96f, 0.72f, 0.15f, 0.15f);
            DrawBox(noticeRect, notColor, Color.clear);
            GUI.Label(new Rect(noticeRect.x + 8, noticeRect.y + 2, cw - 16, 18), LastApiNotice, subTitleStyle);
            curY += 26;
        }

        // --- СОДЕРЖИМОЕ АКТИВНОЙ ВКЛАДКИ ---
        float contentHeight = r.y + r.height - curY - 54f;
        Rect contentRect = new Rect(r.x + p, curY, cw, contentHeight);

        switch (activeTab)
        {
            case 0: DrawDashboardTab(contentRect); break;
            case 1: DrawEquipmentAlertsTab(contentRect); break;
            case 2: DrawRecommendationsTab(contentRect); break;
            case 3: DrawChatAndSettingsTab(contentRect); break;
        }

        // --- ПОДВАЛ ОКНА: КНОПКИ ДЕЙСТВИЯ ---
        float footerY = r.y + r.height - 46f;
        DrawLine(new Rect(r.x + p, footerY - 6, cw, 1), new Color(1f, 1f, 1f, 0.08f));

        float btnW = (cw - 16) / 3f;
        if (GUI.Button(new Rect(r.x + p, footerY, btnW, 34), IsAnalyzing ? "Анализирую..." : "⚡ Комплексный аудит (GPT)", primaryButtonStyle))
        {
            RunFactoryAudit(null);
        }

        if (GUI.Button(new Rect(r.x + p + btnW + 8, footerY, btnW, 34), "🔍 Анализ узких мест", buttonStyle))
        {
            RunFactoryAudit("Определи главное узкое место завода и риски задержки сменного такта.");
        }

        if (GUI.Button(new Rect(r.x + p + (btnW + 8) * 2, footerY, btnW, 34), "🛠️ План ТО узлов", buttonStyle))
        {
            RunFactoryAudit("Сформируй график превентивного техобслуживания наиболее изношенных узлов.");
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

        var snap = FactoryTelemetryCollector.CollectSnapshot();
        reportScroll = GUI.BeginScrollView(r, reportScroll, new Rect(0, 0, r.width - 18, 920));
        float y = 2;

        // 1. KPI КАРТОЧКИ ЦЕХА (4 плитки в ряд)
        float kpiW = (r.width - 24 - 18) / 4f;
        float kpiH = 58f;

        // KPI 1: OEE
        Rect kpi1 = new Rect(0, y, kpiW, kpiH);
        DrawBox(kpi1, new Color(0.08f, 0.12f, 0.18f), new Color(0.2f, 0.35f, 0.5f, 0.4f));
        GUI.Label(new Rect(kpi1.x, kpi1.y + 6, kpiW, 14), "OEE ЦЕХА", kpiLabelStyle);
        GUI.Label(new Rect(kpi1.x, kpi1.y + 20, kpiW, 22), $"{Mathf.RoundToInt(snap.averageOee * 100f)}%", kpiValStyle);
        GUI.Label(new Rect(kpi1.x, kpi1.y + 40, kpiW, 14), "Норма ≥ 85%", kpiLabelStyle);

        // KPI 2: Выпуск
        Rect kpi2 = new Rect(kpiW + 6, y, kpiW, kpiH);
        DrawBox(kpi2, new Color(0.08f, 0.12f, 0.18f), new Color(0.2f, 0.35f, 0.5f, 0.4f));
        GUI.Label(new Rect(kpi2.x, kpi2.y + 6, kpiW, 14), "ВЫПУСК АВТО", kpiLabelStyle);
        GUI.Label(new Rect(kpi2.x, kpi2.y + 20, kpiW, 22), $"{snap.totalFact}", kpiValStyle);
        GUI.Label(new Rect(kpi2.x, kpi2.y + 40, kpiW, 14), $"План: {snap.totalPlan} авто", kpiLabelStyle);

        // KPI 3: Простои
        Rect kpi3 = new Rect((kpiW + 6) * 2, y, kpiW, kpiH);
        DrawBox(kpi3, new Color(0.08f, 0.12f, 0.18f), new Color(0.2f, 0.35f, 0.5f, 0.4f));
        GUI.Label(new Rect(kpi3.x, kpi3.y + 6, kpiW, 14), "ПРОСТОИ ОБОРУДОВАНИЯ", kpiLabelStyle);
        Color dtColor = snap.totalDowntimeMin > 60 ? new Color(0.96f, 0.5f, 0.2f) : Color.white;
        GUI.Label(new Rect(kpi3.x, kpi3.y + 20, kpiW, 22), $"<color=#{ColorUtility.ToHtmlStringRGB(dtColor)}>{snap.totalDowntimeMin} мин</color>", kpiValStyle);
        GUI.Label(new Rect(kpi3.x, kpi3.y + 40, kpiW, 14), "За смену", kpiLabelStyle);

        // KPI 4: Узкое место
        Rect kpi4 = new Rect((kpiW + 6) * 3, y, kpiW, kpiH);
        DrawBox(kpi4, new Color(0.08f, 0.12f, 0.18f), new Color(0.2f, 0.35f, 0.5f, 0.4f));
        GUI.Label(new Rect(kpi4.x, kpi4.y + 6, kpiW, 14), "УЗКОЕ МЕСТО (BOTTLENECK)", kpiLabelStyle);
        string shortBottle = !string.IsNullOrEmpty(snap.bottleneckProcess) ? snap.bottleneckProcess : "Не выявлено";
        if (shortBottle.Length > 18) shortBottle = shortBottle.Substring(0, 16) + "..";
        GUI.Label(new Rect(kpi4.x, kpi4.y + 20, kpiW, 22), $"<color=#38BDF8>{shortBottle}</color>", kpiValStyle);
        GUI.Label(new Rect(kpi4.x, kpi4.y + 40, kpiW, 14), "Требует внимания", kpiLabelStyle);

        y += kpiH + 12;

        // 2. СТАТУС-ВЕРДИКТ ИНСПЕКЦИИ
        Color sevColor = LatestReport.severity == AIInspectorEngine.AlertSeverity.Normal ? new Color(0.18f, 0.8f, 0.45f)
                       : LatestReport.severity == AIInspectorEngine.AlertSeverity.Warning ? new Color(0.96f, 0.72f, 0.15f)
                       : new Color(0.94f, 0.28f, 0.28f);

        Rect statusBox = new Rect(0, y, r.width - 24, 60);
        DrawBox(statusBox, new Color(sevColor.r, sevColor.g, sevColor.b, 0.12f), sevColor);

        Rect sevBadge = new Rect(statusBox.x + 12, y + 10, 140, 20);
        DrawBox(sevBadge, sevColor, Color.clear);
        GUI.Label(sevBadge, LatestReport.severityText, badgeStyle);

        GUI.Label(new Rect(statusBox.x + 162, y + 10, statusBox.width - 174, 42), LatestReport.summaryTitle, boldBodyStyle);
        y += 70;

        // 3. СЕКЦИЯ: ТОП-ПРЕДУПРЕЖДЕНИЯ
        GUI.Label(new Rect(0, y, r.width - 24, 18), "⚠️ КЛЮЧЕВЫЕ ТЕХНОЛОГИЧЕСКИЕ АНОМАЛИИ", sectionHeaderStyle);
        y += 22;

        if (LatestReport.findings != null && LatestReport.findings.Count > 0)
        {
            int showCount = Mathf.Min(4, LatestReport.findings.Count);
            for (int i = 0; i < showCount; i++)
            {
                string f = LatestReport.findings[i];
                Rect alertRow = new Rect(0, y, r.width - 24, 34);
                Color alertBg = f.Contains("🔴") ? new Color(0.94f, 0.28f, 0.28f, 0.12f)
                              : f.Contains("🟡") ? new Color(0.96f, 0.72f, 0.15f, 0.12f)
                              : new Color(1f, 1f, 1f, 0.04f);
                Color alertBorder = f.Contains("🔴") ? new Color(0.94f, 0.28f, 0.28f, 0.35f)
                                  : f.Contains("🟡") ? new Color(0.96f, 0.72f, 0.15f, 0.35f)
                                  : new Color(1f, 1f, 1f, 0.08f);

                DrawBox(alertRow, alertBg, alertBorder);
                GUI.Label(new Rect(10, y + 8, alertRow.width - 90, 20), f, bodyStyle);

                if (GUI.Button(new Rect(alertRow.width - 76, y + 5, 70, 24), "Узлы →", buttonStyle))
                {
                    activeTab = 1; // Переход во вкладку узлов
                }

                y += 38;
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
        GUI.Label(new Rect(0, y, r.width - 24, 18), "💡 ОПЕРАТИВНЫЕ ПРЕДПИСАНИЯ И РЕКОМЕНДАЦИИ ИИ", sectionHeaderStyle);
        y += 22;

        if (LatestReport.recommendations != null && LatestReport.recommendations.Count > 0)
        {
            int recShow = Mathf.Min(3, LatestReport.recommendations.Count);
            for (int i = 0; i < recShow; i++)
            {
                Rect recBox = new Rect(0, y, r.width - 24, 40);
                DrawBox(recBox, new Color(0.08f, 0.14f, 0.24f), new Color(0.2f, 0.45f, 0.75f, 0.35f));

                // Бейдж номера
                Rect numPill = new Rect(8, y + 8, 24, 24);
                DrawBox(numPill, new Color(0.2f, 0.5f, 0.9f, 0.3f), Color.clear);
                GUI.Label(numPill, $"{i + 1}", badgeStyle);

                // Текст предписания
                GUI.Label(new Rect(38, y + 6, recBox.width - 50, 30), LatestReport.recommendations[i], bodyStyle);
                y += 44;
            }

            if (LatestReport.recommendations.Count > 3)
            {
                if (GUI.Button(new Rect(0, y, r.width - 24, 26), $"Показать все {LatestReport.recommendations.Count} рекомендаций подробнее →", buttonStyle))
                {
                    activeTab = 2; // Переход во вкладку всех рекомендаций
                }
                y += 32;
            }
        }

        y += 8;

        // 5. СЕКЦИЯ: ПОЛНЫЙ ТЕКСТ ОТЧЕТА
        GUI.Label(new Rect(0, y, r.width - 24, 18), "📋 АНАЛИТИЧЕСКАЯ ЗАПИСКА НЕЙРОСЕТИ", sectionHeaderStyle);
        y += 22;

        Rect memoBox = new Rect(0, y, r.width - 24, 320);
        DrawBox(memoBox, new Color(0.05f, 0.07f, 0.1f), new Color(1f, 1f, 1f, 0.08f));
        GUI.Label(new Rect(12, y + 8, memoBox.width - 24, 304), LatestReport.mainAnalysis, bodyStyle);
        y += 330;

        GUI.EndScrollView();
    }

    /// <summary>
    /// Вкладка 1: Подробный мониторинг каждого поста и узла цеха (температуры, здоровье, ТО).
    /// </summary>
    private void DrawEquipmentAlertsTab(Rect r)
    {
        var snap = FactoryTelemetryCollector.CollectSnapshot();
        unitsScroll = GUI.BeginScrollView(r, unitsScroll, new Rect(0, 0, r.width - 18, Mathf.Max(snap.processes.Count * 230f, r.height)));

        float y = 0;
        foreach (var p in snap.processes)
        {
            // Плашка процесса
            Rect pCard = new Rect(0, y, r.width - 24, 28);
            Color pHeaderBg = p.oee < 0.75f ? new Color(0.94f, 0.28f, 0.28f, 0.25f)
                            : p.oee < 0.85f ? new Color(0.96f, 0.72f, 0.15f, 0.2f)
                            : new Color(0.18f, 0.8f, 0.45f, 0.15f);

            DrawBox(pCard, pHeaderBg, Color.clear);
            GUI.Label(new Rect(10, y + 5, r.width - 40, 20),
                $"🏭 {p.title} ({p.code})  ·  OEE: {Mathf.RoundToInt(p.oee * 100f)}%  ·  Выпуск: {p.fact}/{p.plan}  ·  Простой: {p.downtimeMin} мин", boldBodyStyle);
            y += 32;

            foreach (var u in p.units)
            {
                Rect uCard = new Rect(0, y, r.width - 24, 36);
                Color hc = u.health >= 0.80f ? new Color(0.18f, 0.8f, 0.45f)
                         : u.health >= 0.65f ? new Color(0.96f, 0.72f, 0.15f)
                         : new Color(0.94f, 0.28f, 0.28f);

                DrawBox(uCard, new Color(1f, 1f, 1f, 0.03f), new Color(1f, 1f, 1f, 0.06f));

                // Имя узла
                GUI.Label(new Rect(12, y + 8, 180, 20), u.name, boldBodyStyle);

                // Температура с цветовым бейджем
                Color tempColor = u.temp > 58f ? new Color(0.94f, 0.28f, 0.28f)
                                : u.temp > 48f ? new Color(0.96f, 0.72f, 0.15f)
                                : new Color(0.18f, 0.8f, 0.45f);
                GUI.Label(new Rect(195, y + 8, 110, 20), $"t = <color=#{ColorUtility.ToHtmlStringRGB(tempColor)}>{u.temp:F1}°C</color>", bodyStyle);

                // Наработка и часы до ТО
                GUI.Label(new Rect(305, y + 8, 180, 20), $"Наработка: {u.hours}ч  ·  ТО: {u.hoursToService}ч", subTitleStyle);

                // Прогресс-бар здоровья узла
                Rect bar = new Rect(r.width - 150, y + 14, 110, 10);
                DrawProgressBar(bar, u.health, hc, new Color(1f, 1f, 1f, 0.12f));

                GUI.Label(new Rect(r.width - 150, y + 1, 110, 12), $"Здоровье {Mathf.RoundToInt(u.health * 100f)}%", kpiLabelStyle);

                y += 40;
            }
            y += 10;
        }

        GUI.EndScrollView();
    }

    /// <summary>
    /// Вкладка 2: Полный развернутый список всех инженерных предписаний и рекомендаций.
    /// </summary>
    private void DrawRecommendationsTab(Rect r)
    {
        if (LatestReport == null || LatestReport.recommendations == null || LatestReport.recommendations.Count == 0)
        {
            GUI.Label(r, "Рекомендации формируются в процессе аудита.", bodyStyle);
            return;
        }

        unitsScroll = GUI.BeginScrollView(r, unitsScroll, new Rect(0, 0, r.width - 18, LatestReport.recommendations.Count * 65f + 40f));
        float y = 0;

        GUI.Label(new Rect(0, y, r.width - 24, 20), "🛠️ ПОЛНЫЙ ПЕРЕЧЕНЬ ИНЖЕНЕРНЫХ ПРЕДПИСАНИЙ ДЛЯ СЛУЖБ ЦЕХА:", titleStyle);
        y += 28;

        for (int i = 0; i < LatestReport.recommendations.Count; i++)
        {
            string rec = LatestReport.recommendations[i];
            Rect card = new Rect(0, y, r.width - 24, 54);
            DrawBox(card, new Color(0.08f, 0.13f, 0.22f), new Color(0.2f, 0.4f, 0.7f, 0.35f));

            // Номерной бейдж
            Rect numPill = new Rect(10, y + 12, 28, 28);
            DrawBox(numPill, new Color(0.2f, 0.5f, 0.9f, 0.25f), new Color(0.2f, 0.5f, 0.9f, 0.5f));
            GUI.Label(numPill, $"{i + 1}", badgeStyle);

            // Определение службы по тексту
            string tag = rec.Contains("механик") || rec.Contains("смазк") || rec.Contains("СГМ") ? "[СГМ]"
                       : rec.Contains("робот") || rec.Contains("разгон") || rec.Contains("KUKA") ? "[РТК]"
                       : rec.Contains("брак") || rec.Contains("герметик") || rec.Contains("стекл") ? "[ОТК]"
                       : rec.Contains("логист") || rec.Contains("поток") || rec.Contains("буфер") ? "[ЛОГИСТИКА]"
                       : "[ТЕХНОЛОГИ]";

            Color tagColor = tag == "[СГМ]" ? new Color(0.2f, 0.6f, 1f)
                           : tag == "[РТК]" ? new Color(0.7f, 0.4f, 1f)
                           : tag == "[ОТК]" ? new Color(0.2f, 0.8f, 0.5f)
                           : new Color(1f, 0.6f, 0.2f);

            GUI.Label(new Rect(48, y + 6, 120, 18), $"<color=#{ColorUtility.ToHtmlStringRGB(tagColor)}>{tag}</color>", boldBodyStyle);
            GUI.Label(new Rect(48, y + 22, card.width - 60, 30), rec, bodyStyle);

            y += 58;
        }

        GUI.EndScrollView();
    }

    /// <summary>
    /// Вкладка 3: Интерактивный диалог с ИИ и настройки параметров OpenAI.
    /// </summary>
    private void DrawChatAndSettingsTab(Rect r)
    {
        unitsScroll = GUI.BeginScrollView(r, unitsScroll, new Rect(0, 0, r.width - 18, 560));
        float y = 0;

        // БЛОК 1: ДИАЛОГ С ИИ
        GUI.Label(new Rect(0, y, r.width - 24, 20), "💬 ВОПРОС ЦИФРОВОМУ ИНСПЕКТОРУ:", titleStyle);
        y += 24;

        Rect inputRect = new Rect(0, y, r.width - 130, 32);
        customQuestionInput = GUI.TextField(inputRect, customQuestionInput, inputStyle);

        if (GUI.Button(new Rect(r.width - 120, y, 96, 32), "Спросить", primaryButtonStyle))
        {
            if (!string.IsNullOrWhiteSpace(customQuestionInput))
            {
                RunFactoryAudit(customQuestionInput);
                activeTab = 0;
            }
        }
        y += 40;

        GUI.Label(new Rect(0, y, r.width - 24, 18), "Быстрые сценарии оператора:", subTitleStyle);
        y += 20;

        string[] quickQuestions =
        {
            "Как повысить текущий OEE завода на 5%?",
            "Какие манипуляторы имеют повышенную температуру?",
            "Оцени риск срыва сменного плана на линии колес."
        };

        foreach (var q in quickQuestions)
        {
            if (GUI.Button(new Rect(0, y, r.width - 24, 26), "• " + q, tabStyle))
            {
                customQuestionInput = q;
                RunFactoryAudit(q);
                activeTab = 0;
            }
            y += 30;
        }

        y += 16;
        DrawLine(new Rect(0, y, r.width - 24, 1), new Color(1f, 1f, 1f, 0.08f));
        y += 14;

        // БЛОК 2: НАСТРОЙКИ API
        GUI.Label(new Rect(0, y, r.width - 24, 20), "🔧 НАСТРОЙКИ OPENAI API:", titleStyle);
        y += 24;

        GUI.Label(new Rect(0, y, 140, 22), "API Key:", bodyStyle);
        apiKeyInput = GUI.TextField(new Rect(140, y, r.width - 250, 26), apiKeyInput, inputStyle);

        if (GUI.Button(new Rect(r.width - 100, y, 76, 26), "Сохранить", buttonStyle))
        {
            ApiKey = apiKeyInput;
            LastApiNotice = "✅ Ключ успешно обновлен и сохранен!";
        }
        y += 36;

        GUI.Label(new Rect(0, y, 140, 22), "Модель нейросети:", bodyStyle);
        string[] models = { "gpt-4o-mini", "gpt-4o" };
        for (int i = 0; i < models.Length; i++)
        {
            Rect mr = new Rect(140 + i * 115, y, 105, 26);
            if (GUI.Button(mr, models[i], model == models[i] ? activeTabStyle : tabStyle))
            {
                model = models[i];
            }
        }
        y += 38;

        forceSimulationMode = GUI.Toggle(new Rect(0, y, r.width - 24, 22), forceSimulationMode, " Автономный режим симуляции (без запросов в сеть)");
        y += 26;

        autoAnalyze = GUI.Toggle(new Rect(0, y, r.width - 24, 22), autoAnalyze, $" Автоматический периодический аудит (каждые {autoAnalyzeIntervalSeconds:F0} сек)");
        y += 30;

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

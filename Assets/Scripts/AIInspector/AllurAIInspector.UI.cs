using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

// Окно ИИ-Инспектора (IMGUI). Вёрстка в логических пикселях под высоту 1080 и масштабируется под экран,
// как правая панель CellSidebar. Тексты ИИ приходят с эмодзи и Markdown — здесь они чистятся
// (эмодзи шрифт Unity не рисует) и раскладываются на заголовки, списки и абзацы.
public partial class AllurAIInspector
{
    // ---------- палитра (в тон CellSidebar) ----------
    static readonly Color UiBg = new Color(0.043f, 0.055f, 0.078f, 0.985f);
    static readonly Color UiCard = new Color(1f, 1f, 1f, 0.04f);
    static readonly Color UiCardHi = new Color(1f, 1f, 1f, 0.07f);
    static readonly Color UiLine = new Color(1f, 1f, 1f, 0.07f);
    static readonly Color UiInk = new Color(0.93f, 0.95f, 0.98f);
    static readonly Color UiMuted = new Color(0.60f, 0.66f, 0.74f);
    static readonly Color UiFaint = new Color(0.44f, 0.50f, 0.58f);
    static readonly Color UiGood = new Color(0.25f, 0.78f, 0.45f);
    static readonly Color UiWarn = new Color(0.96f, 0.70f, 0.14f);
    static readonly Color UiBad = new Color(0.93f, 0.33f, 0.33f);
    static readonly Color UiAccent = new Color(0.32f, 0.56f, 1f);

    const float RefHeight = 1080f;
    static readonly CultureInfo Ru = new CultureInfo("ru-RU");
    static readonly string[] TabNames = { "Сводка", "Узкие места", "Обслуживание", "Спросить ИИ", "Настройки" };
    static readonly string[] TabTitles = { "Сводка для руководителя", "Узкие места потока", "План обслуживания оборудования", "Вопрос ИИ-Инспектору", "Подключение и режим работы" };

    Texture2D whiteTex;
    GUIStyle uiText, uiField;
    float uiScale = 1f;
    float[] contentHeights = new float[5];
    bool showFullReport, showFullBottleneck, showFullGuidelines;
    int maintenanceFilter;          // 0 все, 1 срочно, 2 внимание
    int expandedTask = -1;
    FactoryTelemetryCollector.FactorySnapshot uiSnap;
    float uiSnapTime = -10f;

    // окно открыто — клики и перетаскивание по 3D-сцене не должны проходить сквозь него
    public static bool IsModalOpen => Instance != null && Instance.isActiveAndEnabled && Instance.isWindowOpen;

    FactoryTelemetryCollector.FactorySnapshot Snap
    {
        get
        {
            if (uiSnap == null || Time.unscaledTime - uiSnapTime > 1f)
            {
                uiSnap = FactoryTelemetryCollector.CollectSnapshot();
                uiSnapTime = Time.unscaledTime;
            }
            return uiSnap;
        }
    }

    // =====================================================================================
    // Точка входа
    // =====================================================================================

    private void OnGUI()
    {
        GUI.depth = -100;
        EnsureUi();

        uiScale = Screen.height / RefHeight;
        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
        float lw = Screen.width / uiScale;
        float lh = RefHeight;

        if (!isWindowOpen)
        {
            DrawLauncher();
            GUI.matrix = oldMatrix;
            return;
        }

        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            isWindowOpen = false;
            e.Use();
            GUI.matrix = oldMatrix;
            return;
        }

        // свободная область слева от панели процесса
        float sidebar = 0f;
        Camera cam = Camera.main;
        if (cam != null && cam.rect.width < 0.98f) sidebar = Screen.width * (1f - cam.rect.width) / uiScale;
        float availW = lw - sidebar;
        if (availW < 900f) availW = lw;

        Fill(new Rect(0, 0, availW, lh), new Color(0f, 0f, 0f, 0.35f));

        float w = Mathf.Min(1240f, availW - 48f);
        float h = Mathf.Min(960f, lh - 56f);
        Vector2 home = new Vector2((availW - w) * 0.5f, (lh - h) * 0.5f);
        if (!hasCustomPos) windowPos = home;
        windowPos.x = Mathf.Clamp(windowPos.x, 8f, Mathf.Max(8f, lw - w - 8f));
        windowPos.y = Mathf.Clamp(windowPos.y, 8f, Mathf.Max(8f, lh - h - 8f));
        windowRect = new Rect(windowPos.x, windowPos.y, w, h);

        DrawWindow(windowRect);
        GUI.matrix = oldMatrix;
    }

    void EnsureUi()
    {
        if (whiteTex == null)
        {
            whiteTex = new Texture2D(1, 1);
            whiteTex.SetPixel(0, 0, Color.white);
            whiteTex.Apply();
        }
        if (uiText == null)
        {
            uiText = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Clip
            };
            // пустой стиль без стандартной серой подложки Unity — фон поля рисуем сами
            uiField = new GUIStyle
            {
                font = GUI.skin.textField.font,
                fontSize = 15,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(14, 14, 0, 0),
                clipping = TextClipping.Clip
            };
            foreach (GUIStyleState st in new[] { uiField.normal, uiField.focused, uiField.hover, uiField.active })
                st.textColor = UiInk;
        }
    }

    // =====================================================================================
    // Кнопка вызова в левом верхнем углу
    // =====================================================================================

    void DrawLauncher()
    {
        Rect r = new Rect(20, 20, 292, 52);
        bool hover = r.Contains(Event.current.mousePosition);
        Round(r, hover ? new Color(0.09f, 0.11f, 0.15f, 0.97f) : new Color(0.06f, 0.075f, 0.1f, 0.94f), 14f);
        RoundBorder(r, hover ? new Color(UiAccent.r, UiAccent.g, UiAccent.b, 0.6f) : UiLine, 1f, 14f);

        float score = FactoryHealthScore;
        Color sc = ScoreColor(score);
        float pulse = 0.55f + 0.45f * Mathf.PingPong(Time.unscaledTime * 1.2f, 1f);
        Round(new Rect(r.x + 18, r.y + 20, 12, 12), new Color(sc.r, sc.g, sc.b, IsAnalyzing ? pulse : 1f), 6f);

        Text(new Rect(r.x + 42, r.y + 8, 180, 20), "ИИ-Инспектор", 16, UiInk, TextAnchor.MiddleLeft, true);
        Text(new Rect(r.x + 42, r.y + 27, 180, 18), IsAnalyzing ? "анализирует данные…" : "состояние цеха " + score.ToString("0", Ru) + " из 100", 13, UiMuted, TextAnchor.MiddleLeft, false);

        Rect key = new Rect(r.xMax - 42, r.y + 14, 26, 24);
        RoundBorder(key, UiLine, 1f, 6f);
        Text(key, "I", 13, UiMuted, TextAnchor.MiddleCenter, true);

        if (GUI.Button(r, GUIContent.none, GUIStyle.none)) ToggleWindow();
    }

    // =====================================================================================
    // Окно
    // =====================================================================================

    void DrawWindow(Rect r)
    {
        // тень и подложка
        Round(new Rect(r.x - 2, r.y + 6, r.width + 4, r.height + 4), new Color(0f, 0f, 0f, 0.35f), 20f);
        Round(r, UiBg, 18f);
        RoundBorder(r, new Color(1f, 1f, 1f, 0.09f), 1f, 18f);

        const float P = 32f;
        float x = r.x + P, cw = r.width - P * 2;

        // ---------- шапка (за неё окно перетаскивается) ----------
        Rect drag = new Rect(r.x, r.y, r.width - 120, 92);
        HandleDrag(drag);

        Text(new Rect(x, r.y + 24, 600, 16), "ALLUR  ·  ЦИФРОВОЙ ИИ-ИНСПЕКТОР", 12, UiFaint, TextAnchor.MiddleLeft, true);
        Text(new Rect(x, r.y + 42, cw - 420, 34), TabTitles[activeTab], 27, UiInk, TextAnchor.MiddleLeft, true);

        // справа: источник, время, закрыть
        Rect close = new Rect(r.xMax - P - 36, r.y + 30, 36, 36);
        if (IconButton(close, "×", 22)) isWindowOpen = false;

        bool online = LatestReport != null && !LatestReport.isSimulated;
        string mode = online ? "GPT · " + model : "Автономный режим";
        Color modeColor = online ? UiGood : UiAccent;
        float pillW = Measure(mode, 13, true) + 40;
        Rect modePill = new Rect(close.x - 14 - pillW, r.y + 34, pillW, 28);
        Round(modePill, Tint(modeColor, 0.14f), 14f);
        Round(new Rect(modePill.x + 14, modePill.y + 10, 8, 8), modeColor, 4f);
        Text(new Rect(modePill.x + 28, modePill.y, pillW - 34, 28), mode, 13, modeColor, TextAnchor.MiddleLeft, true);

        DateTime stamp = LatestReport != null ? LatestReport.timestamp : DateTime.Now;
        Text(new Rect(modePill.x - 230, r.y + 34, 216, 28), "обновлено в " + stamp.ToString("HH:mm", Ru), 13, UiMuted, TextAnchor.MiddleRight, false);

        // ---------- вкладки ----------
        float ty = r.y + 96;
        float tx = x;
        for (int i = 0; i < TabNames.Length; i++)
        {
            string label = TabNames[i];
            float tw = Measure(label, 15, true) + 8;
            int badge = 0;
            Color badgeColor = UiBad;
            if (i == 2 && LatestMaintenanceReport != null) badge = LatestMaintenanceReport.criticalCount;
            float full = tw + (badge > 0 ? 30 : 0);
            Rect tr = new Rect(tx, ty, full, 40);
            bool active = i == activeTab;
            bool hover = tr.Contains(Event.current.mousePosition);
            Text(new Rect(tx, ty, tw, 36), label, 15, active ? UiInk : hover ? new Color(0.8f, 0.85f, 0.9f) : UiMuted, TextAnchor.MiddleLeft, active);
            if (badge > 0)
            {
                Rect b = new Rect(tx + tw + 2, ty + 9, 24, 18);
                Round(b, Tint(badgeColor, 0.2f), 9f);
                Text(b, badge.ToString(Ru), 11, badgeColor, TextAnchor.MiddleCenter, true);
            }
            if (active) Round(new Rect(tx, ty + 38, full - 8, 3), UiAccent, 1.5f);
            if (GUI.Button(tr, GUIContent.none, GUIStyle.none)) activeTab = i;
            tx += full + 26;
        }
        Fill(new Rect(r.x, ty + 41, r.width, 1), UiLine);

        // ---------- уведомление о работе ИИ ----------
        float cy = ty + 56;
        string notice = CleanInline(LastApiNotice);
        if (!string.IsNullOrEmpty(notice))
        {
            bool bad = LastApiNotice.Contains("⚠") || LastApiNotice.Contains("Ошибк") || LastApiNotice.Contains("ошибк");
            Color nc = bad ? UiWarn : UiGood;
            Rect nr = new Rect(x, cy, cw, 34);
            Round(nr, Tint(nc, 0.09f), 10f);
            Round(new Rect(nr.x + 14, nr.y + 13, 8, 8), nc, 4f);
            Text(new Rect(nr.x + 32, nr.y, nr.width - 44, 34), notice, 13, new Color(0.85f, 0.88f, 0.92f), TextAnchor.MiddleLeft, false);
            cy += 46;
        }

        // ---------- подвал ----------
        float footerH = 72f;
        float fy = r.yMax - footerH;
        Fill(new Rect(r.x, fy, r.width, 1), UiLine);
        DrawFooter(new Rect(x, fy, cw, footerH));

        // ---------- содержимое ----------
        Rect content = new Rect(x, cy, cw, fy - cy - 8);
        if (activeTab == 3)
        {
            DrawChat(content);
            return;
        }

        Vector2 scroll = TabScroll();
        float viewH = Mathf.Max(contentHeights[activeTab], content.height);
        scroll = GUI.BeginScrollView(content, scroll, new Rect(0, 0, content.width - 14, viewH), false, false, GUIStyle.none, GUIStyle.none);
        float used = 0f;
        float cwInner = content.width - 14;
        switch (activeTab)
        {
            case 0: used = DrawSummary(cwInner); break;
            case 1: used = DrawBottlenecks(cwInner); break;
            case 2: used = DrawMaintenance(cwInner); break;
            case 4: used = DrawSettings(cwInner); break;
        }
        GUI.EndScrollView();
        contentHeights[activeTab] = used + 16f;
        SetTabScroll(scroll);
        DrawScrollIndicator(content, scroll.y, viewH);
    }

    void DrawFooter(Rect f)
    {
        string source = activeTab == 1 && LatestBottleneckReport != null ? LatestBottleneckReport.sourceName
                      : activeTab == 2 && LatestMaintenanceReport != null ? LatestMaintenanceReport.sourceName
                      : LatestReport != null ? LatestReport.sourceName : "";
        Text(new Rect(f.x, f.y, f.width - 420, f.height), "Источник: " + CleanInline(source) + "     ·     Esc — закрыть, I — открыть", 13, UiFaint, TextAnchor.MiddleLeft, false);

        Rect primary = new Rect(f.xMax - 240, f.y + 16, 240, 42);
        switch (activeTab)
        {
            case 0:
                if (PrimaryButton(primary, IsAnalyzing ? "Анализирую…" : "Обновить анализ", IsAnalyzing)) RunFactoryAudit(null);
                break;
            case 1:
                if (PrimaryButton(primary, IsBottleneckAnalyzing ? "Анализирую…" : "Пересчитать поток", IsBottleneckAnalyzing)) RunBottleneckAnalysis();
                break;
            case 2:
                if (PrimaryButton(primary, IsMaintenanceAnalyzing ? "Формирую план…" : "Обновить план", IsMaintenanceAnalyzing)) RunMaintenancePlanAnalysis();
                break;
            case 3:
                if (GhostButton(primary, "Очистить диалог"))
                {
                    ChatHistory.Clear();
                    ChatHistory.Add(new ChatMessage { sender = ChatMessage.SenderType.AI, text = "Диалог очищен. Спросите о любом участке цеха, простоях или выполнении плана.", timestamp = DateTime.Now });
                }
                break;
        }
    }

    // =====================================================================================
    // Вкладка «Сводка»
    // =====================================================================================

    float DrawSummary(float w)
    {
        var snap = Snap;
        var rep = LatestReport;
        float y = 0;
        if (rep == null)
        {
            Text(new Rect(0, 0, w, 40), "Нажмите «Обновить анализ», чтобы ИИ собрал данные цеха.", 16, UiMuted, TextAnchor.MiddleLeft, false);
            return 40;
        }

        // ---------- вердикт ----------
        Color sev = SeverityColor(rep.severity);
        float score = rep.factoryHealthScore;
        float headH = Height(rep.summaryTitle, 22, w - 420, true);
        float vh = Mathf.Max(140f, headH + 86f);
        Rect v = new Rect(0, y, w, vh);
        Round(v, UiCard, 14f);
        Round(new Rect(0, y, 6, vh), sev, 3f);

        string sevText = Sentence(rep.severityText);
        float sw = Measure(sevText, 13, true) + 36;
        Rect sp = new Rect(30, y + 24, sw, 28);
        Round(sp, Tint(sev, 0.16f), 14f);
        Round(new Rect(sp.x + 13, sp.y + 10, 8, 8), sev, 4f);
        Text(new Rect(sp.x + 27, sp.y, sw - 30, 28), sevText, 13, sev, TextAnchor.MiddleLeft, true);

        Text(new Rect(30, y + 62, w - 420, headH), CleanInline(rep.summaryTitle), 22, UiInk, TextAnchor.UpperLeft, true, true);
        if (snap != null)
        {
            string shift = "Смена пройдена на " + Pct(snap.shiftProgress) + "  ·  собрано " + N(snap.totalFact) + " из " + N(snap.totalPlan) + " авто";
            Text(new Rect(30, y + 66 + headH, w - 420, 22), shift, 15, UiMuted, TextAnchor.MiddleLeft, false);
        }

        // индекс состояния справа
        float ix = w - 330;
        Fill(new Rect(ix - 30, y + 24, 1, vh - 48), UiLine);
        Text(new Rect(ix, y + 22, 300, 18), "Индекс состояния цеха", 13, UiMuted, TextAnchor.MiddleLeft, false);
        Text(new Rect(ix, y + 42, 300, 60), "<b>" + score.ToString("0", Ru) + "</b><size=22><color=#7A8594>  / 100</color></size>", 52, ScoreColor(score), TextAnchor.MiddleLeft, false);
        Bar(new Rect(ix, y + 108, 280, 8), score / 100f, ScoreColor(score), 0.85f);
        Text(new Rect(ix, y + 118, 300, 16), "норма от 85", 12, UiFaint, TextAnchor.MiddleLeft, false);
        y += vh + 18;

        // ---------- ключевые показатели ----------
        if (snap != null)
        {
            float gap = 16f;
            float kw = (w - gap * 3) / 4f;
            float kh = 150f;

            float oee = snap.averageOee;
            Kpi(new Rect(0, y, kw, kh), "Эффективность оборудования", Pct(oee), "OEE, цель 85%", oee, oee >= 0.85f ? UiGood : oee >= 0.75f ? UiWarn : UiBad, 0.85f);

            float done = snap.totalPlan > 0 ? (float)snap.totalFact / snap.totalPlan : 0f;
            Kpi(new Rect(kw + gap, y, kw, kh), "Выпуск за смену", N(snap.totalFact), "план " + N(snap.totalPlan) + " авто  ·  " + Pct(done), done, done >= snap.shiftProgress - 0.05f ? UiGood : UiWarn, snap.shiftProgress);

            int dt = snap.totalDowntimeMin;
            Color dc = dt > 90 ? UiBad : dt > 30 ? UiWarn : UiGood;
            Kpi(new Rect((kw + gap) * 2, y, kw, kh), "Простои оборудования", dt.ToString(Ru) + " мин", dt > 90 ? "выше нормы" : dt > 30 ? "под контролем" : "в норме", Mathf.Clamp01(dt / 120f), dc, -1f);

            Rect bn = new Rect((kw + gap) * 3, y, kw, kh);
            Round(bn, UiCard, 14f);
            Text(new Rect(bn.x + 22, y + 20, kw - 44, 18), "Узкое место", 13, UiMuted, TextAnchor.MiddleLeft, false);
            string station = string.IsNullOrEmpty(snap.bottleneckProcess) ? "не выявлено" : CleanInline(snap.bottleneckProcess);
            Text(new Rect(bn.x + 22, y + 44, kw - 44, 56), station, 19, UiInk, TextAnchor.UpperLeft, true, true);
            Rect link = new Rect(bn.x + 22, y + kh - 40, kw - 44, 22);
            bool lh = link.Contains(Event.current.mousePosition);
            Text(link, "Разобрать причины  →", 14, lh ? Color.white : UiAccent, TextAnchor.MiddleLeft, true);
            if (GUI.Button(link, GUIContent.none, GUIStyle.none)) activeTab = 1;
            y += kh + 18;
        }

        // ---------- две колонки: внимание / действия ----------
        float colGap = 18f;
        float colW = (w - colGap) / 2f;
        float leftH = ListCardHeight(rep.findings, colW, false, 5);
        float rightH = ListCardHeight(rep.recommendations, colW, true, 4);
        float ch = Mathf.Max(leftH, rightH);
        DrawListCard(new Rect(0, y, colW, ch), "Что требует внимания", rep.findings, false, 5);
        DrawListCard(new Rect(colW + colGap, y, colW, ch), "Что рекомендуем сделать", rep.recommendations, true, 4);
        y += ch + 18;

        // ---------- полный отчёт ----------
        y = Collapsible(y, w, "Полный отчёт ИИ", ref showFullReport, rep.mainAnalysis);
        return y;
    }

    void Kpi(Rect r, string label, string value, string sub, float frac, Color c, float marker)
    {
        Round(r, UiCard, 14f);
        Text(new Rect(r.x + 22, r.y + 20, r.width - 44, 18), label, 13, UiMuted, TextAnchor.MiddleLeft, false);
        Text(new Rect(r.x + 22, r.y + 42, r.width - 44, 46), value, 36, UiInk, TextAnchor.MiddleLeft, true);
        Text(new Rect(r.x + 22, r.y + 90, r.width - 44, 18), sub, 13, UiMuted, TextAnchor.MiddleLeft, false);
        Bar(new Rect(r.x + 22, r.yMax - 28, r.width - 44, 8), frac, c, marker);
    }

    float ListCardHeight(List<string> items, float w, bool numbered, int max)
    {
        float h = 64f;
        int n = items == null ? 0 : Mathf.Min(max, items.Count);
        for (int i = 0; i < n; i++) h += Height(CleanInline(items[i]), 15, w - 84, false) + 18f;
        return Mathf.Max(h + 8f, 140f);
    }

    void DrawListCard(Rect r, string title, List<string> items, bool numbered, int max)
    {
        Round(r, UiCard, 14f);
        Text(new Rect(r.x + 24, r.y + 20, r.width - 48, 24), title, 17, UiInk, TextAnchor.MiddleLeft, true);
        float y = r.y + 60;
        int n = items == null ? 0 : Mathf.Min(max, items.Count);
        if (n == 0)
        {
            Text(new Rect(r.x + 24, y, r.width - 48, 22), "Замечаний нет — оборудование работает в допуске.", 15, UiMuted, TextAnchor.MiddleLeft, false);
            return;
        }
        for (int i = 0; i < n; i++)
        {
            string raw = items[i];
            string s = CleanInline(raw);
            float th = Height(s, 15, r.width - 84, false);
            if (numbered)
            {
                Rect num = new Rect(r.x + 24, y, 24, 24);
                Round(num, Tint(UiAccent, 0.18f), 12f);
                Text(num, (i + 1).ToString(Ru), 12, UiAccent, TextAnchor.MiddleCenter, true);
            }
            else
            {
                Color dot = LevelColor(Level(raw));
                Round(new Rect(r.x + 32, y + 8, 9, 9), dot, 4.5f);
            }
            Text(new Rect(r.x + 60, y + 2, r.width - 84, th), s, 15, new Color(0.86f, 0.89f, 0.93f), TextAnchor.UpperLeft, false, true);
            y += th + 18f;
        }
    }

    // =====================================================================================
    // Вкладка «Узкие места»
    // =====================================================================================

    float DrawBottlenecks(float w)
    {
        if (LatestBottleneckReport == null) LatestBottleneckReport = AIInspectorEngine.GenerateBottleneckReport(Snap);
        var b = LatestBottleneckReport;
        float y = 0;

        // ---------- главное узкое место ----------
        const float heroH = 140f;
        Rect hero = new Rect(0, y, w, heroH);
        Round(hero, UiCard, 14f);
        Round(new Rect(0, y, 6, heroH), UiBad, 3f);
        float sx = w - 600;
        Text(new Rect(30, y + 22, 500, 18), "Главное узкое место  ·  " + b.bottleneckCode, 13, UiMuted, TextAnchor.MiddleLeft, false);
        Text(new Rect(30, y + 44, sx - 60, 40), CleanInline(b.bottleneckStation), 28, UiInk, TextAnchor.MiddleLeft, true);
        Text(new Rect(30, y + 88, sx - 60, 44), "Этот участок задаёт темп всему цеху: пока он не ускорится, остальные посты будут ждать.", 15, UiMuted, TextAnchor.UpperLeft, false, true);

        Fill(new Rect(sx - 24, y + 26, 1, heroH - 52), UiLine);
        Stat(new Rect(sx, y + 32, 150, 80), "OEE участка", Pct(b.stationOee), b.stationOee >= 0.85f ? UiGood : b.stationOee >= 0.75f ? UiWarn : UiBad);
        Stat(new Rect(sx + 160, y + 32, 160, 80), "Простой", b.downtimeMin.ToString(Ru) + " мин", b.downtimeMin > 30 ? UiWarn : UiInk);
        Stat(new Rect(sx + 330, y + 32, 250, 80), "Выпуск: факт / план", N(b.fact) + " / " + N(b.plan), UiInk);
        y += heroH + 18;

        // ---------- схема потока ----------
        Rect flow = new Rect(0, y, w, 200);
        Round(flow, UiCard, 14f);
        Text(new Rect(24, y + 20, w - 48, 24), "Как это влияет на поток", 17, UiInk, TextAnchor.MiddleLeft, true);
        float fy = y + 62;
        float boxW = (w - 48 - 2 * 70) / 3f;
        Buffer(new Rect(24, fy, boxW, 112), "Накопитель перед участком", 3, UiBad, "переполнен — предыдущие посты тормозят");
        Arrow(new Rect(24 + boxW, fy, 70, 112));
        Rect st = new Rect(24 + boxW + 70, fy, boxW, 112);
        Round(st, Tint(UiAccent, 0.12f), 12f);
        RoundBorder(st, Tint(UiAccent, 0.6f), 1.5f, 12f);
        Text(new Rect(st.x + 18, fy + 14, boxW - 36, 22), CleanInline(b.bottleneckStation), 16, UiInk, TextAnchor.MiddleLeft, true);
        Text(new Rect(st.x + 18, fy + 42, boxW - 36, 30), "<b>" + b.cycleTimeSec.ToString("0", Ru) + " с</b> на авто", 22, UiInk, TextAnchor.MiddleLeft, false);
        Text(new Rect(st.x + 18, fy + 76, boxW - 36, 20), "цель " + b.taktTargetSec.ToString("0", Ru) + " с  ·  <color=#EE5555>+" + b.taktLagSec.ToString("0", Ru) + " с</color>", 14, UiMuted, TextAnchor.MiddleLeft, false);
        Arrow(new Rect(st.xMax, fy, 70, 112));
        Buffer(new Rect(st.xMax + 70, fy, boxW, 112), "Накопитель после участка", 0, UiWarn, "пуст — следующие посты простаивают");
        y += 200 + 18;

        // ---------- последствия ----------
        float gap = 16f, tw = (w - gap * 2) / 3f;
        Kpi(new Rect(0, y, tw, 150), "Цикл участка", b.cycleTimeSec.ToString("0", Ru) + " с", "цель " + b.taktTargetSec.ToString("0", Ru) + " с на автомобиль", Mathf.Clamp01(b.taktTargetSec / Mathf.Max(1f, b.cycleTimeSec)), UiWarn, -1f);
        Kpi(new Rect(tw + gap, y, tw, 150), "Недовыпуск за сутки", "≈ " + N(b.lostCarsEstimate) + " авто", "из-за отставания и простоев", Mathf.Clamp01(b.plan > 0 ? (float)b.lostCarsEstimate / b.plan * 4f : 0f), UiBad, -1f);
        Kpi(new Rect((tw + gap) * 2, y, tw, 150), "Отставание от такта", "+" + b.taktLagSec.ToString("0", Ru) + " с", "на каждом автомобиле", Mathf.Clamp01(b.taktLagSec / 30f), UiWarn, -1f);
        y += 150 + 18;

        // ---------- причины и план ----------
        float colGap = 18f, colW = (w - colGap) / 2f;
        float ch = Mathf.Max(ListCardHeight(b.rootCauses, colW, false, 6), StepsHeight(b.actionSteps, colW));
        DrawListCard(new Rect(0, y, colW, ch), "Почему возник затор", b.rootCauses, false, 6);
        DrawSteps(new Rect(colW + colGap, y, colW, ch), "Что сделать, чтобы его убрать", b.actionSteps);
        y += ch + 18;

        y = Collapsible(y, w, "Подробный разбор ИИ", ref showFullBottleneck, b.rawAiAnalysis);
        return y;
    }

    void Stat(Rect r, string label, string value, Color c)
    {
        Text(new Rect(r.x, r.y, r.width, 18), label, 13, UiMuted, TextAnchor.MiddleLeft, false);
        Text(new Rect(r.x, r.y + 22, r.width, 40), value, 26, c, TextAnchor.MiddleLeft, true);
    }

    void Buffer(Rect r, string title, int filled, Color c, string note)
    {
        Round(r, new Color(1f, 1f, 1f, 0.03f), 12f);
        Text(new Rect(r.x + 18, r.y + 14, r.width - 36, 20), title, 14, UiMuted, TextAnchor.MiddleLeft, false);
        for (int i = 0; i < 3; i++)
        {
            Rect slot = new Rect(r.x + 18 + i * 44, r.y + 44, 36, 26);
            if (i < filled) Round(slot, c, 6f);
            else RoundBorder(slot, new Color(1f, 1f, 1f, 0.18f), 1.5f, 6f);
        }
        Text(new Rect(r.x + 160, r.y + 44, r.width - 170, 26), filled + " из 3", 16, c, TextAnchor.MiddleLeft, true);
        Text(new Rect(r.x + 18, r.y + 80, r.width - 36, 20), note, 13, UiFaint, TextAnchor.MiddleLeft, false);
    }

    void Arrow(Rect r)
    {
        float cy = r.y + r.height * 0.5f;
        Fill(new Rect(r.x + 14, cy - 1, r.width - 30, 2), new Color(1f, 1f, 1f, 0.25f));
        Text(new Rect(r.x, cy - 14, r.width - 8, 28), "›", 30, new Color(1f, 1f, 1f, 0.45f), TextAnchor.MiddleRight, true);
    }

    float StepsHeight(List<string> steps, float w)
    {
        float h = 64f;
        foreach (string s in steps)
        {
            SplitStep(s, out string who, out string what);
            h += (who.Length > 0 ? 22 : 0) + Height(what, 15, w - 84, false) + 20f;
        }
        return h + 8f;
    }

    void DrawSteps(Rect r, string title, List<string> steps)
    {
        Round(r, UiCard, 14f);
        Text(new Rect(r.x + 24, r.y + 20, r.width - 48, 24), title, 17, UiInk, TextAnchor.MiddleLeft, true);
        float y = r.y + 60;
        for (int i = 0; i < steps.Count; i++)
        {
            SplitStep(steps[i], out string who, out string what);
            Rect num = new Rect(r.x + 24, y, 24, 24);
            Round(num, Tint(UiAccent, 0.18f), 12f);
            Text(num, (i + 1).ToString(Ru), 12, UiAccent, TextAnchor.MiddleCenter, true);
            float ty = y + 2;
            if (who.Length > 0)
            {
                Text(new Rect(r.x + 60, ty, r.width - 84, 20), who, 13, UiAccent, TextAnchor.MiddleLeft, true);
                ty += 22;
            }
            float th = Height(what, 15, r.width - 84, false);
            Text(new Rect(r.x + 60, ty, r.width - 84, th), what, 15, new Color(0.86f, 0.89f, 0.93f), TextAnchor.UpperLeft, false, true);
            y = ty + th + 20f;
        }
    }

    // «2. Наладчику [РТК]: Проверить …» → кто: «Наладчику (РТК)», что: «Проверить …»
    static void SplitStep(string raw, out string who, out string what)
    {
        string s = Regex.Replace(CleanInline(raw), @"^\d+[.)]\s*", "");
        int c = s.IndexOf(':');
        if (c > 0 && c < 48)
        {
            who = Regex.Replace(s.Substring(0, c), @"\[([^\]]+)\]", "· $1").Trim();
            what = s.Substring(c + 1).Trim();
        }
        else
        {
            who = "";
            what = s;
        }
    }

    // =====================================================================================
    // Вкладка «Обслуживание»
    // =====================================================================================

    float DrawMaintenance(float w)
    {
        if (LatestMaintenanceReport == null) LatestMaintenanceReport = AIInspectorEngine.GenerateMaintenancePlanReport(Snap);
        var m = LatestMaintenanceReport;
        float y = 0;

        // ---------- счётчики (они же фильтр) ----------
        float gap = 16f, cw = (w - gap * 2) / 3f;
        Counter(new Rect(0, y, cw, 110), "Срочно, в течение суток", m.criticalCount, UiBad, 1);
        Counter(new Rect(cw + gap, y, cw, 110), "В ближайшие 3 дня", m.warningCount, UiWarn, 2);
        Counter(new Rect((cw + gap) * 2, y, cw, 110), "По плану", m.scheduledCount, UiGood, 3);
        y += 110 + 18;

        // ---------- таблица узлов ----------
        List<AIInspectorEngine.MaintenanceTask> rows = new List<AIInspectorEngine.MaintenanceTask>();
        foreach (var t in m.tasks)
        {
            int lvl = TaskLevel(t);
            if (maintenanceFilter == 0 || (maintenanceFilter == 1 && lvl == 2) || (maintenanceFilter == 2 && lvl == 1) || (maintenanceFilter == 3 && lvl == 0)) rows.Add(t);
        }

        float rowH = 64f;
        float tableTop = y;
        float headerH = 92f;
        float bodyH = 0f;
        for (int i = 0; i < rows.Count; i++) bodyH += rowH + (i == expandedTask ? 74f : 0f);
        Rect table = new Rect(0, y, w, headerH + bodyH + 12f);
        Round(table, UiCard, 14f);

        string filterName = maintenanceFilter == 1 ? "срочные" : maintenanceFilter == 2 ? "на ближайшие 3 дня" : maintenanceFilter == 3 ? "плановые" : "все узлы";
        Text(new Rect(24, y + 20, 600, 24), "Узлы оборудования  ·  " + filterName, 17, UiInk, TextAnchor.MiddleLeft, true);
        if (maintenanceFilter != 0)
        {
            Rect all = new Rect(w - 184, y + 16, 160, 32);
            if (GhostButton(all, "Показать все")) { maintenanceFilter = 0; expandedTask = -1; }
        }

        // колонки
        float c0 = 24, c1 = 196, c2 = w * 0.52f, c3 = w * 0.69f, c4 = w * 0.84f, c5 = w - 110;
        float hy = y + 60;
        Text(new Rect(c0, hy, 160, 20), "Срочность", 12, UiFaint, TextAnchor.MiddleLeft, true);
        Text(new Rect(c1, hy, 300, 20), "Узел и участок", 12, UiFaint, TextAnchor.MiddleLeft, true);
        Text(new Rect(c2, hy, 200, 20), "Кто обслуживает", 12, UiFaint, TextAnchor.MiddleLeft, true);
        Text(new Rect(c3, hy, 200, 20), "Остаток ресурса", 12, UiFaint, TextAnchor.MiddleLeft, true);
        Text(new Rect(c4, hy, 120, 20), "Нагрев", 12, UiFaint, TextAnchor.MiddleLeft, true);
        Text(new Rect(c5, hy, 90, 20), "До ТО", 12, UiFaint, TextAnchor.MiddleLeft, true);
        y += headerH;

        if (rows.Count == 0)
        {
            Text(new Rect(24, y, w - 48, 40), "В этой категории узлов нет.", 15, UiMuted, TextAnchor.MiddleLeft, false);
            y += 40;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            var t = rows[i];
            int lvl = TaskLevel(t);
            Color lc = lvl == 2 ? UiBad : lvl == 1 ? UiWarn : UiGood;
            bool open = i == expandedTask;
            Rect row = new Rect(8, y, w - 16, rowH + (open ? 74f : 0f));
            bool hover = row.Contains(Event.current.mousePosition);
            if (hover || open) Round(row, new Color(1f, 1f, 1f, open ? 0.05f : 0.03f), 10f);
            Fill(new Rect(24, y, w - 48, 1), UiLine);

            string lt = lvl == 2 ? "Срочно" : lvl == 1 ? "Скоро" : "Планово";
            Rect pill = new Rect(c0, y + 18, Measure(lt, 13, true) + 34, 28);
            Round(pill, Tint(lc, 0.15f), 14f);
            Round(new Rect(pill.x + 12, pill.y + 10, 8, 8), lc, 4f);
            Text(new Rect(pill.x + 26, pill.y, pill.width - 28, 28), lt, 13, lc, TextAnchor.MiddleLeft, true);

            Text(new Rect(c1, y + 12, c2 - c1 - 16, 22), CleanInline(t.unitName), 16, UiInk, TextAnchor.MiddleLeft, true);
            Text(new Rect(c1, y + 34, c2 - c1 - 16, 20), CleanInline(t.stationName), 13, UiMuted, TextAnchor.MiddleLeft, false);

            Department(t.department, out string code, out string dept);
            Text(new Rect(c2, y + 12, c3 - c2 - 12, 22), dept, 15, new Color(0.86f, 0.89f, 0.93f), TextAnchor.MiddleLeft, false);
            if (code.Length > 0) Text(new Rect(c2, y + 34, c3 - c2 - 12, 20), code, 12, UiFaint, TextAnchor.MiddleLeft, true);

            Color hc = t.health < 0.65f ? UiBad : t.health < 0.8f ? UiWarn : UiGood;
            Text(new Rect(c3, y + 12, 120, 22), Pct(t.health), 16, UiInk, TextAnchor.MiddleLeft, true);
            Bar(new Rect(c3, y + 40, c4 - c3 - 40, 6), t.health, hc, -1f);

            Color tc = t.temperature > 55f ? UiBad : t.temperature > 48f ? UiWarn : UiInk;
            Text(new Rect(c4, y, 120, rowH), t.temperature.ToString("0", Ru) + " °C", 16, tc, TextAnchor.MiddleLeft, true);
            Text(new Rect(c5, y, 100, rowH), HoursText(t.hoursToService), 16, lvl == 2 ? UiBad : UiInk, TextAnchor.MiddleLeft, true);

            if (open)
            {
                float dy = y + rowH;
                Text(new Rect(c1, dy, w - c1 - 32, 20), "<b>Что сделать:</b> " + CleanInline(t.procedure), 14, new Color(0.82f, 0.86f, 0.91f), TextAnchor.UpperLeft, false, true);
                Text(new Rect(c1, dy + 32, w - c1 - 32, 20), "<b>Запчасти и материалы:</b> " + CleanInline(t.partsAndConsumables), 14, UiMuted, TextAnchor.UpperLeft, false, true);
            }

            if (GUI.Button(row, GUIContent.none, GUIStyle.none)) expandedTask = open ? -1 : i;
            y += row.height;
        }
        y = table.yMax + 18;

        // ---------- запчасти ----------
        if (m.sparePartsSummary.Count > 0)
        {
            float ph = 64f;
            foreach (string s in m.sparePartsSummary) ph += Height(CleanInline(s), 15, w - 84, false) + 14f;
            Rect parts = new Rect(0, y, w, ph + 6f);
            Round(parts, UiCard, 14f);
            Text(new Rect(24, y + 20, w - 48, 24), "Что заказать на склад", 17, UiInk, TextAnchor.MiddleLeft, true);
            float py = y + 60;
            foreach (string s in m.sparePartsSummary)
            {
                string c = CleanInline(s);
                float th = Height(c, 15, w - 84, false);
                Round(new Rect(32, py + 8, 7, 7), UiAccent, 3.5f);
                Text(new Rect(60, py + 2, w - 84, th), c, 15, new Color(0.86f, 0.89f, 0.93f), TextAnchor.UpperLeft, false, true);
                py += th + 14f;
            }
            y = parts.yMax + 18;
        }

        y = Collapsible(y, w, "Регламент работ и техника безопасности", ref showFullGuidelines, m.rawAiAnalysis);
        return y;
    }

    void Counter(Rect r, string label, int value, Color c, int filter)
    {
        bool active = maintenanceFilter == filter;
        bool hover = r.Contains(Event.current.mousePosition);
        Round(r, active ? Tint(c, 0.12f) : hover ? UiCardHi : UiCard, 14f);
        if (active) RoundBorder(r, Tint(c, 0.7f), 1.5f, 14f);
        Text(new Rect(r.x + 24, r.y + 18, r.width - 48, 18), label, 13, UiMuted, TextAnchor.MiddleLeft, false);
        Text(new Rect(r.x + 24, r.y + 40, 120, 48), value.ToString(Ru), 40, c, TextAnchor.MiddleLeft, true);
        Text(new Rect(r.x + 24, r.y + 40, r.width - 48, 48), active ? "показаны в таблице" : "показать в таблице", 13, active ? c : UiFaint, TextAnchor.MiddleRight, false);
        if (GUI.Button(r, GUIContent.none, GUIStyle.none))
        {
            maintenanceFilter = active ? 0 : filter;
            expandedTask = -1;
        }
    }

    static int TaskLevel(AIInspectorEngine.MaintenanceTask t)
    {
        if (t.urgencyText != null && t.urgencyText.Contains("СРОЧНО")) return 2;
        if (t.urgencyText != null && t.urgencyText.Contains("ВНИМАНИЕ")) return 1;
        return 0;
    }

    static void Department(string raw, out string code, out string name)
    {
        Match m = Regex.Match(raw ?? "", @"^\s*\[([^\]]+)\]\s*(.*)$");
        if (m.Success)
        {
            code = m.Groups[1].Value;
            name = m.Groups[2].Value;
        }
        else
        {
            code = "";
            name = raw ?? "";
        }
    }

    static string HoursText(int h)
    {
        if (h < 24) return h + " ч";
        int d = h / 24;
        return d + " " + Plural(d, "день", "дня", "дней");
    }

    // =====================================================================================
    // Вкладка «Спросить ИИ»
    // =====================================================================================

    void DrawChat(Rect r)
    {
        float inputH = 52f, chipsH = 40f;
        Rect area = new Rect(r.x, r.y, r.width, r.height - inputH - chipsH - 28);
        Round(area, new Color(1f, 1f, 1f, 0.025f), 14f);

        float maxBubble = Mathf.Min(760f, area.width * 0.74f);
        float viewH = Mathf.Max(contentHeights[3], area.height - 8);
        Rect inner = new Rect(area.x + 6, area.y + 6, area.width - 12, area.height - 12);
        chatScroll = GUI.BeginScrollView(inner, chatScroll, new Rect(0, 0, inner.width - 14, viewH), false, false, GUIStyle.none, GUIStyle.none);
        float y = 18f;
        float iw = inner.width - 14;
        for (int i = 0; i < ChatHistory.Count; i++)
        {
            var msg = ChatHistory[i];
            bool me = msg.sender == ChatMessage.SenderType.Operator;
            string body = msg.isPending ? "Анализирую данные цеха" + new string('.', 1 + (int)(Time.unscaledTime * 2.5f) % 3) : ChatRich(msg.text);
            float bw = Mathf.Min(maxBubble, Measure(StripTags(body), 15, false) + 44f);
            bw = Mathf.Max(bw, 220f);
            float th = Height(body, 15, bw - 40, false);
            float bh = th + 58f;
            float bx = me ? iw - bw - 18 : 62;

            if (!me)
            {
                Rect av = new Rect(18, y, 34, 34);
                Round(av, Tint(UiAccent, 0.22f), 17f);
                Text(av, "ИИ", 12, UiAccent, TextAnchor.MiddleCenter, true);
            }
            Rect bubble = new Rect(bx, y, bw, bh);
            Round(bubble, me ? new Color(0.20f, 0.36f, 0.72f, 0.95f) : new Color(1f, 1f, 1f, 0.06f), 16f);
            string who = me ? "Вы" : "ИИ-Инспектор";
            Text(new Rect(bx + 20, y + 12, bw - 40, 18), who + "  ·  " + msg.timestamp.ToString("HH:mm", Ru), 12, me ? new Color(0.85f, 0.9f, 1f) : UiFaint, TextAnchor.MiddleLeft, true);
            Text(new Rect(bx + 20, y + 38, bw - 40, th), body, 15, me ? Color.white : new Color(0.88f, 0.91f, 0.95f), TextAnchor.UpperLeft, false, true);
            y += bh + 16f;
        }
        GUI.EndScrollView();
        contentHeights[3] = y + 8f;
        DrawScrollIndicator(inner, chatScroll.y, viewH);

        // ---------- готовые вопросы ----------
        string[] chips = { "Как поднять OEE на 5%?", "Где узкое место?", "Что нужно обслужить?", "Есть ли перегрев?", "Успеем выполнить план?" };
        string[] asks =
        {
            "Как повысить текущий OEE завода на 5%?",
            "Определи главное узкое место завода и риски задержки сменного такта.",
            "Сформируй график превентивного техобслуживания наиболее изношенных узлов.",
            "Какие роботы и приводы имеют повышенную температуру?",
            "Оцени риск срыва сменного плана выпуска авто."
        };
        float cx = r.x, cy = area.yMax + 14;
        for (int i = 0; i < chips.Length; i++)
        {
            float cw = Measure(chips[i], 14, false) + 32;
            Rect chip = new Rect(cx, cy, cw, 34);
            bool hover = chip.Contains(Event.current.mousePosition);
            Round(chip, hover ? Tint(UiAccent, 0.2f) : new Color(1f, 1f, 1f, 0.05f), 17f);
            RoundBorder(chip, hover ? Tint(UiAccent, 0.6f) : UiLine, 1f, 17f);
            Text(chip, chips[i], 14, hover ? Color.white : new Color(0.82f, 0.86f, 0.92f), TextAnchor.MiddleCenter, false);
            if (GUI.Button(chip, GUIContent.none, GUIStyle.none) && !IsChatResponding) AskChatQuestion(asks[i]);
            cx += cw + 10;
        }

        // ---------- поле ввода ----------
        Rect field = new Rect(r.x, r.yMax - inputH, r.width - 170, inputH);
        Round(field, new Color(1f, 1f, 1f, 0.06f), 14f);
        bool focused = GUI.GetNameOfFocusedControl() == "ChatInputField";
        RoundBorder(field, focused ? Tint(UiAccent, 0.8f) : UiLine, 1.5f, 14f);
        if (string.IsNullOrEmpty(customQuestionInput) && !focused)
            Text(new Rect(field.x + 18, field.y, field.width - 36, inputH), "Спросите о любом участке, простоях или выполнении плана…", 15, UiFaint, TextAnchor.MiddleLeft, false);

        Event e = Event.current;
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && focused)
        {
            if (!string.IsNullOrWhiteSpace(customQuestionInput)) AskChatQuestion(customQuestionInput);
            e.Use();
        }
        GUI.SetNextControlName("ChatInputField");
        customQuestionInput = GUI.TextField(new Rect(field.x + 4, field.y, field.width - 8, inputH), customQuestionInput, uiField);

        Rect send = new Rect(r.xMax - 156, r.yMax - inputH, 156, inputH);
        if (PrimaryButton(send, IsChatResponding ? "Думаю…" : "Отправить", IsChatResponding || string.IsNullOrWhiteSpace(customQuestionInput)))
            AskChatQuestion(customQuestionInput);
    }

    // ответ ИИ в чате: Markdown → rich text, построчно
    static string ChatRich(string raw)
    {
        var sb = new StringBuilder();
        foreach (Block b in ParseDoc(raw))
        {
            if (sb.Length > 0) sb.Append('\n');
            switch (b.kind)
            {
                case BlockKind.Heading: sb.Append("<b>").Append(b.text).Append("</b>"); break;
                case BlockKind.Bullet: sb.Append("•  ").Append(b.text); break;
                case BlockKind.Number: sb.Append(b.num).Append(".  ").Append(b.text); break;
                case BlockKind.Gap: break;
                default: sb.Append(b.text); break;
            }
        }
        return sb.ToString();
    }

    // =====================================================================================
    // Вкладка «Настройки»
    // =====================================================================================

    float DrawSettings(float w)
    {
        float y = 0;
        Rect card = new Rect(0, y, w, 380);
        Round(card, UiCard, 14f);
        Text(new Rect(24, y + 20, w - 48, 24), "Подключение к OpenAI", 17, UiInk, TextAnchor.MiddleLeft, true);
        Text(new Rect(24, y + 46, w - 48, 20), "Ключ хранится только на этом компьютере. Без ключа инспектор работает в автономном режиме на данных цеха.", 14, UiMuted, TextAnchor.MiddleLeft, false);

        Text(new Rect(24, y + 86, 300, 20), "API-ключ", 13, UiMuted, TextAnchor.MiddleLeft, true);
        Rect field = new Rect(24, y + 110, w - 240, 48);
        Round(field, new Color(1f, 1f, 1f, 0.06f), 12f);
        RoundBorder(field, UiLine, 1f, 12f);
        apiKeyInput = GUI.PasswordField(new Rect(field.x + 4, field.y, field.width - 8, field.height), apiKeyInput ?? "", '•', uiField);
        if (PrimaryButton(new Rect(w - 200, y + 110, 176, 48), "Сохранить ключ", false))
        {
            ApiKey = apiKeyInput;
            LastApiNotice = "✅ Ключ сохранён. Следующий анализ пойдёт через OpenAI.";
        }

        Text(new Rect(24, y + 182, 300, 20), "Модель", 13, UiMuted, TextAnchor.MiddleLeft, true);
        string[] models = { "gpt-4o-mini", "gpt-4o" };
        string[] modelNotes = { "быстрая и недорогая", "точнее, но медленнее" };
        for (int i = 0; i < models.Length; i++)
        {
            Rect mr = new Rect(24 + i * 236, y + 206, 220, 56);
            bool on = model == models[i];
            bool hover = mr.Contains(Event.current.mousePosition);
            Round(mr, on ? Tint(UiAccent, 0.16f) : hover ? UiCardHi : new Color(1f, 1f, 1f, 0.03f), 12f);
            RoundBorder(mr, on ? Tint(UiAccent, 0.8f) : UiLine, on ? 1.5f : 1f, 12f);
            Text(new Rect(mr.x + 16, mr.y + 8, mr.width - 32, 20), models[i], 15, on ? Color.white : UiInk, TextAnchor.MiddleLeft, true);
            Text(new Rect(mr.x + 16, mr.y + 30, mr.width - 32, 18), modelNotes[i], 12, UiMuted, TextAnchor.MiddleLeft, false);
            if (GUI.Button(mr, GUIContent.none, GUIStyle.none)) model = models[i];
        }

        forceSimulationMode = Switch(new Rect(24, y + 284, w - 48, 32), forceSimulationMode, "Автономный режим", "анализ без обращения к интернету");
        autoAnalyze = Switch(new Rect(24, y + 326, w - 48, 32), autoAnalyze, "Автоматический анализ", "каждые " + autoAnalyzeIntervalSeconds.ToString("0", Ru) + " секунд");
        y += 380 + 18;

        Rect diag = new Rect(0, y, w, 150);
        Round(diag, UiCard, 14f);
        Text(new Rect(24, y + 20, w - 48, 24), "Состояние", 17, UiInk, TextAnchor.MiddleLeft, true);
        bool hasKey = !OpenAIClient.IsPlaceholderKey(apiKey);
        Row(y + 60, w, "Ключ OpenAI", hasKey ? "подключён" : "не задан", hasKey ? UiGood : UiWarn);
        Row(y + 88, w, "Модель", model + "  ·  ожидание ответа до " + requestTimeoutSeconds + " с", UiInk);
        Row(y + 116, w, "Режим", forceSimulationMode || !hasKey ? "автономный эксперт Allur" : "OpenAI, при сбое — автономный эксперт", UiInk);
        return y + 150;
    }

    void Row(float y, float w, string label, string value, Color c)
    {
        Text(new Rect(24, y, 220, 22), label, 14, UiMuted, TextAnchor.MiddleLeft, false);
        Text(new Rect(244, y, w - 268, 22), value, 14, c, TextAnchor.MiddleLeft, true);
    }

    bool Switch(Rect r, bool value, string label, string note)
    {
        Rect track = new Rect(r.x, r.y + 4, 46, 24);
        Round(track, value ? UiAccent : new Color(1f, 1f, 1f, 0.15f), 12f);
        Round(new Rect(value ? track.xMax - 22 : track.x + 2, track.y + 2, 20, 20), Color.white, 10f);
        Text(new Rect(r.x + 62, r.y, 260, r.height), label, 15, UiInk, TextAnchor.MiddleLeft, true);
        Text(new Rect(r.x + 62 + Measure(label, 15, true) + 14, r.y, 400, r.height), note, 13, UiMuted, TextAnchor.MiddleLeft, false);
        if (GUI.Button(new Rect(r.x, r.y, 420, r.height), GUIContent.none, GUIStyle.none)) return !value;
        return value;
    }

    // =====================================================================================
    // Общие блоки
    // =====================================================================================

    // сворачиваемый документ: заголовок-кнопка и текст ИИ с заголовками, списками и абзацами
    float Collapsible(float y, float w, string title, ref bool open, string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return y;
        List<Block> blocks = ParseDoc(markdown);
        float docH = open ? DocHeight(blocks, w - 48) + 16f : 0f;
        Rect card = new Rect(0, y, w, 64 + docH);
        Round(card, UiCard, 14f);
        Rect head = new Rect(0, y, w, 64);
        bool hover = head.Contains(Event.current.mousePosition);
        Text(new Rect(24, y, w - 220, 64), title, 17, UiInk, TextAnchor.MiddleLeft, true);
        Text(new Rect(w - 224, y, 200, 64), open ? "Свернуть  ˄" : "Развернуть  ˅", 14, hover ? Color.white : UiAccent, TextAnchor.MiddleRight, true);
        if (GUI.Button(head, GUIContent.none, GUIStyle.none)) open = !open;
        if (open)
        {
            Fill(new Rect(24, y + 64, w - 48, 1), UiLine);
            DrawDoc(blocks, 24, y + 80, w - 48);
        }
        return card.yMax + 18;
    }

    enum BlockKind { Heading, Bullet, Number, Para, Gap }

    struct Block
    {
        public BlockKind kind;
        public string text;
        public string num;
        public int level;
    }

    static readonly Dictionary<string, List<Block>> docCache = new Dictionary<string, List<Block>>();

    static List<Block> ParseDoc(string md)
    {
        if (md == null) md = "";
        if (docCache.TryGetValue(md, out List<Block> cached)) return cached;
        var list = new List<Block>();
        foreach (string rawLine in md.Replace("\r", "").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                if (list.Count > 0 && list[list.Count - 1].kind != BlockKind.Gap) list.Add(new Block { kind = BlockKind.Gap });
                continue;
            }
            int level = Level(line);
            string plain = StripEmoji(line).Trim();
            if (plain.StartsWith("#"))
            {
                list.Add(new Block { kind = BlockKind.Heading, text = Md(Sentence(plain.TrimStart('#').Trim())), level = level });
                continue;
            }
            Match num = Regex.Match(plain, @"^(\d+)[.)]\s+(.*)$");
            if (num.Success)
            {
                list.Add(new Block { kind = BlockKind.Number, num = num.Groups[1].Value, text = Md(Tidy(num.Groups[2].Value)), level = level });
                continue;
            }
            if (plain.StartsWith("• ") || plain.StartsWith("- ") || (plain.StartsWith("* ") && !plain.StartsWith("**")))
            {
                list.Add(new Block { kind = BlockKind.Bullet, text = Md(Tidy(plain.Substring(2))), level = level });
                continue;
            }
            // строка целиком жирная и короткая — это подзаголовок
            if (plain.StartsWith("**") && plain.EndsWith("**") && plain.Length < 90)
            {
                list.Add(new Block { kind = BlockKind.Heading, text = Md(Sentence(plain.Trim('*', ' ', ':'))), level = level });
                continue;
            }
            list.Add(new Block { kind = BlockKind.Para, text = Md(Tidy(plain)), level = level });
        }
        if (docCache.Count > 64) docCache.Clear();
        docCache[md] = list;
        return list;
    }

    float DocHeight(List<Block> blocks, float w)
    {
        float h = 0;
        foreach (Block b in blocks) h += BlockHeight(b, w);
        return h;
    }

    float BlockHeight(Block b, float w)
    {
        switch (b.kind)
        {
            case BlockKind.Heading: return Height(b.text, 16, w, true) + 18f;
            case BlockKind.Gap: return 8f;
            case BlockKind.Para: return Height(b.text, 15, w, false) + 8f;
            default: return Height(b.text, 15, w - 30, false) + 8f;
        }
    }

    void DrawDoc(List<Block> blocks, float x, float y, float w)
    {
        Color body = new Color(0.84f, 0.87f, 0.92f);
        foreach (Block b in blocks)
        {
            float h = BlockHeight(b, w);
            switch (b.kind)
            {
                case BlockKind.Heading:
                    Text(new Rect(x, y + 8, w, h - 14), b.text, 16, UiInk, TextAnchor.UpperLeft, true, true);
                    break;
                case BlockKind.Para:
                    Text(new Rect(x, y, w, h - 8), b.text, 15, body, TextAnchor.UpperLeft, false, true);
                    break;
                case BlockKind.Bullet:
                    Round(new Rect(x + 6, y + 8, 6, 6), b.level != 0 ? LevelColor(b.level) : UiFaint, 3f);
                    Text(new Rect(x + 30, y, w - 30, h - 8), b.text, 15, body, TextAnchor.UpperLeft, false, true);
                    break;
                case BlockKind.Number:
                    Text(new Rect(x, y, 26, 20), b.num + ".", 15, UiAccent, TextAnchor.UpperLeft, true);
                    Text(new Rect(x + 30, y, w - 30, h - 8), b.text, 15, body, TextAnchor.UpperLeft, false, true);
                    break;
            }
            y += h;
        }
    }

    // =====================================================================================
    // Очистка текстов ИИ
    // =====================================================================================

    // 2 — красный (критично), 1 — жёлтый (внимание), 0 — нейтрально, -1 — зелёный (норма)
    static int Level(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        if (s.Contains("🔴") || s.Contains("🚨") || s.Contains("Аномалия") || s.Contains("СРОЧНО")) return 2;
        if (s.Contains("🟡") || s.Contains("⚠") || s.Contains("Контроль узла")) return 1;
        if (s.Contains("🟢") || s.Contains("✅")) return -1;
        return 0;
    }

    static Color LevelColor(int level)
    {
        return level == 2 ? UiBad : level == 1 ? UiWarn : level == -1 ? UiGood : UiAccent;
    }

    // одна строка: без эмодзи и Markdown, с нормальным регистром
    static string CleanInline(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return Md(Tidy(StripEmoji(s).Trim()));
    }

    static string StripEmoji(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c)) { i++; continue; }   // эмодзи вне базовой плоскости
            if (char.IsLowSurrogate(c)) continue;
            if (c == '️' || c == '‍' || c == '⃣') continue;
            if ((c >= '⌀' && c <= '⏿') || (c >= '☀' && c <= '➿') || (c >= '⬀' && c <= '⯿')) continue;
            sb.Append(c);
        }
        string r = Regex.Replace(sb.ToString(), @"[ \t]{2,}", " ");
        return r.Replace("« ", "«").Replace(" »", "»");
    }

    // убираем английские пояснения в скобках и подписи вроде «Аномалия:»
    static string Tidy(string s)
    {
        s = Regex.Replace(s, @"\s*\((?:[A-Za-z][A-Za-z \-/]*)\)", "");
        s = Regex.Replace(s, @"^(Аномалия|Контроль узла):\s*", "");
        s = Regex.Replace(s, @"^\[([^\]]+)\]\s*", "$1 · ");
        return s.Trim();
    }

    static string Md(string s)
    {
        s = Regex.Replace(s, @"\*\*(.+?)\*\*", "<b>$1</b>");
        s = Regex.Replace(s, @"(?<![\*\w])\*(?!\s)(.+?)(?<!\s)\*(?![\*\w])", "<i>$1</i>");
        return s.Replace("**", "");
    }

    // «КРАТКАЯ СВОДКА РУКОВОДИТЕЛЯ» → «Краткая сводка руководителя»
    static string Sentence(string s)
    {
        s = Tidy(StripEmoji(s ?? "").Trim());
        int letters = 0, upper = 0;
        foreach (char c in s)
        {
            if (char.IsLetter(c)) { letters++; if (char.IsUpper(c)) upper++; }
        }
        if (letters > 3 && upper > letters * 0.7f)
        {
            s = s.ToLower(Ru);
            if (s.Length > 0) s = char.ToUpper(s[0], Ru) + s.Substring(1);
            s = Regex.Replace(s, @"\b(oee|ото|сгм|ртк|этл|отк|то|gpt)\b", m => m.Value.ToUpper(Ru));
        }
        return s;
    }

    static string StripTags(string s) => Regex.Replace(s ?? "", "<[^>]+>", "");

    // =====================================================================================
    // Примитивы рисования
    // =====================================================================================

    void Fill(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, whiteTex);
        GUI.color = old;
    }

    void Round(Rect r, Color c, float radius)
    {
        GUI.DrawTexture(r, whiteTex, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);
    }

    void RoundBorder(Rect r, Color c, float width, float radius)
    {
        GUI.DrawTexture(r, whiteTex, ScaleMode.StretchToFill, true, 0f, c, width, radius);
    }

    void Text(Rect r, string s, int size, Color c, TextAnchor a, bool bold, bool wrap = false)
    {
        uiText.fontSize = size;
        uiText.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        uiText.alignment = a;
        uiText.wordWrap = wrap;
        uiText.normal.textColor = c;
        GUI.Label(r, s, uiText);
    }

    float Height(string s, int size, float w, bool bold)
    {
        uiText.fontSize = size;
        uiText.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        uiText.wordWrap = true;
        return Mathf.Ceil(uiText.CalcHeight(new GUIContent(s), w));
    }

    float Measure(string s, int size, bool bold)
    {
        uiText.fontSize = size;
        uiText.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        uiText.wordWrap = false;
        return uiText.CalcSize(new GUIContent(s)).x;
    }

    void Bar(Rect r, float frac, Color c, float marker)
    {
        Round(r, new Color(1f, 1f, 1f, 0.08f), r.height * 0.5f);
        float fw = Mathf.Clamp01(frac) * r.width;
        if (fw > 1f) Round(new Rect(r.x, r.y, Mathf.Max(fw, r.height), r.height), c, r.height * 0.5f);
        if (marker >= 0f) Fill(new Rect(r.x + Mathf.Clamp01(marker) * r.width - 1, r.y - 4, 2, r.height + 8), new Color(1f, 1f, 1f, 0.7f));
    }

    bool PrimaryButton(Rect r, string label, bool disabled)
    {
        bool hover = !disabled && r.Contains(Event.current.mousePosition);
        Color c = disabled ? new Color(UiAccent.r, UiAccent.g, UiAccent.b, 0.35f) : hover ? new Color(0.42f, 0.64f, 1f) : UiAccent;
        Round(r, c, 12f);
        Text(r, label, 15, disabled ? new Color(1f, 1f, 1f, 0.7f) : Color.white, TextAnchor.MiddleCenter, true);
        return GUI.Button(r, GUIContent.none, GUIStyle.none) && !disabled;
    }

    bool GhostButton(Rect r, string label)
    {
        bool hover = r.Contains(Event.current.mousePosition);
        Round(r, hover ? UiCardHi : new Color(1f, 1f, 1f, 0.03f), 12f);
        RoundBorder(r, hover ? new Color(1f, 1f, 1f, 0.25f) : UiLine, 1f, 12f);
        Text(r, label, 14, hover ? Color.white : UiInk, TextAnchor.MiddleCenter, true);
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    bool IconButton(Rect r, string glyph, int size)
    {
        bool hover = r.Contains(Event.current.mousePosition);
        if (hover) Round(r, UiCardHi, r.height * 0.5f);
        Text(r, glyph, size, hover ? Color.white : UiMuted, TextAnchor.MiddleCenter, false);
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    void DrawScrollIndicator(Rect area, float scrollY, float viewH)
    {
        if (viewH <= area.height + 1f) return;
        float trackH = area.height - 16f;
        float thumbH = Mathf.Max(40f, trackH * area.height / viewH);
        float t = Mathf.Clamp01(scrollY / (viewH - area.height));
        Round(new Rect(area.xMax - 6, area.y + 8 + (trackH - thumbH) * t, 4, thumbH), new Color(1f, 1f, 1f, 0.18f), 2f);
    }

    void HandleDrag(Rect bar)
    {
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && bar.Contains(e.mousePosition))
        {
            if (e.clickCount == 2) hasCustomPos = false;
            else
            {
                isDraggingHeader = true;
                dragMouseOffset = e.mousePosition - windowPos;
            }
            e.Use();
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
        }
    }

    Vector2 TabScroll()
    {
        switch (activeTab)
        {
            case 1: return bottleneckScroll;
            case 2: return maintenanceScroll;
            case 4: return settingsScroll;
            default: return reportScroll;
        }
    }

    void SetTabScroll(Vector2 v)
    {
        switch (activeTab)
        {
            case 1: bottleneckScroll = v; break;
            case 2: maintenanceScroll = v; break;
            case 4: settingsScroll = v; break;
            default: reportScroll = v; break;
        }
    }

    static Color Tint(Color c, float a) => new Color(c.r, c.g, c.b, a);

    static Color SeverityColor(AIInspectorEngine.AlertSeverity s)
    {
        return s == AIInspectorEngine.AlertSeverity.Critical ? UiBad : s == AIInspectorEngine.AlertSeverity.Warning ? UiWarn : UiGood;
    }

    static Color ScoreColor(float score) => score >= 85f ? UiGood : score >= 70f ? UiWarn : UiBad;

    static string N(int v) => v.ToString("N0", Ru);

    static string Pct(float f) => Mathf.RoundToInt(f * 100f).ToString(Ru) + "%";

    static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        if (m10 == 1 && m100 != 11) return one;
        if (m10 >= 2 && m10 <= 4 && (m100 < 10 || m100 >= 20)) return few;
        return many;
    }
}

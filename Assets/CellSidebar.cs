using System;
using System.Collections.Generic;
using UnityEngine;

// Синтетическая статистика одного процесса (цифровой двойник).
// Данные стабильные: зависят только от имени объекта, типа процесса и даты, поэтому не "прыгают" между запусками.
public class CellStats
{
    public const float ShiftMinutes = 16f * 60f; // две смены, 07:00–23:00

    public class Day
    {
        public DateTime date;
        public int plan, gross, fact, defects, breakdowns, downtimeMin;
        public float availability, performance, quality, oee, cycleSec, energyKwh, consumable;
    }

    public class Breakdown
    {
        public DateTime time;
        public string unit, what;
        public int minutes;
    }

    public class Unit
    {
        public string name;
        public int hours, hoursToService;
        public float temp, health;
    }

    public readonly List<Day> days = new List<Day>();                  // 30 дней, последний — сегодня
    public readonly List<Breakdown> breakdowns = new List<Breakdown>(); // новые первыми
    public readonly List<Unit> units = new List<Unit>();

    static uint Hash(string s)
    {
        uint h = 2166136261;
        for (int i = 0; i < s.Length; i++)
        {
            h ^= s[i];
            h *= 16777619;
        }
        return h;
    }

    public static CellStats Generate(FactoryProcess p)
    {
        CellStats st = new CellStats();
        System.Random r = new System.Random((int)(Hash(p.Code + "|" + p.name) & 0x7fffffff));
        DateTime today = DateTime.Today;
        int basePlan = Mathf.RoundToInt(p.BasePlanPerDay * (1f + r.Next(0, 4) * 0.03f));
        string[] equipment = p.Equipment;
        string[] faults = p.Faults;
        Vector2 energy = p.EnergyPerUnit;
        Vector2 cons = p.ConsumablePerUnit;

        for (int i = 29; i >= 0; i--)
        {
            Day d = new Day();
            d.date = today.AddDays(-i);
            d.plan = d.date.DayOfWeek == DayOfWeek.Sunday ? 0
                   : d.date.DayOfWeek == DayOfWeek.Saturday ? basePlan / 2
                   : basePlan;

            if (d.plan > 0)
            {
                double u = r.NextDouble();
                d.breakdowns = u < 0.6 ? 0 : u < 0.88 ? 1 : u < 0.97 ? 2 : 3;
                for (int k = 0; k < d.breakdowns; k++)
                {
                    Breakdown b = new Breakdown();
                    b.minutes = r.NextDouble() < 0.06 ? 90 + r.Next(0, 150) : 6 + r.Next(0, 55);
                    b.unit = equipment[r.Next(0, equipment.Length)];
                    b.what = faults[r.Next(0, faults.Length)];
                    b.time = d.date.AddMinutes(7 * 60 + r.Next(0, (int)ShiftMinutes));
                    d.downtimeMin += b.minutes;
                    if (b.time <= DateTime.Now) st.breakdowns.Add(b);
                }

                d.availability = Mathf.Clamp01(1f - d.downtimeMin / ShiftMinutes);
                d.performance = 0.9f + (float)r.NextDouble() * 0.09f;
                d.gross = Mathf.RoundToInt(d.plan * d.availability * d.performance * 1.03f);
                d.defects = Mathf.RoundToInt(d.gross * (0.004f + (float)r.NextDouble() * 0.014f));
                d.fact = d.gross - d.defects;
                d.quality = d.gross > 0 ? (float)d.fact / d.gross : 1f;
                d.oee = d.availability * d.performance * d.quality;
                d.cycleSec = d.gross > 0 ? (ShiftMinutes - d.downtimeMin) * 60f / d.gross : 0f;
                d.energyKwh = d.gross * Mathf.Lerp(energy.x, energy.y, (float)r.NextDouble());
                d.consumable = d.gross * Mathf.Lerp(cons.x, cons.y, (float)r.NextDouble());
            }
            st.days.Add(d);
        }
        st.breakdowns.Sort((a, b) => b.time.CompareTo(a.time));

        foreach (string name in equipment)
        {
            Unit un = new Unit();
            un.name = name;
            un.hours = 6000 + r.Next(0, 12000);
            un.hoursToService = 12 + r.Next(0, 480);
            un.temp = 34f + (float)r.NextDouble() * 20f;
            int fails = 0;
            foreach (Breakdown b in st.breakdowns) if (b.unit == name) fails++;
            un.health = Mathf.Clamp(0.99f - fails * 0.035f - (float)r.NextDouble() * 0.05f, 0.55f, 0.99f);
            st.units.Add(un);
        }
        return st;
    }

    // доля смены, прошедшая к текущему моменту (для сегодняшнего плана "к этому часу")
    public static float ShiftProgress()
    {
        DateTime n = DateTime.Now;
        float h = n.Hour + n.Minute / 60f;
        return Mathf.Clamp01((h - 7f) / 16f);
    }
}

// Правая панель со статистикой процесса. Рисуется через IMGUI, без Canvas.
public class CellSidebar
{
    public const float LogicalWidth = 420f;

    readonly Dictionary<FactoryProcess, CellStats> cache = new Dictionary<FactoryProcess, CellStats>();
    int selected = 29;
    Vector2 scroll;
    float contentHeight = 1600f;
    GUIStyle text, scrollbar;

    static readonly string[] Dow = { "вс", "пн", "вт", "ср", "чт", "пт", "сб" };
    static readonly string[] Months = { "января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря" };

    static readonly Color Bg = new Color(0.055f, 0.07f, 0.095f, 0.97f);
    static readonly Color Card = new Color(1f, 1f, 1f, 0.05f);
    static readonly Color Line = new Color(1f, 1f, 1f, 0.08f);
    static readonly Color TextMain = new Color(0.92f, 0.94f, 0.97f);
    static readonly Color TextDim = new Color(0.55f, 0.62f, 0.7f);
    static readonly Color Good = new Color(0.25f, 0.78f, 0.45f);
    static readonly Color Warn = new Color(0.96f, 0.7f, 0.14f);
    static readonly Color Bad = new Color(0.92f, 0.3f, 0.3f);
    static readonly Color Accent = new Color(0.3f, 0.55f, 1f);

    // ширина панели на экране в пикселях
    public static float PixelWidth()
    {
        return Mathf.Min(LogicalWidth * Screen.height / 900f, Screen.width * 0.42f);
    }

    public void Open(FactoryProcess p)
    {
        selected = 29;
        scroll = Vector2.zero;
        Stats(p);
    }

    CellStats Stats(FactoryProcess p)
    {
        CellStats st;
        if (!cache.TryGetValue(p, out st))
        {
            st = CellStats.Generate(p);
            cache[p] = st;
        }
        return st;
    }

    // сегодняшний день: факт "к этому часу" + изделия, реально выпущенные процессом с момента запуска
    CellStats.Day Live(CellStats.Day d, FactoryProcess p, CellStats st)
    {
        if (d.date != DateTime.Today || d.plan == 0) return d;
        float f = CellStats.ShiftProgress();
        CellStats.Day t = new CellStats.Day();
        t.date = d.date;
        t.plan = d.plan;
        t.gross = Mathf.RoundToInt(d.gross * f) + Mathf.Max(0, p.CompletedUnits);
        t.defects = Mathf.RoundToInt(d.defects * f);
        t.fact = t.gross - t.defects;
        DateTime now = DateTime.Now;
        foreach (CellStats.Breakdown b in st.breakdowns)
        {
            if (b.time.Date == d.date && b.time <= now)
            {
                t.breakdowns++;
                t.downtimeMin += b.minutes;
            }
        }
        float elapsed = Mathf.Max(1f, f * CellStats.ShiftMinutes);
        t.availability = Mathf.Clamp01(1f - t.downtimeMin / elapsed);
        t.performance = d.performance;
        t.quality = t.gross > 0 ? (float)t.fact / t.gross : 1f;
        t.oee = t.availability * t.performance * t.quality;
        t.cycleSec = d.cycleSec;
        t.energyKwh = d.gross > 0 ? d.energyKwh / d.gross * t.gross : 0f;
        t.consumable = d.gross > 0 ? d.consumable / d.gross * t.gross : 0f;
        return t;
    }

    public void Draw(float slide, FactoryProcess p)
    {
        if (p == null) return;
        CellStats st = Stats(p);

        if (text == null)
        {
            text = new GUIStyle(GUI.skin.label);
            text.wordWrap = false;
            text.clipping = TextClipping.Clip;
            text.padding = new RectOffset(0, 0, 0, 0);
            text.margin = new RectOffset(0, 0, 0, 0);
            scrollbar = GUI.skin.verticalScrollbar;
        }

        float px = PixelWidth();
        float s = px / LogicalWidth;
        float h = Screen.height / s;
        Matrix4x4 old = GUI.matrix;
        float x0 = (Screen.width - px * slide) / s;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

        Rect panel = new Rect(x0, 0, LogicalWidth, h);
        Fill(panel, Bg);
        Fill(new Rect(x0, 0, 1, h), new Color(1f, 1f, 1f, 0.12f));

        scroll = GUI.BeginScrollView(panel, scroll, new Rect(0, 0, LogicalWidth - 14, contentHeight), false, false, GUIStyle.none, scrollbar);
        float y = DrawContent(p, st, LogicalWidth - 14);
        contentHeight = y + 20f;
        GUI.EndScrollView();

        GUI.matrix = old;
    }

    float DrawContent(FactoryProcess p, CellStats st, float w)
    {
        const float P = 20f;
        float cw = w - P * 2;
        float y = 18f;

        // ---------- шапка ----------
        Text(new Rect(P, y, cw, 16), "ЦИФРОВОЙ ДВОЙНИК · " + p.Code, 11, TextDim, TextAnchor.MiddleLeft, true);
        y += 18;
        string num = System.Text.RegularExpressions.Regex.Match(p.name, @"\d+$").Value;
        Text(new Rect(P, y, cw, 28), p.Title + (num.Length > 0 ? " · №" + num : ""), 21, TextMain, TextAnchor.MiddleLeft, true);
        y += 34;

        CellStats.Breakdown last = st.breakdowns.Count > 0 ? st.breakdowns[0] : null;
        Pill(new Rect(P, y, 108, 22), "●  В работе", Good);
        if (last != null)
        {
            TimeSpan ago = DateTime.Now - last.time;
            string agoText = ago.TotalHours < 1 ? Mathf.Max(1, (int)ago.TotalMinutes) + " мин" : ago.TotalHours < 48 ? (int)ago.TotalHours + " ч" : (int)ago.TotalDays + " дн";
            Text(new Rect(P + 118, y, cw - 118, 22), "последняя поломка " + agoText + " назад", 12, TextDim, TextAnchor.MiddleLeft, false);
        }
        y += 34;

        // ---------- сейчас ----------
        Rect live = new Rect(P, y, cw, 74);
        Fill(live, Card);
        Text(new Rect(P + 14, y + 10, 160, 14), "СЕЙЧАС", 10, TextDim, TextAnchor.MiddleLeft, true);
        Text(new Rect(P + 14, y + 26, cw - 150, 22), p.PhaseName, 16, p.PhaseColor, TextAnchor.MiddleLeft, true);
        Text(new Rect(P + 14, y + 48, cw - 28, 16), p.LiveDetail, 12, TextDim, TextAnchor.MiddleLeft, false);
        Text(new Rect(P + cw - 150, y + 10, 136, 14), p.UnitName.ToUpper() + " №" + (p.CompletedUnits + 1), 10, TextDim, TextAnchor.MiddleRight, true);
        if (p.HasSwatch)
        {
            Fill(new Rect(P + cw - 30, y + 30, 16, 16), p.SwatchColor);
            Text(new Rect(P + cw - 150, y + 28, 112, 20), p.SwatchName, 14, TextMain, TextAnchor.MiddleRight, false);
        }
        y += 74 + 18;

        // ---------- сегодня ----------
        CellStats.Day today = Live(st.days[29], p, st);
        Section(ref y, P, cw, "Сегодня, " + DateLong(today.date));
        if (today.plan == 0)
        {
            Text(new Rect(P, y, cw, 20), "Выходной: плановое обслуживание", 14, TextDim, TextAnchor.MiddleLeft, false);
            y += 30;
        }
        else
        {
            int planNow = Mathf.RoundToInt(today.plan * CellStats.ShiftProgress());
            Text(new Rect(P, y, cw / 2, 36), today.fact.ToString("N0"), 32, TextMain, TextAnchor.MiddleLeft, true);
            Text(new Rect(P, y + 36, cw / 2, 16), "сделано " + p.UnitNamePlural, 12, TextDim, TextAnchor.MiddleLeft, false);
            Text(new Rect(P + cw / 2, y, cw / 2, 36), today.plan.ToString("N0"), 32, TextDim, TextAnchor.MiddleRight, true);
            Text(new Rect(P + cw / 2, y + 36, cw / 2, 16), "план на день", 12, TextDim, TextAnchor.MiddleRight, false);
            y += 60;

            Rect bar = new Rect(P, y, cw, 10);
            Fill(bar, Line);
            float pf = Mathf.Clamp01((float)today.fact / today.plan);
            Color pc = today.fact >= planNow * 0.97f ? Good : today.fact >= planNow * 0.9f ? Warn : Bad;
            Fill(new Rect(P, y, cw * pf, 10), pc);
            float mx = P + cw * Mathf.Clamp01((float)planNow / today.plan);
            Fill(new Rect(mx - 1, y - 4, 2, 18), TextMain);
            y += 16;
            int diff = today.fact - planNow;
            Text(new Rect(P, y, cw, 16), "План к этому часу: " + planNow.ToString("N0") + "   ·   " + (diff >= 0 ? "опережение +" + diff.ToString("N0") : "отставание " + diff.ToString("N0")), 12, diff >= 0 ? Good : Warn, TextAnchor.MiddleLeft, false);
            y += 30;
        }

        // ---------- график ----------
        Section(ref y, P, cw, "План / факт за 14 дней");
        y = DrawChart(st, p, P, y, cw);
        y += 14;

        // ---------- выбранный день ----------
        CellStats.Day d = Live(st.days[selected], p, st);
        if (GUI.Button(new Rect(P, y, 28, 26), "◀") && selected > 0) selected--;
        if (GUI.Button(new Rect(P + cw - 28, y, 28, 26), "▶") && selected < 29) selected++;
        Text(new Rect(P + 34, y, cw - 68, 26), DateLong(d.date) + ", " + Dow[(int)d.date.DayOfWeek] + (selected == 29 ? " (сегодня)" : ""), 15, TextMain, TextAnchor.MiddleCenter, true);
        y += 36;

        if (d.plan == 0)
        {
            Text(new Rect(P, y, cw, 20), "Выходной: плановое обслуживание оборудования", 13, TextDim, TextAnchor.MiddleLeft, false);
            y += 34;
        }
        else
        {
            // OEE и составляющие
            float tw = (cw - 10) / 2f;
            Tile(new Rect(P, y, tw, 62), "OEE", d.oee, 0.85f, 0.75f);
            Tile(new Rect(P + tw + 10, y, tw, 62), "Доступность", d.availability, 0.92f, 0.85f);
            y += 72;
            Tile(new Rect(P, y, tw, 62), "Производительность", d.performance, 0.95f, 0.9f);
            Tile(new Rect(P + tw + 10, y, tw, 62), "Качество", d.quality, 0.99f, 0.98f);
            y += 76;

            float done = (float)d.fact / d.plan;
            Row(ref y, P, cw, "План", d.plan.ToString("N0") + " шт", TextMain);
            Row(ref y, P, cw, "Факт (годные)", d.fact.ToString("N0") + " шт", TextMain);
            Row(ref y, P, cw, "Выполнение плана", Mathf.RoundToInt(done * 100f) + " %", done >= 0.97f ? Good : done >= 0.9f ? Warn : Bad);
            Row(ref y, P, cw, "Брак", d.defects.ToString("N0") + " шт  (" + (d.gross > 0 ? (d.defects * 100f / d.gross).ToString("0.0") : "0") + " %)", d.defects > d.gross * 0.012f ? Warn : TextMain);
            Row(ref y, P, cw, "Поломок", d.breakdowns.ToString(), d.breakdowns == 0 ? Good : d.breakdowns > 1 ? Bad : Warn);
            Row(ref y, P, cw, "Простой", d.downtimeMin + " мин", d.downtimeMin > 60 ? Bad : d.downtimeMin > 0 ? Warn : Good);
            Row(ref y, P, cw, "Средний цикл", d.cycleSec.ToString(d.cycleSec < 20f ? "0.0" : "0") + " с / " + p.UnitName, TextMain);
            Row(ref y, P, cw, "Электроэнергия", d.energyKwh.ToString("N0") + " кВт·ч", TextMain);
            Row(ref y, P, cw, p.ConsumableName, d.consumable.ToString(d.consumable < 100f ? "N1" : "N0") + " " + p.ConsumableUnit, TextMain);
            y += 16;
        }

        // ---------- итоги за 30 дней ----------
        Section(ref y, P, cw, "Итоги за 30 дней");
        int sp = 0, sf = 0, sd = 0, sb = 0, sdown = 0, workDays = 0;
        for (int i = 0; i < 30; i++)
        {
            CellStats.Day x = Live(st.days[i], p, st);
            sp += x.plan; sf += x.fact; sd += x.defects; sb += x.breakdowns; sdown += x.downtimeMin;
            if (x.plan > 0) workDays++;
        }
        float runHours = workDays * CellStats.ShiftMinutes / 60f - sdown / 60f;
        Row(ref y, P, cw, "План / факт", sp.ToString("N0") + " / " + sf.ToString("N0") + "  (" + (sp > 0 ? Mathf.RoundToInt(sf * 100f / sp) : 0) + " %)", TextMain);
        Row(ref y, P, cw, "Брак", sd.ToString("N0") + " шт", TextMain);
        Row(ref y, P, cw, "Поломок", sb.ToString(), TextMain);
        Row(ref y, P, cw, "Суммарный простой", (sdown / 60f).ToString("0.0") + " ч", TextMain);
        Row(ref y, P, cw, "MTBF (наработка на отказ)", sb > 0 ? (runHours / sb).ToString("0") + " ч" : "—", TextMain);
        Row(ref y, P, cw, "MTTR (среднее время ремонта)", sb > 0 ? (sdown / (float)sb).ToString("0") + " мин" : "—", TextMain);
        y += 16;

        // ---------- журнал поломок ----------
        Section(ref y, P, cw, "Журнал поломок");
        int shown = Mathf.Min(8, st.breakdowns.Count);
        if (shown == 0)
        {
            Text(new Rect(P, y, cw, 18), "Поломок не было", 12, TextDim, TextAnchor.MiddleLeft, false);
            y += 24;
        }
        for (int i = 0; i < shown; i++)
        {
            CellStats.Breakdown b = st.breakdowns[i];
            Color c = b.minutes >= 60 ? Bad : b.minutes >= 25 ? Warn : TextDim;
            Fill(new Rect(P, y + 3, 3, 30), c);
            Text(new Rect(P + 10, y, cw - 100, 16), b.time.ToString("dd.MM HH:mm") + "  ·  " + b.unit, 11, TextDim, TextAnchor.MiddleLeft, true);
            Text(new Rect(P + cw - 80, y, 80, 16), b.minutes + " мин", 11, c, TextAnchor.MiddleRight, true);
            Text(new Rect(P + 10, y + 17, cw - 10, 18), b.what, 13, TextMain, TextAnchor.MiddleLeft, false);
            y += 42;
        }
        y += 10;

        // ---------- оборудование ----------
        Section(ref y, P, cw, "Состояние оборудования");
        foreach (CellStats.Unit un in st.units)
        {
            Fill(new Rect(P, y, cw, 52), Card);
            Text(new Rect(P + 12, y + 8, cw - 24, 18), un.name, 14, TextMain, TextAnchor.MiddleLeft, true);
            Text(new Rect(P + 12, y + 8, cw - 24, 18), un.hours.ToString("N0") + " ч  ·  до ТО " + un.hoursToService + " ч  ·  " + un.temp.ToString("0") + " °C", 11, un.hoursToService < 48 ? Warn : TextDim, TextAnchor.MiddleRight, false);
            Rect hb = new Rect(P + 12, y + 34, cw - 80, 6);
            Fill(hb, Line);
            Color hc = un.health >= 0.9f ? Good : un.health >= 0.75f ? Warn : Bad;
            Fill(new Rect(hb.x, hb.y, hb.width * un.health, 6), hc);
            Text(new Rect(P + cw - 62, y + 28, 50, 18), Mathf.RoundToInt(un.health * 100f) + " %", 12, hc, TextAnchor.MiddleRight, true);
            y += 60;
        }
        // ---------- ИИ-Инспектор ----------
        Section(ref y, P, cw, "Цифровой ИИ-Инспектор");
        DrawAIInspectorSection(p, st, ref y, P, cw);

        y += 6;
        Text(new Rect(P, y, cw, 16), "Данные синтетические · обновляются в реальном времени", 10, TextDim, TextAnchor.MiddleLeft, false);
        y += 20;
        return y;
    }

    void DrawAIInspectorSection(FactoryProcess p, CellStats st, ref float y, float P, float cw)
    {
        AllurAIInspector inspector = AllurAIInspector.Instance;
        if (inspector == null)
        {
            inspector = UnityEngine.Object.FindAnyObjectByType<AllurAIInspector>();
        }

        Rect cardRect = new Rect(P, y, cw, 110);
        Fill(cardRect, Card);

        // Статус
        bool isAnalyzing = inspector != null && inspector.IsAnalyzing;
        string statusText = isAnalyzing ? "ИИ анализирует..." : "● Готов к аудиту ячейки";
        Color statusColor = isAnalyzing ? Warn : Good;
        Pill(new Rect(P + 12, y + 10, 150, 20), statusText, statusColor);

        // Индекс процесса
        Text(new Rect(P + cw - 120, y + 10, 110, 20), "Оценка: " + (st != null && st.days.Count > 0 ? Mathf.RoundToInt(st.days[st.days.Count - 1].oee * 100f) + "% OEE" : "—"), 11, TextDim, TextAnchor.MiddleRight, false);

        // Краткое наставление
        string hint = isAnalyzing 
            ? "Идет опрос телеметрии и сопоставление со стандартами Allur..." 
            : "ИИ оценивает температурные аномалии, наработку узлов и такт.";
        Text(new Rect(P + 12, y + 36, cw - 24, 28), hint, 11, TextMain, TextAnchor.MiddleLeft, false);

        // Кнопки
        Rect btnAudit = new Rect(P + 12, y + 70, (cw - 30) * 0.55f, 28);
        Fill(btnAudit, new Color(Accent.r, Accent.g, Accent.b, 0.25f));
        Text(btnAudit, "⚡ Аудит ячейки", 11, TextMain, TextAnchor.MiddleCenter, true);
        if (GUI.Button(btnAudit, GUIContent.none, GUIStyle.none))
        {
            if (inspector == null)
            {
                Camera cam = Camera.main;
                if (cam != null) inspector = cam.gameObject.AddComponent<AllurAIInspector>();
            }
            if (inspector != null)
            {
                inspector.RunProcessAudit(p);
                inspector.isWindowOpen = true;
            }
        }

        Rect btnOpen = new Rect(P + 18 + btnAudit.width, y + 70, (cw - 30) * 0.45f, 28);
        Fill(btnOpen, new Color(1f, 1f, 1f, 0.08f));
        Text(btnOpen, "Панель ИИ [ I ]", 11, TextDim, TextAnchor.MiddleCenter, true);
        if (GUI.Button(btnOpen, GUIContent.none, GUIStyle.none))
        {
            if (inspector == null)
            {
                Camera cam = Camera.main;
                if (cam != null) inspector = cam.gameObject.AddComponent<AllurAIInspector>();
            }
            if (inspector != null)
            {
                inspector.ToggleWindow();
            }
        }

        y += 118;
    }

    float DrawChart(CellStats st, FactoryProcess p, float x, float y, float w)
    {
        const float H = 110f;
        int first = 16;
        int max = 1;
        for (int i = first; i < 30; i++) max = Mathf.Max(max, Mathf.Max(st.days[i].plan, Live(st.days[i], p, st).fact));
        float slot = w / 14f;
        float bw = slot * 0.62f;

        Fill(new Rect(x, y + H, w, 1), Line);
        for (int i = first; i < 30; i++)
        {
            CellStats.Day d = Live(st.days[i], p, st);
            float sx = x + (i - first) * slot;
            Rect hit = new Rect(sx, y, slot, H + 18);
            if (i == selected) Fill(hit, new Color(1f, 1f, 1f, 0.06f));

            if (d.plan == 0)
            {
                Text(new Rect(sx, y + H - 16, slot, 14), "ТО", 9, TextDim, TextAnchor.MiddleCenter, false);
            }
            else
            {
                float pr = (float)d.fact / d.plan;
                Color c = i == 29 ? Accent : pr >= 0.97f ? Good : pr >= 0.9f ? Warn : Bad;
                float fh = H * d.fact / max;
                Fill(new Rect(sx + (slot - bw) / 2f, y + H - fh, bw, fh), c);
                float ph = H * d.plan / max;
                Fill(new Rect(sx + 1, y + H - ph, slot - 2, 2), TextMain);
                if (d.breakdowns > 0)
                    Fill(new Rect(sx + slot / 2f - 2, y + H - fh - 8, 4, 4), Bad);
            }
            Text(new Rect(sx, y + H + 3, slot, 14), d.date.Day.ToString(), 10, i == selected ? TextMain : TextDim, TextAnchor.MiddleCenter, i == selected);
            if (GUI.Button(hit, GUIContent.none, GUIStyle.none)) selected = i;
        }
        y += H + 22;

        // легенда
        Fill(new Rect(x, y + 6, 10, 2), TextMain);
        Text(new Rect(x + 14, y, 40, 14), "план", 10, TextDim, TextAnchor.MiddleLeft, false);
        Fill(new Rect(x + 52, y + 2, 8, 8), Good);
        Text(new Rect(x + 64, y, 40, 14), "факт", 10, TextDim, TextAnchor.MiddleLeft, false);
        Fill(new Rect(x + 102, y + 5, 4, 4), Bad);
        Text(new Rect(x + 110, y, 60, 14), "поломка", 10, TextDim, TextAnchor.MiddleLeft, false);
        Text(new Rect(x + 170, y, w - 170, 14), "нажми на столбик →", 10, TextDim, TextAnchor.MiddleRight, false);
        return y + 16;
    }

    // ---------- примитивы ----------

    static string DateLong(DateTime d)
    {
        return d.Day + " " + Months[d.Month - 1];
    }

    static void Fill(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    void Text(Rect r, string s, int size, Color c, TextAnchor a, bool bold)
    {
        text.fontSize = size;
        text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        text.alignment = a;
        text.normal.textColor = c;
        GUI.Label(r, s, text);
    }

    void Pill(Rect r, string s, Color c)
    {
        Fill(r, new Color(c.r, c.g, c.b, 0.16f));
        Text(r, s, 12, c, TextAnchor.MiddleCenter, true);
    }

    void Section(ref float y, float x, float w, string title)
    {
        Text(new Rect(x, y, w, 18), title.ToUpper(), 11, TextDim, TextAnchor.MiddleLeft, true);
        y += 20;
        Fill(new Rect(x, y, w, 1), Line);
        y += 12;
    }

    void Row(ref float y, float x, float w, string label, string value, Color vc)
    {
        Text(new Rect(x, y, w * 0.55f, 20), label, 13, TextDim, TextAnchor.MiddleLeft, false);
        Text(new Rect(x + w * 0.4f, y, w * 0.6f, 20), value, 13, vc, TextAnchor.MiddleRight, true);
        y += 22;
        Fill(new Rect(x, y - 1, w, 1), Line);
        y += 2;
    }

    void Tile(Rect r, string title, float v, float good, float warn)
    {
        Fill(r, Card);
        Color c = v >= good ? Good : v >= warn ? Warn : Bad;
        Text(new Rect(r.x + 12, r.y + 8, r.width - 24, 14), title, 11, TextDim, TextAnchor.MiddleLeft, false);
        Text(new Rect(r.x + 12, r.y + 24, r.width - 24, 26), (v * 100f).ToString("0.0") + " %", 21, c, TextAnchor.MiddleLeft, true);
        Fill(new Rect(r.x, r.y + r.height - 3, r.width * Mathf.Clamp01(v), 3), c);
    }
}

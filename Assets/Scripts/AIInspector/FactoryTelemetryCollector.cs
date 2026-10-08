using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Сборщик телеметрии цифрового двойника завода Allur в реальном времени.
/// Извлекает операционные данные из физических процессов (FactoryProcess),
/// синтетической статистики (CellStats) и перемещений персонала (WorkerCrowd).
/// </summary>
public static class FactoryTelemetryCollector
{
    [Serializable]
    public class UnitTelemetry
    {
        public string name;
        public int hours;
        public int hoursToService;
        public float temp;
        public float health;
    }

    [Serializable]
    public class BreakdownTelemetry
    {
        public string unit;
        public string what;
        public int minutes;
        public string timeAgo;
    }

    [Serializable]
    public class ProcessTelemetry
    {
        public string name;
        public string code;
        public string title;
        public string phaseName;
        public int phase;
        public int completedUnits;
        public string liveDetail;

        public int plan;
        public int gross;
        public int fact;
        public int defects;
        public int breakdowns;
        public int downtimeMin;

        public float availability;
        public float performance;
        public float quality;
        public float oee;

        public float energyKwh;
        public float consumable;
        public string consumableName;

        public List<UnitTelemetry> units = new List<UnitTelemetry>();
        public List<BreakdownTelemetry> recentBreakdowns = new List<BreakdownTelemetry>();
    }

    [Serializable]
    public class FactorySnapshot
    {
        public DateTime timestamp;
        public float shiftProgress; // 0..1
        public int totalProcesses;
        public int totalPlan;
        public int totalFact;
        public int totalDefects;
        public int totalDowntimeMin;
        public float averageOee;
        public float factoryHealthScore; // 0..100
        public string bottleneckProcess;
        public int workersCount;

        public List<ProcessTelemetry> processes = new List<ProcessTelemetry>();
    }

    /// <summary>
    /// Собирает полный снимок телеметрии всех процессов завода на текущий момент.
    /// </summary>
    public static FactorySnapshot CollectSnapshot()
    {
        FactorySnapshot snapshot = new FactorySnapshot
        {
            timestamp = DateTime.Now,
            shiftProgress = CellStats.ShiftProgress()
        };

        FactoryProcess[] processes = UnityEngine.Object.FindObjectsByType<FactoryProcess>(FindObjectsSortMode.None);
        snapshot.totalProcesses = processes.Length;

        // Рабочие
        WorkerCrowd crowd = UnityEngine.Object.FindAnyObjectByType<WorkerCrowd>();
        if (crowd != null)
        {
            snapshot.workersCount = crowd.transform.childCount;
        }

        float sumOee = 0f;
        int activeProcessCount = 0;
        float minOee = 2f;
        string worstProcess = "Не определен";

        foreach (FactoryProcess p in processes)
        {
            if (p == null) continue;

            CellStats st = CellStats.Generate(p);
            CellStats.Day today = st.days.Count > 0 ? st.days[st.days.Count - 1] : new CellStats.Day();
            CellStats.Day live = CalculateLiveDay(today, p, st);

            ProcessTelemetry pt = new ProcessTelemetry
            {
                name = p.gameObject.name,
                code = p.Code,
                title = p.Title,
                phaseName = p.PhaseName,
                phase = p.Phase,
                completedUnits = p.CompletedUnits,
                liveDetail = p.LiveDetail,

                plan = live.plan,
                gross = live.gross,
                fact = live.fact,
                defects = live.defects,
                breakdowns = live.breakdowns,
                downtimeMin = live.downtimeMin,

                availability = live.availability,
                performance = live.performance,
                quality = live.quality,
                oee = live.oee,

                energyKwh = live.energyKwh,
                consumable = live.consumable,
                consumableName = p.ConsumableName
            };

            foreach (CellStats.Unit u in st.units)
            {
                pt.units.Add(new UnitTelemetry
                {
                    name = u.name,
                    hours = u.hours,
                    hoursToService = u.hoursToService,
                    temp = u.temp,
                    health = u.health
                });
            }

            DateTime now = DateTime.Now;
            int bCount = Mathf.Min(5, st.breakdowns.Count);
            for (int i = 0; i < bCount; i++)
            {
                CellStats.Breakdown b = st.breakdowns[i];
                TimeSpan ago = now - b.time;
                string agoText = ago.TotalHours < 1 ? Mathf.Max(1, (int)ago.TotalMinutes) + " мин"
                               : ago.TotalHours < 48 ? (int)ago.TotalHours + " ч"
                               : (int)ago.TotalDays + " дн";

                pt.recentBreakdowns.Add(new BreakdownTelemetry
                {
                    unit = b.unit,
                    what = b.what,
                    minutes = b.minutes,
                    timeAgo = agoText
                });
            }

            snapshot.processes.Add(pt);

            snapshot.totalPlan += live.plan;
            snapshot.totalFact += live.fact;
            snapshot.totalDefects += live.defects;
            snapshot.totalDowntimeMin += live.downtimeMin;

            if (live.plan > 0)
            {
                sumOee += live.oee;
                activeProcessCount++;

                if (live.oee < minOee)
                {
                    minOee = live.oee;
                    worstProcess = p.Title + " (" + p.Code + ")";
                }
            }
        }

        snapshot.averageOee = activeProcessCount > 0 ? (sumOee / activeProcessCount) : 0.85f;
        snapshot.bottleneckProcess = worstProcess;

        // Расчет интегрального рейтинга здоровья завода (0..100)
        float baseScore = snapshot.averageOee * 70f; // до 70 баллов от OEE
        float defectPenalty = snapshot.totalFact > 0 ? Mathf.Clamp01((float)snapshot.totalDefects / snapshot.totalFact) * 150f : 0f;
        float downtimePenalty = Mathf.Clamp(snapshot.totalDowntimeMin / 120f, 0f, 15f);
        snapshot.factoryHealthScore = Mathf.Clamp(baseScore + 30f - defectPenalty - downtimePenalty, 20f, 99.5f);

        return snapshot;
    }

    /// <summary>
    /// Собирает снимок для конкретного выбранного процесса.
    /// </summary>
    public static ProcessTelemetry CollectProcessTelemetry(FactoryProcess p)
    {
        if (p == null) return null;
        CellStats st = CellStats.Generate(p);
        CellStats.Day today = st.days.Count > 0 ? st.days[st.days.Count - 1] : new CellStats.Day();
        CellStats.Day live = CalculateLiveDay(today, p, st);

        ProcessTelemetry pt = new ProcessTelemetry
        {
            name = p.gameObject.name,
            code = p.Code,
            title = p.Title,
            phaseName = p.PhaseName,
            phase = p.Phase,
            completedUnits = p.CompletedUnits,
            liveDetail = p.LiveDetail,

            plan = live.plan,
            gross = live.gross,
            fact = live.fact,
            defects = live.defects,
            breakdowns = live.breakdowns,
            downtimeMin = live.downtimeMin,

            availability = live.availability,
            performance = live.performance,
            quality = live.quality,
            oee = live.oee,

            energyKwh = live.energyKwh,
            consumable = live.consumable,
            consumableName = p.ConsumableName
        };

        foreach (CellStats.Unit u in st.units)
        {
            pt.units.Add(new UnitTelemetry
            {
                name = u.name,
                hours = u.hours,
                hoursToService = u.hoursToService,
                temp = u.temp,
                health = u.health
            });
        }

        DateTime now = DateTime.Now;
        int bCount = Mathf.Min(5, st.breakdowns.Count);
        for (int i = 0; i < bCount; i++)
        {
            CellStats.Breakdown b = st.breakdowns[i];
            TimeSpan ago = now - b.time;
            string agoText = ago.TotalHours < 1 ? Mathf.Max(1, (int)ago.TotalMinutes) + " мин"
                           : ago.TotalHours < 48 ? (int)ago.TotalHours + " ч"
                           : (int)ago.TotalDays + " дн";

            pt.recentBreakdowns.Add(new BreakdownTelemetry
            {
                unit = b.unit,
                what = b.what,
                minutes = b.minutes,
                timeAgo = agoText
            });
        }

        return pt;
    }

    /// <summary>
    /// Формирует структурированный промпт для языковой модели OpenAI,
    /// содержащий полную картину работы завода в реальном времени.
    /// </summary>
    public static string BuildTelemetryPromptText(FactorySnapshot snap, string focusTopic = null)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== ТЕЛЕМЕТРИЯ ЦИФРОВОГО ДВОЙНИКА АВТОЗАВОДА ALLUR ===");
        sb.AppendLine($"Время фиксации: {snap.timestamp:dd.MM.yyyy HH:mm:ss}");
        sb.AppendLine($"Прогресс текущей смены: {Mathf.RoundToInt(snap.shiftProgress * 100f)}%");
        sb.AppendLine($"Всего производственных участков: {snap.totalProcesses}");
        sb.AppendLine($"Суточный план / факт выпуска: {snap.totalPlan} / {snap.totalFact} шт");
        sb.AppendLine($"Выявленный брак: {snap.totalDefects} шт");
        sb.AppendLine($"Суммарный простой оборудования: {snap.totalDowntimeMin} мин");
        sb.AppendLine($"Средний OEE завода: {Mathf.RoundToInt(snap.averageOee * 100f)}%");
        sb.AppendLine($"Интегральный индекс здоровья завода: {snap.factoryHealthScore:F1}/100");
        sb.AppendLine($"Узкое место (Bottleneck): {snap.bottleneckProcess}");
        sb.AppendLine($"Персонал логистики в цехе: {snap.workersCount} чел.");
        sb.AppendLine();

        sb.AppendLine("--- ДЕТАЛИЗАЦИЯ ПО ПРОИЗВОДСТВЕННЫМ УЧАСТКАМ ---");
        foreach (var p in snap.processes)
        {
            sb.AppendLine($"[Участок: {p.title} ({p.code})]");
            sb.AppendLine($"  Текущая стадия: {p.phaseName} ({p.liveDetail})");
            sb.AppendLine($"  Выпуск с запуска: {p.completedUnits} шт | План/Факт сегодня: {p.plan}/{p.fact} шт");
            sb.AppendLine($"  Эффективность OEE: {Mathf.RoundToInt(p.oee * 100f)}% (Доступность: {Mathf.RoundToInt(p.availability * 100f)}%, Качество: {Mathf.RoundToInt(p.quality * 100f)}%)");
            sb.AppendLine($"  Простой: {p.downtimeMin} мин, поломок: {p.breakdowns}");

            // Критические узлы
            List<string> unitAlerts = new List<string>();
            foreach (var u in p.units)
            {
                if (u.health < 0.8f || u.hoursToService < 48 || u.temp > 48f)
                {
                    unitAlerts.Add($"{u.name} (здоровье: {Mathf.RoundToInt(u.health * 100f)}%, до ТО: {u.hoursToService}ч, темп: {u.temp:F0}°C)");
                }
            }
            if (unitAlerts.Count > 0)
            {
                sb.AppendLine("  ВНИМАНИЕ ПО УЗЛАМ: " + string.Join("; ", unitAlerts));
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(focusTopic))
        {
            sb.AppendLine("--- СПЕЦИАЛЬНЫЙ ЗАПРОС ОПЕРАТОРА / ТЕМА ФОКУСА ---");
            sb.AppendLine(focusTopic);
            sb.AppendLine();
        }

        sb.AppendLine("--- ТРЕБОВАНИЯ К ВАШЕМУ ЭКСПЕРТНОМУ ОТВЕТУ ---");
        sb.AppendLine("Ответьте строго по структуре:");
        sb.AppendLine("СТАТУС: [КРИТИЧЕСКИЙ РИСК | ТРЕБУЕТ ВНИМАНИЯ | ШТАТНЫЙ РЕЖИМ]");
        sb.AppendLine("ЗАГОЛОВОК: [Краткий заголовок ситуации]");
        sb.AppendLine();
        sb.AppendLine("### ⚡ КРАТКАЯ СВОДКА РУКОВОДИТЕЛЯ");
        sb.AppendLine("- Оцените сменный темп, выполнение плана (факт/план), текущий OEE и главное узкое место цеха (Bottleneck).");
        sb.AppendLine();
        sb.AppendLine("### ⚠️ ПРЕДУПРЕЖДЕНИЯ И РИСКИ");
        sb.AppendLine("- Перечислите конкретные предупреждения: узлы с температурой > 45°C, износ > 20%, остаток ресурса до ТО < 48ч, риски брака и простоя.");
        sb.AppendLine();
        sb.AppendLine("### 🔬 ДЕТАЛЬНЫЙ ТЕХНИЧЕСКИЙ АНАЛИЗ");
        sb.AppendLine("- Проанализируйте работу участков сварки RobotCell, вклейки стекол, колесной линии, сход-развала и логистики. Укажите первопричины простоя и дисбаланса такта.");
        sb.AppendLine();
        sb.AppendLine("### 🛠️ МАКСИМАЛЬНЫЕ ПРАКТИЧЕСКИЕ РЕКОМЕНДАЦИИ");
        sb.AppendLine("- Дайте не менее 5-7 конкретных нумерованных предписаний (1..N): срочные действия дежурного инженера, предиктивное ТО службы механиков, регулировка пневматики/скоростей и контроль качества.");

        return sb.ToString();
    }

    private static CellStats.Day CalculateLiveDay(CellStats.Day d, FactoryProcess p, CellStats st)
    {
        if (d.date != DateTime.Today || d.plan == 0) return d;
        float f = CellStats.ShiftProgress();
        CellStats.Day t = new CellStats.Day
        {
            date = d.date,
            plan = d.plan,
            gross = Mathf.RoundToInt(d.gross * f) + Mathf.Max(0, p.CompletedUnits),
            defects = Mathf.RoundToInt(d.defects * f)
        };
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
}

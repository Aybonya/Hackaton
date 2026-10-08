using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Аналитический движок ИИ-инспектора.
/// При отсутствии подключения к OpenAI (или при использовании тестового ключа)
/// производит детальный экспертный аудит завода на основе реальной телеметрии цифрового двойника.
/// </summary>
public static class AIInspectorEngine
{
    public const string SystemRolePrompt =
        "Вы — Главный Цифровой Инспектор качества и операционной эффективности автозавода Allur.\n" +
        "Ваша цель: глубокий непрерывный мониторинг технологических постов (сварка кузовов RobotCell, вклейка стекол, сборка колес, стенд сход-развала, внутрицеховая логистика).\n" +
        "Вы обязаны выполнять комплексную экспертную работу:\n" +
        "1. Предупреждать: фиксировать перегревы сервоприводов, износ редукторов, дефицит ресурса до ТО (<48ч), риски брака и угрозы срыва сменного такта.\n" +
        "2. Анализировать: рассчитывать OEE, находить узкие места (Bottleneck), объяснять первопричины простоев и рассинхронизации тактов.\n" +
        "3. Выдавать краткую сводку: формировать мгновенную сводку руководителя (Executive Summary) со статусом выполнения сменного задания.\n" +
        "4. Выдавать максимальный пакет практических рекомендаций: конкретные пошаговые предписания для дежурного инженера, наладчиков РТК, службы механиков (СГМ) и логистики.\n" +
        "Отвечайте профессионально, технически грамотно на русском языке с четким форматированием.";

    public enum AlertSeverity
    {
        Normal,
        Warning,
        Critical
    }

    [Serializable]
    public class AuditReport
    {
        public DateTime timestamp;
        public AlertSeverity severity;
        public string severityText;
        public float factoryHealthScore;
        public string summaryTitle;
        public string mainAnalysis;
        public List<string> findings = new List<string>();
        public List<string> recommendations = new List<string>();
        public bool isSimulated;
        public string sourceName;
    }

    [Serializable]
    public class BottleneckReport
    {
        public DateTime timestamp;
        public string sourceName;
        public string bottleneckStation;
        public string bottleneckCode;
        public float stationOee;
        public int downtimeMin;
        public int completedUnits;
        public int plan;
        public int fact;
        public float taktTargetSec;
        public float cycleTimeSec;
        public float taktLagSec;
        public int lostCarsEstimate;
        public string bufferUpstream;
        public string bufferDownstream;
        public List<string> rootCauses = new List<string>();
        public List<string> actionSteps = new List<string>();
        public string rawAiAnalysis;
    }

    [Serializable]
    public class MaintenanceTask
    {
        public string stationName;
        public string unitName;
        public int operatingHours;
        public int hoursToService;
        public float temperature;
        public float health; // 0..1
        public string urgencyText; // "🔴 СРОЧНО (<24ч)", "🟡 ВНИМАНИЕ (<72ч)", "🟢 ПЛАНОВО"
        public Color urgencyColor;
        public string department; // "[СГМ] Механики", "[РТК] Роботы", "[ЭТЛ] Электрики"
        public string procedure;
        public string partsAndConsumables;
    }

    [Serializable]
    public class MaintenancePlanReport
    {
        public DateTime timestamp;
        public string sourceName;
        public int criticalCount;
        public int warningCount;
        public int scheduledCount;
        public List<MaintenanceTask> tasks = new List<MaintenanceTask>();
        public List<string> sparePartsSummary = new List<string>();
        public string generalGuidelines;
        public string rawAiAnalysis;
    }

    /// <summary>
    /// Парсит и структурирует ответ языковой модели OpenAI, извлекая уровни тревоги,
    /// краткую сводку, список предупреждений и конкретные рекомендации.
    /// </summary>
    public static AuditReport ParseOpenAIReport(string text, FactoryTelemetryCollector.FactorySnapshot snap, string model)
    {
        AuditReport report = new AuditReport
        {
            timestamp = DateTime.Now,
            factoryHealthScore = snap != null ? snap.factoryHealthScore : 90f,
            isSimulated = false,
            sourceName = "OpenAI Cloud (" + model + ")",
            mainAnalysis = text
        };

        if (string.IsNullOrWhiteSpace(text)) return report;

        string lower = text.ToLowerInvariant();
        if (lower.Contains("критическ") || lower.Contains("аварийн") || lower.Contains("critical"))
        {
            report.severity = AlertSeverity.Critical;
            report.severityText = "КРИТИЧЕСКИЙ РИСК";
        }
        else if (lower.Contains("требует внимания") || lower.Contains("внимание") || lower.Contains("предупрежден") || lower.Contains("warning"))
        {
            report.severity = AlertSeverity.Warning;
            report.severityText = "ТРЕБУЕТ ВНИМАНИЯ";
        }
        else
        {
            report.severity = AlertSeverity.Normal;
            report.severityText = "ШТАТНЫЙ РЕЖИМ";
        }

        string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("ЗАГОЛОВОК:", StringComparison.OrdinalIgnoreCase))
            {
                report.summaryTitle = trimmed.Substring("ЗАГОЛОВОК:".Length).Trim();
                break;
            }
        }
        if (string.IsNullOrEmpty(report.summaryTitle))
        {
            report.summaryTitle = $"Комплексный аудит цифрового двойника ({report.severityText})";
        }

        bool inFindings = false;
        bool inRecs = false;

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Contains("ПРЕДУПРЕЖДЕНИ") || line.Contains("РИСКИ") || line.Contains("ВЫЯВЛЕННЫЕ"))
            {
                inFindings = true;
                inRecs = false;
                continue;
            }
            if (line.Contains("РЕКОМЕНДАЦИ") || line.Contains("ПРЕДПИСАНИ"))
            {
                inRecs = true;
                inFindings = false;
                continue;
            }
            if (line.StartsWith("###") || line.StartsWith("===") || (line.StartsWith("##") && line.Contains("АНАЛИЗ")))
            {
                if (!line.Contains("ПРЕДУПРЕЖДЕНИ") && !line.Contains("РЕКОМЕНДАЦИ"))
                {
                    inFindings = false;
                    inRecs = false;
                }
            }

            if (inFindings && (line.StartsWith("-") || line.StartsWith("•") || line.StartsWith("*") || line.StartsWith("⚠️") || line.StartsWith("🔴") || line.StartsWith("🟡")))
            {
                string item = line.TrimStart('-', '•', '*', ' ', '\t');
                if (!string.IsNullOrWhiteSpace(item) && item.Length > 5 && report.findings.Count < 6)
                {
                    report.findings.Add(item);
                }
            }

            if (inRecs && line.Length > 3 && (char.IsDigit(line[0]) || line.StartsWith("-") || line.StartsWith("•") || line.StartsWith("*") || line.StartsWith("💡") || line.StartsWith("🛠️")))
            {
                string item = line.TrimStart('-', '•', '*', ' ', '\t', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ')');
                if (!string.IsNullOrWhiteSpace(item) && item.Length > 8 && report.recommendations.Count < 8)
                {
                    report.recommendations.Add(item);
                }
            }
        }

        if (report.findings.Count == 0 && snap != null)
        {
            report.findings.Add($"Текущий OEE: {Mathf.RoundToInt(snap.averageOee * 100f)}% | Выпуск: {snap.totalFact}/{snap.totalPlan} авто.");
            if (!string.IsNullOrEmpty(snap.bottleneckProcess))
                report.findings.Add("Узкое место цеха: " + snap.bottleneckProcess);
        }
        if (report.recommendations.Count == 0)
        {
            report.recommendations.Add("Службе главного механика провести предиктивную диагностику узлов сварочной ячейки и конвейера.");
            report.recommendations.Add("Синхронизировать тактовое время сборочных станций для предотвращения заторов.");
        }

        return report;
    }

    /// <summary>
    /// Генерирует комплексный аудит всего завода на основе телеметрии.
    /// Включает предупреждения, детальный анализ, краткую сводку и максимальный пакет рекомендаций.
    /// </summary>
    public static AuditReport GenerateSimulatedAudit(FactoryTelemetryCollector.FactorySnapshot snap, string customQuery = null)
    {
        AuditReport report = new AuditReport
        {
            timestamp = DateTime.Now,
            factoryHealthScore = snap.factoryHealthScore,
            isSimulated = true,
            sourceName = "Экспертный ИИ-Инспектор Allur (Анализ телеметрии)"
        };

        // Анализ критических узлов
        List<string> criticalUnits = new List<string>();
        List<string> warningUnits = new List<string>();
        FactoryTelemetryCollector.ProcessTelemetry worstOeeProcess = null;
        float minOee = 2f;

        foreach (var p in snap.processes)
        {
            if (p.plan > 0 && p.oee < minOee)
            {
                minOee = p.oee;
                worstOeeProcess = p;
            }

            foreach (var u in p.units)
            {
                if (u.health < 0.65f || u.temp > 58f || u.hoursToService < 16)
                {
                    criticalUnits.Add($"[{p.title}] {u.name}: нагрев {u.temp:F1}°C, износ {Mathf.RoundToInt((1f - u.health) * 100f)}%, ТО через {u.hoursToService}ч");
                }
                else if (u.health < 0.80f || u.temp > 49f || u.hoursToService < 60)
                {
                    warningUnits.Add($"[{p.title}] {u.name}: темп {u.temp:F1}°C, ТО через {u.hoursToService}ч");
                }
            }
        }

        // Определение уровня тревоги
        if (criticalUnits.Count > 0 || snap.averageOee < 0.75f || snap.totalDowntimeMin > 90)
        {
            report.severity = AlertSeverity.Critical;
            report.severityText = "КРИТИЧЕСКИЙ РИСК";
            report.summaryTitle = "Обнаружены риски срыва сменного такта и локальный перегрев узлов";
        }
        else if (warningUnits.Count > 0 || snap.averageOee < 0.88f || snap.totalDowntimeMin > 30)
        {
            report.severity = AlertSeverity.Warning;
            report.severityText = "ТРЕБУЕТ ВНИМАНИЯ";
            report.summaryTitle = "Штатный темп с локальными предупреждениями по обслуживанию узлов";
        }
        else
        {
            report.severity = AlertSeverity.Normal;
            report.severityText = "ШТАТНЫЙ РЕЖИМ";
            report.summaryTitle = "Производственный поток стабилен, параметры оборудования в допуске";
        }

        // 1. Формирование предупреждений и находок (Findings / Alerts)
        report.findings.Add($"Производительность цеха: общий OEE = {Mathf.RoundToInt(snap.averageOee * 100f)}% (норма ≥ 85%). Собрано {snap.totalFact} из {snap.totalPlan} авто.");

        if (worstOeeProcess != null)
        {
            report.findings.Add($"Узкое место цеха (Bottleneck): «{worstOeeProcess.title}» ({worstOeeProcess.code}). OEE: {Mathf.RoundToInt(worstOeeProcess.oee * 100f)}%, простой: {worstOeeProcess.downtimeMin} мин.");
        }

        if (criticalUnits.Count > 0)
        {
            int maxCrit = Mathf.Min(3, criticalUnits.Count);
            for (int i = 0; i < maxCrit; i++)
            {
                report.findings.Add("🔴 Аномалия: " + criticalUnits[i]);
            }
        }
        else if (warningUnits.Count > 0)
        {
            int maxWarn = Mathf.Min(3, warningUnits.Count);
            for (int i = 0; i < maxWarn; i++)
            {
                report.findings.Add("🟡 Контроль узла: " + warningUnits[i]);
            }
        }

        if (snap.totalDefects > 0)
        {
            float defRate = snap.totalFact > 0 ? ((float)snap.totalDefects / (snap.totalFact + snap.totalDefects) * 100f) : 0f;
            report.findings.Add($"Качество выпуска: {snap.totalDefects} дефектов ({defRate:F2}% брака). Зоны: герметизация стекол и швы кузова.");
        }

        if (snap.workersCount > 0)
        {
            report.findings.Add($"Логистика и комплектация: {snap.workersCount} рабочих перемещают компоненты в проходе. Поставки осуществляются по графику Just-in-Time.");
        }

        // 2. Формирование максимального пакета практических рекомендаций (Recommendations)
        if (criticalUnits.Count > 0)
        {
            report.recommendations.Add("Срочно направить дежурную бригаду СГМ (Службы главного механика) на осмотр критических узлов с заменой смазки и контролем вибраций.");
        }
        else
        {
            report.recommendations.Add("Запланировать превентивный вибродиагностический контроль подшипниковых опор и сервомоторов перед началом следующей смены.");
        }

        if (worstOeeProcess != null && worstOeeProcess.downtimeMin > 15)
        {
            report.recommendations.Add($"Оптимизировать тактовое время на участке «{worstOeeProcess.title}»: проверить давление в пневмосети (норма 6.2 бар) и датчики позиционирования кареток.");
        }
        else
        {
            report.recommendations.Add("Скорректировать кривые разгона/торможения роботов KUKA/Fanuc для уменьшения тепловыделения приводов на 12-15%.");
        }

        report.recommendations.Add("Провести калибровку расхода праймера и полиуретанового герметика на станции вклейки стекол для снижения дефектов краевого шва.");
        report.recommendations.Add("Проверить нулевые юстировочные метки лазерных измерителей на стенде регулировки сход-развала (Wheel Aligner).");
        report.recommendations.Add("Выровнять объем межоперационного буфера между роботизированной сваркой и линией установки колес до 3 кузовов.");
        report.recommendations.Add("Оптимизировать маршруты складских тележек операторов логистики, исключив встречные потоки в центральном проходе цеха.");
        report.recommendations.Add("Установить контрольный порог сигнализации SCADA по температуре приводов на отметку 48°C для предупреждения аварийных остановок.");

        // 3. Формирование главного текста отчета (Сводка, Алерты, Анализ, Рекомендации)
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("### ⚡ КРАТКАЯ СВОДКА РУКОВОДИТЕЛЯ (EXECUTIVE SUMMARY)");
        sb.AppendLine($"• **Статус завода:** {report.severityText} | Интегральный индекс надежности: **{report.factoryHealthScore:F1}%**");
        sb.AppendLine($"• **Выполнение сменного плана:** {snap.totalFact} из {snap.totalPlan} ед. (прогресс смены: {Mathf.RoundToInt(snap.shiftProgress * 100f)}%)");
        sb.AppendLine($"• **Эффективность OEE:** {Mathf.RoundToInt(snap.averageOee * 100f)}% | Суммарное время простоя оборудования: **{snap.totalDowntimeMin} мин**");
        sb.AppendLine($"• **Главное узкое место (Bottleneck):** {snap.bottleneckProcess}");
        sb.AppendLine();

        sb.AppendLine("### ⚠️ ПРЕДУПРЕЖДЕНИЯ И ТЕХНОЛОГИЧЕСКИЕ РИСКИ");
        foreach (var f in report.findings)
        {
            sb.AppendLine("• " + f);
        }
        sb.AppendLine();

        sb.AppendLine("### 🔬 ДЕТАЛЬНЫЙ ТЕХНИЧЕСКИЙ АНАЛИЗ ПОСТОВ");
        sb.AppendLine($"В цехе функционирует {snap.totalProcesses} технологических участков. Роботизированная ячейка сварки RobotCell поддерживает синхронизацию манипуляторов, однако повышение температуры приводов свидетельствует о повышенной плотности сменного цикла.");
        if (worstOeeProcess != null)
        {
            sb.AppendLine($"Участок «{worstOeeProcess.title}» ограничивает пропускную способность цеха из-за локальных задержек цикла ({worstOeeProcess.downtimeMin} мин простоя).");
        }
        sb.AppendLine($"Уровень дефектов ({snap.totalDefects} шт) находится в пределах технологического допуска, но требует усиленного контроля герметичности и геометрии швов.");
        sb.AppendLine();

        sb.AppendLine("### 🛠️ МАКСИМАЛЬНЫЙ ПАКЕТ ПРАКТИЧЕСКИХ РЕКОМЕНДАЦИЙ");
        for (int i = 0; i < report.recommendations.Count; i++)
        {
            sb.AppendLine($"{i + 1}. {report.recommendations[i]}");
        }

        if (!string.IsNullOrEmpty(customQuery))
        {
            sb.AppendLine();
            sb.AppendLine($"### 💬 ОТВЕТ НА ЗАПРОС ОПЕРАТОРА: «{customQuery}»");
            sb.AppendLine(GenerateCustomQueryAnswer(customQuery, snap));
        }

        report.mainAnalysis = sb.ToString();
        return report;
    }

    /// <summary>
    /// Генерирует локальный аудит конкретного производственного процесса.
    /// </summary>
    public static AuditReport GenerateProcessAudit(FactoryProcess p, string customQuery = null)
    {
        var pt = FactoryTelemetryCollector.CollectProcessTelemetry(p);
        AuditReport report = new AuditReport
        {
            timestamp = DateTime.Now,
            isSimulated = true,
            sourceName = "Инспектор поста: " + p.Title
        };

        if (pt == null)
        {
            report.mainAnalysis = "Нет данных для процесса.";
            return report;
        }

        float health = pt.oee * 70f + pt.quality * 20f + (pt.downtimeMin > 30 ? 0 : 10f);
        report.factoryHealthScore = Mathf.Clamp(health, 30f, 99f);

        report.severity = pt.oee < 0.75f || pt.downtimeMin > 45 ? AlertSeverity.Critical
                        : pt.oee < 0.88f || pt.downtimeMin > 15 ? AlertSeverity.Warning
                        : AlertSeverity.Normal;
        report.severityText = report.severity == AlertSeverity.Critical ? "ТРЕБУЕТ ВМЕШАТЕЛЬСТВА"
                            : report.severity == AlertSeverity.Warning ? "ВНИМАНИЕ" : "ШТАТНО";

        report.summaryTitle = $"{pt.title} ({pt.code}): стадия «{pt.phaseName}»";

        report.findings.Add($"Эффективность OEE: {Mathf.RoundToInt(pt.oee * 100f)}% (Готовность: {Mathf.RoundToInt(pt.availability * 100f)}%, Качество: {Mathf.RoundToInt(pt.quality * 100f)}%).");
        report.findings.Add($"Выпуск: {pt.completedUnits} готовых изделий с момента старта. Суточный факт: {pt.fact} из {pt.plan} план.");
        report.findings.Add($"Простой: {pt.downtimeMin} мин, поломок за смену: {pt.breakdowns}.");

        // Узлы
        foreach (var u in pt.units)
        {
            if (u.health < 0.8f || u.hoursToService < 48 || u.temp > 48f)
            {
                report.findings.Add($"Узел {u.name}: ресурс {Mathf.RoundToInt(u.health * 100f)}%, t={u.temp:F1}°C, до ТО {u.hoursToService} ч.");
                report.recommendations.Add($"Запланировать ревизию узла {u.name} (текущая температура: {u.temp:F0}°C).");
            }
        }

        if (report.recommendations.Count == 0)
        {
            report.recommendations.Add("Параметры ячейки соответствуют технологической карте. Сохранять текущий темп.");
            report.recommendations.Add("Проверить запас расходных материалов (" + pt.consumableName + ") на следующую смену.");
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"🤖 **ИНСПЕКЦИЯ ПОСТА: {pt.title.ToUpper()} ({pt.code})**");
        sb.AppendLine($"Статус: {report.severityText} | Текущий OEE: {Mathf.RoundToInt(pt.oee * 100f)}%");
        sb.AppendLine($"Текущая операция: {pt.phaseName} ({pt.liveDetail})");
        sb.AppendLine();
        sb.AppendLine("🔍 **АНАЛИЗ ПАРАМЕТРОВ:**");
        foreach (var f in report.findings) sb.AppendLine("• " + f);
        sb.AppendLine();
        sb.AppendLine("💡 **РЕКОМЕНДАЦИИ ИНСПЕКТОРА:**");
        for (int i = 0; i < report.recommendations.Count; i++) sb.AppendLine($"{i + 1}. {report.recommendations[i]}");

        report.mainAnalysis = sb.ToString();
        return report;
    }

    /// <summary>
    /// Генерирует глубокий отчет по узким местам (Bottleneck) и такту линии.
    /// </summary>
    public static BottleneckReport GenerateBottleneckReport(FactoryTelemetryCollector.FactorySnapshot snap, string openAiRawText = null, string model = null)
    {
        BottleneckReport report = new BottleneckReport
        {
            timestamp = DateTime.Now,
            sourceName = !string.IsNullOrEmpty(openAiRawText) ? $"OpenAI Cloud ({model ?? "gpt-4o-mini"})" : "Экспертный ИИ-Инспектор Allur",
            rawAiAnalysis = openAiRawText
        };

        if (snap == null) return report;

        // Поиск процесса с наихудшими показателями
        FactoryTelemetryCollector.ProcessTelemetry worstProc = null;
        float minOee = 2f;
        foreach (var p in snap.processes)
        {
            if (p.plan > 0 && p.oee < minOee)
            {
                minOee = p.oee;
                worstProc = p;
            }
        }

        if (worstProc == null && snap.processes.Count > 0)
        {
            worstProc = snap.processes[0];
        }

        if (worstProc != null)
        {
            report.bottleneckStation = worstProc.title;
            report.bottleneckCode = worstProc.code;
            report.stationOee = worstProc.oee;
            report.downtimeMin = worstProc.downtimeMin;
            report.completedUnits = worstProc.completedUnits;
            report.plan = worstProc.plan;
            report.fact = worstProc.fact;
        }
        else
        {
            report.bottleneckStation = !string.IsNullOrEmpty(snap.bottleneckProcess) ? snap.bottleneckProcess : "Линия сборки колес";
            report.bottleneckCode = "WHEEL-01";
            report.stationOee = 0.72f;
            report.downtimeMin = 35;
            report.plan = 22;
            report.fact = 14;
        }

        report.taktTargetSec = 45f;
        report.cycleTimeSec = Mathf.Round(45f + (1f - report.stationOee) * 32f);
        report.taktLagSec = Mathf.Max(0f, report.cycleTimeSec - report.taktTargetSec);
        // недовыпуск участка за сутки: доля плана, потерянная из-за медленного цикла, плюс машины, не сделанные за время простоя
        report.lostCarsEstimate = Mathf.Max(1, Mathf.RoundToInt(report.plan * report.taktLagSec / Mathf.Max(1f, report.cycleTimeSec) + report.downtimeMin * 60f / Mathf.Max(1f, report.cycleTimeSec)));

        report.bufferUpstream = "⚠️ Буфер ДО узла: ПЕРЕПОЛНЕН (3/3 кузова). Риск аварийного останова предшествующей сварки кузовов.";
        report.bufferDownstream = "⚠️ Буфер ПОСЛЕ узла: ГОЛОДАНИЕ (0/3 кузова). Линия сход-развала и сдачи простаивает в ожидании.";

        report.rootCauses.Add($"🔴 Пневматика/Механика: Падение давления в цеховой магистрали затяжки до 5.1 бар (норма 6.2 бар), увеличение времени зажима.");
        report.rootCauses.Add($"🟡 Кинематика узла: Температурный дрейф привода подачи (t = 53°C), задержка позиционирования на +{report.taktLagSec:F0} сек.");
        report.rootCauses.Add($"🟡 Внутрицеховая логистика: Неритмичная доставка крепежных метизов и ступичных гаек со склада операторами Just-in-Time.");

        report.actionSteps.Add($"1. Дежурному механику [СГМ]: Отрегулировать входной редуктор пневмолинии поста «{report.bottleneckStation}» до 6.2–6.4 бар.");
        report.actionSteps.Add($"2. Наладчику [РТК]: Проверить концевые датчики каретки и скорректировать ускорение сервопривода (снижение времени цикла на ~5 сек).");
        report.actionSteps.Add($"3. Логистической службе: Сформировать оперативный буфер крепежа на 10 циклов непосредственно в зоне оператора поста.");
        report.actionSteps.Add($"4. Сменному мастеру: Перераспределить операцию предварительной наживки на вспомогательный пост для выравнивания такта.");

        if (string.IsNullOrEmpty(report.rawAiAnalysis))
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"### 🔍 ЭКСПЕРТНЫЙ АНАЛИЗ УЗКОГО МЕСТА: {report.bottleneckStation.ToUpper()} ({report.bottleneckCode})");
            sb.AppendLine($"• Лимитирующий фактор цеха: Текущий OEE участка составляет **{Mathf.RoundToInt(report.stationOee * 100f)}%**, время простоя за смену: **{report.downtimeMin} мин**.");
            sb.AppendLine($"• Рассинхронизация такта: Фактический цикл **{report.cycleTimeSec:F0} сек** превышает целевой такт линии (**{report.taktTargetSec:F0} сек**) на **+{report.taktLagSec:F0} сек** на каждое авто.");
            sb.AppendLine($"• Оценка потерь сменного выпуска: Недополучено порядка **{report.lostCarsEstimate} готовых автомобилей** из-за рассинхронизации.");
            sb.AppendLine();
            sb.AppendLine("### ⚠️ ВЛИЯНИЕ НА ТЕХНОЛОГИЧЕСКИЙ ПОТОК");
            sb.AppendLine("Задержка на данном посте блокирует конвейер Allur, вызывая затор на входе и технологическое голодание на финишных линиях сход-развала и сдачи ОТК.");
            report.rawAiAnalysis = sb.ToString();
        }

        return report;
    }

    /// <summary>
    /// Генерирует детальный график превентивного ТО узлов оборудования цеха.
    /// </summary>
    public static MaintenancePlanReport GenerateMaintenancePlanReport(FactoryTelemetryCollector.FactorySnapshot snap, string openAiRawText = null, string model = null)
    {
        MaintenancePlanReport report = new MaintenancePlanReport
        {
            timestamp = DateTime.Now,
            sourceName = !string.IsNullOrEmpty(openAiRawText) ? $"OpenAI Cloud ({model ?? "gpt-4o-mini"})" : "Экспертный ИИ-Инспектор Allur",
            rawAiAnalysis = openAiRawText
        };

        if (snap == null) return report;

        foreach (var p in snap.processes)
        {
            foreach (var u in p.units)
            {
                MaintenanceTask task = new MaintenanceTask
                {
                    stationName = p.title,
                    unitName = u.name,
                    operatingHours = u.hours,
                    hoursToService = u.hoursToService,
                    temperature = u.temp,
                    health = u.health
                };

                // Определение срочности
                if (u.hoursToService < 24 || u.health < 0.65f || u.temp > 55f)
                {
                    task.urgencyText = "🔴 СРОЧНО (<24ч)";
                    task.urgencyColor = new Color(0.94f, 0.28f, 0.28f);
                    report.criticalCount++;
                }
                else if (u.hoursToService < 72 || u.health < 0.80f || u.temp > 48f)
                {
                    task.urgencyText = "🟡 ВНИМАНИЕ (<72ч)";
                    task.urgencyColor = new Color(0.96f, 0.72f, 0.15f);
                    report.warningCount++;
                }
                else
                {
                    task.urgencyText = "🟢 ПЛАНОВО";
                    task.urgencyColor = new Color(0.18f, 0.8f, 0.45f);
                    report.scheduledCount++;
                }

                // Определение службы
                string nLow = u.name.ToLowerInvariant();
                if (nLow.Contains("робот") || nLow.Contains("серво") || nLow.Contains("манипулятор") || nLow.Contains("захват") || nLow.Contains("kuka"))
                {
                    task.department = "[РТК] Робототехники";
                    task.procedure = "Вибродиагностика привода, юстировка энкодера, проверка кабельного шлейфа, контроль температуры.";
                    task.partsAndConsumables = "Кабельный гибкий шлейф Igus, высокотемпературная смазка Mobilith SHC 220.";
                }
                else if (nLow.Contains("редуктор") || nLow.Contains("транспортер") || nLow.Contains("каретка") || nLow.Contains("шпиндель") || nLow.Contains("пневм"))
                {
                    task.department = "[СГМ] Механики";
                    task.procedure = "Проверка люфтов зубчатых зацеплений, слив/замена редукторного масла, замена уплотнительных манжет.";
                    task.partsAndConsumables = "Масло редукторное Shell Omala S4 WE 320 (3 л), комплект сальников 45х65.";
                }
                else
                {
                    task.department = "[ЭТЛ] Электрики";
                    task.procedure = "Протяжка клеммников силового шкафа, продувка воздушных фильтров теплообменника ПЧ, калибровка датчиков.";
                    task.partsAndConsumables = "Фильтрующие маты Rittal, термопаста КПТ-19, предохранители 25A.";
                }

                report.tasks.Add(task);
            }
        }

        // Сортировка: сначала самые критические по остатку до ТО
        report.tasks.Sort((a, b) => a.hoursToService.CompareTo(b.hoursToService));

        report.sparePartsSummary.Add("• Редукторное синтетическое масло Shell Omala S4 WE 320 — 10 л (для приводов и конвейеров).");
        report.sparePartsSummary.Add("• Высокотемпературная литиевая пластичная смазка Mobilith SHC 220 — 4 картриджа.");
        report.sparePartsSummary.Add("• Комплект уплотнений пневмоцилиндров Festo DNC-63 — 2 ремкомплекта.");
        report.sparePartsSummary.Add("• Индуктивные датчики приближения Pepperl+Fuchs M12 — 3 шт.");

        if (string.IsNullOrEmpty(report.rawAiAnalysis))
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("### 🛠️ РЕГЛАМЕНТ ПРОВЕДЕНИЯ ПРЕВЕНТИВНОГО ТО СЛУЖБАМИ ЦЕХА");
            sb.AppendLine($"• Состояние парка оборудования: критических узлов — **{report.criticalCount}**, предупредительных — **{report.warningCount}**, штатных — **{report.scheduledCount}**.");
            sb.AppendLine("• **Технологическое окно:** проведение работ запланировано в межсменный перерыв без остановки основного конвейера цеха.");
            sb.AppendLine("• **Протокол безопасности:** обязательное отключение силовых фидеров по стандарту LOTO (Lockout/Tagout) перед ревизией редукторов.");
            sb.AppendLine("• **Приемка ОТК:** после замены смазки и калибровки обязателен тестовый прогон узла на холостом ходу в течение 10 минут с тепловизионным контролем.");
            report.rawAiAnalysis = sb.ToString();
        }

        return report;
    }

    /// <summary>
    /// Генерирует прямой детальный ответ на любой вопрос оператора по телеметрии завода.
    /// </summary>
    public static string GenerateDirectChatAnswer(string query, FactoryTelemetryCollector.FactorySnapshot snap)
    {
        if (string.IsNullOrWhiteSpace(query)) return "Пожалуйста, введите ваш вопрос.";
        string q = query.ToLowerInvariant();

        if (q.Contains("oee") || q.Contains("эффективн") || q.Contains("производительн"))
        {
            return $"📊 **Текущий OEE автозавода Allur: {Mathf.RoundToInt(snap.averageOee * 100f)}%** (целевой норматив ≥ 85%).\n\n" +
                   $"Основной резерв роста эффективности (+5–7% к OEE) кроется в устранении микропростоев на участке «{snap.bottleneckProcess}». " +
                   $"Суммарный простой оборудования за смену уже достиг {snap.totalDowntimeMin} мин.\n\n" +
                   $"💡 **Рекомендации инспектора:**\n" +
                   $"1. Выровнять межоперационный буфер накопителя до 3 кузовов;\n" +
                   $"2. Провести внеочередную ревизию пневматических зажимов;\n" +
                   $"3. Исключить задержки подачи метизов и колес со склада Just-in-Time.";
        }

        if (q.Contains("узк") || q.Contains("бутылочн") || q.Contains("bottleneck") || q.Contains("проблем") || q.Contains("затор"))
        {
            return $"🔍 **Главное узкое место цеха — участок «{snap.bottleneckProcess}».**\n\n" +
                   $"На этом посту фиксируется наибольшая задержка такта по сравнению с остальными операциями завода. " +
                   $"Из-за этого входной накопитель перед участком переполнен (3/3 кузова), а последующие посты испытывают голодание деталей.\n\n" +
                   $"👉 *Для детального ознакомления с первопричинами и пошаговым планом перейдите во вкладку «🔍 Узкие места».*";
        }

        if (q.Contains("то") || q.Contains("обслуживан") || q.Contains("ремонт") || q.Contains("сгм") || q.Contains("механик") || q.Contains("износ"))
        {
            return $"🛠️ **План превентивного техобслуживания узлов:**\n\n" +
                   $"По данным телеметрии, ряд сервоприводов и редукторов имеют остаток ресурса до ТО менее 48 моточасов. " +
                   $"Службе главного механика [СГМ] и наладчикам [РТК] рекомендовано провести вибродиагностику и доливку редукторного масла в ближайший технологический перерыв смены.\n\n" +
                   $"👉 *Полный интерактивный график и список узлов доступен во вкладке «🛠️ План ТО узлов».*";
        }

        if (q.Contains("брак") || q.Contains("качеств") || q.Contains("дефект") || q.Contains("отк"))
        {
            return $"🛡️ **Контроль качества выпуска:**\n\n" +
                   $"За текущую смену зафиксировано {snap.totalDefects} единиц с отклонениями по качеству при общем выпуске {snap.totalFact} авто.\n\n" +
                   $"💡 **Ключевые зоны внимания:**\n" +
                   $"• Герметизация краевого шва стекол (проверить давление в дозаторе праймера и вязкость герметика);\n" +
                   $"• Соосность установки колес на лазерном стенде сход-развала Wheel Aligner.";
        }

        if (q.Contains("робот") || q.Contains("температур") || q.Contains("нагрев") || q.Contains("перегрев"))
        {
            return $"🌡️ **Термический режим робототехники:**\n\n" +
                   $"В роботизированной ячейке сварки RobotCell приводы манипуляторов работают в плотном цикле с температурой до 54-58°C. " +
                   $"Критический порог аварийного останова — 65°C.\n\n" +
                   $"💡 Рекомендуется оптимизировать кривые разгона/торможения роботов KUKA/Fanuc, что снизит пиковые нагрузки на двигатели и понизит температуру на 10-12%.";
        }

        if (q.Contains("план") || q.Contains("выпуск") || q.Contains("смен") || q.Contains("авто"))
        {
            return $"📦 **Выполнение сменного задания:**\n\n" +
                   $"Собрано **{snap.totalFact} из {snap.totalPlan}** автомобилей (прогресс текущей смены: {Mathf.RoundToInt(snap.shiftProgress * 100f)}%). " +
                   $"Индекс надежности цеха: **{snap.factoryHealthScore:F1}%**.\n\n" +
                   $"При оперативной стабилизации такта на узком месте сменный план завода Allur будет успешно выполнен.";
        }

        return $"🤖 **Ответ Инспектора Allur на запрос:** «{query}»\n\n" +
               $"Текущее состояние производства: завод функционирует в штатно-напряженном режиме (индекс здоровья {snap.factoryHealthScore:F0}%). " +
               $"Собрано {snap.totalFact} из {snap.totalPlan} авто. Главный фокус внимания инженеров — стабилизация такта на участке «{snap.bottleneckProcess}» и превентивный контроль ресурса узлов до ТО.";
    }

    private static string GenerateCustomQueryAnswer(string query, FactoryTelemetryCollector.FactorySnapshot snap)
    {
        return GenerateDirectChatAnswer(query, snap);
    }
}

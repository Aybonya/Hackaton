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

    private static string GenerateCustomQueryAnswer(string query, FactoryTelemetryCollector.FactorySnapshot snap)
    {
        string q = query.ToLowerInvariant();
        if (q.Contains("oee") || q.Contains("эффективн"))
        {
            return $"Для повышения среднего OEE (текущий {Mathf.RoundToInt(snap.averageOee * 100f)}%) критически важно сократить простои на участке «{snap.bottleneckProcess}». Рекомендуется переход с реактивного ремонта на виброакустический мониторинг подшипников.";
        }
        if (q.Contains("узк") || q.Contains("проблем") || q.Contains("бутылочн"))
        {
            return $"Главным узким местом цеха в данный момент является {snap.bottleneckProcess}. Задержки на этом участке сдерживают общий такт выпуска остальных постов.";
        }
        if (q.Contains("то") || q.Contains("ремонт") || q.Contains("обслуживан"))
        {
            return "Анализ наработки указывает на необходимость превентивного сервиса для узлов с остатком менее 48 часов на постах сварки и конвейерных транспортерах.";
        }
        if (q.Contains("брак") || q.Contains("качеств") || q.Contains("дефект"))
        {
            return $"Уровень брака зафиксирован на отметке {snap.totalDefects} единиц. Рекомендуется калибровка сварочных электродов и проверка вязкости праймера для стекла.";
        }

        return $"По вашему запросу «{query}»: производственная линия функционирует стабильно (индекс {snap.factoryHealthScore:F0}%). Рекомендуется следовать регламенту сменного задания.";
    }
}

using UnityEngine;

// Пост установки лобового стекла (модель WindshieldStation_Animated.fbx).
// Вешается на корень модели. Цикл анимации 12 с = одно стекло, установленное в кузов.
public class WindshieldStation : AnimatedProcess
{
    // стадии цикла по таймингу анимации WindshieldInstall_Loop (секунды от начала цикла)
    static readonly float[] STARTS = { 0f, 1.47f, 2.63f, 5.07f, 6.5f, 7.2f, 10.8f };
    static readonly string[] PHASES =
    {
        "Подача кузова",
        "Захват стекла",
        "Перенос и разворот стекла",
        "Установка в проём",
        "Прижим стекла",
        "Возврат робота",
        "Ожидание следующего кузова"
    };

    protected override float[] PhaseStarts => STARTS;

    public override string Code => "ПОСТ G-3";
    public override string Title => "Установка лобового стекла";
    public override string UnitName => "кузов";
    public override string UnitNamePlural => "кузовов";
    public override string[] PhaseNames => PHASES;

    // 12 с на кузов → максимум 4800 за две смены
    public override int BasePlanPerDay => 4100;
    public override string[] Equipment => new[] { "Робот", "Вакуумный захват", "Конвейер кузовов", "Подача стёкол" };
    public override string[] Faults => new[]
    {
        "Потеря вакуума в захвате", "Скол на кромке стекла", "Смещение кузова на конвейере",
        "Сбой сервопривода оси 4", "Толщина клеевого валика вне допуска", "Датчик наличия стекла не видит деталь",
        "Не найден проём (ошибка позиционирования)", "Заклинивание конвейера кузовов", "Срабатывание ограждения",
        "Калибровка TCP после касания"
    };
    public override Vector2 EnergyPerUnit => new Vector2(1.1f, 1.5f);
    public override string ConsumableName => "Клей для стёкол";
    public override string ConsumableUnit => "кг";
    public override Vector2 ConsumablePerUnit => new Vector2(0.38f, 0.45f);
}

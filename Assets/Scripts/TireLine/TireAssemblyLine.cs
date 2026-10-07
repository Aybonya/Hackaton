using UnityEngine;

// Линия сборки колёс (модель TireAssemblyLine_Animated.fbx).
// Вешается на корень модели. Цикл анимации 8 с = одно колесо.
public class TireAssemblyLine : AnimatedProcess
{
    // стадии цикла по таймингу анимации WheelAssembly_Loop (секунды от начала цикла)
    static readonly float[] STARTS = { 0f, 1.73f, 2.9f, 4.97f, 6.57f };
    static readonly string[] PHASES =
    {
        "Подача диска на паллете",
        "Смазка борта и захват шины",
        "Перенос шины роботом",
        "Запрессовка шины на диск",
        "Возврат робота"
    };

    protected override float[] PhaseStarts => STARTS;

    public override string Code => "ЛИНИЯ W-2";
    public override string Title => "Сборка колёс";
    public override string UnitName => "колесо";
    public override string UnitNamePlural => "колёс";
    public override string[] PhaseNames => PHASES;

    // 8 с на колесо → максимум 7200 за две смены; план с запасом на переналадку и обслуживание
    public override int BasePlanPerDay => 6200;
    public override string[] Equipment => new[] { "Робот", "Пресс", "Щётка смазки", "Конвейер паллет", "Подача шин" };
    public override string[] Faults => new[]
    {
        "Не сработал захват шины", "Износ пальцев захвата", "Пресс: давление ниже нормы",
        "Перекос шины при запрессовке", "Закончилась монтажная паста", "Застревание паллеты",
        "Датчик наличия диска не видит деталь", "Сбой сервопривода робота", "Обрыв ремня подачи шин",
        "Срабатывание ограждения"
    };
    public override Vector2 EnergyPerUnit => new Vector2(0.35f, 0.45f);
    public override string ConsumableName => "Монтажная паста";
    public override string ConsumableUnit => "кг";
    public override Vector2 ConsumablePerUnit => new Vector2(0.008f, 0.011f);
}

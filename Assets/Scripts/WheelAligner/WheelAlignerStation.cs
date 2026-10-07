using UnityEngine;

// Пост регулировки развала-схождения с оператором (модель WheelAligner_Operator_Animated.fbx).
// Вешается на корень модели. Анимация 16 с показывает операцию ускоренно,
// поэтому план и средний цикл в статистике — как у реального поста (минуты на автомобиль).
public class WheelAlignerStation : AnimatedProcess
{
    // стадии цикла по таймингу анимации Loop (секунды от начала цикла)
    static readonly float[] STARTS = { 0f, 4f, 7.5f, 11f, 14f };
    static readonly string[] PHASES =
    {
        "Замер углов установки колёс",
        "Оператор идёт к автомобилю",
        "Регулировка схождения",
        "Возврат оператора к стойке",
        "Контрольный замер и протокол"
    };

    protected override float[] PhaseStarts => STARTS;

    public override string Code => "ПОСТ K-6";
    public override string Title => "Развал-схождение";
    public override string UnitName => "автомобиль";
    public override string UnitNamePlural => "автомобилей";
    public override string[] PhaseNames => PHASES;

    public override int BasePlanPerDay => 380;
    public override string[] Equipment => new[] { "Камера L", "Камера R", "Захваты колёс", "Каретка подъёма", "Консоль" };
    public override string[] Faults => new[]
    {
        "Мишень захвата не распознана камерой", "Загрязнение объектива камеры", "Сбой калибровки стенда",
        "Захват соскользнул с обода", "Каретка не поднялась в позицию", "Зависание ПО консоли",
        "Схождение вне допуска — повторная регулировка", "Потеря связи с камерой", "Ошибка считывания VIN",
        "Разряд аккумулятора захвата"
    };
    public override Vector2 EnergyPerUnit => new Vector2(0.05f, 0.08f);
    public override string ConsumableName => "Работа оператора";
    public override string ConsumableUnit => "ч";
    public override Vector2 ConsumablePerUnit => new Vector2(0.03f, 0.045f);
}

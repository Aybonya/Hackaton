using UnityEngine;

// Ячейка роботизированной дуговой сварки (модель robotic_workcell_animated.fbx).
// Вешается на корень модели. Цикл анимации 24 с = одна деталь: три шва, между ними поворот стола.
public class WeldingWorkcell : AnimatedProcess
{
    // стадии цикла по таймингу анимации WorkcellLoop (секунды от начала цикла)
    static readonly float[] STARTS = { 0f, 3f, 6f, 7f, 9f, 13f, 14f, 16f, 19f, 20f, 22f };
    static readonly string[] PHASES =
    {
        "Подвод горелки к шву",
        "Сварка шва 1",
        "Отвод горелки",
        "Поворот стола",
        "Сварка шва 2",
        "Отвод горелки",
        "Поворот стола",
        "Сварка шва 3",
        "Отвод горелки",
        "Поворот стола: смена детали",
        "Возврат робота в исходное"
    };

    protected override float[] PhaseStarts => STARTS;

    public override string Code => "ЯЧЕЙКА A-5";
    public override string Title => "Дуговая сварка узлов";
    public override string UnitName => "деталь";
    public override string UnitNamePlural => "деталей";
    public override string[] PhaseNames => PHASES;

    // сварка — оранжевая, повороты стола — синие, остальное — по общей палитре
    public override Color PhaseColor
    {
        get
        {
            int p = Phase;
            if (p == 1 || p == 4 || p == 7) return new Color(1f, 0.55f, 0.15f);
            if (p == 3 || p == 6 || p == 9) return new Color(0.4f, 0.65f, 1f);
            return base.PhaseColor;
        }
    }

    // 24 с на деталь → максимум 2400 за две смены
    public override int BasePlanPerDay => 2050;
    public override string[] Equipment => new[] { "Робот", "Сварочная горелка", "Поворотный стол", "Источник тока", "Подача проволоки" };
    public override string[] Faults => new[]
    {
        "Залипание проволоки в наконечнике", "Износ контактного наконечника", "Нет защитного газа",
        "Брызги налипли в сопле горелки", "Стол не дошёл до позиции", "Сбой источника сварочного тока",
        "Не найден шов (поиск касанием)", "Обрыв подачи проволоки", "Срабатывание ограждения",
        "Калибровка TCP после касания"
    };
    public override Vector2 EnergyPerUnit => new Vector2(0.6f, 0.9f);
    public override string ConsumableName => "Сварочная проволока";
    public override string ConsumableUnit => "кг";
    public override Vector2 ConsumablePerUnit => new Vector2(0.12f, 0.16f);
}

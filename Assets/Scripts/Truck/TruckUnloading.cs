using UnityEngine;

// Приезд грузовика Allur и разгрузка (модель Allur_Truck_Unloading.glb, импорт через glTFast).
// Вешается на корень модели. Цикл анимации ~27 с = один грузовик: въезд, двери, двое грузчиков выносят коробки.
public class TruckUnloading : AnimatedProcess
{
    // стадии цикла по таймингу анимации Arrive_And_Unload (секунды от начала цикла)
    static readonly float[] STARTS = { 0f, 4f, 5.2f, 7f, 11.8f, 13.7f, 26.2f };
    static readonly string[] PHASES =
    {
        "Въезд грузовика",
        "Парковка у ворот",
        "Открытие дверей кузова",
        "Подход грузчиков",
        "Взятие коробок",
        "Вынос коробок на склад",
        "Разгрузка завершена"
    };

    protected override float[] PhaseStarts => STARTS;

    public override string Code => "РАМПА T-1";
    public override string Title => "Приёмка грузовика Allur";
    public override string UnitName => "грузовик";
    public override string UnitNamePlural => "грузовиков";
    public override string[] PhaseNames => PHASES;

    // ~27 с на машину в анимации; в жизни рампа принимает десятки машин за две смены
    public override int BasePlanPerDay => 60;
    public override string[] Equipment => new[] { "Рампа", "Ворота склада", "Тележка", "Сканер накладных" };
    public override string[] Faults => new[]
    {
        "Грузовик опоздал к окну приёмки", "Не открывается дверь кузова", "Повреждённая упаковка",
        "Расхождение с накладной", "Сканер не читает штрихкод", "Рампа занята другой машиной",
        "Заклинило ворота склада", "Нет свободного грузчика"
    };
    public override Vector2 EnergyPerUnit => new Vector2(0.2f, 0.4f);
    public override string ConsumableName => "Стрейч-плёнка";
    public override string ConsumableUnit => "м";
    public override Vector2 ConsumablePerUnit => new Vector2(18f, 25f);

    // грузовик въезжает издалека, поэтому рамку берём по всему пути анимации, а не по позе в покое
    // (в единицах модели: путь машины по X, грузчики уносят коробки вбок по Z)
    static readonly Bounds PathBounds = new Bounds(new Vector3(-2.6f, 0.7f, -0.85f), new Vector3(7.2f, 1.4f, 2.5f));
    public override Bounds LocalBounds => PathBounds;
}

using UnityEngine;

// Общая основа для любого производственного процесса в цехе (ячейка сварки, линия колёс и т. д.).
// Через неё AllurExperience находит процесс по клику, подлетает к нему камерой,
// а CellSidebar показывает живое состояние и синтетическую статистику.
public abstract class FactoryProcess : MonoBehaviour
{
    // ---------- что это за процесс ----------
    public abstract string Code { get; }          // "ЯЧЕЙКА R-4"
    public abstract string Title { get; }         // "Сварка и окраска кузова"
    public abstract string UnitName { get; }      // "кузов"
    public abstract string UnitNamePlural { get; } // "кузовов"

    // ---------- живое состояние ----------
    public abstract string[] PhaseNames { get; }
    public abstract int Phase { get; }
    public abstract int CompletedUnits { get; }   // сколько изделий выпущено с момента запуска
    public abstract string LiveDetail { get; }    // строка под названием стадии
    public string PhaseName => PhaseNames[Mathf.Clamp(Phase, 0, PhaseNames.Length - 1)];
    public virtual Color PhaseColor => new Color(0.4f, 0.65f, 1f);

    // необязательный цветной квадратик в блоке "Сейчас" (например, цвет краски)
    public virtual bool HasSwatch => false;
    public virtual Color SwatchColor => Color.white;
    public virtual string SwatchName => "";

    // ---------- для синтетической статистики ----------
    public abstract int BasePlanPerDay { get; }
    public abstract string[] Equipment { get; }   // узлы, которые могут ломаться
    public abstract string[] Faults { get; }
    public virtual Vector2 EnergyPerUnit => new Vector2(9f, 10.5f);      // кВт·ч на изделие, от и до
    public virtual string ConsumableName => "Краска";
    public virtual string ConsumableUnit => "л";
    public virtual Vector2 ConsumablePerUnit => new Vector2(3f, 3.4f);

    // габариты в локальных координатах объекта: по ним кликают и под них подбирается камера
    public abstract Bounds LocalBounds { get; }

    // рамка в окне Scene, чтобы было видно, где стоит процесс
    protected virtual void OnDrawGizmos()
    {
        Bounds b = LocalBounds;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.94f, 0.7f, 0.14f, 1f);
        Gizmos.DrawWireCube(b.center, b.size);
        Gizmos.color = new Color(0.94f, 0.7f, 0.14f, 0.15f);
        Gizmos.DrawCube(new Vector3(b.center.x, b.min.y + 0.01f, b.center.z), new Vector3(b.size.x, 0.02f, b.size.z));
    }
}

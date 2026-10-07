using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Процессы из анимированных FBX: настройка импорта и пункты меню, которые ставят их в цех.
// Чтобы подключить новый FBX-процесс: положи модель в Assets/Models, добавь её путь в Models
// и сделай пункт меню по образцу ниже со своим компонентом (наследник AnimatedProcess).
public class ProcessModelSetup : AssetPostprocessor
{
    const string TireLinePath = "Assets/Models/TireAssemblyLine_Animated.fbx";
    const string WindshieldPath = "Assets/Models/WindshieldStation_Animated.fbx";
    const string WorkcellPath = "Assets/Models/robotic_workcell_animated.fbx";
    const string AlignerPath = "Assets/Models/WheelAligner_Operator_Animated.fbx";

    const string WorkerPath = "Assets/Models/Technician_Walk_Carry_Box.fbx";

    static readonly string[] Models = { TireLinePath, WindshieldPath, WorkcellPath, AlignerPath, WorkerPath };

    // в этих файлах размеры уже в метрах, хотя единицей записан сантиметр:
    // пересчёт единиц выключаем, иначе модель станет в 100 раз меньше
    static readonly string[] MeterModels = { WorkcellPath };

    // анимация в этих FBX — движение отдельных деталей, поэтому импортируем как Legacy:
    // на модель сам добавится компонент Animation, и его легко зациклить без Animator Controller
    void OnPreprocessModel()
    {
        if (System.Array.IndexOf(Models, assetPath) < 0) return;
        Configure((ModelImporter)assetImporter, assetPath);
    }

    static void Configure(ModelImporter mi, string path)
    {
        mi.animationType = ModelImporterAnimationType.Legacy;
        mi.importAnimation = true;
        if (System.Array.IndexOf(MeterModels, path) >= 0)
        {
            mi.useFileScale = false;
            mi.globalScale = 1f;
        }
    }

    static bool IsConfigured(ModelImporter mi, string path)
    {
        if (mi.animationType != ModelImporterAnimationType.Legacy) return false;
        if (System.Array.IndexOf(MeterModels, path) >= 0 && mi.useFileScale) return false;
        return true;
    }

    // одним нажатием: по одному каждому процессу, которого ещё нет в сцене, и рабочие
    [MenuItem("Allur/Расставить все процессы и рабочих", priority = 0)]
    public static void AddEverything()
    {
        if (Object.FindAnyObjectByType<RobotCell>() == null) RobotCellSetup.AddCellToOpenScene();
        if (Object.FindAnyObjectByType<TireAssemblyLine>() == null) AddTireLine();
        if (Object.FindAnyObjectByType<WindshieldStation>() == null) AddWindshieldStation();
        if (Object.FindAnyObjectByType<WeldingWorkcell>() == null) AddWeldingWorkcell();
        if (Object.FindAnyObjectByType<WheelAlignerStation>() == null) AddWheelAligner();
        if (Object.FindAnyObjectByType<WorkerCrowd>() == null) AddWorkers();
        Debug.Log("ProcessModelSetup: все процессы и рабочие расставлены. Сохрани сцену (Ctrl+S) и нажми Play.");
    }

    [MenuItem("Allur/Добавить рабочих с коробками")]
    public static void AddWorkers()
    {
        WorkerCrowd existing = Object.FindAnyObjectByType<WorkerCrowd>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("ProcessModelSetup: рабочие уже есть в сцене (" + existing.name + "). Количество меняется в Min/Max Count.");
            return;
        }

        ModelImporter mi = AssetImporter.GetAtPath(WorkerPath) as ModelImporter;
        if (mi == null)
        {
            Debug.LogError("ProcessModelSetup: не найдена модель " + WorkerPath);
            return;
        }
        if (!IsConfigured(mi, WorkerPath))
        {
            Configure(mi, WorkerPath);
            mi.SaveAndReimport();
        }

        GameObject go = new GameObject("Workers");
        WorkerCrowd crowd = go.AddComponent<WorkerCrowd>();
        crowd.workerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerPath);

        Undo.RegisterCreatedObjectUndo(go, "Добавить рабочих");
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log("ProcessModelSetup: добавлены рабочие (5–10 человек появятся в Play). Сохрани сцену (Ctrl+S).");
    }

    [MenuItem("Allur/Добавить пост развал-схождения в цех")]
    public static void AddWheelAligner()
    {
        AddModelProcess<WheelAlignerStation>(AlignerPath, "Aligner_");
    }

    [MenuItem("Allur/Добавить ячейку дуговой сварки в цех")]
    public static void AddWeldingWorkcell()
    {
        AddModelProcess<WeldingWorkcell>(WorkcellPath, "Workcell_");
    }

    [MenuItem("Allur/Добавить линию сборки колёс в цех")]
    public static void AddTireLine()
    {
        AddModelProcess<TireAssemblyLine>(TireLinePath, "TireLine_");
    }

    [MenuItem("Allur/Добавить пост установки стекла в цех")]
    public static void AddWindshieldStation()
    {
        AddModelProcess<WindshieldStation>(WindshieldPath, "Windshield_");
    }

    static void AddModelProcess<T>(string modelPath, string namePrefix) where T : AnimatedProcess
    {
        // если модель уже успела импортироваться с другими настройками — переимпортировать
        ModelImporter mi = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (mi == null)
        {
            Debug.LogError("ProcessModelSetup: не найдена модель " + modelPath);
            return;
        }
        if (!IsConfigured(mi, modelPath))
        {
            Configure(mi, modelPath);
            mi.SaveAndReimport();
        }

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        int count = Object.FindObjectsByType<T>().Length;
        go.name = namePrefix + (count + 1);
        go.transform.position = RobotCellSetup.FreeSlot();

        T process = go.AddComponent<T>();

        // поставить модель ровно на пол цеха (y = 0), по центру места
        Bounds b = process.LocalBounds;
        Vector3 bottomCenter = go.transform.TransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
        go.transform.position += go.transform.position - bottomCenter;

        Undo.RegisterCreatedObjectUndo(go, "Добавить " + go.name);
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        SceneView.FrameLastActiveSceneView();
        Debug.Log("ProcessModelSetup: " + go.name + " поставлен в " + go.transform.position + ". Сохрани сцену (Ctrl+S).");
    }
}

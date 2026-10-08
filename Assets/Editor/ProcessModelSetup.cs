using UnityEditor;
using UnityEditor.AssetImporters;
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

    // GLB импортирует пакет glTFast (не ModelImporter): анимации у него сразу Legacy
    const string TruckPath = "Assets/Models/Allur_Truck_Unloading.glb";
    // модель грузовика сделана в уменьшенном масштабе (грузчик ~0.36 ед.), увеличиваем до роста ~1.6 м
    const float TruckScale = 4.5f;

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

    // при смене версии Unity сам переимпортирует модели, которые проходят через этот скрипт
    public override uint GetVersion() => 2;

    // после стандартной сборки материалов, чтобы дополнять, а не перезаписывать
    public override int GetPostprocessOrder() => 100;

    // Текстуры вшиты в FBX, но ссылаются на папку *.fbm, которой нет, и Unity оставляет материал белым.
    // Поэтому картинки вынуты из FBX в Assets/Models/Textures/<имя модели>/ и подключаются здесь.
    const string TexturesRoot = "Assets/Models/Textures/";

    // какие из вынутых картинок — карты нормалей (их надо импортировать как Normal Map)
    static readonly string[] NormalMaps =
    {
        TexturesRoot + "WindshieldStation_Animated/Image_2.jpg",
        TexturesRoot + "robotic_workcell_animated/2.jpg"
    };

    void OnPreprocessTexture()
    {
        if (System.Array.IndexOf(NormalMaps, assetPath) < 0) return;
        ((TextureImporter)assetImporter).textureType = TextureImporterType.NormalMap;
    }

    void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
    {
        if (System.Array.IndexOf(Models, assetPath) < 0) return;

        Texture2D baseColor = ExtractedTexture(description, "DiffuseColor");
        if (baseColor != null && material.HasProperty("_MainTex") && material.GetTexture("_MainTex") == null)
        {
            material.mainTexture = baseColor;
            material.color = Color.white;
        }

        Texture2D normal = ExtractedTexture(description, "NormalMap");
        if (normal != null && material.HasProperty("_BumpMap") && material.GetTexture("_BumpMap") == null)
        {
            material.SetTexture("_BumpMap", normal);
            material.EnableKeyword("_NORMALMAP");
        }
    }

    // картинка из папки модели, на которую в FBX ссылается свойство материала (или null)
    Texture2D ExtractedTexture(MaterialDescription description, string property)
    {
        if (!description.TryGetProperty(property, out TexturePropertyDescription tex) || string.IsNullOrEmpty(tex.path)) return null;

        string file = System.IO.Path.GetFileName(tex.path.Replace('\\', '/'));
        if (file.StartsWith("*")) file = file.Substring(1) + ".jpg";   // "*0" — ссылка на вшитую картинку 0.jpg
        string path = TexturesRoot + System.IO.Path.GetFileNameWithoutExtension(assetPath) + "/" + file;
        if (!System.IO.File.Exists(path)) return null;

        context.DependsOnArtifact(path);   // текстура импортируется раньше модели и переимпорт модели при её смене
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
        if (Object.FindAnyObjectByType<TruckUnloading>() == null) AddTruckUnloading();
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

    [MenuItem("Allur/Добавить приезд грузовика Allur")]
    public static void AddTruckUnloading()
    {
        AddModelProcess<TruckUnloading>(TruckPath, "Truck_", TruckScale);
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

    static void AddModelProcess<T>(string modelPath, string namePrefix, float scale = 1f) where T : AnimatedProcess
    {
        // если FBX уже успел импортироваться с другими настройками — переимпортировать
        ModelImporter mi = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (mi != null && !IsConfigured(mi, modelPath))
        {
            Configure(mi, modelPath);
            mi.SaveAndReimport();
        }

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
        {
            Debug.LogError("ProcessModelSetup: не найдена модель " + modelPath +
                           (modelPath.EndsWith(".glb") ? " (для GLB нужен пакет glTFast — Window → Package Manager)" : ""));
            return;
        }
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        int count = Object.FindObjectsByType<T>().Length;
        go.name = namePrefix + (count + 1);
        go.transform.position = RobotCellSetup.FreeSlot();
        go.transform.localScale = Vector3.one * scale;

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

    // что Unity реально импортировал из FBX процессов: клипы, их длина, число анимируемых каналов и ключей
    [MenuItem("Allur/Диагностика анимаций", priority = 100)]
    static void DiagnoseAnimations()
    {
        var sb = new System.Text.StringBuilder("ANIMDIAG\n");
        foreach (string path in Models)
        {
            ModelImporter mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) { sb.AppendLine(path + ": нет модели"); continue; }
            sb.AppendLine(path + "  type=" + mi.animationType + " takes=" + mi.importedTakeInfos.Length);
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(o is AnimationClip c) || c.name.StartsWith("__preview__")) continue;
                EditorCurveBinding[] bs = AnimationUtility.GetCurveBindings(c);
                int keys = 0;
                float lastKey = 0f;
                string sample = "";
                foreach (EditorCurveBinding b in bs)
                {
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(c, b);
                    keys += curve.length;
                    if (curve.length > 0) lastKey = Mathf.Max(lastKey, curve[curve.length - 1].time);
                    if (sample == "" && b.propertyName == "m_LocalRotation.x" && curve.length > 2)
                    {
                        sample = b.path + " rot.x:";
                        for (int i = 0; i <= 12; i++) sample += " " + curve.Evaluate(c.length * i / 12f).ToString("0.00");
                    }
                }
                sb.AppendLine("  clip " + c.name + " len=" + c.length + " каналов=" + bs.Length + " ключей=" + keys + " последний ключ=" + lastKey);
                if (sample != "") sb.AppendLine("    " + sample);
            }
        }
        foreach (AnimatedProcess p in Object.FindObjectsByType<AnimatedProcess>())
        {
            Animation a = p.GetComponentInChildren<Animation>();
            string clips = "";
            if (a != null) foreach (AnimationState s in a) clips += s.name + "(" + s.length + ") ";
            sb.AppendLine("scene " + p.name + ": " + (a == null ? "нет Animation" : "clip=" + (a.clip ? a.clip.name : "null") + " states=" + clips));
        }
        Debug.Log(sb.ToString());
    }

    // во время Play сцену менять нельзя (и добавленное пропало бы после Stop) — пункты меню серые
    [MenuItem("Allur/Расставить все процессы и рабочих", true)]
    [MenuItem("Allur/Добавить рабочих с коробками", true)]
    [MenuItem("Allur/Добавить приезд грузовика Allur", true)]
    [MenuItem("Allur/Добавить пост развал-схождения в цех", true)]
    [MenuItem("Allur/Добавить ячейку дуговой сварки в цех", true)]
    [MenuItem("Allur/Добавить линию сборки колёс в цех", true)]
    [MenuItem("Allur/Добавить пост установки стекла в цех", true)]
    [MenuItem("Allur/Создать сцену RobotCell", true)]
    [MenuItem("Allur/Добавить ячейку RobotCell в цех", true)]
    static bool NotPlaying() => !EditorApplication.isPlaying;
}

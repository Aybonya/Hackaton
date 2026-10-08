using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Creates an editable demonstration layout for the Allur equipment-monitoring case.</summary>
public static class AllurFactoryBuilder
{
    private const string FactoryRootName = "Allur Factory";
    private const string DefaultApiUrl = "http://127.0.0.1:8000/api/equipment";

    private readonly struct StationDefinition
    {
        public readonly string Name;
        public readonly string Id;
        public readonly Vector3 Position;

        public StationDefinition(string name, string id, Vector3 position)
        {
            Name = name;
            Id = id;
            Position = position;
        }
    }

    private static readonly StationDefinition[] Stations =
    {
        new StationDefinition("Сварка кузова", "Machine_1", new Vector3(-6f, 1f, 0f)),
        new StationDefinition("Покрасочная камера", "Machine_2", new Vector3(-2f, 1f, 0f)),
        new StationDefinition("Сборка двигателя", "Machine_3", new Vector3(2f, 1f, 0f)),
        new StationDefinition("Контроль качества", "Machine_4", new Vector3(6f, 1f, 0f))
    };

    [MenuItem("Tools/ALLUR/Создать или обновить цех")]
    public static void BuildFactory()
    {
        var root = GameObject.Find(FactoryRootName);
        if (root == null)
        {
            root = new GameObject(FactoryRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create ALLUR factory");
        }

        ClearChildren(root.transform);
        CreateFloor(root.transform);
        foreach (var station in Stations) CreateStation(root.transform, station);

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("ALLUR factory layout has been created. Configure the API URL on each FactoryMonitor if needed.", root);
    }

    private static void ClearChildren(Transform root)
    {
        for (var i = root.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);
    }

    private static void CreateFloor(Transform parent)
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Main floor";
        floor.transform.SetParent(parent, false);
        floor.transform.localScale = new Vector3(4f, 1f, 4f);
        var renderer = floor.GetComponent<Renderer>();
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetColor("_BaseColor", new Color(0.2f, 0.2f, 0.2f));
        block.SetColor("_Color", new Color(0.2f, 0.2f, 0.2f));
        renderer.SetPropertyBlock(block);
        Undo.RegisterCreatedObjectUndo(floor, "Create factory floor");
    }

    private static void CreateStation(Transform parent, StationDefinition station)
    {
        var machine = GameObject.CreatePrimitive(PrimitiveType.Cube);
        machine.name = station.Name;
        machine.transform.SetParent(parent, false);
        machine.transform.localPosition = station.Position;
        machine.transform.localScale = new Vector3(2f, 2f, 2f);

        var monitor = machine.AddComponent<FactoryMonitor>();
        monitor.Configure(station.Id, DefaultApiUrl);
        Undo.RegisterCreatedObjectUndo(machine, "Create factory station");
    }
}

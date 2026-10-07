using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Меню "Allur/Создать сцену RobotCell": создаёт сцену с ячейкой роботов и добавляет её в Build Settings.
// Из командной строки: -executeMethod RobotCellSetup.CreateScene
public static class RobotCellSetup
{
    const string ScenePath = "Assets/Scenes/RobotCell.unity";

    [MenuItem("Allur/Создать сцену RobotCell")]
    public static void CreateScene()
    {
        // спросить про несохранённые изменения в открытой сцене (в batch mode вопроса нет)
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        // уже созданную сцену не перезаписываем, просто открываем
        if (File.Exists(ScenePath))
        {
            Debug.Log("RobotCellSetup: сцена " + ScenePath + " уже есть, открываю её без изменений.");
            if (!Application.isBatchMode)
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            AddToBuildSettings();
            return;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        GameObject cell = new GameObject("Cell");
        cell.transform.position = Vector3.zero;
        cell.AddComponent<RobotCell>();

        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
        {
            AssetDatabase.CreateFolder("Assets", "Scenes");
        }

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            Debug.LogError("RobotCellSetup: не удалось сохранить сцену в " + ScenePath);
            return;
        }

        AddToBuildSettings();
        Debug.Log("RobotCellSetup: сцена создана: " + ScenePath + ". Нажми Play.");
    }

    // места под ячейки внутри цеха: проходы между рядами колонн (z = ±12), от центра к краям
    static readonly Vector3[] Slots =
    {
        new Vector3(0, 0, 12), new Vector3(25, 0, 12), new Vector3(-25, 0, 12), new Vector3(50, 0, 12), new Vector3(-50, 0, 12),
        new Vector3(0, 0, -12), new Vector3(25, 0, -12), new Vector3(-25, 0, -12), new Vector3(50, 0, -12), new Vector3(-50, 0, -12)
    };

    // первое свободное место (не занятое ни одним процессом: ячейкой, линией колёс и т. д.)
    public static Vector3 FreeSlot()
    {
        FactoryProcess[] existing = Object.FindObjectsByType<FactoryProcess>();
        foreach (Vector3 slot in Slots)
        {
            bool busy = false;
            foreach (FactoryProcess p in existing)
            {
                Vector3 d = p.transform.position - slot;
                d.y = 0f;
                if (d.magnitude < 5f)
                {
                    busy = true;
                    break;
                }
            }
            if (!busy) return slot;
        }
        // все места заняты: ставим рядом с последним, дальше двигай руками
        return existing.Length > 0 ? existing[existing.Length - 1].transform.position + new Vector3(0, 0, 10) : Vector3.zero;
    }

    [MenuItem("Allur/Добавить ячейку RobotCell в цех")]
    public static void AddCellToOpenScene()
    {
        RobotCell[] existing = Object.FindObjectsByType<RobotCell>();
        Vector3 pos = FreeSlot();

        GameObject go = new GameObject("RobotCell_" + (existing.Length + 1));
        go.transform.position = pos;
        RobotCell cell = go.AddComponent<RobotCell>();
        cell.buildEnvironment = false;  // свет, туман и пол даёт сцена завода
        cell.controlCamera = false;     // камерой управляет AllurExperience
        cell.showHud = false;           // панели нескольких ячеек наложились бы друг на друга

        Undo.RegisterCreatedObjectUndo(go, "Добавить ячейку RobotCell");
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        SceneView.FrameLastActiveSceneView();
        Debug.Log("RobotCellSetup: " + go.name + " поставлена в " + pos + ". Сохрани сцену (Ctrl+S).");
    }

    static void AddToBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (EditorBuildSettingsScene s in scenes)
        {
            if (s.path == ScenePath)
            {
                return;
            }
        }
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}

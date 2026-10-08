using UnityEditor;
using UnityEngine;

/// <summary>
/// Пункты меню Unity Editor для быстрой настройки и открытия ИИ-Инспектора Allur.
/// Гарантирует постоянное наличие компонента инспектора в любом режиме запуска.
/// </summary>
[InitializeOnLoad]
public static class AIInspectorMenu
{
    static AIInspectorMenu()
    {
        // Автоматически сохраняем актуальный ключ пользователя при загрузке проекта
        if (!PlayerPrefs.HasKey(OpenAIClient.PrefKey) || OpenAIClient.IsPlaceholderKey(PlayerPrefs.GetString(OpenAIClient.PrefKey, "")))
        {
            OpenAIClient.SaveApiKey(OpenAIClient.DefaultApiKey);
        }

        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.update += EditorUpdateCheck;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            AllurAIInspector.EnsureInstanceExists();
        }
    }

    private static void EditorUpdateCheck()
    {
        if (Application.isPlaying)
        {
            if (AllurAIInspector.Instance == null && Object.FindAnyObjectByType<AllurAIInspector>() == null)
            {
                AllurAIInspector.EnsureInstanceExists();
            }
        }
    }

    [MenuItem("Allur/Открыть окно ИИ-Инспектора (Play Mode)", priority = 10)]
    public static void OpenInspectorWindow()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Allur AI Inspector", "Запустите симуляцию (кнопка Play ▶️ вверху), затем нажмите эту кнопку или клавишу [ I ] / [ Tab ] в окне Game.", "Понятно");
            return;
        }

        AllurAIInspector inspector = AllurAIInspector.EnsureInstanceExists();
        if (inspector != null)
        {
            inspector.isWindowOpen = true;
        }
    }

    [MenuItem("Allur/Подключить рабочий OpenAI API ключ", priority = 19)]
    public static void ApplyUserKey()
    {
        string key = OpenAIClient.LoadApiKey();
        if (!string.IsNullOrEmpty(key) && !OpenAIClient.IsPlaceholderKey(key))
        {
            OpenAIClient.SaveApiKey(key);
            AllurAIInspector inspector = AllurAIInspector.EnsureInstanceExists();
            if (inspector != null)
            {
                inspector.ApiKey = key;
                inspector.ForceSimulationMode = false;
                inspector.RunFactoryAudit(null);
            }
            Debug.Log("AIInspectorMenu: OpenAI API ключ успешно активирован в PlayerPrefs.");
        }
        else
        {
            Debug.LogWarning("AIInspectorMenu: Ключ не задан в local_api_key.txt или настройках.");
        }
    }

    [MenuItem("Allur/Добавить ИИ-Инспектора на сцену", priority = 20)]
    public static void SetupInspectorInScene()
    {
        AllurAIInspector existing = Object.FindAnyObjectByType<AllurAIInspector>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("AIInspectorMenu: ИИ-Инспектор уже присутствует на объекте: " + existing.gameObject.name);
            return;
        }

        Camera mainCam = Camera.main;
        GameObject target = mainCam != null ? mainCam.gameObject : new GameObject("AI_Inspector");

        AllurAIInspector comp = target.AddComponent<AllurAIInspector>();
        comp.ApiKey = OpenAIClient.DefaultApiKey;
        Selection.activeGameObject = target;
        Undo.RegisterCreatedObjectUndo(comp, "Add AI Inspector");

        Debug.Log("AIInspectorMenu: Компонент AllurAIInspector успешно добавлен на " + target.name +
                  " с настроенным API-ключом. Нажмите Play и затем клавишу [ I ] или [ Tab ] в игре.");
    }

    [MenuItem("Allur/Очистить сохраненный OpenAI ключ (сброс в демо-режим)", priority = 21)]
    public static void ResetKey()
    {
        if (PlayerPrefs.HasKey(OpenAIClient.PrefKey))
        {
            PlayerPrefs.DeleteKey(OpenAIClient.PrefKey);
            PlayerPrefs.Save();
            Debug.Log("AIInspectorMenu: Сохраненный ключ OpenAI сброшен в PlayerPrefs.");
        }
        else
        {
            Debug.Log("AIInspectorMenu: Сохраненного ключа в PlayerPrefs не было.");
        }
    }
}

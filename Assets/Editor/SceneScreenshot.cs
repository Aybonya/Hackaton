using System.IO;
using UnityEditor;
using UnityEngine;

// Чистый скриншот окна Scene: тот же ракурс, но без сетки, гизмо, рамок выделения и панелей.
// Рендерится отдельной временной камерой в 2 раза крупнее окна и сохраняется в папку Screenshots в корне проекта.
public static class SceneScreenshot
{
    const int Scale = 2;

    [MenuItem("Allur/Скриншот вида Scene %#k", priority = 101)]
    static void Capture()
    {
        SceneView view = SceneView.lastActiveSceneView;
        if (view == null || view.camera == null)
        {
            Debug.LogWarning("SceneScreenshot: открой окно Scene и наведи нужный ракурс.");
            return;
        }

        Camera src = view.camera;
        int w = Mathf.Max(1, src.pixelWidth) * Scale;
        int h = Mathf.Max(1, src.pixelHeight) * Scale;

        GameObject tmp = new GameObject("ScreenshotCamera") { hideFlags = HideFlags.HideAndDontSave };
        Camera cam = tmp.AddComponent<Camera>();
        cam.CopyFrom(src);
        cam.cameraType = CameraType.Game;   // без сетки и гизмо окна Scene
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 8);
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "scene_" + System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png");
        File.WriteAllBytes(file, tex.EncodeToPNG());

        cam.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(tmp);

        Debug.Log("SceneScreenshot: сохранён " + file + " (" + w + "×" + h + ")");
        EditorUtility.RevealInFinder(file);
    }
}

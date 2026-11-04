using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;

public static class BoardGameProjectSetup
{
    [MenuItem("Board Game/Create Starter Scene")]
    public static void CreateStarterScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cameraObject = new GameObject("Main Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.035f, .055f, .09f);
        cameraObject.AddComponent<AudioListener>();
        new GameObject("Board Game").AddComponent<BoardGameBootstrap>();
        const string folder = "Assets/Scenes";
        Directory.CreateDirectory(folder);
        EditorSceneManager.SaveScene(scene, folder + "/StarportFrontier.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(folder + "/StarportFrontier.unity", true) };
        AssetDatabase.SaveAssets();
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;

// ponytail: launch-arg helper so `-executeMethod OpenMainScene.Run` opens the real
// scene instead of Unity's default empty one. Safe to keep; harmless if unused.
public static class OpenMainScene
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
    }
}

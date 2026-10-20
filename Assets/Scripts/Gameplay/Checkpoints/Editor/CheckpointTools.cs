using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Menus for laying out checkpoints and adding the pause menu to a level:
//   GameObject > Puzzle > Checkpoint              a checkpoint on the floor in front of the Scene view
//   Tools > Puzzle > Add Pause Menu To Scene      the Escape menu, if the scene has none yet
public static class CheckpointTools
{
    public const string PauseMenuPrefabPath = "Assets/Prefabs/UI/Pause Menu.prefab";
    public const string CheckpointPrefabPath = "Assets/Prefabs/Puzzle/Checkpoint.prefab";
    public const string LevelStartPrefabPath = "Assets/Prefabs/Puzzle/Level Start Checkpoint.prefab";

    [MenuItem("GameObject/Puzzle/Checkpoint", false, 11)]
    private static void CreateCheckpointFromMenu(MenuCommand command)
    {
        CreateFromMenu(command, false);
    }

    [MenuItem("GameObject/Puzzle/Level Start Checkpoint", false, 12)]
    private static void CreateLevelStartFromMenu(MenuCommand command)
    {
        CreateFromMenu(command, true);
    }

    private static void CreateFromMenu(MenuCommand command, bool isLevelStart)
    {
        // The hierarchy menu runs once per selected object; one checkpoint is enough.
        if (command.context != null && command.context != Selection.activeObject) return;

        SceneView view = SceneView.lastActiveSceneView;
        Vector3 point = view != null ? view.pivot : Vector3.zero;
        Vector3 facing = view != null ? Vector3.ProjectOnPlane(view.rotation * Vector3.forward, Vector3.up) : Vector3.forward;

        if (facing.sqrMagnitude < 0.01f) facing = Vector3.forward;

        int count = Object.FindObjectsByType<Checkpoint>(FindObjectsSortMode.None).Length;
        string roomName = isLevelStart ? "Level Start" : $"Room {count + 1}";

        Checkpoint checkpoint = CreateCheckpoint(GroundBelow(point), facing.normalized, roomName, isLevelStart);

        if (checkpoint != null) Selection.activeGameObject = checkpoint.gameObject;
    }

    [MenuItem("Tools/Puzzle/Add Pause Menu To Scene")]
    private static void AddPauseMenuFromMenu()
    {
        GameObject menu = AddPauseMenu();

        if (menu != null) Selection.activeGameObject = menu;
    }

    // An instance of the Checkpoint or Level Start prefab, so later changes to the prefab
    // reach every checkpoint in every level.
    public static Checkpoint CreateCheckpoint(Vector3 floorPoint, Vector3 facing, string roomName, bool isLevelStart)
    {
        string path = isLevelStart ? LevelStartPrefabPath : CheckpointPrefabPath;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        if (prefab == null)
        {
            Debug.LogError($"Checkpoint prefab not found at {path}.");

            return null;
        }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(go, "Create Checkpoint");

        go.name = isLevelStart ? $"Level Start - {roomName}" : $"Checkpoint - {roomName}";
        go.transform.SetPositionAndRotation(floorPoint, Quaternion.LookRotation(facing, Vector3.up));

        Checkpoint checkpoint = go.GetComponent<Checkpoint>();

        SerializedObject serialized = new SerializedObject(checkpoint);
        serialized.FindProperty("roomName").stringValue = roomName;
        serialized.FindProperty("isLevelStart").boolValue = isLevelStart;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return checkpoint;
    }

    public static GameObject AddPauseMenu()
    {
        PauseMenu existing = Object.FindAnyObjectByType<PauseMenu>(FindObjectsInactive.Include);

        if (existing != null)
        {
            Debug.Log("This scene already has a Pause Menu.", existing);

            return existing.gameObject;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PauseMenuPrefabPath);

        if (prefab == null)
        {
            Debug.LogError($"Pause Menu prefab not found at {PauseMenuPrefabPath}.");

            return null;
        }

        GameObject menu = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(menu, "Add Pause Menu");
        EditorSceneManager.MarkSceneDirty(menu.scene);

        return menu;
    }

    private static Vector3 GroundBelow(Vector3 point)
    {
        Physics.SyncTransforms();

        return Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 50f,
                               Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? hit.point
            : point;
    }
}

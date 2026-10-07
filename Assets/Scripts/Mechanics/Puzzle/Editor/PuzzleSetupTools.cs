using UnityEditor;
using UnityEngine;

// One-click setup for pressure plates and portal gates, so a puzzle is built from menus
// rather than by hand-wiring components:
//   GameObject > Puzzle > Pressure Plate            a new plate in front of the Scene view
//   Tools > Puzzle > Make Selected Portal a Gate    turns any portal into a working gate
//   Portal Gate header > Add Pressure Plate         a new plate already linked to that gate
public static class PuzzleSetupTools
{
    public const string PlatePrefabPath = "Assets/Prefabs/Puzzle/Pressure Plate.prefab";

    // How far in front of a portal a new plate is put.
    private const float PlateDistance = 4f;

    [MenuItem("GameObject/Puzzle/Pressure Plate", false, 10)]
    private static void CreatePlateFromMenu(MenuCommand command)
    {
        // The hierarchy menu runs once per selected object; one plate is enough.
        if (command.context != null && command.context != Selection.activeObject) return;

        SceneView view = SceneView.lastActiveSceneView;
        Vector3 point = view != null ? view.pivot : Vector3.zero;

        GameObject plate = InstantiatePlate(GroundBelow(point), null);

        if (plate != null) Selection.activeGameObject = plate;
    }

    [MenuItem("Tools/Puzzle/Make Selected Portal a Gate")]
    private static void MakeSelectedGates()
    {
        foreach (GameObject selected in Selection.gameObjects)
        {
            PortalGate gate = MakeGate(selected);

            if (gate == null)
            {
                Debug.LogWarning($"'{selected.name}' has no vortex to fit a gate to. Select a portal's root object.", selected);
            }
        }
    }

    [MenuItem("Tools/Puzzle/Make Selected Portal a Gate", true)]
    private static bool CanMakeSelectedGates()
    {
        return Selection.gameObjects.Length > 0;
    }

    [MenuItem("CONTEXT/PortalGate/Add Pressure Plate")]
    private static void AddPlateFromComponent(MenuCommand command)
    {
        PressurePlate plate = AddPlate((PortalGate)command.context);

        if (plate != null) Selection.activeGameObject = plate.gameObject;
    }

    [MenuItem("CONTEXT/PortalGate/Fit Barrier To Vortex")]
    private static void FitFromComponent(MenuCommand command)
    {
        PortalGate gate = (PortalGate)command.context;

        Undo.RecordObject(gate.GetComponent<BoxCollider>(), "Fit Barrier To Vortex");

        if (!gate.FitBarrierToVortex()) Debug.LogWarning("No vortex renderer found to fit to.", gate);

        PrefabUtility.RecordPrefabInstancePropertyModifications(gate.GetComponent<BoxCollider>());
    }

    // Adds the gate and sizes its wall to the vortex disc. Returns null when the object has no
    // vortex, so nothing half set up is left behind.
    public static PortalGate MakeGate(GameObject portal)
    {
        PortalGate gate = portal.GetComponent<PortalGate>();

        if (gate == null)
        {
            gate = Undo.AddComponent<PortalGate>(portal);
        }

        BoxCollider box = portal.GetComponent<BoxCollider>();
        Undo.RecordObject(box, "Make Portal Gate");

        if (!gate.FitBarrierToVortex())
        {
            Undo.DestroyObjectImmediate(gate);
            Undo.DestroyObjectImmediate(box);

            return null;
        }

        PrefabUtility.RecordPrefabInstancePropertyModifications(box);

        return gate;
    }

    // A new plate on the ground in front of the portal, on whichever side has floor, already in
    // the gate's list.
    public static PressurePlate AddPlate(PortalGate gate)
    {
        Vector3 centre = gate.BarrierCentre;
        Vector3 facing = Vector3.ProjectOnPlane(gate.Facing, Vector3.up).normalized;

        if (facing.sqrMagnitude < 0.01f) facing = Vector3.ProjectOnPlane(gate.transform.forward, Vector3.up).normalized;

        // The portal's foot, so the side whose floor is level with it wins over a pit.
        float footHeight = gate.GetComponentInChildren<Renderer>() != null
            ? gate.GetComponentInChildren<Renderer>().bounds.min.y
            : gate.transform.position.y;

        Vector3 best = centre + facing * PlateDistance;
        float bestScore = float.MaxValue;

        foreach (float side in new[] { 1f, -1f })
        {
            Vector3 probe = centre + facing * (PlateDistance * side);

            if (!Physics.Raycast(probe + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 20f,
                                 Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;

            float score = Mathf.Abs(hit.point.y - footHeight);

            if (score < bestScore)
            {
                bestScore = score;
                best = hit.point;
            }
        }

        GameObject plateObject = InstantiatePlate(best, gate.transform.parent);

        if (plateObject == null) return null;

        plateObject.name = $"Pressure Plate ({gate.name})";

        PressurePlate plate = plateObject.GetComponent<PressurePlate>();

        Undo.RecordObject(gate, "Add Pressure Plate");
        gate.AddPlate(plate);
        PrefabUtility.RecordPrefabInstancePropertyModifications(gate);

        return plate;
    }

    private static GameObject InstantiatePlate(Vector3 position, Transform parent)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlatePrefabPath);

        if (prefab == null)
        {
            Debug.LogError($"Pressure Plate prefab not found at {PlatePrefabPath}.");

            return null;
        }

        GameObject plate = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(plate, "Create Pressure Plate");

        if (parent != null) Undo.SetTransformParent(plate.transform, parent, "Create Pressure Plate");

        plate.transform.position = position;
        plate.transform.rotation = Quaternion.identity;

        // Real size whatever group it lands in, or a scaled level folder makes a giant plate.
        Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;
        plate.transform.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);

        return plate;
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

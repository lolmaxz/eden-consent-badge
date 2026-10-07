using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EdenApis
{
    public class BadgeV1UpgraderWindow : EditorWindow
    {
        private const string Version1MeshGuid = "1c5c3dc74e4be994a821bbc65efa9a85";
        private const string Version1MeshSha256 = "0CADBCB41F87268B45066015E989CF7761FC7A3A28051783C3FD2246DE241F08";

        private const string UniversalGuid = "d34470a5797ccf84fa1bdc8886e340da";
        private const string BonelessGuid = "18465008bd68cfeab878897b8ee6a2d8";
        private const string TwoSidedGuid = "7d2910129cf190a4a896ed3ef7e1fe43";

        private const string ArmatureLinkWarning =
            "This badge is not parented to an avatar bone. It was placed in the same spot, but it will not move with the avatar until you add a VRCFury Armature Link on it and choose the bone it should follow.";

        private static readonly ReplacementOption[] Options =
        {
            new ReplacementOption(
                "Universal Eden Badge",
                UniversalGuid,
                "Universal Eden Badge",
                "Has bones, so you can add physics with a physbone or use a rotation constraint."),
            new ReplacementOption(
                "(Boneless) Universal Eden Badge",
                BonelessGuid,
                "(Boneless) Universal Eden Badge",
                "Has no bones. Use this for a static badge that will not move."),
            new ReplacementOption(
                "(2-Sided) Universal Eden Badge",
                TwoSidedGuid,
                "(2-Sided) Universal Eden Badge",
                "Visible from the front and the back, with a metal band around the outside. Use this when it can be seen from behind, such as an earring. It also has bones.")
        };

        private readonly List<BadgeMatch> matches = new List<BadgeMatch>();
        private readonly Dictionary<string, string> hashByAssetPath = new Dictionary<string, string>();

        private Vector2 scroll;
        private int optionIndex;
        private string status = "";
        private bool editingOldPrefabAsset;

        private struct ReplacementOption
        {
            public readonly string Label;
            public readonly string Guid;
            public readonly string InstructionPrefix;
            public readonly string FallbackDescription;

            public ReplacementOption(string label, string guid, string instructionPrefix, string fallbackDescription)
            {
                Label = label;
                Guid = guid;
                InstructionPrefix = instructionPrefix;
                FallbackDescription = fallbackDescription;
            }
        }

        private sealed class BadgeMatch
        {
            public GameObject Badge;
            public bool Selected = true;
            public bool UnderBone;
            public string Path;
        }

        [MenuItem("Tools/Eden/Upgrade V1 Badge")]
        public static void OpenWindow()
        {
            BadgeV1UpgraderWindow window = GetWindow<BadgeV1UpgraderWindow>("Upgrade V1 Badge");
            window.minSize = new Vector2(560f, 480f);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Upgrade V1 Badge");
            Scan(true);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Upgrade V1 Badge", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Looks through the open scene for an Eden Badge 1.0, even if it was renamed. A match is a mesh that still uses the original EdenHeart.fbx. Upgrade replaces that object with a V2 prefab, copies its position and scale, and turns the new heart so it keeps the old facing.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Refresh", "Search the open scene again and select every V1 badge it finds.")))
                    Scan(true);
            }

            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.HelpBox(status, MessageType.None);

            if (editingOldPrefabAsset)
            {
                EditorGUILayout.HelpBox(
                    "The V1 badge prefab itself is open. Open the avatar scene, or the avatar prefab, then refresh.",
                    MessageType.Warning);
                EdenBadgeCredits.Draw();
                return;
            }

            DrawMatches();
            GUILayout.Space(8f);
            DrawOptions();
            GUILayout.Space(8f);
            DrawUpgradeButton();
            EdenBadgeCredits.Draw();
        }

        private void DrawMatches()
        {
            int alive = 0;
            int notOnBone = 0;
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Badge == null)
                    continue;
                alive++;
                if (!matches[i].UnderBone)
                    notOnBone++;
            }

            EditorGUILayout.LabelField(
                alive == 0 ? "No V1 badges in this scene" : "Found " + alive + " V1 badge" + (alive == 1 ? "" : "s"),
                EditorStyles.boldLabel);

            if (alive == 0)
            {
                EditorGUILayout.HelpBox(
                    "Nothing using the original EdenHeart mesh was found. Open the scene that contains the avatar, or open the avatar prefab, then press Refresh.",
                    MessageType.Warning);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(140f));
            bool selectionChanged = false;
            for (int i = 0; i < matches.Count; i++)
            {
                BadgeMatch match = matches[i];
                if (match.Badge == null)
                    continue;

                EditorGUILayout.BeginVertical("box");
                EditorGUI.BeginChangeCheck();
                match.Selected = EditorGUILayout.ToggleLeft(
                    new GUIContent(match.Badge.name, "Include this badge in the upgrade. Checked badges are also selected in the Hierarchy."),
                    match.Selected);
                if (EditorGUI.EndChangeCheck())
                    selectionChanged = true;

                EditorGUILayout.LabelField(match.Path, EditorStyles.miniLabel);
                if (match.UnderBone)
                    EditorGUILayout.LabelField("Parented to an avatar bone.", EditorStyles.miniLabel);
                else
                    EditorGUILayout.LabelField(ArmatureLinkWarning, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();

            if (selectionChanged)
                SelectChecked();

            if (notOnBone > 0)
            {
                EditorGUILayout.HelpBox(
                    "At least one badge is not on an avatar bone. The upgrade will still move it, and you will need a VRCFury Armature Link afterwards.",
                    MessageType.Warning);
            }
        }

        private void DrawOptions()
        {
            EditorGUILayout.LabelField("Replace with", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("All three work on Quest and PC. Universal is the usual choice.", EditorStyles.miniLabel);

            string[] labels = new string[Options.Length];
            for (int i = 0; i < Options.Length; i++)
                labels[i] = Options[i].Label;

            optionIndex = EditorGUILayout.Popup(
                new GUIContent("Prefab", "Which V2 badge replaces the V1 badge. Universal is the default."),
                optionIndex,
                labels);
            optionIndex = Mathf.Clamp(optionIndex, 0, Options.Length - 1);
            EditorGUILayout.HelpBox(LoadDescription(Options[optionIndex]), MessageType.None);
        }

        private void DrawUpgradeButton()
        {
            int selected = CountSelected();
            using (new EditorGUI.DisabledScope(selected == 0 || LoadReplacementPrefab() == null))
            {
                if (GUILayout.Button(
                        new GUIContent(
                            "Upgrade selected",
                            "Removes each checked V1 badge and puts the chosen V2 prefab in its place. Position and scale are copied, and the heart is turned to keep the old facing."),
                        GUILayout.Height(36f)))
                {
                    UpgradeSelected();
                }
            }

            if (LoadReplacementPrefab() == null)
            {
                EditorGUILayout.HelpBox(
                    "Could not find " + Options[optionIndex].Label + " in the project.",
                    MessageType.Error);
            }
        }

        private void Scan(bool selectMatches)
        {
            matches.Clear();
            editingOldPrefabAsset = false;
            status = "";

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && IsOldBadgePrefabPath(stage.assetPath))
            {
                editingOldPrefabAsset = true;
                return;
            }

            HashSet<int> seen = new HashSet<int>();
            if (stage != null)
            {
                Collect(stage.prefabContentsRoot, seen);
            }
            else
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded)
                        continue;

                    GameObject[] roots = scene.GetRootGameObjects();
                    for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                        Collect(roots[rootIndex], seen);
                }
            }

            if (selectMatches && matches.Count > 0)
                SelectChecked();
        }

        private void Collect(GameObject root, HashSet<int> seen)
        {
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                if (!IsVersion1HeartMesh(filters[i].sharedMesh))
                    continue;

                GameObject badge = ResolveBadgeRoot(filters[i].gameObject);
                if (badge == null || !seen.Add(badge.GetInstanceID()))
                    continue;

                matches.Add(new BadgeMatch
                {
                    Badge = badge,
                    Selected = true,
                    UnderBone = IsUnderAvatarBone(badge.transform),
                    Path = GetHierarchyPath(badge.transform)
                });
            }
        }

        private void UpgradeSelected()
        {
            GameObject prefab = LoadReplacementPrefab();
            if (prefab == null)
                return;

            List<BadgeMatch> chosen = new List<BadgeMatch>();
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Selected && matches[i].Badge != null)
                    chosen.Add(matches[i]);
            }

            if (chosen.Count == 0)
                return;

            ReplacementOption option = Options[optionIndex];
            int looseCount = 0;
            for (int i = 0; i < chosen.Count; i++)
            {
                if (!chosen[i].UnderBone)
                    looseCount++;
            }

            string message = "Replace " + chosen.Count + " V1 badge" + (chosen.Count == 1 ? "" : "s")
                + " with " + option.Label + "?\n\nPosition and scale are copied. The new heart is turned so it keeps the old facing. You can undo this.";
            if (looseCount > 0)
            {
                message += "\n\n" + looseCount + " of them " + (looseCount == 1 ? "is" : "are")
                    + " not on an avatar bone. After the swap, add a VRCFury Armature Link on those and choose the bone they should follow.";
            }

            if (!EditorUtility.DisplayDialog("Upgrade V1 badge", message, "Upgrade", "Cancel"))
                return;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Upgrade Eden Badge");

            List<GameObject> created = new List<GameObject>();
            int looseUpgraded = 0;
            for (int i = 0; i < chosen.Count; i++)
            {
                GameObject upgraded = ReplaceBadge(chosen[i].Badge, prefab);
                if (upgraded == null)
                    continue;

                created.Add(upgraded);
                if (!chosen[i].UnderBone)
                    looseUpgraded++;
            }

            Undo.CollapseUndoOperations(undoGroup);

            Scan(false);
            status = "Upgraded " + created.Count + " badge" + (created.Count == 1 ? "" : "s") + " to " + option.Label + ".";
            if (looseUpgraded > 0)
            {
                status += " " + looseUpgraded + " still need a VRCFury Armature Link because they were not on an avatar bone.";
            }

            if (created.Count > 0)
                Selection.objects = created.ToArray();
        }

        private static GameObject ReplaceBadge(GameObject oldBadge, GameObject prefab)
        {
            if (oldBadge == null || IsAvatarRoot(oldBadge))
                return null;

            Transform oldTransform = oldBadge.transform;
            Transform parent = oldTransform.parent;
            int sibling = oldTransform.GetSiblingIndex();
            Vector3 localPosition = oldTransform.localPosition;
            Quaternion oldWorldRotation = oldTransform.rotation;
            Vector3 localScale = oldTransform.localScale;
            int layer = oldBadge.layer;
            bool active = oldBadge.activeSelf;
            Scene scene = oldBadge.scene;

            GameObject created = parent != null
                ? PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject
                : PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (created == null)
            {
                Debug.LogError("Could not create " + prefab.name + ".");
                return null;
            }

            if (parent == null && created.scene != scene)
                SceneManager.MoveGameObjectToScene(created, scene);

            Undo.RegisterCreatedObjectUndo(created, "Upgrade Eden Badge");
            created.layer = layer;
            created.SetActive(active);
            created.transform.localPosition = localPosition;
            created.transform.localScale = localScale;
            AlignHeartFacing(created.transform, oldWorldRotation);

            Undo.DestroyObjectImmediate(oldBadge);
            created.transform.SetSiblingIndex(sibling);
            return created;
        }

        private static void AlignHeartFacing(Transform root, Quaternion oldWorldRotation)
        {
            Transform heart = FindHeartTransform(root);
            if (heart == null || heart == root)
            {
                root.rotation = oldWorldRotation;
                return;
            }

            // The V2 mesh sits on the EdenHeart child, which the new FBX rotates
            // away from the prefab root. Turn the root so that child keeps the old facing.
            Quaternion heartFromRoot = Quaternion.Inverse(root.rotation) * heart.rotation;
            root.rotation = oldWorldRotation * Quaternion.Inverse(heartFromRoot);
        }

        private static Transform FindHeartTransform(Transform root)
        {
            Transform named = null;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name != "EdenHeart")
                    continue;

                named = transforms[i];
                if (transforms[i].GetComponent<Renderer>() != null)
                    return transforms[i];
            }

            return named;
        }

        private GameObject LoadReplacementPrefab()
        {
            optionIndex = Mathf.Clamp(optionIndex, 0, Options.Length - 1);
            string path = AssetDatabase.GUIDToAssetPath(Options[optionIndex].Guid);
            if (string.IsNullOrEmpty(path))
                return null;

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static string LoadDescription(ReplacementOption option)
        {
            string[] guids = AssetDatabase.FindAssets("Install Instructions");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!path.EndsWith("Install Instructions.txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] lines = File.ReadAllLines(path);
                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string trimmed = lines[lineIndex].Trim();
                    if (trimmed.StartsWith(option.InstructionPrefix, StringComparison.Ordinal))
                        return trimmed;
                }
            }

            return option.FallbackDescription;
        }

        private bool IsVersion1HeartMesh(Mesh mesh)
        {
            if (mesh == null)
                return false;

            string path = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(path))
                return false;

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.Equals(guid, Version1MeshGuid, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                return false;

            return string.Equals(GetFileHash(path), Version1MeshSha256, StringComparison.OrdinalIgnoreCase);
        }

        private string GetFileHash(string assetPath)
        {
            string cached;
            if (hashByAssetPath.TryGetValue(assetPath, out cached))
                return cached;

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string absolute = Path.Combine(projectRoot, assetPath);
            if (!File.Exists(absolute))
            {
                hashByAssetPath[assetPath] = "";
                return "";
            }

            using (FileStream stream = File.OpenRead(absolute))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                string text = BitConverter.ToString(hash).Replace("-", "");
                hashByAssetPath[assetPath] = text;
                return text;
            }
        }

        private static GameObject ResolveBadgeRoot(GameObject meshObject)
        {
            if (meshObject == null || IsAvatarRoot(meshObject))
                return null;

            GameObject best = meshObject;
            Transform current = meshObject.transform.parent;
            while (current != null && !IsAvatarRoot(current.gameObject))
            {
                if (PrefabUtility.IsAnyPrefabInstanceRoot(current.gameObject))
                {
                    string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(current.gameObject);
                    if (IsOldBadgePrefabPath(path))
                        best = current.gameObject;
                    else
                        break;
                }

                current = current.parent;
            }

            return IsAvatarRoot(best) ? null : best;
        }

        private static bool IsOldBadgePrefabPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string name = Path.GetFileNameWithoutExtension(path);
            return name.IndexOf("EdenHeart", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsUnderAvatarBone(Transform badge)
        {
            if (badge == null || badge.parent == null)
                return false;

            Transform descriptor = null;
            Transform humanoid = null;
            Transform walker = badge.parent;
            while (walker != null)
            {
                if (HasAvatarDescriptor(walker.gameObject))
                    descriptor = walker;

                Animator animator = walker.GetComponent<Animator>();
                if (animator != null && animator.isHuman && animator.avatar != null)
                    humanoid = walker;

                walker = walker.parent;
            }

            Transform avatarRoot = descriptor != null ? descriptor : humanoid;
            if (avatarRoot == null)
                return false;

            return badge.parent != avatarRoot && badge.IsChildOf(avatarRoot);
        }

        private static bool IsAvatarRoot(GameObject gameObject)
        {
            if (gameObject == null)
                return false;

            if (HasAvatarDescriptor(gameObject))
                return true;

            Animator animator = gameObject.GetComponent<Animator>();
            return animator != null && animator.isHuman && animator.avatar != null;
        }

        private static bool HasAvatarDescriptor(GameObject gameObject)
        {
            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                    continue;

                if (components[i].GetType().Name == "VRCAvatarDescriptor")
                    return true;
            }

            return false;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform.parent == null)
                return transform.name;

            return GetHierarchyPath(transform.parent) + "/" + transform.name;
        }

        private int CountSelected()
        {
            int count = 0;
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Selected && matches[i].Badge != null)
                    count++;
            }

            return count;
        }

        private void SelectChecked()
        {
            List<UnityEngine.Object> selected = new List<UnityEngine.Object>();
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Selected && matches[i].Badge != null)
                    selected.Add(matches[i].Badge);
            }

            Selection.objects = selected.ToArray();
        }
    }
}

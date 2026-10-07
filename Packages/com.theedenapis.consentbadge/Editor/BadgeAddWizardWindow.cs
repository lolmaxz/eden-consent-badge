using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EdenApis
{
    public class BadgeAddWizardWindow : EditorWindow
    {
        private const string UniversalGuid = "d34470a5797ccf84fa1bdc8886e340da";
        private const string BonelessGuid = "18465008bd68cfeab878897b8ee6a2d8";
        private const string TwoSidedGuid = "7d2910129cf190a4a896ed3ef7e1fe43";

        private static readonly BadgeChoice[] Choices =
        {
            new BadgeChoice(
                "Universal Eden Badge",
                UniversalGuid,
                true,
                "Has bones, so you can add physics with a physbone or use a rotation constraint."),
            new BadgeChoice(
                "(Boneless) Universal Eden Badge",
                BonelessGuid,
                false,
                "Has no bones. Use this for a static badge that will not move."),
            new BadgeChoice(
                "(2-Sided) Universal Eden Badge",
                TwoSidedGuid,
                true,
                "Visible from the front and the back, with a metal band around the outside. Use this when it can be seen from behind, such as an earring. It also has bones.")
        };

        private readonly List<BoneChoice> bones = new List<BoneChoice>();

        private GameObject avatar;
        private int choiceIndex;
        private int boneIndex = -1;
        private string boneFilter = "";
        private Vector2 boneScroll;
        private string status = "";

        private struct BadgeChoice
        {
            public readonly string Label;
            public readonly string Guid;
            public readonly bool HasBones;
            public readonly string Description;

            public BadgeChoice(string label, string guid, bool hasBones, string description)
            {
                Label = label;
                Guid = guid;
                HasBones = hasBones;
                Description = description;
            }
        }

        private struct BoneChoice
        {
            public Transform Bone;
            public string Label;
        }

        [MenuItem("Tools/Eden/Add Badge")]
        public static void OpenWindow()
        {
            BadgeAddWizardWindow window = GetWindow<BadgeAddWizardWindow>("Add Badge");
            window.minSize = new Vector2(460f, 560f);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Add Badge");
            RefreshAvatar(true);
        }

        private void OnSelectionChange()
        {
            RefreshAvatar(false);
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Add Eden Consent Badge", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Version 2. Finds the avatar from your selection, or the first active avatar in the scene. Choose a badge and a bone. Add places that prefab on the bone and selects it.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Refresh", "Look again at the selection, then at the open scene.")))
                    RefreshAvatar(true);
            }

            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.HelpBox(status, MessageType.None);

            DrawAvatar();
            GUILayout.Space(8f);
            DrawChoices();
            GUILayout.Space(8f);
            DrawBones();
            GUILayout.Space(8f);
            DrawAddButton();
            EdenBadgeCredits.Draw();
        }

        private void DrawAvatar()
        {
            EditorGUILayout.LabelField("Avatar", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(
                    new GUIContent("Avatar", "The avatar this badge will be added to."),
                    avatar,
                    typeof(GameObject),
                    true);
            }
        }

        private void DrawChoices()
        {
            EditorGUILayout.LabelField("Badge", EditorStyles.boldLabel);
            string[] labels = new string[Choices.Length];
            for (int i = 0; i < Choices.Length; i++)
                labels[i] = Choices[i].Label;

            choiceIndex = EditorGUILayout.Popup(
                new GUIContent("Prefab", "Which Eden Consent Badge to add. The prefabs live in Packages / Eden Consent Badge."),
                choiceIndex,
                labels);
            choiceIndex = Mathf.Clamp(choiceIndex, 0, Choices.Length - 1);
            EditorGUILayout.HelpBox(Choices[choiceIndex].Description, MessageType.None);

            BoneChoice bone = CurrentBone();
            if (!Choices[choiceIndex].HasBones && bone.Bone != null && HasPhysBone(bone.Bone.gameObject))
            {
                EditorGUILayout.HelpBox(
                    bone.Bone.name + " already has a PhysBone. This badge has no bones of its own, so that PhysBone will move it. You can still add it after confirming.",
                    MessageType.Warning);
            }
        }

        private void DrawBones()
        {
            EditorGUILayout.LabelField("Bone", EditorStyles.boldLabel);
            if (avatar == null)
            {
                EditorGUILayout.HelpBox("Select an avatar, or open a scene that has one.", MessageType.Warning);
                return;
            }

            if (bones.Count == 0)
            {
                EditorGUILayout.HelpBox("This avatar has no bones to attach to.", MessageType.Warning);
                return;
            }

            boneFilter = EditorGUILayout.TextField(
                new GUIContent("Filter", "Show bones whose name contains this text."),
                boneFilter);

            boneScroll = EditorGUILayout.BeginScrollView(boneScroll, GUILayout.MinHeight(220f));
            int shown = 0;
            for (int i = 0; i < bones.Count; i++)
            {
                if (!BoneMatchesFilter(bones[i]))
                    continue;

                shown++;
                bool selected = i == boneIndex;
                bool next = GUILayout.Toggle(
                    selected,
                    new GUIContent(bones[i].Label, "Parent the badge to " + bones[i].Bone.name + "."),
                    EditorStyles.radioButton);
                if (next && !selected)
                    boneIndex = i;
            }

            EditorGUILayout.EndScrollView();
            if (shown == 0)
                EditorGUILayout.HelpBox("No bones match that filter.", MessageType.None);
        }

        private void DrawAddButton()
        {
            BoneChoice bone = CurrentBone();
            using (new EditorGUI.DisabledScope(avatar == null || bone.Bone == null))
            {
                if (GUILayout.Button(new GUIContent("Add to avatar", "Create the badge on the chosen bone and select it."), GUILayout.Height(32f)))
                    AddBadge();
            }
        }

        private void AddBadge()
        {
            RefreshAvatar(false);
            BoneChoice bone = CurrentBone();
            if (avatar == null || bone.Bone == null)
            {
                status = "Choose an avatar and a bone first.";
                return;
            }

            GameObject prefab = LoadPrefab(Choices[choiceIndex].Guid);
            if (prefab == null)
            {
                status = "Could not find " + Choices[choiceIndex].Label + " in the project.";
                return;
            }

            if (!Choices[choiceIndex].HasBones && HasPhysBone(bone.Bone.gameObject))
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "PhysBone on this bone",
                    bone.Bone.name + " already has a PhysBone. This badge has no bones of its own, so that PhysBone will move it.\n\nAdd the badge anyway?",
                    "Add badge",
                    "Cancel");
                if (!proceed)
                    return;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, bone.Bone) as GameObject;
            if (instance == null)
            {
                status = "Could not create the badge.";
                return;
            }

            Undo.RegisterCreatedObjectUndo(instance, "Add Eden Consent Badge");
            Transform added = instance.transform;
            Quaternion rotation = added.localRotation;
            Vector3 scale = added.localScale;
            Undo.RecordObject(added, "Add Eden Consent Badge");
            added.localPosition = Vector3.zero;
            added.localRotation = rotation;
            added.localScale = scale;

            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);
            status = "Added " + Choices[choiceIndex].Label + " on " + bone.Bone.name + ".";
        }

        private void RefreshAvatar(bool resetStatus)
        {
            if (resetStatus)
                status = "";

            GameObject next = FindAvatarFromSelection();
            if (next == null)
                next = FindFirstActiveAvatar();

            bool avatarChanged = next != avatar;
            avatar = next;
            if (avatarChanged || bones.Count == 0)
                RebuildBones();

            TryPreselectBoneFromSelection();
        }

        private void RebuildBones()
        {
            bones.Clear();
            boneIndex = -1;
            if (avatar == null)
                return;

            Animator animator = avatar.GetComponent<Animator>();
            if (animator == null)
                animator = avatar.GetComponentInChildren<Animator>(true);

            HashSet<Transform> seen = new HashSet<Transform>();
            HashSet<string> usedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (animator != null && animator.isHuman)
            {
                foreach (HumanBodyBones humanBone in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (humanBone == HumanBodyBones.LastBone)
                        continue;

                    Transform bone = animator.GetBoneTransform(humanBone);
                    if (bone == null || !seen.Add(bone))
                        continue;

                    string label = PrettyBoneName(humanBone);
                    usedLabels.Add(label);
                    bones.Add(new BoneChoice
                    {
                        Bone = bone,
                        Label = label
                    });
                }
            }

            Transform skeletonRoot = FindSkeletonRoot(animator);
            if (skeletonRoot != null)
                CollectHierarchyBones(skeletonRoot, seen, usedLabels);

            SkinnedMeshRenderer[] meshes = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
            {
                Transform[] skinnedBones = meshes[meshIndex].bones;
                if (skinnedBones == null)
                    continue;

                for (int boneIndexInMesh = 0; boneIndexInMesh < skinnedBones.Length; boneIndexInMesh++)
                {
                    Transform bone = skinnedBones[boneIndexInMesh];
                    if (bone == null || !seen.Add(bone))
                        continue;

                    bones.Add(new BoneChoice
                    {
                        Bone = bone,
                        Label = UniqueBoneLabel(bone, usedLabels)
                    });
                }
            }

            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i].Bone.name == "Chest" || bones[i].Label == "Chest")
                {
                    boneIndex = i;
                    break;
                }
            }

            if (boneIndex < 0 && bones.Count > 0)
                boneIndex = 0;
        }

        private Transform FindSkeletonRoot(Animator animator)
        {
            Transform hips = null;
            if (animator != null && animator.isHuman)
                hips = animator.GetBoneTransform(HumanBodyBones.Hips);

            if (hips != null && hips.parent != null && hips.parent != avatar.transform)
                return hips.parent;

            if (hips != null)
                return hips;

            return FindDescendantByName(avatar.transform, "Armature");
        }

        private void CollectHierarchyBones(Transform root, HashSet<Transform> seen, HashSet<string> usedLabels)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (IsMeshObject(child.gameObject))
                    continue;

                if (seen.Add(child))
                {
                    bones.Add(new BoneChoice
                    {
                        Bone = child,
                        Label = UniqueBoneLabel(child, usedLabels)
                    });
                }

                CollectHierarchyBones(child, seen, usedLabels);
            }
        }

        private static Transform FindDescendantByName(Transform root, string name)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == name)
                    return child;

                Transform nested = FindDescendantByName(child, name);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private static bool IsMeshObject(GameObject gameObject)
        {
            return gameObject.GetComponent<MeshRenderer>() != null
                || gameObject.GetComponent<SkinnedMeshRenderer>() != null;
        }

        private static string UniqueBoneLabel(Transform bone, HashSet<string> usedLabels)
        {
            string label = bone.name;
            Transform walker = bone;
            while (!usedLabels.Add(label) && walker.parent != null)
            {
                walker = walker.parent;
                label = walker.name + "/" + label;
            }

            return label;
        }

        private void TryPreselectBoneFromSelection()
        {
            Transform selected = Selection.activeTransform;
            if (selected == null)
                return;

            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i].Bone == selected)
                {
                    boneIndex = i;
                    return;
                }
            }
        }

        private BoneChoice CurrentBone()
        {
            if (boneIndex < 0 || boneIndex >= bones.Count)
                return default;

            if (!BoneMatchesFilter(bones[boneIndex]))
                return default;

            return bones[boneIndex];
        }

        private bool BoneMatchesFilter(BoneChoice bone)
        {
            if (string.IsNullOrEmpty(boneFilter))
                return true;

            return bone.Label.IndexOf(boneFilter, StringComparison.OrdinalIgnoreCase) >= 0
                || bone.Bone.name.IndexOf(boneFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static GameObject FindAvatarFromSelection()
        {
            Transform[] selected = Selection.transforms;
            for (int i = 0; i < selected.Length; i++)
            {
                Transform walker = selected[i];
                while (walker != null)
                {
                    if (HasAvatarDescriptor(walker.gameObject))
                        return walker.gameObject;

                    walker = walker.parent;
                }
            }

            return null;
        }

        private static GameObject FindFirstActiveAvatar()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null)
                return FindFirstActive(stage.prefabContentsRoot.transform);

            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded)
                    continue;

                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    GameObject found = FindFirstActive(roots[i].transform);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        private static GameObject FindFirstActive(Transform root)
        {
            if (root == null || !root.gameObject.activeSelf)
                return null;

            if (HasAvatarDescriptor(root.gameObject))
                return root.gameObject;

            for (int i = 0; i < root.childCount; i++)
            {
                GameObject found = FindFirstActive(root.GetChild(i));
                if (found != null)
                    return found;
            }

            return null;
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

        private static bool HasPhysBone(GameObject gameObject)
        {
            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                    continue;

                if (components[i].GetType().Name == "VRCPhysBone")
                    return true;
            }

            return false;
        }

        private static GameObject LoadPrefab(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                return null;

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static string PrettyBoneName(HumanBodyBones bone)
        {
            string raw = bone.ToString();
            StringBuilder builder = new StringBuilder(raw.Length + 8);
            for (int i = 0; i < raw.Length; i++)
            {
                if (i > 0 && char.IsUpper(raw[i]) && !char.IsUpper(raw[i - 1]))
                    builder.Append(' ');

                builder.Append(raw[i]);
            }

            return builder.ToString();
        }
    }
}

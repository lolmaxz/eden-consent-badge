using System.IO;
using UnityEditor;
using UnityEngine;
using EdenApis;

namespace EdenApis.AtlasGenerator
{
    public class AtlasGeneratorWindow : EditorWindow
    {
        private const int AtlasSize = 2048;
        private const int CellSize = 1024;
        private const float OverviewPreviewSize = 400f;
        private const float QuadrantPreviewSize = 300f;
        private const int PreviewAtlasSize = 512;
        private const int PreviewCellSize = 256;
        private const int ViewCount = 5;

        private const string TipOverview =
            "Shows all four hearts together, the way they are saved on the badge right now.";

        private const string TipEden =
            "Top-left heart. Locked. This logo is part of the badge setup and cannot be edited here.";

        private const string TipHephia =
            "Top-right heart. Locked. This logo is part of the badge setup and cannot be edited here.";

        private const string TipCustom1 =
            "Bottom-left heart. This is Custom 1 on the badge menu. You can replace this logo.";

        private const string TipCustom2 =
            "Bottom-right heart. This is Custom 2 on the badge menu. You can replace this logo.";

        private const int Custom1Tab = 3;
        private const int Custom2Tab = 4;

        private const string TipTexture =
            "Image placed in this heart. It keeps its proportions, is fitted inside the square, then scaled.";

        private const string TipScale =
            "Size of the image inside the heart. 1 fits the longest side. Lower shrinks it and leaves more empty space. Higher zooms in and crops the edges.";

        private const string TipSample =
            "Reads the average colour of the four corners and uses that as the background to remove.";

        private const string TipClear =
            "Drops the image chosen for this heart. The saved logo stays on the badge until you press Generate Atlas. Generate then leaves this heart empty.";

        private const string TipBackground =
            "Colour treated as empty space around the logo. Sample Background fills this in from the corners.";

        private const string TipTolerance =
            "How far a pixel can be from the background colour and still be removed. 0 removes only an exact match. 1 removes almost any colour.";

        private const string TipMaskMode =
            "Flood Fill removes background that touches the corners, and keeps holes inside the logo. Color Key removes every pixel close to the background colour, including holes.";

        private const string TipCutout =
            "Paints removed pixels black in the colour atlas. The colour image stays fully opaque. The mask is what the badge uses to light and cut out the logo.";

        private const string TipGenerate =
            "Builds the atlas and mask, then overwrites the textures already used by the badge. You will be asked to confirm.";

        private const string TipColourPreview =
            "Colour for this view, with the heart frame drawn on top. The frame is only a guide and is not saved into the atlas.";

        private const string TipMaskPreview =
            "White is the logo the badge keeps. Black is the background it cuts away.";

        private const string TipLockedTexture =
            "This image is stored on the Atlas Generator config. It cannot be changed from this window.";

        private const string TipLockedSetting =
            "This value is stored on the Atlas Generator config. It cannot be changed from this window.";

        private static readonly Rect FullTexCoords = new Rect(0f, 0f, 1f, 1f);

        private static bool bakingFromSettings;

        private readonly CustomSlot custom1 = new CustomSlot(0, 0);
        private readonly CustomSlot custom2 = new CustomSlot(1, 0);

        private AtlasGeneratorConfig assets;
        private Texture2D bannerLogo;
        private Texture2D colourPreviewTexture;
        private Texture2D maskPreviewTexture;
        private GUIContent[] viewContents;
        private GUIStyle bannerTitleStyle;
        private GUIStyle bannerSubtitleStyle;
        private GUIStyle bannerCreditStyle;

        private TextureUtility.PixelBuffer savedColour;
        private TextureUtility.PixelBuffer savedMask;
        private bool savedCacheReady;

        private int viewIndex;
        private bool showingSavedAtlas = true;
        private bool previewDirty;
        private double previewUpdateTime;

        private enum AtlasMode
        {
            Colour,
            Mask,
            MaskG,
            MaskPreview
        }

        private struct AtlasSlot
        {
            public Texture2D Texture;
            public MaskMode MaskMode;
            public Color Background;
            public float Tolerance;
            public bool CutoutBackground;
            public int Column;
            public int Row;
            public float Scale;
        }

        private struct ProcessedAtlasSlot
        {
            public ProcessedTextures Texture;
            public bool CutoutBackground;
            public int Column;
            public int Row;
        }

        private struct ProcessedTextures
        {
            public Texture2D Colour;
            public Texture2D Mask;
        }

        private sealed class CustomSlot
        {
            public readonly int Column;
            public readonly int Row;

            public Texture2D Texture;
            public MaskMode MaskMode = MaskMode.FloodFill;
            public Color Background = Color.black;
            public float Tolerance = 0.5f;
            public float Scale = 1f;
            public bool CutoutBackground;
            public bool Cleared;

            public CustomSlot(int column, int row)
            {
                Column = column;
                Row = row;
            }
        }

        [MenuItem("Tools/Eden/Atlas Generator")]
        public static void OpenWindow()
        {
            AtlasGeneratorWindow window = GetWindow<AtlasGeneratorWindow>("Atlas Generator");
            window.minSize = new Vector2(980f, 860f);
        }

        internal static bool TryBakeFromSettings(EdenBadgeSettings.Data session)
        {
            if (session == null)
                return false;

            bakingFromSettings = true;
            AtlasGeneratorWindow window = CreateInstance<AtlasGeneratorWindow>();
            try
            {
                if (!window.HasValidConfig())
                {
                    Debug.LogWarning(
                        "Eden Consent Badge could not rebuild its pictures because the atlas setup is missing.");
                    return false;
                }

                ApplySessionSlot(window.custom1, session.custom1);
                ApplySessionSlot(window.custom2, session.custom2);
                window.WarnIfSavedTextureMissing(session.custom1, "Custom 1");
                window.WarnIfSavedTextureMissing(session.custom2, "Custom 2");
                window.WriteAtlasFiles();
                window.RefreshOpenWindows();
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("Eden Consent Badge could not rebuild its pictures. " + exception.Message);
                return false;
            }
            finally
            {
                DestroyImmediate(window);
                bakingFromSettings = false;
            }
        }

        private void OnEnable()
        {
            if (bakingFromSettings)
            {
                LoadConfig();
                return;
            }

            titleContent = new GUIContent("Atlas Generator");
            minSize = new Vector2(980f, 860f);
            LoadConfig();
            LoadBanner();
            LoadSession();
            EditorApplication.update += Update;

            if (custom1.Texture != null || custom2.Texture != null || custom1.Cleared || custom2.Cleared)
                MarkPreviewDirty();
            else
                ShowSavedAtlas();
        }

        private void OnDisable()
        {
            EditorApplication.update -= Update;
            if (!bakingFromSettings)
                SaveSession();
            DestroyPreviewTextures();
        }

        private void Update()
        {
            if (showingSavedAtlas || !previewDirty)
                return;

            if (EditorApplication.timeSinceStartup < previewUpdateTime)
                return;

            RefreshPreviewTextures();
            Repaint();
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.MouseMove)
                Repaint();

            EnsureUiContent();
            DrawBanner();
            GUILayout.Space(8f);
            viewIndex = GUILayout.Toolbar(viewIndex, viewContents, GUILayout.Height(26f));
            viewIndex = Mathf.Clamp(viewIndex, 0, ViewCount - 1);
            GUILayout.Space(8f);

            if (!HasValidConfig())
            {
                EditorGUILayout.HelpBox(
                    "Atlas Generator config is missing, or it does not point at the colour atlas and the mask.",
                    MessageType.Error);
                EdenBadgeCredits.Draw();
                return;
            }

            switch (viewIndex)
            {
                case 1:
                    DrawLockedSlot(assets.fixedTopLeft, 0, 1, "Eden");
                    break;
                case 2:
                    DrawLockedSlot(assets.fixedTopRight, 1, 1, "Hephia");
                    break;
                case Custom1Tab:
                    DrawCustomSlot(custom1, TipCustom1);
                    break;
                case Custom2Tab:
                    DrawCustomSlot(custom2, TipCustom2);
                    break;
                default:
                    DrawOverview();
                    break;
            }

            if (viewIndex == Custom1Tab || viewIndex == Custom2Tab)
            {
                GUILayout.Space(12f);
                DrawGenerateButton();
            }

            EdenBadgeCredits.Draw();
        }

        private void DrawBanner()
        {
            EnsureBannerStyles();

            Rect banner = GUILayoutUtility.GetRect(0f, 92f, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint)
                return;

            EditorGUI.DrawRect(banner, new Color(0.09f, 0.045f, 0.16f, 1f));
            EditorGUI.DrawRect(
                new Rect(banner.x, banner.yMax - 4f, banner.width, 2f),
                new Color(0.35f, 0.9f, 1f, 1f));
            EditorGUI.DrawRect(
                new Rect(banner.x, banner.yMax - 2f, banner.width, 2f),
                new Color(1f, 0.38f, 0.78f, 1f));

            const float logoHeight = 74f;
            float logoWidth = logoHeight;
            if (bannerLogo != null && bannerLogo.height > 0)
                logoWidth = logoHeight * bannerLogo.width / bannerLogo.height;

            Rect logoRect = new Rect(banner.x + 12f, banner.y + 7f, logoWidth, logoHeight);
            if (bannerLogo != null)
                GUI.DrawTexture(logoRect, bannerLogo, ScaleMode.ScaleToFit, true);

            const float creditWidth = 190f;
            float textX = logoRect.xMax + 16f;
            float textWidth = Mathf.Max(0f, banner.width - textX - creditWidth - 16f);
            GUI.Label(new Rect(textX, banner.y + 22f, textWidth, 28f), "Atlas Generator", bannerTitleStyle);
            GUI.Label(new Rect(textX, banner.y + 48f, textWidth, 20f), "Eden Badge", bannerSubtitleStyle);
            GUI.Label(
                new Rect(banner.xMax - creditWidth - 12f, banner.yMax - 22f, creditWidth, 16f),
                "Atlas system by Rekka",
                bannerCreditStyle);
        }

        private void DrawOverview()
        {
            EditorGUILayout.HelpBox(
                "Eden and Hephia are locked. Choose Custom 1 or Custom 2 to put your own image in a heart.",
                MessageType.Info);
            GUILayout.Space(6f);
            DrawPreviewPair(FullTexCoords, "All four hearts, in colour.", "All four hearts, as a mask.", OverviewPreviewSize);
        }

        private void DrawLockedSlot(AtlasSlotConfig config, int column, int row, string name)
        {
            EditorGUILayout.HelpBox(
                name + " is locked. This logo is shared on every badge and can only be changed on the Atlas Generator config.",
                MessageType.Info);

            DrawPreviewPair(
                QuadrantTexCoords(column, row),
                "This heart only, in colour, with the frame on top.",
                "This heart only. White is kept. Black is cut away.",
                QuadrantPreviewSize);

            GUILayout.Space(8f);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(Content("Texture", TipLockedTexture), EditorStyles.boldLabel);
            EditorGUILayout.ObjectField(Content("Texture", TipLockedTexture), config.texture, typeof(Texture2D), false);
            EditorGUILayout.Slider(Content("Scale", TipLockedSetting), config.scale, 0.25f, 2f);
            EditorGUILayout.EndVertical();

            GUILayout.Space(6f);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(Content("Mask", "How this locked logo separates the picture from its background."), EditorStyles.boldLabel);
            EditorGUILayout.ColorField(Content("Background", TipLockedSetting), config.background);
            EditorGUILayout.Slider(Content("Tolerance", TipLockedSetting), config.tolerance, 0f, 1f);
            EditorGUILayout.EnumPopup(Content("Mask Mode", TipLockedSetting), config.maskMode);
            EditorGUILayout.Toggle(Content("Remove Background", TipLockedSetting), config.cutoutBackground);
            EditorGUILayout.EndVertical();
            EditorGUI.EndDisabledGroup();
        }

        private void DrawCustomSlot(CustomSlot slot, string tabTip)
        {
            if (slot.Cleared)
            {
                EditorGUILayout.HelpBox(
                    "This heart is cleared in the preview. Generate Atlas to save that, or assign a new texture.",
                    MessageType.Warning);
            }
            else if (slot.Texture == null)
            {
                EditorGUILayout.HelpBox(
                    "This heart is showing the logo already saved in the atlas. Assign a texture to replace it.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(tabTip, MessageType.None);
            }

            DrawPreviewPair(
                QuadrantTexCoords(slot.Column, slot.Row),
                "This heart only, in colour, with the frame on top.",
                "This heart only. White is kept. Black is cut away.",
                QuadrantPreviewSize);

            GUILayout.Space(8f);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(Content("Texture", "The picture fitted inside this heart."), EditorStyles.boldLabel);
            slot.Texture = (Texture2D)EditorGUILayout.ObjectField(
                Content("Texture", TipTexture), slot.Texture, typeof(Texture2D), false);
            slot.Scale = EditorGUILayout.Slider(Content("Scale", TipScale), slot.Scale, 0.25f, 2f);

            EditorGUI.BeginDisabledGroup(slot.Texture == null);
            if (GUILayout.Button(Content("Sample Background", TipSample)))
            {
                slot.Background = TextureUtility.SampleBackgroundColour(slot.Texture);
                GUI.changed = true;
            }

            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button(Content("Clear", TipClear)))
            {
                slot.Texture = null;
                slot.Cleared = true;
                GUI.changed = true;
            }

            EditorGUILayout.EndVertical();
            bool textureChanged = EditorGUI.EndChangeCheck();

            GUILayout.Space(6f);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(
                Content("Mask", "Decides which pixels are the logo and which pixels are empty background."),
                EditorStyles.boldLabel);
            slot.Background = EditorGUILayout.ColorField(Content("Background", TipBackground), slot.Background);
            slot.Tolerance = EditorGUILayout.Slider(Content("Tolerance", TipTolerance), slot.Tolerance, 0f, 1f);
            slot.MaskMode = (MaskMode)EditorGUILayout.EnumPopup(Content("Mask Mode", TipMaskMode), slot.MaskMode);
            slot.CutoutBackground = EditorGUILayout.Toggle(Content("Remove Background", TipCutout), slot.CutoutBackground);
            EditorGUILayout.EndVertical();
            bool maskChanged = EditorGUI.EndChangeCheck();

            if (textureChanged || maskChanged)
                OnCustomSlotChanged(slot);
        }

        private void DrawGenerateButton()
        {
            if (GUILayout.Button(Content("Generate Atlas", TipGenerate), GUILayout.Height(40f)))
                GenerateAndSaveAtlas();
        }

        private void DrawPreviewPair(Rect texCoords, string colourTip, string maskTip, float previewSize)
        {
            EditorGUILayout.BeginHorizontal();
            DrawPreview(CurrentColourTexture, "Texture", colourTip, texCoords, true, previewSize);
            GUILayout.Space(10f);
            DrawPreview(CurrentMaskTexture, "Mask", maskTip, texCoords, false, previewSize);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPreview(Texture2D texture, string title, string tooltip, Rect texCoords, bool alphaBlend,
            float previewSize)
        {
            EditorGUILayout.BeginVertical();
            GUILayout.Label(Content(title, tooltip), EditorStyles.boldLabel);
            Rect rect = GUILayoutUtility.GetRect(previewSize, previewSize, GUILayout.ExpandWidth(false));

            if (Event.current.type == EventType.Repaint && texture != null)
                GUI.DrawTextureWithTexCoords(rect, texture, texCoords, alphaBlend);

            if (Event.current.type == EventType.Repaint && assets.overlayTexture != null)
                GUI.DrawTextureWithTexCoords(rect, assets.overlayTexture, texCoords, true);

            EditorGUILayout.EndVertical();
        }

        private void OnCustomSlotChanged(CustomSlot slot)
        {
            if (slot.Texture != null)
                slot.Cleared = false;

            SaveSession();
            if (custom1.Texture != null || custom2.Texture != null || custom1.Cleared || custom2.Cleared)
                MarkPreviewDirty();
            else
                ShowSavedAtlas();
        }

        private void ShowSavedAtlas()
        {
            showingSavedAtlas = true;
            previewDirty = false;
            DestroyPreviewTextures();
        }

        private void MarkPreviewDirty()
        {
            showingSavedAtlas = false;
            previewDirty = true;
            previewUpdateTime = EditorApplication.timeSinceStartup + 0.05d;
        }

        private Texture2D CurrentColourTexture =>
            showingSavedAtlas || colourPreviewTexture == null ? assets.atlasTexture : colourPreviewTexture;

        private Texture2D CurrentMaskTexture =>
            showingSavedAtlas || maskPreviewTexture == null ? assets.atlasMask : maskPreviewTexture;

        private static Rect QuadrantTexCoords(int column, int row)
        {
            return new Rect(column * 0.5f, row * 0.5f, 0.5f, 0.5f);
        }

        private static GUIContent Content(string label, string tooltip)
        {
            return new GUIContent(label, tooltip);
        }

        private void EnsureUiContent()
        {
            if (viewContents != null)
                return;

            viewContents = new[]
            {
                Content("Overview", TipOverview),
                Content("Eden", TipEden),
                Content("Hephia", TipHephia),
                Content("Custom 1", TipCustom1),
                Content("Custom 2", TipCustom2)
            };
        }

        private void EnsureBannerStyles()
        {
            if (bannerTitleStyle == null)
            {
                bannerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 20,
                    alignment = TextAnchor.MiddleLeft
                };
                bannerTitleStyle.normal.textColor = new Color(0.74f, 0.58f, 1f, 1f);

                bannerSubtitleStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleLeft
                };
                bannerSubtitleStyle.normal.textColor = new Color(1f, 0.46f, 0.78f, 1f);
            }

            if (bannerCreditStyle == null)
            {
                bannerCreditStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleRight,
                    fontStyle = FontStyle.Italic
                };
                Color credit = new Color(1f, 0.72f, 0.88f, 1f);
                bannerCreditStyle.normal.textColor = credit;
                bannerCreditStyle.hover.textColor = credit;
                bannerCreditStyle.active.textColor = credit;
                bannerCreditStyle.focused.textColor = credit;
                bannerCreditStyle.onNormal.textColor = credit;
                bannerCreditStyle.onHover.textColor = credit;
                bannerCreditStyle.onActive.textColor = credit;
                bannerCreditStyle.onFocused.textColor = credit;
            }
        }

        private void LoadConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:AtlasGeneratorConfig");
            if (guids.Length == 0)
            {
                Debug.LogError("No AtlasGeneratorConfig asset found.");
                return;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            assets = AssetDatabase.LoadAssetAtPath<AtlasGeneratorConfig>(path);
        }

        private void LoadBanner()
        {
            string[] guids = AssetDatabase.FindAssets("EdenApisLogo t:Texture2D");
            if (guids.Length == 0)
                return;

            bannerLogo = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private void LoadSession()
        {
            EdenBadgeSettings.Data session = EdenBadgeSettings.Load();
            ApplySessionSlot(custom1, session.custom1);
            ApplySessionSlot(custom2, session.custom2);
            if (!EdenBadgeSettings.HadSavedSettings && TryMigrateEditorPrefs())
                SaveSession();
        }

        private void SaveSession()
        {
            EdenBadgeSettings.Save(CaptureSlot(custom1), CaptureSlot(custom2));
        }

        private static void ApplySessionSlot(CustomSlot slot, EdenBadgeSettings.Slot saved)
        {
            if (saved == null)
                return;

            slot.Cleared = saved.cleared;
            slot.Scale = saved.scale <= 0f ? 1f : saved.scale;
            slot.Tolerance = Mathf.Clamp01(saved.tolerance);
            slot.CutoutBackground = saved.cutoutBackground;
            slot.MaskMode = (MaskMode)saved.maskMode;
            slot.Background = new Color(saved.backgroundR, saved.backgroundG, saved.backgroundB, saved.backgroundA);

            if (slot.Cleared || string.IsNullOrEmpty(saved.textureGuid))
            {
                slot.Texture = null;
                return;
            }

            string texturePath = AssetDatabase.GUIDToAssetPath(saved.textureGuid);
            slot.Texture = string.IsNullOrEmpty(texturePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        }

        private static EdenBadgeSettings.Slot CaptureSlot(CustomSlot slot)
        {
            string guid = "";
            if (slot.Texture != null)
                guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(slot.Texture));

            return new EdenBadgeSettings.Slot
            {
                textureGuid = guid ?? "",
                scale = slot.Scale,
                tolerance = slot.Tolerance,
                maskMode = (int)slot.MaskMode,
                cutoutBackground = slot.CutoutBackground,
                backgroundR = slot.Background.r,
                backgroundG = slot.Background.g,
                backgroundB = slot.Background.b,
                backgroundA = slot.Background.a,
                cleared = slot.Cleared
            };
        }

        private bool TryMigrateEditorPrefs()
        {
            const string oldCustom1 = "EdenApis.AtlasGenerator.Custom3";
            const string oldCustom2 = "EdenApis.AtlasGenerator.Custom4";
            bool hasCustom1 = EditorPrefs.HasKey(oldCustom1 + ".scale") || EditorPrefs.HasKey(oldCustom1 + ".guid");
            bool hasCustom2 = EditorPrefs.HasKey(oldCustom2 + ".scale") || EditorPrefs.HasKey(oldCustom2 + ".guid");
            if (!hasCustom1 && !hasCustom2)
                return false;

            ApplyEditorPrefs(custom1, oldCustom1);
            ApplyEditorPrefs(custom2, oldCustom2);
            DeleteEditorPrefs(oldCustom1);
            DeleteEditorPrefs(oldCustom2);
            return true;
        }

        private static void ApplyEditorPrefs(CustomSlot slot, string prefix)
        {
            if (!EditorPrefs.HasKey(prefix + ".scale") && !EditorPrefs.HasKey(prefix + ".guid"))
                return;

            string guid = EditorPrefs.GetString(prefix + ".guid", string.Empty);
            if (!string.IsNullOrEmpty(guid))
            {
                string texturePath = AssetDatabase.GUIDToAssetPath(guid);
                slot.Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            }

            if (!EditorPrefs.HasKey(prefix + ".scale"))
                return;

            slot.Scale = EditorPrefs.GetFloat(prefix + ".scale", slot.Scale);
            slot.Tolerance = EditorPrefs.GetFloat(prefix + ".tolerance", slot.Tolerance);
            slot.CutoutBackground = EditorPrefs.GetBool(prefix + ".cutout", slot.CutoutBackground);
            slot.MaskMode = (MaskMode)EditorPrefs.GetInt(prefix + ".maskMode", (int)slot.MaskMode);
            slot.Background = new Color(
                EditorPrefs.GetFloat(prefix + ".bgR", 0f),
                EditorPrefs.GetFloat(prefix + ".bgG", 0f),
                EditorPrefs.GetFloat(prefix + ".bgB", 0f),
                EditorPrefs.GetFloat(prefix + ".bgA", 1f));
        }

        private static void DeleteEditorPrefs(string prefix)
        {
            EditorPrefs.DeleteKey(prefix + ".guid");
            EditorPrefs.DeleteKey(prefix + ".scale");
            EditorPrefs.DeleteKey(prefix + ".tolerance");
            EditorPrefs.DeleteKey(prefix + ".cutout");
            EditorPrefs.DeleteKey(prefix + ".maskMode");
            EditorPrefs.DeleteKey(prefix + ".bgR");
            EditorPrefs.DeleteKey(prefix + ".bgG");
            EditorPrefs.DeleteKey(prefix + ".bgB");
            EditorPrefs.DeleteKey(prefix + ".bgA");
        }

        private void RefreshPreviewTextures()
        {
            DestroyPreviewTextures();
            ProcessedAtlasSlot[] processed = ProcessAllSlots(PreviewCellSize);
            colourPreviewTexture = BuildAtlasTexture(AtlasMode.Colour, processed, PreviewAtlasSize, PreviewCellSize);
            maskPreviewTexture = BuildAtlasTexture(AtlasMode.MaskPreview, processed, PreviewAtlasSize, PreviewCellSize);
            PreserveSavedCustoms(colourPreviewTexture, maskPreviewTexture, PreviewCellSize, true);
            CleanupProcessedSlots(processed);
            previewDirty = false;
        }

        private void DestroyPreviewTextures()
        {
            if (colourPreviewTexture != null)
            {
                DestroyImmediate(colourPreviewTexture);
                colourPreviewTexture = null;
            }

            if (maskPreviewTexture != null)
            {
                DestroyImmediate(maskPreviewTexture);
                maskPreviewTexture = null;
            }
        }

        private Texture2D BuildAtlasTexture(AtlasMode mode, ProcessedAtlasSlot[] processedSlots, int atlasSize,
            int cellSize)
        {
            Texture2D atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, false);
            Color[] clear = new Color[atlasSize * atlasSize];
            atlas.SetPixels(clear);

            foreach (ProcessedAtlasSlot slot in processedSlots)
            {
                if (mode == AtlasMode.Colour && slot.CutoutBackground)
                    TextureUtility.ApplyMask(slot.Texture.Colour, slot.Texture.Mask);

                Texture2D tex = mode == AtlasMode.Colour ? slot.Texture.Colour : slot.Texture.Mask;
                if (tex == null)
                    continue;

                Color[] pixels = tex.GetPixels();
                switch (mode)
                {
                    case AtlasMode.Colour:
                        for (int i = 0; i < pixels.Length; i++)
                            pixels[i].a = 1f;
                        break;
                    case AtlasMode.Mask:
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            bool hasData = pixels[i].r > 0.001f || pixels[i].g > 0.001f || pixels[i].b > 0.001f;
                            pixels[i].r = hasData ? 1f : 0f;
                            pixels[i].g = hasData ? 1f : 0f;
                            pixels[i].b = hasData ? 1f : 0f;
                            pixels[i].a = hasData ? 0f : 1f;
                        }

                        break;
                    case AtlasMode.MaskG:
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            bool hasData = pixels[i].r > 0.001f || pixels[i].g > 0.001f || pixels[i].b > 0.001f;
                            pixels[i].r = 0f;
                            pixels[i].g = hasData ? 1f : 0f;
                            pixels[i].b = 0f;
                            pixels[i].a = hasData ? 0f : 1f;
                        }

                        break;
                    case AtlasMode.MaskPreview:
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            bool hasData = pixels[i].r > 0.001f || pixels[i].g > 0.001f || pixels[i].b > 0.001f;
                            pixels[i].r = hasData ? 1f : 0f;
                            pixels[i].g = hasData ? 1f : 0f;
                            pixels[i].b = hasData ? 1f : 0f;
                            pixels[i].a = 1f;
                        }

                        break;
                    default:
                        Debug.LogError("Invalid atlas mode: " + mode);
                        continue;
                }

                atlas.SetPixels(slot.Column * cellSize, slot.Row * cellSize, cellSize, cellSize, pixels);
            }

            atlas.Apply();
            return atlas;
        }

        private AtlasSlot[] GetAtlasSlots()
        {
            return new[]
            {
                new AtlasSlot
                {
                    Texture = assets.fixedTopLeft.texture,
                    Background = assets.fixedTopLeft.background,
                    MaskMode = assets.fixedTopLeft.maskMode,
                    Tolerance = assets.fixedTopLeft.tolerance,
                    CutoutBackground = assets.fixedTopLeft.cutoutBackground,
                    Scale = assets.fixedTopLeft.scale,
                    Column = 0,
                    Row = 1
                },
                new AtlasSlot
                {
                    Texture = assets.fixedTopRight.texture,
                    Background = assets.fixedTopRight.background,
                    MaskMode = assets.fixedTopRight.maskMode,
                    Tolerance = assets.fixedTopRight.tolerance,
                    CutoutBackground = assets.fixedTopRight.cutoutBackground,
                    Scale = assets.fixedTopRight.scale,
                    Column = 1,
                    Row = 1
                },
                SlotFromCustom(custom1),
                SlotFromCustom(custom2)
            };
        }

        private static AtlasSlot SlotFromCustom(CustomSlot slot)
        {
            return new AtlasSlot
            {
                Texture = slot.Texture,
                Background = slot.Background,
                MaskMode = slot.MaskMode,
                Tolerance = slot.Tolerance,
                CutoutBackground = slot.CutoutBackground,
                Column = slot.Column,
                Row = slot.Row,
                Scale = slot.Scale
            };
        }

        private ProcessedAtlasSlot[] ProcessAllSlots(int cellSize)
        {
            AtlasSlot[] slots = GetAtlasSlots();
            ProcessedAtlasSlot[] processed = new ProcessedAtlasSlot[slots.Length];

            for (int i = 0; i < slots.Length; i++)
            {
                processed[i] = new ProcessedAtlasSlot
                {
                    Texture = ProcessTexture(
                        slots[i].Texture,
                        slots[i].Background,
                        slots[i].MaskMode,
                        slots[i].Tolerance,
                        slots[i].Scale,
                        cellSize),
                    CutoutBackground = slots[i].CutoutBackground,
                    Column = slots[i].Column,
                    Row = slots[i].Row
                };
            }

            return processed;
        }

        private ProcessedTextures ProcessTexture(Texture2D source, Color background, MaskMode maskMode, float tolerance,
            float scale, int targetSize)
        {
            if (!source)
                return new ProcessedTextures();

            Texture2D colour = TextureUtility.ResizeTexturePreserveAspect(
                source, scale, targetSize, FilterMode.Bilinear);
            Texture2D mask = GenerateMask(source, background, maskMode, tolerance);
            mask = TextureUtility.ResizeTexturePreserveAspect(mask, scale, targetSize, FilterMode.Point);

            return new ProcessedTextures
            {
                Colour = colour,
                Mask = mask
            };
        }

        private Texture2D GenerateMask(Texture2D source, Color background, MaskMode mode, float tolerance)
        {
            return TextureUtility.WithReadableTexture(source, readable =>
            {
                Color[] pixels = readable.GetPixels();
                switch (mode)
                {
                    case MaskMode.FloodFill:
                        return TextureUtility.GenerateFloodFillMask(readable, pixels, background, tolerance);
                    case MaskMode.ColorKey:
                        return TextureUtility.GenerateColorKeyMask(readable, pixels, background, tolerance);
                    default:
                        return null;
                }
            });
        }

        private void GenerateAndSaveAtlas()
        {
            if (!assets)
            {
                Debug.LogError("Cannot generate atlas because the config asset is missing.");
                return;
            }

            if (assets.atlasTexture == null || assets.atlasMask == null)
            {
                Debug.LogError("Cannot regenerate atlas because the generated Atlas or Mask texture is missing.");
                return;
            }

            bool overwrite = EditorUtility.DisplayDialog(
                "Overwrite atlas",
                "This will overwrite the colour atlas and the mask already used by the Eden badge.\n\nEden and Hephia are rebuilt from the badge setup. Custom 1 or Custom 2 is replaced only if you assigned a new texture. A cleared heart is emptied. The other custom heart stays as it is.\n\nThis cannot be undone.",
                "Overwrite",
                "Cancel");
            if (!overwrite)
                return;

            WriteAtlasFiles();
            EdenBadgeSettings.MarkAtlasBaked();
            ShowSavedAtlas();
            Debug.Log("Atlas generated");
        }

        private void WriteAtlasFiles()
        {
            ProcessedAtlasSlot[] processed = ProcessAllSlots(CellSize);
            Texture2D atlas = BuildAtlasTexture(AtlasMode.Colour, processed, AtlasSize, CellSize);
            Texture2D mask = BuildAtlasTexture(AtlasMode.Mask, processed, AtlasSize, CellSize);
            PreserveSavedCustoms(atlas, mask, CellSize, false);

            string colourPath = AssetDatabase.GetAssetPath(assets.atlasTexture);
            File.WriteAllBytes(colourPath, atlas.EncodeToPNG());
            DestroyImmediate(atlas);

            string maskPath = AssetDatabase.GetAssetPath(assets.atlasMask);
            File.WriteAllBytes(maskPath, mask.EncodeToPNG());
            DestroyImmediate(mask);

            AssetDatabase.ImportAsset(colourPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(maskPath, ImportAssetOptions.ForceSynchronousImport);
            ApplyImportSettings(colourPath, AtlasMode.Colour);
            ApplyImportSettings(maskPath, AtlasMode.Mask);
            CleanupProcessedSlots(processed);

            savedCacheReady = false;
            savedColour = default;
            savedMask = default;
        }

        private void RefreshOpenWindows()
        {
            AtlasGeneratorWindow[] open = Resources.FindObjectsOfTypeAll<AtlasGeneratorWindow>();
            for (int i = 0; i < open.Length; i++)
            {
                if (open[i] == null || open[i] == this)
                    continue;

                open[i].savedCacheReady = false;
                open[i].ShowSavedAtlas();
                open[i].Repaint();
            }
        }

        private void WarnIfSavedTextureMissing(EdenBadgeSettings.Slot saved, string label)
        {
            if (saved == null || saved.cleared || string.IsNullOrEmpty(saved.textureGuid))
                return;

            if (!string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(saved.textureGuid)))
                return;

            Debug.LogWarning(
                "Eden Consent Badge could not find the saved " + label + " image, so that heart was rebuilt without it.");
        }

        private void PreserveSavedCustoms(Texture2D colourAtlas, Texture2D maskAtlas, int cellSize, bool previewMask)
        {
            bool keepCustom1 = custom1.Texture == null && !custom1.Cleared;
            bool keepCustom2 = custom2.Texture == null && !custom2.Cleared;
            if (!keepCustom1 && !keepCustom2)
                return;

            EnsureSavedCache();
            if (keepCustom1)
            {
                TextureUtility.CopyQuadrant(savedColour, custom1.Column, custom1.Row, colourAtlas, cellSize, false);
                TextureUtility.CopyQuadrant(savedMask, custom1.Column, custom1.Row, maskAtlas, cellSize, previewMask);
            }

            if (keepCustom2)
            {
                TextureUtility.CopyQuadrant(savedColour, custom2.Column, custom2.Row, colourAtlas, cellSize, false);
                TextureUtility.CopyQuadrant(savedMask, custom2.Column, custom2.Row, maskAtlas, cellSize, previewMask);
            }

            if (colourAtlas != null)
                colourAtlas.Apply();
            if (maskAtlas != null)
                maskAtlas.Apply();
        }

        private void EnsureSavedCache()
        {
            if (savedCacheReady)
                return;

            savedCacheReady = true;
            savedColour = TextureUtility.ReadPixels(assets.atlasTexture);
            savedMask = TextureUtility.ReadPixels(assets.atlasMask);
            if (!savedColour.IsValid || !savedMask.IsValid)
            {
                Debug.LogWarning(
                    "Atlas Generator could not read the saved atlas, so an unchanged custom heart may be left empty.");
            }
        }

        private void CleanupProcessedSlots(ProcessedAtlasSlot[] slots)
        {
            foreach (ProcessedAtlasSlot slot in slots)
            {
                if (slot.Texture.Colour != null)
                    DestroyImmediate(slot.Texture.Colour);
                if (slot.Texture.Mask != null)
                    DestroyImmediate(slot.Texture.Mask);
            }
        }

        private void ApplyImportSettings(string assetPath, AtlasMode mode)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return;

            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = false;
            importer.sRGBTexture = mode == AtlasMode.Colour;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private bool HasValidConfig()
        {
            if (!assets)
                return false;
            if (assets.atlasTexture == null)
                return false;
            if (assets.atlasMask == null)
                return false;
            return true;
        }
    }
}

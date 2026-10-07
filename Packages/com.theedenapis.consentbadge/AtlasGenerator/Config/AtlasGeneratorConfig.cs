using UnityEngine;

namespace EdenApis.AtlasGenerator
{
    public class AtlasGeneratorConfig : ScriptableObject
    {
        public AtlasSlotConfig fixedTopLeft;
        public AtlasSlotConfig fixedTopRight;

        public Texture2D overlayTexture;

        public Texture2D atlasTexture;
        public Texture2D atlasMask;
    }

    [System.Serializable]
    public class AtlasSlotConfig
    {
        public Texture2D texture;
        public MaskMode maskMode = MaskMode.FloodFill;
        public Color background = Color.black;
        [Range(0f, 1f)] public float tolerance = 0.5f;
        public bool cutoutBackground = false;
        public float scale = 1f;
    }
}
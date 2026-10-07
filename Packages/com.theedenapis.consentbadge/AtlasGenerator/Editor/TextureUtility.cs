using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EdenApis.AtlasGenerator
{
    internal static class TextureUtility
    {
        #region Structures

        private struct Pixel
        {
            public int X;
            public int Y;

            public Pixel(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        private struct ImporterState
        {
            public bool Readable;

            public bool Crunch;

            public TextureImporterCompression Compression;
        }

        internal struct PixelBuffer
        {
            public Color[] Pixels;
            public int Width;
            public int Height;

            public bool IsValid => Pixels != null && Width > 1 && Height > 1;
        }

        #endregion

        #region Public API Calls

        internal static Texture2D ResizeTexturePreserveAspect(Texture2D source, float scaleModifier, int targetSize,
            FilterMode filterMode)
        {
            RenderTexture rt = RenderTexture.GetTemporary(targetSize, targetSize, 0,
                RenderTextureFormat.ARGB32);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            GL.Clear(true, true, Color.clear);

            GL.PushMatrix();
            GL.LoadPixelMatrix(0, targetSize, targetSize, 0);

            float srcWidth = source.width;
            float srcHeight = source.height;

            float scale = Mathf.Min(
                targetSize / srcWidth,
                targetSize / srcHeight);

            scale *= scaleModifier;

            float width = srcWidth * scale;
            float height = srcHeight * scale;

            Rect drawRect = new Rect(
                (targetSize - width) * 0.5f,
                (targetSize - height) * 0.5f,
                width, height);

            FilterMode previousMode = source.filterMode;
            source.filterMode = filterMode;

            Graphics.DrawTexture(drawRect, source);

            source.filterMode = previousMode;

            GL.PopMatrix();

            Texture2D result = new Texture2D(targetSize, targetSize,
                TextureFormat.RGBA32, false);

            result.ReadPixels(new Rect(0, 0, targetSize, targetSize), 0, 0);
            result.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return result;
        }

        internal static Color SampleBackgroundColour(Texture2D texture)
        {
            return WithReadableTexture(texture,
                readable => { return SampleBackgroundColourFromReadableTexture(readable); });
        }

        internal static Texture2D GenerateFloodFillMask(Texture2D source, Color[] sourcePixels,
            Color background, float tolerance)
        {
            int width = source.width;
            int height = source.height;

            bool[] visited = new bool[sourcePixels.Length];

            Queue<Pixel> queue = new Queue<Pixel>();

            SeedFloodFill(queue, width, height);
            RunFloodFill(queue, visited, sourcePixels, width, height, background, tolerance);

            return BuildMaskTexture(visited, width, height);
        }

        internal static Texture2D GenerateColorKeyMask(Texture2D source, Color[] sourcePixels,
            Color background, float tolerance)
        {
            int width = source.width;
            int height = source.height;

            Color[] maskPixels = new Color[sourcePixels.Length];

            for (int i = 0; i < sourcePixels.Length; i++)
            {
                bool isBackground = IsBackgroundPixel(sourcePixels[i], background, tolerance);
                maskPixels[i] = isBackground ? Color.black : Color.white;
                float g = sourcePixels[i].grayscale;
            }

            Texture2D mask = new Texture2D(width, height, TextureFormat.RGBA32, false);

            mask.SetPixels(maskPixels);
            mask.Apply();

            return mask;
        }

        internal static PixelBuffer ReadPixels(Texture2D texture)
        {
            if (!texture)
                return default;

            return WithReadableTexture(texture, readable => new PixelBuffer
            {
                Pixels = readable.GetPixels(),
                Width = readable.width,
                Height = readable.height
            });
        }

        internal static void CopyQuadrant(PixelBuffer source, int column, int row, Texture2D destination,
            int destinationCellSize, bool forceOpaque)
        {
            if (!source.IsValid || !destination || destinationCellSize <= 0)
                return;

            int sourceCellWidth = source.Width / 2;
            int sourceCellHeight = source.Height / 2;
            if (sourceCellWidth <= 0 || sourceCellHeight <= 0)
                return;

            int sourceX = column * sourceCellWidth;
            int sourceY = row * sourceCellHeight;
            Color[] destinationPixels = new Color[destinationCellSize * destinationCellSize];

            if (sourceCellWidth == destinationCellSize && sourceCellHeight == destinationCellSize)
            {
                for (int y = 0; y < destinationCellSize; y++)
                {
                    int sourceRow = (sourceY + y) * source.Width + sourceX;
                    int destinationRow = y * destinationCellSize;
                    for (int x = 0; x < destinationCellSize; x++)
                    {
                        Color pixel = source.Pixels[sourceRow + x];
                        if (forceOpaque)
                            pixel.a = 1f;
                        destinationPixels[destinationRow + x] = pixel;
                    }
                }
            }
            else
            {
                for (int y = 0; y < destinationCellSize; y++)
                {
                    float v = ((y + 0.5f) / destinationCellSize) * sourceCellHeight - 0.5f;
                    int y0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, sourceCellHeight - 1);
                    int y1 = Mathf.Min(y0 + 1, sourceCellHeight - 1);
                    float fy = Mathf.Clamp01(v - y0);

                    for (int x = 0; x < destinationCellSize; x++)
                    {
                        float u = ((x + 0.5f) / destinationCellSize) * sourceCellWidth - 0.5f;
                        int x0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, sourceCellWidth - 1);
                        int x1 = Mathf.Min(x0 + 1, sourceCellWidth - 1);
                        float fx = Mathf.Clamp01(u - x0);

                        Color c00 = source.Pixels[(sourceY + y0) * source.Width + sourceX + x0];
                        Color c10 = source.Pixels[(sourceY + y0) * source.Width + sourceX + x1];
                        Color c01 = source.Pixels[(sourceY + y1) * source.Width + sourceX + x0];
                        Color c11 = source.Pixels[(sourceY + y1) * source.Width + sourceX + x1];
                        Color pixel = Color.Lerp(Color.Lerp(c00, c10, fx), Color.Lerp(c01, c11, fx), fy);
                        if (forceOpaque)
                            pixel.a = 1f;
                        destinationPixels[y * destinationCellSize + x] = pixel;
                    }
                }
            }

            destination.SetPixels(
                column * destinationCellSize,
                row * destinationCellSize,
                destinationCellSize,
                destinationCellSize,
                destinationPixels);
        }

        internal static void ApplyMask(Texture2D colour, Texture2D mask)
        {
            if (!colour || !mask)
                return;
            
            Color[] colourPixels = colour.GetPixels();
            Color[] maskPixels = mask.GetPixels();

            for (int i = 0; i < colourPixels.Length; i++)
            {
                if (maskPixels[i].r < 0.99f)
                {
                    colourPixels[i].r = 0f;
                    colourPixels[i].g = 0f;
                    colourPixels[i].b = 0f;
                }

                colourPixels[i].a = maskPixels[i].r;
            }

            colour.SetPixels(colourPixels);
            colour.Apply();
        }

        #endregion

        #region Private Logic

        private static bool IsBackgroundPixel(Color pixel, Color background, float tolerance)
        {
            if (background.a < 0.99f)
                return MatchesBackgroundAlpha(pixel, background.a, tolerance);

            return MatchesBackgroundColour(pixel, background, tolerance);
        }

        private static bool MatchesBackgroundColour(Color pixel, Color background, float tolerance)
        {
            float dr = pixel.r - background.r;
            float dg = pixel.g - background.g;
            float db = pixel.b - background.b;

            float distance = Mathf.Sqrt(
                dr * dr +
                dg * dg +
                db * db);

            return (distance / Mathf.Sqrt(3f)) <= tolerance;
        }

        private static bool MatchesBackgroundAlpha(Color pixel, float backgroundAlpha, float tolerance)
        {
            return Mathf.Abs(pixel.a - backgroundAlpha) <= tolerance;
        }

        private static void SeedFloodFill(Queue<Pixel> queue, int width, int height)
        {
            queue.Enqueue(new Pixel(0, 0));
            queue.Enqueue(new Pixel(width - 1, 0));
            queue.Enqueue(new Pixel(0, height - 1));
            queue.Enqueue(new Pixel(width - 1, height - 1));
        }

        private static void RunFloodFill(Queue<Pixel> queue, bool[] visited, Color[] pixels,
            int width, int height, Color background, float tolerance)
        {
            while (queue.Count > 0)
            {
                Pixel p = queue.Dequeue();

                if (p.X < 0 || p.X >= width ||
                    p.Y < 0 || p.Y >= height)
                    continue;

                int index = p.Y * width + p.X;

                if (visited[index])
                    continue;

                if (!IsBackgroundPixel(pixels[index], background, tolerance))
                    continue;

                visited[index] = true;
                EnqueueNeighbours(queue, p);
            }
        }

        private static void EnqueueNeighbours(Queue<Pixel> queue, Pixel p)
        {
            queue.Enqueue(new Pixel(p.X + 1, p.Y));
            queue.Enqueue(new Pixel(p.X - 1, p.Y));
            queue.Enqueue(new Pixel(p.X, p.Y + 1));
            queue.Enqueue(new Pixel(p.X, p.Y - 1));
        }

        private static Texture2D BuildMaskTexture(bool[] visited, int width, int height)
        {
            Texture2D mask = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false);

            Color[] colours = new Color[visited.Length];

            for (int i = 0; i < visited.Length; i++)
            {
                colours[i] = visited[i]
                    ? Color.black
                    : Color.white;
            }

            mask.SetPixels(colours);
            mask.Apply();

            return mask;
        }

        private static void SampleCorner(Texture2D texture, int startX, int startY, int width,
            int height, ref Color total, ref int count)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color pixel = texture.GetPixel(startX + x, startY + y);
                    if (pixel.a < 0.05f) continue;
                    total += pixel;
                    count++;
                }
            }
        }

        private static Color SampleBackgroundColourFromReadableTexture(Texture2D texture)
        {
            const int sampleSize = 10;

            Color total = Color.black;
            int count = 0;

            int width = Mathf.Min(sampleSize, texture.width);
            int height = Mathf.Min(sampleSize, texture.height);

            TextureUtility.SampleCorner(texture, 0, 0, width, height, ref total, ref count);
            TextureUtility.SampleCorner(texture, texture.width - width, 0, width, height, ref total, ref count);
            TextureUtility.SampleCorner(texture, 0, texture.height - height, width, height, ref total, ref count);
            TextureUtility.SampleCorner(texture, texture.width - width, texture.height - height, width, height,
                ref total, ref count);

            if (count == 0)
                return new Color(0f, 0f, 0f, 0f);

            return total / count;
        }

        #endregion

        #region Import Helpers

        public static T WithReadableTexture<T>(Texture2D texture, Func<Texture2D, T> action)
        {
            string path = AssetDatabase.GetAssetPath(texture);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null)
                return action(texture);

            if (importer.isReadable &&
                (!importer.crunchedCompression ||
                 importer.textureCompression == TextureImporterCompression.Uncompressed))
            {
                return action(texture);
            }

            ImporterState state = MakeTextureReadable(importer);

            try
            {
                Texture2D readable = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                return action(readable);
            }
            finally
            {
                RestoreImporter(importer, state);
            }
        }

        private static ImporterState MakeTextureReadable(TextureImporter importer)
        {
            ImporterState state =
                new ImporterState
                {
                    Readable = importer.isReadable,
                    Compression = importer.textureCompression,
                    Crunch = importer.crunchedCompression
                };

            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.SaveAndReimport();

            return state;
        }

        private static void RestoreImporter(TextureImporter importer, ImporterState state)
        {
            importer.isReadable = state.Readable;
            importer.textureCompression = state.Compression;
            importer.crunchedCompression = state.Crunch;
            importer.SaveAndReimport();
        }

        #endregion
    }
}
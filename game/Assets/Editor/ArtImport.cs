// ═══════════ ArtImport.cs — как Unity импортирует атласы рисунка из полигона (Assets/Resources/Art) ═══════════
// Без сжатия и без перевода из sRGB: шейдер Men перекрашивает маркерный пурпурный цвет стороны по точным значениям
// пикселей, сжатие сдвинуло бы их. Мипмапы — чтобы издали бойцы не рябили.
using UnityEditor;
using UnityEngine;

public sealed class ArtImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith("Assets/Resources/Art/")) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = false;
        // карта объёма (*_n, ArtNormals): в A — металл, а не прозрачность — цвет под нулевой A не трогать
        ti.alphaIsTransparency = !assetPath.EndsWith("_n.png");
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.mipmapEnabled = true;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.filterMode = FilterMode.Trilinear;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.maxTextureSize = 2048;
        ti.npotScale = TextureImporterNPOTScale.None;
    }
}

#if UNITY_EDITOR
using UnityEditor;
using System.IO;
using System.Linq;
using System.Text;
using DawgSharp;

/// <summary>
/// This builds the optimized dictionary DAWG
/// </summary>
public static class DictionaryBuilder
{
    private static readonly Encoding Utf8Strict = new UTF8Encoding(false, true);

    [MenuItem("Tools/Build Dictionary")]
    public static void Build()
    {
        const string input = "Assets/Resources/words.txt";
        const string output = "Assets/Resources/words_dawg.bytes";

        var builder = new DawgBuilder<bool>();

        foreach (var line in File.ReadLines(input, Utf8Strict))
        {
            var w = line.Trim()
                        .ToUpper()
                        .Replace("Ё", "Е");

            if (w.Length < 2 || !w.All(TileInfo.IsValidLetter)) continue;
            
            builder.Insert(w, true);
        }

        var dawg = builder.BuildDawg();

        var file = File.OpenWrite(output);
        dawg.SaveTo(file);
        file.Close();

        AssetDatabase.Refresh();
        UnityEngine.Debug.Log("Dictionary built!");
    }
}
#endif

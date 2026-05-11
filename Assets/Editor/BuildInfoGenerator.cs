#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public sealed class BuildInfoGenerator : IPreprocessBuildWithReport
{
    private const string OutputPath = "Assets/Resources/build_info.txt";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        Generate();
    }

    [MenuItem("Tools/Generate Build Info")]
    public static void Generate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        string timestampUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
        File.WriteAllText(OutputPath, timestampUtc + Environment.NewLine, Utf8NoBom);
        AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);
    }
}
#endif

using UnityEngine;
using UnityEditor;

namespace GPGPUCPU.Editor
{
    public class HexImporter : AssetPostprocessor
    {
        void OnPreprocessAsset()
        {
            if (assetPath.EndsWith(".hex"))
            {
                // .hexファイルをTextAssetとして強制的にインポート
                var importer = assetImporter as PluginImporter;
                if (importer != null)
                {
                    importer.SetCompatibleWithAnyPlatform(false);
                }
            }
        }
    }
    
    // .hexファイルのメタファイルを自動生成
    [InitializeOnLoad]
    public class HexFileSetup
    {
        static HexFileSetup()
        {
            EditorApplication.update += CheckHexFiles;
        }

        static void CheckHexFiles()
        {
            EditorApplication.update -= CheckHexFiles;
            
            string[] hexFiles = System.IO.Directory.GetFiles(Application.dataPath, "*.hex", System.IO.SearchOption.AllDirectories);
            foreach (string filePath in hexFiles)
            {
                string relativePath = "Assets" + filePath.Substring(Application.dataPath.Length).Replace("\\", "/");
                AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
            }
        }
    }
}

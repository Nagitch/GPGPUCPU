using UnityEngine;
using UnityEditor;

namespace GPGPUCPU.Editor
{
    public class FilterPaletteSetup : EditorWindow
    {
        [MenuItem("GPGPUCPU/Setup Filter Palette Scene")]
        static void SetupScene()
        {
            // まずサンプルテクスチャを作成
            CreateSampleTextures();
            
            // カメラを検索または作成
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                GameObject cameraObj = new GameObject("Main Camera");
                mainCamera = cameraObj.AddComponent<Camera>();
                cameraObj.tag = "MainCamera";
                cameraObj.transform.position = new Vector3(0, 0, -10);
            }

            // カメラの設定を確認・修正
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = Color.black;
            mainCamera.orthographic = false;
            mainCamera.enabled = true;

            Debug.Log($"Camera setup: {mainCamera.gameObject.name}, enabled={mainCamera.enabled}");

            // シーンに何もない場合、ダミーのQuadを作成（OnRenderImageが呼ばれるようにするため）
            if (GameObject.FindAnyObjectByType<MeshRenderer>() == null)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "DummyQuad";
                quad.transform.position = new Vector3(0, 0, 0);
                quad.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f); // 小さくして目立たなくする
                MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
                Debug.Log("ダミーQuadを作成しました（OnRenderImage呼び出しのため）");
            }

            // FilterPaletteDemoコンポーネントを追加
            FilterPaletteDemo demo = mainCamera.GetComponent<FilterPaletteDemo>();
            if (demo == null)
            {
                demo = mainCamera.gameObject.AddComponent<FilterPaletteDemo>();
                Debug.Log("FilterPaletteDemoコンポーネントをカメラに追加しました");
            }

            // アセットを自動検索して設定
            AutoAssignAssets(demo);

            Selection.activeGameObject = mainCamera.gameObject;
            Debug.Log("セットアップ完了！Inspectorでアセットを確認してください。");
        }

        static void AutoAssignAssets(FilterPaletteDemo demo)
        {
            // ComputeShaderを検索
            if (demo.shader == null)
            {
                string[] shaderGuids = AssetDatabase.FindAssets("FilterPalette t:ComputeShader");
                if (shaderGuids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(shaderGuids[0]);
                    demo.shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                    Debug.Log($"✓ ComputeShader設定: {path}");
                }
                else
                {
                    Debug.LogError("✗ FilterPalette.computeが見つかりません！");
                }
            }

            // SourceTexを検索
            if (demo.SourceTex == null)
            {
                demo.SourceTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GPGPUCPU/SampleSource.png");
                if (demo.SourceTex != null)
                    Debug.Log($"✓ SourceTex設定: SampleSource.png");
                else
                    Debug.LogError("✗ SampleSource.pngが見つかりません！");
            }

            // IndexTexを検索
            if (demo.IndexTex == null)
            {
                demo.IndexTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GPGPUCPU/SampleIndex.png");
                if (demo.IndexTex != null)
                    Debug.Log($"✓ IndexTex設定: SampleIndex.png");
                else
                    Debug.LogError("✗ SampleIndex.pngが見つかりません！");
            }

            // TextAssetを検索（.hex, .json）
            if (demo.BytecodeHex == null)
            {
                demo.BytecodeHex = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/GPGPUCPU/out/bytecode.hex.txt");
                if (demo.BytecodeHex != null)
                    Debug.Log($"✓ Bytecode Hex設定: bytecode.hex.txt");
                else
                    Debug.LogError("✗ bytecode.hex.txtが見つかりません！");
            }

            if (demo.ProgramsJson == null)
            {
                demo.ProgramsJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/GPGPUCPU/out/programs.json");
                if (demo.ProgramsJson != null)
                    Debug.Log($"✓ Programs JSON設定: programs.json");
                else
                    Debug.LogError("✗ programs.jsonが見つかりません！");
            }

            EditorUtility.SetDirty(demo);
        }

        [MenuItem("GPGPUCPU/Create Sample Textures")]
        static void CreateSampleTextures()
        {
            string basePath = "Assets/GPGPUCPU";
            if (!AssetDatabase.IsValidFolder(basePath))
            {
                Debug.LogError($"フォルダが存在しません: {basePath}");
                return;
            }

            // サンプルのSourceTexture作成（256x256のグラデーション）
            Texture2D sourceTex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 256; x++)
                {
                    float r = x / 255f;
                    float g = y / 255f;
                    float b = (x + y) / 510f;
                    sourceTex.SetPixel(x, y, new Color(r, g, b, 1f));
                }
            }
            sourceTex.Apply();
            SaveTexture(sourceTex, "Assets/GPGPUCPU/SampleSource.png", false);

            // サンプルのIndexTexture作成（パレットインデックス - R8フォーマット）
            Texture2D indexTex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 256; x++)
                {
                    byte index = (byte)((x ^ y) & 0xFF);
                    float val = index / 255f;
                    indexTex.SetPixel(x, y, new Color(val, val, val, 1f));
                }
            }
            indexTex.Apply();
            SaveTexture(indexTex, "Assets/GPGPUCPU/SampleIndex.png", true);

            Debug.Log("✓ サンプルテクスチャを作成しました");
            AssetDatabase.Refresh();
        }

        static void SaveTexture(Texture2D texture, string path, bool isSingleChannel)
        {
            byte[] bytes = texture.EncodeToPNG();
            System.IO.File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path);

            // インポート設定を調整
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.sRGBTexture = !isSingleChannel;
                if (isSingleChannel)
                {
                    importer.textureType = TextureImporterType.SingleChannel;
                }
                importer.SaveAndReimport();
            }
        }
    }
}

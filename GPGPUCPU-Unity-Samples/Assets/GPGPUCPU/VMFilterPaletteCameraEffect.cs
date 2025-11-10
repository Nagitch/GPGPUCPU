using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Applies the VMFilterPalette compute shader to the camera output in real time.
/// Attach this to the camera that should render with the palette filter.
/// </summary>
[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
[ExecuteAlways]
public class VMFilterPaletteCameraEffect : MonoBehaviour
{
    [Header("Compute Shader & VM Assets")]
    public ComputeShader shader;
    public Texture indexTexture;
    public TextAsset bytecodeHex;
    public TextAsset programsJson;

    [Header("VM Controls")]
    public bool disableJumps = true;
    [Range(1, 1024)]
    public int maxSteps = 64;

    RenderTexture _outputRT;
    RenderTexture _scaledIndexRT;
    ComputeBuffer _bytecodeBuffer;
    ComputeBuffer _programBuffer;
    int _kernel = -1;
    bool _buffersReady;
    int _lastWidth = -1;
    int _lastHeight = -1;

    void OnEnable()
    {
        _buffersReady = false;
        InitializeResources();
    }

    void OnDisable()
    {
        ReleaseAll();
    }

    void OnDestroy()
    {
        ReleaseAll();
    }

    void OnValidate()
    {
        // Recreate buffers when asset references change while editing.
        _buffersReady = false;
        InitializeResources();
    }

    void InitializeResources()
    {
        if (_buffersReady)
        {
            return;
        }

        ReleaseBuffers();

        if (shader == null || bytecodeHex == null || programsJson == null)
        {
            return;
        }

        try
        {
            _kernel = shader.FindKernel("Run");
        }
        catch
        {
            Debug.LogError("VMFilterPaletteCameraEffect: kernel 'Run' was not found in the compute shader.");
            return;
        }

        ushort[] words16 = LoadHex(bytecodeHex.text);
        if (words16 == null || words16.Length == 0)
        {
            Debug.LogError("VMFilterPaletteCameraEffect: Failed to parse bytecode hex text.");
            return;
        }

        var programs = LoadPrograms(programsJson.text);
        if (programs == null || programs.Length == 0)
        {
            Debug.LogError("VMFilterPaletteCameraEffect: Failed to parse programs JSON.");
            return;
        }

        var words32 = new uint[words16.Length];
        for (int i = 0; i < words16.Length; i++)
        {
            words32[i] = words16[i];
        }

        _bytecodeBuffer = new ComputeBuffer(words32.Length, sizeof(uint));
        _bytecodeBuffer.SetData(words32);

        _programBuffer = new ComputeBuffer(256, sizeof(int) * 2);
        _programBuffer.SetData(programs);

        shader.SetBuffer(_kernel, "Bytecode", _bytecodeBuffer);
        shader.SetBuffer(_kernel, "Programs", _programBuffer);

        _buffersReady = true;
    }

    void ReleaseAll()
    {
        ReleaseBuffers();
        ReleaseRenderTargets();
    }

    void ReleaseBuffers()
    {
        _bytecodeBuffer?.Dispose();
        _bytecodeBuffer = null;
        _programBuffer?.Dispose();
        _programBuffer = null;
        _buffersReady = false;
    }

    void ReleaseRenderTargets()
    {
        DestroyRenderTexture(ref _outputRT);
        DestroyRenderTexture(ref _scaledIndexRT);

        _lastWidth = -1;
        _lastHeight = -1;
    }

    void DestroyRenderTexture(ref RenderTexture rt)
    {
        if (rt == null)
        {
            return;
        }

        rt.Release();
        if (Application.isPlaying)
        {
            Destroy(rt);
        }
        else
        {
            DestroyImmediate(rt);
        }

        rt = null;
    }

    void EnsureRenderTargets(int width, int height)
    {
        if (_outputRT == null || width != _lastWidth || height != _lastHeight)
        {
            ReleaseRenderTargets();

            _outputRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point
            };
            _outputRT.Create();

            if (indexTexture != null)
            {
                _scaledIndexRT = new RenderTexture(width, height, 0, RenderTextureFormat.R8)
                {
                    enableRandomWrite = false,
                    filterMode = FilterMode.Point
                };
                _scaledIndexRT.Create();
            }

            _lastWidth = width;
            _lastHeight = height;
        }
    }

    void UpdateShaderConstants(int width, int height)
    {
        shader.SetInt("Width", width);
        shader.SetInt("Height", height);
        shader.SetInt("DisableJumps", disableJumps ? 1 : 0);
        shader.SetInt("MaxSteps", Mathf.Clamp(maxSteps, 1, 1024));
    }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        InitializeResources();

        if (!_buffersReady || shader == null)
        {
            Graphics.Blit(src, dst);
            return;
        }

        int width = src.width;
        int height = src.height;
        EnsureRenderTargets(width, height);

        if (_outputRT == null)
        {
            Graphics.Blit(src, dst);
            return;
        }

        if (_scaledIndexRT != null)
        {
            Graphics.Blit(indexTexture, _scaledIndexRT);
            shader.SetTexture(_kernel, "IndexTex", _scaledIndexRT);
        }
        else if (indexTexture != null)
        {
            shader.SetTexture(_kernel, "IndexTex", indexTexture);
        }
        else
        {
            Debug.LogWarning("VMFilterPaletteCameraEffect: Index texture is not assigned. The effect cannot run without it.");
            Graphics.Blit(src, dst);
            return;
        }

        shader.SetTexture(_kernel, "SourceTex", src);
        shader.SetTexture(_kernel, "OutTex", _outputRT);

        UpdateShaderConstants(width, height);

        int groupsX = Mathf.CeilToInt(width / 8.0f);
        int groupsY = Mathf.CeilToInt(height / 8.0f);
        shader.Dispatch(_kernel, groupsX, groupsY, 1);

        Graphics.Blit(_outputRT, dst);
    }

    static ushort[] LoadHex(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return System.Array.Empty<ushort>();
        }

        var list = new List<ushort>();
        var lines = text.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("0x") || trimmed.StartsWith("0X"))
            {
                if (ushort.TryParse(trimmed.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out ushort value))
                {
                    list.Add(value);
                }
            }
        }

        return list.ToArray();
    }

    static Vector2Int[] LoadPrograms(string json)
    {
        var results = new Vector2Int[256];
        for (int i = 0; i < results.Length; i++)
        {
            results[i] = Vector2Int.zero;
        }

        if (string.IsNullOrEmpty(json))
        {
            return results;
        }

        var array = MiniJSON.Json.Deserialize(json) as IList;
        if (array == null)
        {
            return results;
        }

        int count = Mathf.Min(array.Count, results.Length);
        for (int i = 0; i < count; i++)
        {
            if (array[i] is IDictionary dictionary)
            {
                int offset = System.Convert.ToInt32(dictionary["offset"]);
                int length = System.Convert.ToInt32(dictionary["length"]);
                results[i] = new Vector2Int(offset, length);
            }
        }

        return results;
    }
}

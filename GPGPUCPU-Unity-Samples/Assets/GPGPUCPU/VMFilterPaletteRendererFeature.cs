using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// URP renderer feature that runs the VMFilterPalette compute shader as a post-process.
/// Add this feature to a Universal Renderer asset to apply the palette filter to cameras that use it.
/// </summary>
public class VMFilterPaletteRendererFeature : ScriptableRendererFeature
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

    VMFilterPalettePass _pass;

    ComputeBuffer _bytecodeBuffer;
    ComputeBuffer _programBuffer;
    int _kernel = -1;
    bool _buffersReady;

    ComputeShader _lastShader;
    TextAsset _lastBytecode;
    TextAsset _lastPrograms;
    Texture _lastIndexTexture;

    static readonly int SourceTexId = Shader.PropertyToID("SourceTex");
    static readonly int OutTexId = Shader.PropertyToID("OutTex");
    static readonly int IndexTexId = Shader.PropertyToID("IndexTex");

    static readonly int WidthId = Shader.PropertyToID("Width");
    static readonly int HeightId = Shader.PropertyToID("Height");
    static readonly int DisableJumpsId = Shader.PropertyToID("DisableJumps");
    static readonly int MaxStepsId = Shader.PropertyToID("MaxSteps");

    public override void Create()
    {
        _pass = new VMFilterPalettePass(this)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass == null)
        {
            return;
        }

        if (!EnsureResources())
        {
            return;
        }

        if (renderingData.cameraData.isSceneViewCamera)
        {
            return;
        }

        _pass.Setup(renderer.cameraColorTargetHandle);
        renderer.EnqueuePass(_pass);
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
#if UNITY_2023_1_OR_NEWER
        if (_pass != null)
        {
            _pass.ConfigureInput(ScriptableRenderPassInput.Color);
        }
#endif
    }

    public override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        ReleaseBuffers();
        _pass?.Dispose();
        _pass = null;
    }

    bool EnsureResources()
    {
        bool referencesChanged = _lastShader != shader ||
            _lastBytecode != bytecodeHex ||
            _lastPrograms != programsJson ||
            _lastIndexTexture != indexTexture;

        if (referencesChanged)
        {
            ReleaseBuffers();
        }

        if (_buffersReady)
        {
            return true;
        }

        ReleaseBuffers();

        if (shader == null || bytecodeHex == null || programsJson == null)
        {
            return false;
        }

        try
        {
            _kernel = shader.FindKernel("Run");
        }
        catch
        {
            Debug.LogError("VMFilterPaletteRendererFeature: kernel 'Run' was not found in the compute shader.");
            return false;
        }

        ushort[] words16 = LoadHex(bytecodeHex.text);
        if (words16 == null || words16.Length == 0)
        {
            Debug.LogError("VMFilterPaletteRendererFeature: Failed to parse bytecode hex text.");
            return false;
        }

        var programs = LoadPrograms(programsJson.text);
        if (programs == null || programs.Length == 0)
        {
            Debug.LogError("VMFilterPaletteRendererFeature: Failed to parse programs JSON.");
            return false;
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

        _buffersReady = true;
        _lastShader = shader;
        _lastBytecode = bytecodeHex;
        _lastPrograms = programsJson;
        _lastIndexTexture = indexTexture;
        return true;
    }

    void ReleaseBuffers()
    {
        _bytecodeBuffer?.Dispose();
        _bytecodeBuffer = null;
        _programBuffer?.Dispose();
        _programBuffer = null;
        _buffersReady = false;
        _kernel = -1;
        _lastShader = null;
        _lastBytecode = null;
        _lastPrograms = null;
        _lastIndexTexture = null;
    }

    void UpdateShaderConstants(CommandBuffer cmd, int width, int height)
    {
        cmd.SetComputeIntParam(shader, WidthId, width);
        cmd.SetComputeIntParam(shader, HeightId, height);
        cmd.SetComputeIntParam(shader, DisableJumpsId, disableJumps ? 1 : 0);
        cmd.SetComputeIntParam(shader, MaxStepsId, Mathf.Clamp(maxSteps, 1, 1024));
    }

    class VMFilterPalettePass : ScriptableRenderPass
    {
        readonly VMFilterPaletteRendererFeature _feature;
        RTHandle _cameraColorTarget;
        RTHandle _outputHandle;
        RTHandle _scaledIndexHandle;

        public VMFilterPalettePass(VMFilterPaletteRendererFeature feature)
        {
            _feature = feature;
        }

        public void Setup(RTHandle cameraColorTarget)
        {
            _cameraColorTarget = cameraColorTarget;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_feature.shader == null || !_feature._buffersReady)
            {
                return;
            }

            if (_feature.indexTexture == null)
            {
                Debug.LogWarning("VMFilterPaletteRendererFeature: Index texture is not assigned. Skipping effect.");
                return;
            }

            var cameraDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            cameraDescriptor.depthBufferBits = 0;
            cameraDescriptor.msaaSamples = 1;
            cameraDescriptor.enableRandomWrite = true;
            cameraDescriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;

            RenderingUtils.ReAllocateIfNeeded(ref _outputHandle, cameraDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_VMFilterPaletteOutput");

            var indexDescriptor = cameraDescriptor;
            indexDescriptor.enableRandomWrite = false;
            indexDescriptor.graphicsFormat = GraphicsFormat.R8_UNorm;
            RenderingUtils.ReAllocateIfNeeded(ref _scaledIndexHandle, indexDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_VMFilterPaletteIndex");

            var cmd = CommandBufferPool.Get("VMFilterPalette");

            cmd.SetComputeBufferParam(_feature.shader, _feature._kernel, "Bytecode", _feature._bytecodeBuffer);
            cmd.SetComputeBufferParam(_feature.shader, _feature._kernel, "Programs", _feature._programBuffer);

            cmd.Blit(_feature.indexTexture, _scaledIndexHandle);

            cmd.SetComputeTextureParam(_feature.shader, _feature._kernel, SourceTexId, _cameraColorTarget);
            cmd.SetComputeTextureParam(_feature.shader, _feature._kernel, OutTexId, _outputHandle);
            cmd.SetComputeTextureParam(_feature.shader, _feature._kernel, IndexTexId, _scaledIndexHandle);

            int width = cameraDescriptor.width;
            int height = cameraDescriptor.height;
            _feature.UpdateShaderConstants(cmd, width, height);

            int groupsX = Mathf.CeilToInt(width / 8.0f);
            int groupsY = Mathf.CeilToInt(height / 8.0f);
            cmd.DispatchCompute(_feature.shader, _feature._kernel, groupsX, groupsY, 1);

            Blitter.BlitCameraTexture(cmd, _outputHandle, _cameraColorTarget);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            RenderingUtils.ReleaseRTHandle(ref _outputHandle);
            RenderingUtils.ReleaseRTHandle(ref _scaledIndexHandle);
        }
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

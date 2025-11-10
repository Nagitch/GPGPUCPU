using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

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

        renderer.EnqueuePass(_pass);
    }

    [System.Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        if (_pass != null)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            _pass.Setup(renderer.cameraColorTargetHandle);
#pragma warning restore CS0618 // Type or member is obsolete
#if UNITY_2023_1_OR_NEWER
            _pass.ConfigureInput(ScriptableRenderPassInput.Color);
#endif
        }
    }

    protected override void Dispose(bool disposing)
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

        [System.Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
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

            RenderingUtils.ReAllocateHandleIfNeeded(ref _outputHandle, cameraDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_VMFilterPaletteOutput");

            var indexDescriptor = cameraDescriptor;
            indexDescriptor.enableRandomWrite = false;
            indexDescriptor.graphicsFormat = GraphicsFormat.R8_UNorm;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _scaledIndexHandle, indexDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_VMFilterPaletteIndex");

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

        // Render Graph implementation for Unity 6+
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
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

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            
            var sourceTexture = resourceData.activeColorTexture;
            
            using (var builder = renderGraph.AddUnsafePass<VMFilterPalettePassData>("VMFilterPalette", out var passData))
            {
                var cameraDescriptor = cameraData.cameraTargetDescriptor;
                cameraDescriptor.depthBufferBits = 0;
                cameraDescriptor.msaaSamples = 1;
                cameraDescriptor.enableRandomWrite = true;
                cameraDescriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;

                var indexDescriptor = cameraDescriptor;
                indexDescriptor.enableRandomWrite = false;
                indexDescriptor.graphicsFormat = GraphicsFormat.R8_UNorm;

                // Create render graph textures
                var outputTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, cameraDescriptor, "_VMFilterPaletteOutput", false);
                var scaledIndexTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, indexDescriptor, "_VMFilterPaletteIndex", false);

                // Pass data
                passData.feature = _feature;
                passData.sourceTexture = sourceTexture;
                passData.outputTexture = outputTexture;
                passData.scaledIndexTexture = scaledIndexTexture;
                passData.cameraDescriptor = cameraDescriptor;

                builder.UseTexture(sourceTexture, AccessFlags.Read);
                builder.UseTexture(outputTexture, AccessFlags.Write);
                builder.UseTexture(scaledIndexTexture, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((VMFilterPalettePassData data, UnsafeGraphContext context) =>
                {
                    // Get CommandBuffer from UnsafeCommandBuffer
                    var unsafeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                    // Set compute buffers
                    unsafeCmd.SetComputeBufferParam(data.feature.shader, data.feature._kernel, "Bytecode", data.feature._bytecodeBuffer);
                    unsafeCmd.SetComputeBufferParam(data.feature.shader, data.feature._kernel, "Programs", data.feature._programBuffer);

                    // Blit index texture to scaled index texture
                    unsafeCmd.Blit(data.feature.indexTexture, data.scaledIndexTexture);

                    // Set compute textures
                    unsafeCmd.SetComputeTextureParam(data.feature.shader, data.feature._kernel, SourceTexId, data.sourceTexture);
                    unsafeCmd.SetComputeTextureParam(data.feature.shader, data.feature._kernel, OutTexId, data.outputTexture);
                    unsafeCmd.SetComputeTextureParam(data.feature.shader, data.feature._kernel, IndexTexId, data.scaledIndexTexture);

                    // Update shader constants
                    int width = data.cameraDescriptor.width;
                    int height = data.cameraDescriptor.height;
                    data.feature.UpdateShaderConstants(unsafeCmd, width, height);

                    // Dispatch compute shader
                    int groupsX = Mathf.CeilToInt(width / 8.0f);
                    int groupsY = Mathf.CeilToInt(height / 8.0f);
                    unsafeCmd.DispatchCompute(data.feature.shader, data.feature._kernel, groupsX, groupsY, 1);

                    // Blit result back to source
                    Blitter.BlitCameraTexture(unsafeCmd, data.outputTexture, data.sourceTexture);
                });
            }
        }

        private class VMFilterPalettePassData
        {
            public VMFilterPaletteRendererFeature feature;
            public TextureHandle sourceTexture;
            public TextureHandle outputTexture;
            public TextureHandle scaledIndexTexture;
            public RenderTextureDescriptor cameraDescriptor;
        }

        public void Dispose()
        {
            _outputHandle?.Release();
            _scaledIndexHandle?.Release();
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

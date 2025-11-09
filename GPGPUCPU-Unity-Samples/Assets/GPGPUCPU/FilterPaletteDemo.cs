// FilterPaletteDemo.cs
using UnityEngine;
using System.Collections.Generic;
public class FilterPaletteDemo : MonoBehaviour
{
    public ComputeShader shader;
    public Texture2D SourceTex;
    public Texture2D IndexTex;
    public TextAsset BytecodeHex;
    public TextAsset ProgramsJson;
    [Header("VM Controls")]
    public bool DisableJumps = true;
    public int MaxSteps = 64;
    RenderTexture _outRT;
    ComputeBuffer _bcBuf;
    ComputeBuffer _progBuf;
    int _kernel;
    bool _loggedRender = false;
    void Start()
    {
        // 必須アセットのチェック
        if (shader == null) { Debug.LogError("ComputeShader が設定されていません！"); return; }
        if (SourceTex == null) { Debug.LogError("SourceTex が設定されていません！"); return; }
        if (IndexTex == null) { Debug.LogError("IndexTex が設定されていません！"); return; }
        if (BytecodeHex == null) { Debug.LogError("BytecodeHex が設定されていません！"); return; }
        if (ProgramsJson == null) { Debug.LogError("ProgramsJson が設定されていません！"); return; }

        Debug.Log($"FilterPaletteDemo Start: SourceTex={SourceTex.width}x{SourceTex.height}");
        
        _kernel = shader.FindKernel("Run");
        _outRT = new RenderTexture(SourceTex.width, SourceTex.height, 0, RenderTextureFormat.ARGB32);
        _outRT.enableRandomWrite = true;
        _outRT.Create();
        
        ushort[] words = LoadHex(BytecodeHex.text);
        Vector2Int[] progs = LoadPrograms(ProgramsJson.text);
        
        Debug.Log($"Bytecode loaded: {words.Length} words, Programs: {progs.Length} entries");
        _bcBuf = new ComputeBuffer(words.Length, sizeof(ushort));
        _bcBuf.SetData(words);
        _progBuf = new ComputeBuffer(256, sizeof(int)*2);
        _progBuf.SetData(progs);
        shader.SetTexture(_kernel, "SourceTex", SourceTex);
        shader.SetTexture(_kernel, "IndexTex", IndexTex);
        shader.SetTexture(_kernel, "OutTex", _outRT);
        shader.SetBuffer(_kernel, "Bytecode", _bcBuf);
        shader.SetBuffer(_kernel, "Programs", _progBuf);
        shader.SetInt("Width", SourceTex.width);
        shader.SetInt("Height", SourceTex.height);
        shader.SetInt("MaxSteps", Mathf.Clamp(MaxSteps, 1, 1024));
        shader.SetInt("DisableJumps", DisableJumps ? 1 : 0);
        int gx = (SourceTex.width + 7) / 8;
        int gy = (SourceTex.height + 7) / 8;
        shader.Dispatch(_kernel, gx, gy, 1);
        
        Debug.Log($"ComputeShader dispatched: groups={gx}x{gy}, Output RT created");
    }
    void Update()
    {
        shader.SetInt("DisableJumps", DisableJumps ? 1 : 0);
        shader.SetInt("MaxSteps", Mathf.Clamp(MaxSteps, 1, 1024));
    }
    void OnDestroy()
    {
        _bcBuf?.Dispose();
        _progBuf?.Dispose();
        if (_outRT != null) _outRT.Release();
    }
    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (_outRT != null)
        {
            Graphics.Blit(_outRT, dst);
            if (!_loggedRender)
            {
                Debug.Log("✓ OnRenderImage called - rendering output");
                _loggedRender = true;
            }
        }
        else
        {
            Graphics.Blit(src, dst);
            Debug.LogWarning("OnRenderImage: _outRT is null!");
        }
    }

    public RenderTexture GetOutputTexture()
    {
        return _outRT;
    }

    static ushort[] LoadHex(string text)
    {
        var lines = text.Split(new[] {'\r','\n'}, System.StringSplitOptions.RemoveEmptyEntries);
        var list = new List<ushort>();
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.StartsWith("0x") || t.StartsWith("0X"))
            {
                if (ushort.TryParse(t.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out ushort v))
                    list.Add(v);
            }
        }
        return list.ToArray();
    }
    static Vector2Int[] LoadPrograms(string json)
    {
        var arr = MiniJSON.Json.Deserialize(json) as System.Collections.IList;
        var res = new Vector2Int[256];
        for (int i=0; i<256; i++) res[i] = Vector2Int.zero;
        for (int i=0; i<res.Length && i<arr.Count; i++)
        {
            var obj = (System.Collections.IDictionary)arr[i];
            int off = System.Convert.ToInt32(obj["offset"]);
            int len = System.Convert.ToInt32(obj["length"]);
            res[i] = new Vector2Int(off, len);
        }
        return res;
    }
}
namespace MiniJSON
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Text;
    public static class Json
    {
        public static object Deserialize(string json)
        {
            if (json == null) return null;
            return Parser.Parse(json);
        }
        sealed class Parser : IDisposable
        {
            const string WORD_BREAK = "{}[],:\"\t\n\r ";
            public static object Parse(string jsonString)
            {
                using (var instance = new Parser(jsonString))
                {
                    return instance.ParseValue();
                }
            }
            System.IO.StringReader json;
            Parser(string jsonString) { json = new System.IO.StringReader(jsonString); }
            public void Dispose() { json.Dispose(); json = null; }
            enum TOKEN { NONE, CURLY_OPEN, CURLY_CLOSE, SQUARE_OPEN, SQUARE_CLOSE, COLON, COMMA, STRING, NUMBER, TRUE, FALSE, NULL }
            object ParseValue()
            {
                switch (NextToken)
                {
                    case TOKEN.STRING:  return ParseString();
                    case TOKEN.NUMBER:  return ParseNumber();
                    case TOKEN.CURLY_OPEN: return ParseObject();
                    case TOKEN.SQUARE_OPEN: return ParseArray();
                    case TOKEN.TRUE: return true;
                    case TOKEN.FALSE: return false;
                    case TOKEN.NULL: return null;
                    default: return null;
                }
            }
            IDictionary ParseObject()
            {
                var table = new Dictionary<string, object>();
                json.Read();
                while (true)
                {
                    switch (NextToken)
                    {
                        case TOKEN.NONE:   return null;
                        case TOKEN.COMMA:  continue;
                        case TOKEN.CURLY_CLOSE: return table;
                        default:
                            string name = ParseString();
                            if (NextToken != TOKEN.COLON) return null;
                            json.Read();
                            table[name] = ParseValue();
                            break;
                    }
                }
            }
            IList ParseArray()
            {
                var array = new List<object>();
                json.Read();
                var parsing = true;
                while (parsing)
                {
                    var token = NextToken;
                    switch (token)
                    {
                        case TOKEN.NONE: return null;
                        case TOKEN.COMMA: continue;
                        case TOKEN.SQUARE_CLOSE: parsing = false; break;
                        default: array.Add(ParseValue()); break;
                    }
                }
                return array;
            }
            string ParseString()
            {
                var s = new System.Text.StringBuilder();
                json.Read();
                while (true)
                {
                    if (json.Peek() == -1) break;
                    var c = NextChar;
                    if (c == '"') break;
                    if (c == '\\')
                    {
                        if (json.Peek() == -1) break;
                        c = NextChar;
                        switch (c)
                        {
                            case '"': s.Append('"'); break;
                            case '\\': s.Append('\\'); break;
                            case '/': s.Append('/'); break;
                            case 'b': s.Append('\b'); break;
                            case 'f': s.Append('\f'); break;
                            case 'n': s.Append('\n'); break;
                            case 'r': s.Append('\r'); break;
                            case 't': s.Append('\t'); break;
                            case 'u':
                                var hex = new char[4];
                                for (int i=0;i<4;i++) hex[i] = NextChar;
                                s.Append((char)System.Convert.ToInt32(new string(hex), 16));
                                break;
                        }
                    }
                    else s.Append(c);
                }
                return s.ToString();
            }
            object ParseNumber()
            {
                var number = NextWord;
                if (number.IndexOf('.') == -1)
                {
                    long parsedInt;
                    long.TryParse(number, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out parsedInt);
                    return parsedInt;
                }
                double parsedDouble;
                double.TryParse(number, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out parsedDouble);
                return parsedDouble;
            }
            void EatWhitespace(){ while (char.IsWhiteSpace(PeekChar)) json.Read(); }
            char PeekChar { get { return System.Convert.ToChar(json.Peek()); } }
            char NextChar { get { return System.Convert.ToChar(json.Read()); } }
            string NextWord { get { var sb=new System.Text.StringBuilder(); while (json.Peek()!=-1 && "{}[],:\"".IndexOf(PeekChar)==-1) sb.Append(NextChar); return sb.ToString(); } }
            TOKEN NextToken
            {
                get
                {
                    EatWhitespace();
                    if (json.Peek() == -1) return TOKEN.NONE;
                    var c = PeekChar;
                    switch (c)
                    {
                        case '{': return TOKEN.CURLY_OPEN;
                        case '}': json.Read(); return TOKEN.CURLY_CLOSE;
                        case '[': return TOKEN.SQUARE_OPEN;
                        case ']': json.Read(); return TOKEN.SQUARE_CLOSE;
                        case ',': json.Read(); return TOKEN.COMMA;
                        case '"': return TOKEN.STRING;
                        case ':': json.Read(); return TOKEN.COLON;
                        case '0':case '1':case '2':case '3':case '4':case '5':case '6':case '7':case '8':case '9':case '-': return TOKEN.NUMBER;
                        default:
                            var w = NextWord;
                            if (w == "true") return TOKEN.TRUE;
                            if (w == "false") return TOKEN.FALSE;
                            if (w == "null") return TOKEN.NULL;
                            return TOKEN.NONE;
                    }
                }
            }
        }
    }
}

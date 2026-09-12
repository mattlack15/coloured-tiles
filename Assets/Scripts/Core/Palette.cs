using UnityEngine;

namespace Jam
{
    public enum ColorId
    {
        Red = 0,
        Green = 1,
        Blue = 2,
        Yellow = 3,
    }

    /// <summary>
    /// Exactly four colours, with no tints, shades or highlights anywhere.
    ///
    /// Tiles and bodies share the same four values, so a red NPC is visibly the same red as the
    /// tile it is running for. The player is distinguished by size and a neutral white halo, never
    /// by a different shade of its own colour - that keeps the read unambiguous.
    /// </summary>
    public static class Palette
    {
        public const int Count = 4;

        static readonly Color[] Colors =
        {
            new Color(0.85f, 0.16f, 0.19f),   // red
            new Color(0.13f, 0.66f, 0.28f),   // green
            new Color(0.14f, 0.36f, 0.85f),   // blue
            new Color(0.93f, 0.75f, 0.08f),   // yellow
        };

        static readonly Material[] Opaque = new Material[Count];
        static Material _neutral;
        static Material _white;
        static Material _surface;
        static Shader _litShader;
        static Shader _unlitShader;

        public static Color Get(ColorId c) => Colors[(int)c];

        static Shader LitShader
        {
            get
            {
                if (_litShader == null)
                {
                    _litShader = Shader.Find("Universal Render Pipeline/Lit");
                    if (_litShader == null) _litShader = Shader.Find("Standard");
                }
                return _litShader;
            }
        }

        static Shader UnlitShader
        {
            get
            {
                if (_unlitShader == null)
                {
                    _unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (_unlitShader == null) _unlitShader = Shader.Find("Unlit/Color");
                    if (_unlitShader == null) _unlitShader = LitShader;
                }
                return _unlitShader;
            }
        }

        /// <summary>One material per colour, shared by tiles and bodies.</summary>
        public static Material Material(ColorId c)
        {
            int i = (int)c;
            if (Opaque[i] == null) Opaque[i] = Create(Colors[i], LitShader);
            return Opaque[i];
        }

        /// <summary>Dark tile with no colour. Not a valid target for anybody.</summary>
        public static Material Neutral
        {
            get
            {
                if (_neutral == null) _neutral = Create(new Color(0.155f, 0.16f, 0.185f), LitShader);
                return _neutral;
            }
        }

        /// <summary>Neutral white. Used for the player halo and target lines - never a fifth colour.</summary>
        public static Material White
        {
            get
            {
                if (_white == null) _white = Create(new Color(1f, 1f, 1f), UnlitShader);
                return _white;
            }
        }

        /// <summary>Dark platform / wall surface.</summary>
        public static Material Surface(Color c)
        {
            if (_surface == null) _surface = Create(c, LitShader);
            return _surface;
        }

        static Material Create(Color c, Shader shader)
        {
            var m = new Material(shader);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.05f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            return m;
        }

        /// <summary>Deterministic 0..1 from a counter, so NPC noise is reproducible per seed.</summary>
        public static float Hash01(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }
}

using UnityEngine;

namespace Jam
{
    /// <summary>Throwaway IMGUI HUD - no uGUI/TMP setup needed for a prototype.</summary>
    public class Hud : MonoBehaviour
    {
        GUIStyle _label;
        GUIStyle _big;
        GUIStyle _small;

        void EnsureStyles()
        {
            if (_label != null) return;

            _label = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            _big = new GUIStyle(GUI.skin.label)
            {
                fontSize = 44,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        }

        void OnGUI()
        {
            var game = GameBootstrap.I;
            if (game == null || game.Board == null || game.Player == null) return;

            EnsureStyles();

            var player = game.Player;
            var board = game.Board;

            // --- top left: objective state ---
            GUI.color = Color.white;
            GUI.Label(new Rect(16, 12, 620, 28),
                $"TIME  {Mathf.Max(0f, game.TimeLeft):0.0}s      CLAIMS  {player.Claims} / {game.targetClaims}",
                _label);

            // --- your colour, as a swatch ---
            var swatch = new Rect(16, 46, 46, 46);
            GUI.color = Palette.Get(player.AssignedColor);
            GUI.DrawTexture(swatch, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(72, 44, 700, 26),
                $"YOUR COLOUR   -   {board.TilesMatchingPlayerColor} lit tiles for you out of {board.LitTileCount} lit on the board", _label);
            GUI.Label(new Rect(72, 68, 620, 22),
                player.ClaimedThisBeat ? "this beat: claimed" : "this beat: still looking...", _small);

            // --- beat meter ---
            var meter = new Rect(16, 100, 240, 10);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(meter, Texture2D.whiteTexture);
            GUI.color = new Color(0.95f, 0.95f, 0.95f, 0.9f);
            GUI.DrawTexture(new Rect(meter.x, meter.y, meter.width * board.BeatProgress, meter.height),
                            Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(264, 94, 400, 24), $"next colour flip  (beat {board.Beat})", _small);

            // --- top right: crowd stats ---
            string mode = game.avoidPlayer ? "NPCs ROUTE AROUND YOU" : "NPCs PRESS INTO YOU";
            GUI.Label(new Rect(Screen.width - 430, 14, 410, 26),
                $"{game.SpawnedNpcs} NPCs   ·   best NPC {game.NpcBest}", _label);
            GUI.Label(new Rect(Screen.width - 430, 42, 410, 22), mode, _small);
            GUI.Label(new Rect(Screen.width - 430, 62, 410, 22),
                game.densitySlowdown ? "crowds SLOW YOU (on)" : "crowds do not slow you (off)", _small);
            GUI.Label(new Rect(Screen.width - 430, 82, 410, 22),
                $"over the side:  you {game.PlayerFalls}   ·   crowd {game.NpcFalls}", _small);

            // --- bottom hints ---
            GUI.Label(new Rect(16, Screen.height - 30, 900, 24),
                "WASD move   ·   Tab target lines   ·   G avoid-player toggle   ·   R restart", _small);

            // --- round end ---
            if (!game.RoundActive)
            {
                var box = new Rect(0, Screen.height * 0.5f - 60, Screen.width, 120);
                GUI.color = new Color(0f, 0f, 0f, 0.65f);
                GUI.DrawTexture(box, Texture2D.whiteTexture);
                GUI.color = game.Won ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.5f, 0.5f);
                GUI.Label(new Rect(0, Screen.height * 0.5f - 52, Screen.width, 60),
                          game.Won ? "GET THROUGH THE CROWD" : "SWALLOWED BY THE CROWD", _big);
                GUI.color = Color.white;
                GUI.Label(new Rect(0, Screen.height * 0.5f + 16, Screen.width, 30),
                          $"claims {game.Player.Claims} / {game.targetClaims}      ·      press R to restart", _label);
            }
        }
    }
}

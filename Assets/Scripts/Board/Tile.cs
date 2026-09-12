using UnityEngine;

namespace Jam
{
    /// <summary>One board cell. Visual only - tiles never block movement.
    ///
    /// A tile is either dark (no colour at all) or lit in one of exactly four colours. Most of the
    /// board is dark, so the lit tiles read as targets rather than as noise.</summary>
    public class Tile : MonoBehaviour
    {
        public Vector2Int Cell;
        public ColorId Current { get; private set; }

        /// <summary>False = this tile has no colour and is not a valid target for anyone.</summary>
        public bool Lit { get; private set; }

        MeshRenderer _renderer;

        public void Bind(MeshRenderer r)
        {
            _renderer = r;
        }

        public void SetLit(ColorId c)
        {
            Lit = true;
            Current = c;
            if (_renderer != null) _renderer.sharedMaterial = Palette.Material(c);
        }

        public void SetDark()
        {
            Lit = false;
            if (_renderer != null) _renderer.sharedMaterial = Palette.Neutral;
        }
    }
}

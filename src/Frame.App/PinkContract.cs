using System.Windows;
using System.Windows.Media;

namespace Frame.App;

// Pink mode scene contract (0.6.0). PinkScene (IModeScene) owns one PinkWorld and drives every effect layer from
// CaptureAtmosphere's clock; layers read and publish shared state here so effects interact instead of running as separate
// overlays (dogs chase butterflies, catch hearts; wind moves petals, flowers, hearts and ribbons together; …).
// Coordinates are DIP in the scene (= window) space; y grows downwards.
sealed class PinkWorld
{
    public double Width { get; set; }     // scene size
    public double Height { get; set; }
    public double Time { get; set; }      // scene seconds (advances only while the scene runs; never catches up)
    public Point? Pointer { get; set; }   // mouse position over the window, null when outside or not over the scene's area
    public double GroundY { get; set; }   // y of the walkable ground line (characters' feet), set by PinkScene from the free UI area
    public Random Random { get; set; } = new(1); // seeded in diagnostics (--seed) for reproducible clips
    // Wind velocity (DIP/s) at a point: one field for all particles, so petals, flowers, hearts and ribbons drift coherently.
    public Func<Point, double, Vector> Wind { get; set; } = (_, _) => new Vector(20, 0);
    // Gust strength at the window centre (0 calm … ~1.6 strongest), set by PinkScene every step: ears, fur, ribbons can lean into it.
    public double Gust { get; set; }
    // Live objects other layers may react to. Each layer adds/removes its own; others only read (or set Claimed / Target).
    // Kinds used: "butterfly", "heart", "petal", "flower", "ribbon", "sparkle", "dog", "secret".
    public readonly List<PinkThing> Things = [];
    // UI rectangles (window space) where nothing interactive should rest or hide, and where characters should not idle
    // (FRAME's controls, text and panels). Refreshed by PinkScene on layout/size changes, never per frame.
    public Rect[] Keepout { get; set; } = [];
    public bool Free(Rect area) { foreach (var r in Keepout) if (r.IntersectsWith(area)) return false; return true; }
    // Shared events between layers, e.g. Raise("heart-caught", p), Raise("bark", p), Raise("secret", p).
    public event Action<string, Point, PinkThing?>? Signal;
    public void Raise(string name, Point at, PinkThing? thing = null) => Signal?.Invoke(name, at, thing);
}

sealed class PinkThing
{
    public string Kind { get; set; } = "";
    public Point Position { get; set; }
    public Vector Velocity { get; set; }
    public double Size { get; set; } = 10; // approximate radius (DIP)
    public bool Landed { get; set; }      // resting (a butterfly on a flower, a sleeping dog)
    public object? Owner { get; set; }    // the layer that created it
    public object? Claimed { get; set; }  // a layer that is interacting with it (e.g. the dog chasing this butterfly)
    public bool Remove { get; set; }      // set by any layer; the owner removes it on its next step
}

// One Pink effect (hearts, flowers, sakura, sparkles, butterflies, ribbons, dogs). Visual is placed by PinkScene in z-order
// (behind FRAME's UI, never hit-testable except deliberate secret elements). Enabled = false must stop all work and hide the
// visual (no per-frame cost). FullRate layers are advanced every composition frame (~60/s; characters), the rest ~30/s.
interface IPinkLayer
{
    string Effect { get; }                // PinkOptions.Effects key
    bool Enabled { get; set; }
    bool FullRate { get; }
    FrameworkElement Visual { get; }
    void Advance(double dt, PinkWorld world);
    void Present(PinkWorld world);        // static state while the clock is stopped (redraw only if needed)
}

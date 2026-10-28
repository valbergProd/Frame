namespace Frame.Core;

// Conservative game detection for "Minimize FRAME when a game starts". Only two signals count:
// a known game executable (GameCatalog plus a detection-only list) or Windows reporting a Direct3D
// exclusive-fullscreen app (SHQueryUserNotificationState = QUNS_RUNNING_D3D_FULL_SCREEN).
// Ordinary fullscreen windows (browsers, video players: QUNS_BUSY) never count. No hooks into games.
public static class GameDetection
{
    // Detection only: these do not create replay folders (routing stays GameCatalog-only).
    static readonly HashSet<string> Extra = new(StringComparer.OrdinalIgnoreCase) {
        "VALORANT-Win64-Shipping.exe", "League of Legends.exe", "GTA5.exe", "GTA5_Enhanced.exe", "RDR2.exe", "Overwatch.exe",
        "eldenring.exe", "Cyberpunk2077.exe", "RainbowSix.exe", "RainbowSix_Vulkan.exe", "cod.exe", "Minecraft.Windows.exe",
        "PUBG-Win64-Shipping.exe", "TslGame.exe", "destiny2.exe", "Warframe.x64.exe", "witcher3.exe", "bf2042.exe",
        "Marvel-Win64-Shipping.exe", "helldivers2.exe", "Palworld-Win64-Shipping.exe", "starfield.exe", "bg3.exe", "bg3_dx11.exe"
    };
    public const int D3DFullscreen = 3; // QUNS_RUNNING_D3D_FULL_SCREEN
    public static bool KnownGame(string? executable) => executable is { Length: > 0 } exe && (GameCatalog.Recognize(exe) != null || Extra.Contains(exe));
    public static bool IsGame(string? executable, int notificationState) => KnownGame(executable) || notificationState == D3DFullscreen;
}

// Decides when to hide FRAME for a game. Hides at most once per game process: if the user reopens FRAME while that
// game keeps running, switching back and forth never hides it again. A different game process re-arms the rule.
public sealed class GameMinimizePolicy
{
    uint handledProcess;
    public uint HandledProcess => handledProcess;
    // busy: an editor/dialog is open, a hotkey is being recorded, or the user worked in FRAME moments ago.
    public bool ShouldHide(uint process, bool isGame, bool enabled, bool frameVisible, bool busy)
    {
        if (!isGame || process == 0 || process == handledProcess) return false;
        // Every first sighting of a game process is consumed, so a refusal (setting off, FRAME busy) is not retried later.
        handledProcess = process;
        return enabled && frameVisible && !busy;
    }
}

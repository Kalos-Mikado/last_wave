using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Run-and-observe capture: Game.exe --scene Shop --screenshot C:/abs/shop.png --after 60
public class ScreenshotOnArg : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        // Story 021: --open-settings shows the Setting screen over the first scene.
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--open-settings") >= 0) LastWave.UI.SettingsPanel.Open(null);
        // Story 024: --open-pause draws the pause menu over the first scene (no room needed; its buttons do nothing).
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--open-pause") >= 0) LastWave.UI.PauseMenu.ShowSample();
        // Story 022: --page play | howtoplay | select opens that menu page (default: the Main Menu).
        var page = Arg("--page");
        var menu = page == null ? null : FindAnyObjectByType<LastWave.Network.RoomMenu>();
        if (menu != null && page == "play") menu.ShowPage(LastWave.Network.RoomMenu.Page.Room);
        if (menu != null && page == "howtoplay") menu.ShowPage(LastWave.Network.RoomMenu.Page.HowToPlay);
        // Story 023: --page select draws the character cards with fake picks (layout only, no room).
        if (menu != null && page == "select") menu.ShowSampleSelect();
        if (Arg("--screenshot") == null) return;
        var go = new GameObject(nameof(ScreenshotOnArg));
        DontDestroyOnLoad(go);
        go.AddComponent<ScreenshotOnArg>();
    }

    static string Arg(string name)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    IEnumerator Start()
    {
        var scene = Arg("--scene");
        if (!string.IsNullOrEmpty(scene)) yield return SceneManager.LoadSceneAsync(scene);
        int after = int.TryParse(Arg("--after"), out var n) ? n : 60;
        for (int i = 0; i < after; i++) yield return null;
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Arg("--screenshot"));
        for (int i = 0; i < 3; i++) yield return null;   // the file is written after the frame
        Application.Quit();
    }
}

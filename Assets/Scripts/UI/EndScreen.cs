using System.Collections.Generic;
using LastWave.Config;
using LastWave.Flow;
using LastWave.Network;
using Unity.Netcode;
using UnityEngine;

namespace LastWave.UI
{
    /// <summary>
    /// Remembers the screen rectangles of IMGUI controls so gameplay code can tell that the mouse is over one
    /// (the Input System knows nothing about IMGUI). IMGUI code calls <see cref="Register"/> every repaint;
    /// <c>PlayerShooting</c> calls <see cref="IsPointerOver"/> from <c>Update</c>, which sees the rectangles
    /// registered during the previous frame's <c>OnGUI</c>. Rectangles older than one frame are ignored.
    /// </summary>
    public static class UiPointerBlock
    {
        private static readonly List<Rect> Rects = new List<Rect>();
        private static int _frame = -1;

        /// <summary>Adds a rectangle for this frame. Call from <c>OnGUI</c> on a repaint, in IMGUI coordinates (origin top-left).</summary>
        /// <param name="guiRect">The rectangle in IMGUI screen coordinates.</param>
        public static void Register(Rect guiRect)
        {
            if (_frame != Time.frameCount)
            {
                Rects.Clear();
                _frame = Time.frameCount;
            }
            Rects.Add(guiRect);
        }

        /// <summary>True when the pointer is inside a rectangle registered in the current or previous frame.</summary>
        /// <param name="screenPosition">Pointer position as the Input System reports it (origin bottom-left).</param>
        public static bool IsPointerOver(Vector2 screenPosition)
        {
            if (Time.frameCount - _frame > 1) return false;
            var gui = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            foreach (var rect in Rects)
            {
                if (rect.Contains(gui)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// The win and lose screens (brief MVP feature 7), drawn in IMGUI on every machine while
    /// <see cref="GameOutcome.Result"/> is not None: a full-screen dim, the VICTORY or DEFEAT banner (story 018) centred,
    /// the cause under it on a loss, a Restart button for the host only (everyone else sees a waiting text) and a Back
    /// to menu button for everyone, both <see cref="UiSkin.Button"/> art buttons. The banner, cause and buttons are
    /// stacked and centred on the screen. It draws above every other IMGUI (lowest GUI depth). Pictures, texts, sizes
    /// (reference pixels, see <see cref="UiSkin.Scale"/>) and colours come from <see cref="GameConfig"/>. It holds no
    /// game state: it only reads <see cref="GameOutcome"/> and asks it to restart.
    /// </summary>
    /// <example>
    /// Scene object in the Game scene (added by <c>LastWaveSetup.Run</c>); nothing to configure.
    /// </example>
    public sealed class EndScreen : MonoBehaviour
    {
        // IMGUI draws the LOWEST depth last, so it is on top; the other scripts keep the default 0.
        private const int GuiDepth = -1000;

        private RoomService _room;

        private void OnGUI()
        {
            var outcome = GameOutcome.Current;
            if (outcome == null || outcome.Result == GameResult.None) return;

            GUI.depth = GuiDepth; // every OnGUI call, layout and repaint alike
            var config = GameConfig.Instance;

            var network = NetworkManager.Singleton;
            bool online = network != null && network.IsListening;
            bool isHost = !online || network.IsServer; // offline = debug preview, shown as the host sees it
            if (_room == null && network != null) _room = network.GetComponent<RoomService>();
            bool busy = _room != null && _room.State == RoomState.Busy; // leaving: no second click

            bool win = outcome.Result == GameResult.Win;
            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint)
            {
                var previousColor = GUI.color;
                GUI.color = config.EndDimColor;
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = previousColor;
            }

            // Layout in screen pixels from reference-pixel config.
            float s = UiSkin.Scale;
            var banner = UiSkin.Trimmed(win ? config.EndWinBannerTexture : config.EndLoseBannerTexture);
            float bannerHeight = config.EndBannerHeight * s;
            float bannerWidth = bannerHeight * banner.width / banner.height;
            float gap = config.EndSpacing * s;
            float causeLine = config.EndCauseFontSize * 1.5f * s;
            float causeHeight = win ? 0f : causeLine + gap;
            float infoHeight = config.EndInfoFontSize * 1.6f * s;
            Vector2 button = config.EndButtonSize * s;
            float restartHeight = isHost ? button.y : infoHeight;
            float total = bannerHeight + gap + causeHeight + restartHeight + gap + button.y;
            float y = (Screen.height - total) * 0.5f;
            float centerX = Screen.width * 0.5f;

            if (repaint) GUI.DrawTexture(new Rect(centerX - bannerWidth * 0.5f, y, bannerWidth, bannerHeight), banner, ScaleMode.ScaleToFit);
            y += bannerHeight + gap;

            if (!win)
            {
                string cause = outcome.Result == GameResult.LoseBase ? config.LoseBaseText : config.LoseEveryoneText;
                UiSkin.Label(new Rect(0f, y, Screen.width, causeLine), cause, config.EndCauseFontSize, TextAnchor.MiddleCenter,
                    config.EndTextColor, FontStyle.Bold);
                y += causeHeight;
            }

            if (busy)
            {
                // Leaving the room: the room service's text ("Leaving...").
                UiSkin.Label(new Rect(0f, y, Screen.width, infoHeight), _room.Message, config.EndInfoFontSize, TextAnchor.MiddleCenter,
                    config.EndTextColor);
            }
            else
            {
                if (isHost)
                {
                    if (UiSkin.Button(new Rect(centerX - button.x * 0.5f, y, button.x, button.y), config.RestartButtonText, config.EndButtonFontSize))
                    {
                        outcome.ServerRestart();
                    }
                }
                else
                {
                    UiSkin.Label(new Rect(0f, y, Screen.width, infoHeight), config.WaitingForHostText, config.EndInfoFontSize,
                        TextAnchor.MiddleCenter, config.EndTextColor);
                }
                y += restartHeight + gap;

                if (UiSkin.Button(new Rect(centerX - button.x * 0.5f, y, button.x, button.y), config.BackToMenuButtonText, config.EndButtonFontSize))
                {
                    if (_room != null && _room.State == RoomState.InRoom) _room.LeaveRoom();
                }
            }

            // A click sponge drawn last: IMGUI gives a click to the first control under it in code order, so the
            // real buttons above win and every other click is swallowed instead of reaching IMGUI below.
            GUI.Button(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, GUIStyle.none);
        }
    }
}

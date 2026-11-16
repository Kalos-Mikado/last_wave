using LastWave.Config;
using LastWave.Flow;
using LastWave.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastWave.UI
{
    /// <summary>
    /// The upgrade cards shown during a wave rest (brief MVP feature 12), drawn in IMGUI on the local machine
    /// while the local player has an open offer (<see cref="PlayerUpgrades.HasOpenOffer"/>): one card per dealt
    /// upgrade (name and value, e.g. "+20% damage"), centred low on the screen, with a short hint above. A card
    /// is picked by clicking it or pressing its number key (1, 2, 3). Nothing is drawn when there is no offer or
    /// once the game has ended. Card rectangles are registered with <see cref="UiPointerBlock"/>, so a click on a
    /// card never fires a shot (story 007 mechanism). It holds no game state: the server decides every pick.
    /// Story 018: each card is the blue frame picture (gold while the mouse is over it) with the upgrade's icon in the
    /// frame's icon window (on a dark fill, so the game never shows through) and the name and value in its lower panel.
    /// Pictures, texts, sizes (reference pixels, see <see cref="UiSkin.Scale"/>), the window and panel rectangles
    /// (fractions of the card) and colours come from <see cref="GameConfig"/>.
    /// </summary>
    /// <example>
    /// Scene object in the Game scene (added by <c>LastWaveSetup.Run</c>); nothing to configure.
    /// </example>
    public sealed class UpgradeCards : MonoBehaviour
    {
        // Above the health bars; below the end screen (-1000).
        private const int GuiDepth = -100;

        // Card text, rebuilt only when the offer changes (no string building per frame).
        private readonly string[] _names = new string[UpgradeRules.MaxChoices];
        private readonly string[] _values = new string[UpgradeRules.MaxChoices];
        private readonly string[] _keys = new string[UpgradeRules.MaxChoices];
        private readonly UpgradeKind[] _kinds = new UpgradeKind[UpgradeRules.MaxChoices];
        private int _builtVersion = -1;
        private PlayerUpgrades _builtFor;

        private bool ShouldShow(out PlayerUpgrades upgrades)
        {
            upgrades = PlayerUpgrades.Local;
            return upgrades != null && upgrades.HasOpenOffer && !GameOutcome.IsOver;
        }

        private void Update()
        {
            if (!ShouldShow(out var upgrades)) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Digit1 up to Digit9 are consecutive keys in the Input System.
            int count = Mathf.Min(upgrades.OfferCount, 9);
            for (int i = 0; i < count; i++)
            {
                if (keyboard[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame)
                {
                    upgrades.RequestPick(i);
                    return;
                }
            }
        }

        private void OnGUI()
        {
            if (!ShouldShow(out var upgrades)) return;

            GUI.depth = GuiDepth; // every OnGUI call, layout and repaint alike
            var config = GameConfig.Instance;
            RebuildTextIfNeeded(upgrades, config);

            int count = upgrades.OfferCount;
            float s = UiSkin.Scale;
            Vector2 size = config.UpgradeCardSize * s;
            float gap = config.UpgradeCardGap * s;
            float totalWidth = count * size.x + (count - 1) * gap;
            float left = (Screen.width - totalWidth) * 0.5f;
            float top = Screen.height - config.UpgradeCardBottomMargin * s - size.y;
            bool repaint = Event.current.type == EventType.Repaint;

            if (repaint)
            {
                float hintHeight = config.UpgradeHintFontSize * 1.6f * s;
                UiSkin.Label(new Rect(0f, top - hintHeight - gap, Screen.width, hintHeight), config.UpgradeHintText,
                    config.UpgradeHintFontSize, TextAnchor.MiddleCenter, config.UpgradeNameColor, FontStyle.Bold);
            }

            Vector2 mouse = Event.current.mousePosition;
            for (int i = 0; i < count; i++)
            {
                var rect = new Rect(left + i * (size.x + gap), top, size.x, size.y);
                if (repaint)
                {
                    UiPointerBlock.Register(rect); // a click on a card is not a shot
                    DrawCard(rect, i, rect.Contains(mouse), config);
                }

                // Invisible button on top: it receives the click. The server confirms by closing the offer.
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    upgrades.RequestPick(i);
                }
            }
        }

        // Dark fill and icon in the window first, then the frame over them (its window is a hole), then the text.
        private void DrawCard(Rect card, int index, bool hover, GameConfig config)
        {
            var window = Part(card, config.UpgradeIconWindow);
            var previousColor = GUI.color;
            GUI.color = config.UpgradeIconBackColor;
            GUI.DrawTexture(window, Texture2D.whiteTexture);
            GUI.color = previousColor;

            float padX = window.width * config.UpgradeIconPadding, padY = window.height * config.UpgradeIconPadding;
            var iconArea = new Rect(window.x + padX, window.y + padY, window.width - 2f * padX, window.height - 2f * padY);
            GUI.DrawTexture(iconArea, UiSkin.UpgradeIcon(_kinds[index]), ScaleMode.ScaleToFit);

            var frame = hover
                ? UiSkin.TrimmedWith(config.UpgradeFrameHoverTexture, config.UpgradeFrameTexture)
                : UiSkin.TrimmedWith(config.UpgradeFrameTexture, config.UpgradeFrameHoverTexture);
            GUI.DrawTexture(card, frame, ScaleMode.StretchToFill);

            float keySize = config.UpgradeKeyFontSize * 1.6f * UiSkin.Scale;
            UiSkin.Label(new Rect(window.x + padX * 0.4f, window.y + padY * 0.3f, keySize, keySize), _keys[index],
                config.UpgradeKeyFontSize, TextAnchor.UpperLeft, config.UpgradeValueColor, FontStyle.Bold);

            var panel = Part(card, config.UpgradeTextPanel);
            float nameHeight = panel.height * config.UpgradeNameShare;
            UiSkin.Label(new Rect(panel.x, panel.y, panel.width, nameHeight), _names[index], config.UpgradeNameFontSize,
                TextAnchor.MiddleCenter, config.UpgradeNameColor, FontStyle.Bold);
            UiSkin.Label(new Rect(panel.x, panel.y + nameHeight, panel.width, panel.height - nameHeight), _values[index],
                config.UpgradeValueFontSize, TextAnchor.UpperCenter, config.UpgradeValueColor);
        }

        // A rectangle given as fractions of the card, in screen pixels.
        private static Rect Part(Rect card, Rect fraction) =>
            new Rect(card.x + fraction.x * card.width, card.y + fraction.y * card.height,
                fraction.width * card.width, fraction.height * card.height);

        private void RebuildTextIfNeeded(PlayerUpgrades upgrades, GameConfig config)
        {
            if (_builtFor == upgrades && _builtVersion == upgrades.OfferVersion) return;
            _builtFor = upgrades;
            _builtVersion = upgrades.OfferVersion;

            for (int i = 0; i < upgrades.OfferCount; i++)
            {
                var definition = UpgradeRules.Find(config.Upgrades, upgrades.GetOfferKind(i));
                string name = definition != null ? definition.DisplayName : upgrades.GetOfferKind(i).ToString();
                string amount = "";
                if (definition != null)
                {
                    amount = definition.IsPercent
                        ? "+" + Mathf.RoundToInt(definition.Value * 100f) + "%"
                        : "+" + Mathf.RoundToInt(definition.Value);
                }
                _names[i] = name;
                _values[i] = amount + " " + name.ToLowerInvariant();
                _keys[i] = (i + 1).ToString();
                _kinds[i] = upgrades.GetOfferKind(i);
            }
        }
    }
}

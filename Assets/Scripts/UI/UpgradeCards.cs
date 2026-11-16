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
    /// Texts, sizes and colours come from <see cref="GameConfig"/>.
    /// </summary>
    /// <example>
    /// Scene object in the Game scene (added by <c>LastWaveSetup.Run</c>); nothing to configure.
    /// </example>
    public sealed class UpgradeCards : MonoBehaviour
    {
        // Above the health bars; below the end screen (-1000).
        private const int GuiDepth = -100;

        private GUIStyle _nameStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _keyStyle;
        private GUIStyle _hintStyle;

        // Card text, rebuilt only when the offer changes (no string building per frame).
        private readonly string[] _names = new string[UpgradeRules.MaxChoices];
        private readonly string[] _values = new string[UpgradeRules.MaxChoices];
        private readonly string[] _keys = new string[UpgradeRules.MaxChoices];
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
            BuildStyles(config);
            RebuildTextIfNeeded(upgrades, config);

            int count = upgrades.OfferCount;
            Vector2 size = config.UpgradeCardSize;
            float gap = config.UpgradeCardGap;
            float totalWidth = count * size.x + (count - 1) * gap;
            float left = (Screen.width - totalWidth) * 0.5f;
            float top = Screen.height - config.UpgradeCardBottomMargin - size.y;
            bool repaint = Event.current.type == EventType.Repaint;

            if (repaint)
            {
                float hintHeight = config.UpgradeHintFontSize * 1.6f;
                GUI.Label(new Rect(0f, top - hintHeight - gap, Screen.width, hintHeight), config.UpgradeHintText, _hintStyle);
            }

            var previousColor = GUI.color;
            Vector2 mouse = Event.current.mousePosition;
            float border = config.UpgradeCardBorderThickness;
            for (int i = 0; i < count; i++)
            {
                var rect = new Rect(left + i * (size.x + gap), top, size.x, size.y);

                if (repaint)
                {
                    UiPointerBlock.Register(rect); // a click on a card is not a shot
                    GUI.color = config.UpgradeCardBorderColor;
                    GUI.DrawTexture(rect, Texture2D.whiteTexture);
                    GUI.color = rect.Contains(mouse) ? config.UpgradeCardHoverColor : config.UpgradeCardBackColor;
                    GUI.DrawTexture(new Rect(rect.x + border, rect.y + border, rect.width - 2f * border, rect.height - 2f * border),
                        Texture2D.whiteTexture);
                    GUI.color = previousColor;

                    GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, config.UpgradeKeyFontSize * 1.6f), _keys[i], _keyStyle);
                    GUI.Label(new Rect(rect.x, rect.y + size.y * 0.22f, rect.width, config.UpgradeNameFontSize * 1.6f), _names[i], _nameStyle);
                    GUI.Label(new Rect(rect.x, rect.y + size.y * 0.58f, rect.width, config.UpgradeValueFontSize * 1.6f), _values[i], _valueStyle);
                }

                // Invisible button on top: it receives the click. The server confirms by closing the offer.
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    upgrades.RequestPick(i);
                }
            }
            GUI.color = previousColor;
        }

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
            }
        }

        private void BuildStyles(GameConfig config)
        {
            _nameStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _nameStyle.fontSize = config.UpgradeNameFontSize;
            _nameStyle.normal.textColor = config.UpgradeNameColor;

            _valueStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            _valueStyle.fontSize = config.UpgradeValueFontSize;
            _valueStyle.normal.textColor = config.UpgradeValueColor;

            _keyStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, fontStyle = FontStyle.Bold };
            _keyStyle.fontSize = config.UpgradeKeyFontSize;
            _keyStyle.normal.textColor = config.UpgradeValueColor;

            _hintStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _hintStyle.fontSize = config.UpgradeHintFontSize;
            _hintStyle.normal.textColor = config.UpgradeNameColor;
        }
    }
}

using DCMultiplayer.Networking;
using HarmonyLib;
using Il2CppSteamworks;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DCMultiplayer.Replication;

// Proof-of-concept "online" badge on the in-game computer's main screen.
// Long-term goal is to host the entire lobby UI on this surface (screen
// inside the world, replaces the IMGUI overlay) — this version just proves
// we can attach a UI element to ComputerShop.mainScreen at runtime.
//
// The ComputerShop GameObject persists for the lifetime of a save; we plant
// the badge once on Awake and refresh its text from Mod.OnUpdate.
internal static class ComputerShopBadge
{
    public static MelonLogger.Instance Log = new("DC_MP_PC");

    static GameObject _badge;
    static TextMeshProUGUI _text;     // canvas-space TMP — lives on mainScreen
    static Il2Cpp.ComputerShop _shop; // remember owning shop so click can toggle screens
    static GameObject _panel;         // our placeholder "Multiplayer" panel
    static TextMeshProUGUI _panelText;

    public static void Plant(Il2Cpp.ComputerShop shop)
    {
        if (shop == null) { Log.Warning("plant: shop null"); return; }
        if (shop.mainScreen == null) { Log.Warning("plant: mainScreen null"); return; }
        if (_badge != null) return;
        _shop = shop;

        // Strategy: clone an existing menu button (Shop / Network Map /
        // Asset Management / Balance Sheet / Hire) so the layout group
        // owns it like any other button. Cloning preserves the
        // RectTransform, Button, Image, label TMP, and Layout cell config
        // — we only have to retitle it and rewire onClick.
        try
        {
            // The mainScreen first child is "Button Grid" (logged earlier
            // as childCount=2). Walk it for the first child carrying a
            // Button component — that's our template button.
            Transform grid = shop.mainScreen.transform.childCount > 0
                ? shop.mainScreen.transform.GetChild(0)
                : null;
            if (grid == null) { Log.Warning("plant: mainScreen has no Button Grid child"); return; }

            // The 5 children of Button Grid are "Icon X bcg" containers
            // (background art); the actual clickable Selectable lives a
            // level or two deeper. We clone the entire icon container
            // (so the layout cell stays intact) and find the Selectable
            // inside it for click-rewiring.
            Log.Msg($"plant: Button Grid has {grid.childCount} children");
            Transform tmpl = null;
            for (int i = 0; i < grid.childCount; i++)
            {
                var c = grid.GetChild(i);
                if (c == null) continue;
                var nestedSel = c.GetComponentInChildren<Selectable>(includeInactive: true);
                if (nestedSel != null && tmpl == null)
                {
                    tmpl = c;
                    Log.Msg($"  [{i}] {c.name}  -> {nestedSel.GetIl2CppType().Name} on '{nestedSel.gameObject.name}'");
                }
            }
            if (tmpl == null)
            {
                // Fallback: just clone the first child even if no
                // Selectable found — at least we'll see it appear in the
                // grid and can iterate next.
                if (grid.childCount > 0) tmpl = grid.GetChild(0);
            }
            if (tmpl == null) { Log.Warning("plant: cannot find a template to clone"); return; }
            Log.Msg($"plant: cloning template '{tmpl.name}'");

            _badge = Object.Instantiate(tmpl.gameObject, grid);
            _badge.name = "DCMP_MultiplayerButton";
            _badge.transform.SetAsLastSibling();

            // Retitle the label. Use the deepest TMP child (the button's
            // visible label) — buttons may also have icon Image children.
            _text = _badge.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (_text != null)
            {
                _text.text = "Multiplayer";
            }

            // The clickable lives inside the icon container as a custom
            // UnityEngine.UI.ButtonExtended (inherits Selectable directly,
            // NOT Button — it carries its own onClick UnityEvent). Cast
            // through Selectable so we follow the il2cpp inheritance graph.
            var sel = _badge.GetComponentInChildren<Selectable>(includeInactive: true);
            var be = sel != null ? sel.TryCast<UnityEngine.UI.ButtonExtended>() : null;
            if (be != null)
            {
                // RemoveAllListeners only drops runtime AddListener entries;
                // the prefab's persistent inspector binding to
                // ButtonShopScreen survives and would still fire on click.
                // Disable each persistent slot explicitly so only our
                // runtime listener runs.
                be.onClick.RemoveAllListeners();
                int persistent = be.onClick.GetPersistentEventCount();
                for (int i = 0; i < persistent; i++)
                    be.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                be.onClick.AddListener(new System.Action(OnMultiplayerButtonClicked));
                Log.Msg($"plant: rewired ButtonExtended.onClick on '{be.gameObject.name}' (disabled {persistent} persistent listener(s))");
            }
            else
            {
                Log.Warning("plant: ButtonExtended cast failed — clicks not rewired");
            }

            // Build the placeholder panel that the click toggles into.
            // We sit it as a sibling of mainScreen (under the same Canvas)
            // so the existing screen-toggle mental model stays intact:
            // showing our panel means hiding mainScreen.
            BuildPanel();

            Log.Msg("Multiplayer button planted in mainScreen Button Grid");
        }
        catch (System.Exception ex)
        {
            Log.Error($"plant failed: {ex.GetType().Name}: {ex.Message}");
            if (_badge != null) try { Object.Destroy(_badge); } catch { }
            _badge = null; _text = null;
        }
    }

    static void BuildPanel()
    {
        if (_panel != null || _shop == null || _shop.mainScreen == null) return;
        try
        {
            // Clone mainScreen itself for a same-shape full-screen panel,
            // then strip out its non-RectTransform components so we don't
            // inherit Button Grid behaviour. We keep the RectTransform
            // (provides the layout host).
            var clone = Object.Instantiate(_shop.mainScreen, _shop.mainScreen.transform.parent);
            clone.name = "DCMP_MultiplayerPanel";
            // Drop all clone children so we start with an empty canvas-sized panel.
            for (int i = clone.transform.childCount - 1; i >= 0; i--)
                Object.Destroy(clone.transform.GetChild(i).gameObject);
            // Drop non-essential clone components (anything that isn't RectTransform / Image / Canvas).
            foreach (var c in clone.GetComponents<Component>())
            {
                if (c == null) continue;
                var n = c.GetIl2CppType().Name;
                if (n == "RectTransform" || n == "Transform" || n == "Image" || n == "CanvasRenderer") continue;
                try { Object.Destroy(c); } catch { }
            }

            // Add a centred TMP child for the "Multiplayer (WIP)" headline.
            // Use the Object.Instantiate-an-existing-TMP trick so we
            // inherit a working RectTransform without fighting IL2CPP.
            var existingTmp = _shop.mainScreen.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (existingTmp != null)
            {
                var headerGo = Object.Instantiate(existingTmp.gameObject, clone.transform);
                headerGo.name = "DCMP_PanelHeader";
                _panelText = headerGo.GetComponent<TextMeshProUGUI>();
                if (_panelText != null)
                {
                    _panelText.text = "DC MULTIPLAYER\n\n(work in progress)\n\nidle";
                    _panelText.alignment = TextAlignmentOptions.Center;
                    _panelText.fontSize = 48;
                }
            }
            else
            {
                Log.Warning("BuildPanel: no TMP template found on mainScreen — header skipped");
            }

            // Back button: the existing secondary screens (Balance Sheet,
            // Asset Management, Hire, Network Map) have a small arrow-icon
            // back button in the top-right corner. Clone the first
            // Selectable we can find on any of those screens — that gives
            // us the right visual + position because we keep its
            // RectTransform anchors. Then rewire its onClick to our
            // handler.
            try
            {
                Transform backTmpl = FindBackButtonTemplate(_shop);
                if (backTmpl != null)
                {
                    Log.Msg($"BuildPanel: cloning back button from '{backTmpl.parent?.name}/{backTmpl.name}'");
                    var backGo = Object.Instantiate(backTmpl.gameObject, clone.transform);
                    backGo.name = "DCMP_BackButton";

                    var backSel = backGo.GetComponentInChildren<Selectable>(includeInactive: true);
                    var backBe = backSel != null ? backSel.TryCast<UnityEngine.UI.ButtonExtended>() : null;
                    if (backBe != null)
                    {
                        backBe.onClick.RemoveAllListeners();
                        int pn = backBe.onClick.GetPersistentEventCount();
                        for (int i = 0; i < pn; i++)
                            backBe.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                        backBe.onClick.AddListener(new System.Action(OnBackClicked));
                        Log.Msg("BuildPanel: back button rewired");
                    }
                }
                else
                {
                    Log.Warning("BuildPanel: no back-button template found on any secondary screen");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"BuildPanel: Back-button setup failed: {ex.Message}");
            }

            clone.SetActive(false);
            _panel = clone;
            Log.Msg("placeholder panel built");
        }
        catch (System.Exception ex)
        {
            Log.Error($"BuildPanel failed: {ex.GetType().Name}: {ex.Message}");
            if (_panel != null) try { Object.Destroy(_panel); } catch { }
            _panel = null; _panelText = null;
        }
    }

    // Walks every secondary screen, lists every Selectable by name +
    // position, and picks one whose name suggests "back/return". We log
    // the full inventory so we can refine the heuristic if no candidate
    // matches by name.
    static Transform FindBackButtonTemplate(Il2Cpp.ComputerShop shop)
    {
        var candidates = new[]
        {
            shop.balanceSheetScreen,
            shop.assetManagementScreen,
            shop.hireScreen,
            shop.networkMapScreen,
        };

        Transform fallback = null;
        foreach (var screen in candidates)
        {
            if (screen == null) continue;
            var sels = screen.GetComponentsInChildren<Selectable>(includeInactive: true);
            Log.Msg($"FindBackButtonTemplate: {screen.name} has {sels.Length} selectables");
            for (int i = 0; i < sels.Length; i++)
            {
                var s = sels[i];
                if (s == null) continue;
                string name = s.gameObject.name;
                string parent = s.transform.parent?.name ?? "<root>";
                Log.Msg($"  - {parent}/{name}");

                string lc = name.ToLowerInvariant();
                if (lc.Contains("back") || lc.Contains("return") || lc.Contains("exit") || lc.Contains("close"))
                {
                    return s.transform;
                }
                // Remember the first Selectable inside a small panel that's
                // NOT a scrollbar/scrollview as a fallback.
                if (fallback == null
                    && !lc.Contains("scroll")
                    && !lc.Contains("slider")
                    && !lc.Contains("dropdown"))
                    fallback = s.transform;
            }
        }
        return fallback;
    }

    static void OnBackClicked()
    {
        if (_shop == null) return;
        try
        {
            if (_panel != null) _panel.SetActive(false);
            if (_shop.mainScreen != null) _shop.mainScreen.SetActive(true);
            Log.Msg("Back to main screen");
        }
        catch (System.Exception ex)
        {
            Log.Error($"back failed: {ex.Message}");
        }
    }

    static void OnMultiplayerButtonClicked()
    {
        string status;
        if (!Il2Cpp.SteamManager.Initialized) status = "Steam not ready";
        else if (!SteamLobby.IsInLobby) status = "idle — F8 to host";
        else
        {
            int n = SteamMatchmaking.GetNumLobbyMembers(SteamLobby.Current);
            string role = SteamLobby.IsHost ? "host" : "client";
            status = $"lobby · {n} member{(n == 1 ? "" : "s")} · {role}";
        }

        // Toggle: hide every other screen, show our placeholder panel.
        // We don't currently rebind the existing back-out path on the
        // computer (ButtonReturnMainScreen) — clicking another menu icon
        // brings the user back to mainScreen via the regular flow.
        if (_panel != null && _shop != null)
        {
            try
            {
                if (_shop.mainScreen != null) _shop.mainScreen.SetActive(false);
                if (_shop.assetManagementScreen != null) _shop.assetManagementScreen.SetActive(false);
                if (_shop.balanceSheetScreen != null) _shop.balanceSheetScreen.SetActive(false);
                if (_shop.hireScreen != null) _shop.hireScreen.SetActive(false);
                if (_shop.networkMapScreen != null) _shop.networkMapScreen.SetActive(false);
                _panel.SetActive(true);
                if (_panelText != null) _panelText.text = $"DC MULTIPLAYER\n\n(work in progress)\n\n{status}";
            }
            catch (System.Exception ex)
            {
                Log.Error($"toggle failed: {ex.Message}");
            }
        }

        Log.Msg($"Multiplayer button clicked — status: {status}");
        EventLog.Emit($"opened multiplayer panel ({status})");
    }

    public static void RefreshText()
    {
        // The button is a static "Multiplayer" label for now; live state
        // lives in the lobby panel we'll mount onto the click. Nothing to
        // refresh here yet — kept as a hook for the next iteration.
    }
}

// Hook ComputerShop's Awake so we can plant the badge as soon as the
// instance comes online (which is also when MainGameManager.instance
// .computerShop becomes non-null).
[HarmonyPatch(typeof(Il2Cpp.ComputerShop), nameof(Il2Cpp.ComputerShop.Awake))]
internal static class CSB_ComputerShop_Awake
{
    static void Postfix(Il2Cpp.ComputerShop __instance) => ComputerShopBadge.Plant(__instance);
}

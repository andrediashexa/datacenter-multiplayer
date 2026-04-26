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
    static GameObject _panel;         // our "Multiplayer" panel
    static TextMeshProUGUI _headerText;
    static TextMeshProUGUI _statusText;
    static TextMeshProUGUI _membersText;
    static TextMeshProUGUI _netText;
    static GameObject _hostButtonGo, _leaveButtonGo, _inviteButtonGo, _copyIdButtonGo, _pingButtonGo;
    static TextMeshProUGUI _mismatchText;
    static Transform _iconButtonTmpl; // a Button Grid icon we clone for actions

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

            // Spawn the four text rows by cloning an existing TMP (an
            // approach we know works under IL2CPP — building a UGUI
            // RectTransform from scratch fails). Each clone is shifted
            // via localPosition so we get a vertical stack without
            // fighting the inherited anchors.
            var existingTmp = _shop.mainScreen.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (existingTmp != null)
            {
                _headerText = SpawnText(existingTmp, clone.transform, "DCMP_Header", "DC MULTIPLAYER", 56, +260f);
                _statusText = SpawnText(existingTmp, clone.transform, "DCMP_Status", "idle", 32, +160f);
                _mismatchText = SpawnText(existingTmp, clone.transform, "DCMP_Mismatch", "", 22, +110f);
                if (_mismatchText != null) _mismatchText.color = new Color(1f, 0.4f, 0.3f);
                _membersText = SpawnText(existingTmp, clone.transform, "DCMP_Members", "", 24, +20f);
                _netText = SpawnText(existingTmp, clone.transform, "DCMP_Net", "", 18, -100f);
            }
            else
            {
                Log.Warning("BuildPanel: no TMP template found on mainScreen — header skipped");
            }

            // Action buttons: clone the entire Button Grid (which already
            // has a HorizontalLayoutGroup or similar that auto-positions
            // its children) so our buttons inherit a working layout
            // without us computing anchors. Then strip the cloned grid's
            // children and re-populate with three retitled icon buttons.
            try
            {
                var sourceGrid = _shop.mainScreen.transform.GetChild(0); // "Button Grid"
                if (sourceGrid != null && sourceGrid.childCount > 0)
                {
                    _iconButtonTmpl = sourceGrid.GetChild(0); // shared by all action buttons

                    var actionGrid = Object.Instantiate(sourceGrid.gameObject, clone.transform);
                    actionGrid.name = "DCMP_ActionGrid";

                    // Strip cloned grid's children — we'll re-fill with our 3.
                    for (int i = actionGrid.transform.childCount - 1; i >= 0; i--)
                        Object.Destroy(actionGrid.transform.GetChild(i).gameObject);

                    _hostButtonGo = SpawnActionButton(actionGrid.transform, "Host Lobby", () => SteamLobby.HostLobby());
                    _leaveButtonGo = SpawnActionButton(actionGrid.transform, "Leave Lobby", () => SteamLobby.Leave());
                    _inviteButtonGo = SpawnActionButton(actionGrid.transform, "Invite", () => SteamLobby.InviteFriendsOverlay());
                    _copyIdButtonGo = SpawnActionButton(actionGrid.transform, "Copy ID", CopyLobbyIdToClipboard);
                    _pingButtonGo = SpawnActionButton(actionGrid.transform, "Ping", BroadcastPing);
                }
                else
                {
                    Log.Warning("BuildPanel: mainScreen has no Button Grid to clone for action row");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"BuildPanel: action grid setup failed: {ex.Message}");
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
            _panel = null; _headerText = null; _statusText = null; _membersText = null; _netText = null; _mismatchText = null;
        }
    }

    static TextMeshProUGUI SpawnText(TextMeshProUGUI template, Transform parent, string name, string text, float fontSize, float yOffset)
    {
        var go = Object.Instantiate(template.gameObject, parent);
        go.name = name;
        go.transform.localPosition = new Vector3(0f, yOffset, 0f);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (t != null)
        {
            t.text = text;
            t.fontSize = fontSize;
            t.alignment = TextAlignmentOptions.Center;
        }
        return t;
    }

    static GameObject SpawnActionButton(Transform parentGrid, string label, System.Action onClick)
    {
        if (_iconButtonTmpl == null || parentGrid == null) return null;
        try
        {
            var go = Object.Instantiate(_iconButtonTmpl.gameObject, parentGrid);
            go.name = $"DCMP_Action_{label}";

            var lbl = go.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (lbl != null) lbl.text = label;

            var sel = go.GetComponentInChildren<Selectable>(includeInactive: true);
            var be = sel != null ? sel.TryCast<UnityEngine.UI.ButtonExtended>() : null;
            if (be != null)
            {
                be.onClick.RemoveAllListeners();
                int pn = be.onClick.GetPersistentEventCount();
                for (int i = 0; i < pn; i++)
                    be.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                be.onClick.AddListener(onClick);
            }
            return go;
        }
        catch (System.Exception ex)
        {
            Log.Warning($"SpawnActionButton('{label}') failed: {ex.Message}");
            return null;
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

    static void CopyLobbyIdToClipboard()
    {
        if (!SteamLobby.IsInLobby) return;
        try
        {
            GUIUtility.systemCopyBuffer = SteamLobby.Current.m_SteamID.ToString();
            EventLog.Emit("copied lobby id to clipboard");
        }
        catch (System.Exception ex) { Log.Warning($"copy id failed: {ex.Message}"); }
    }

    static void BroadcastPing()
    {
        if (!SteamLobby.IsInLobby) { Log.Msg("ping: not in lobby"); return; }
        string text = $"PING from {Il2CppSteamworks.SteamFriends.GetPersonaName()} @ {System.DateTime.Now:HH:mm:ss.fff}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        Transport.Broadcast(Transport.ChControl, bytes);
        EventLog.Emit($"-> {text}");
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
                RefreshText();
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
        if (_panel == null || !_panel.activeSelf) return;
        try
        {
            // Status line + buttons toggled by lobby state
            string status;
            int memberCount = 0;
            if (!Il2Cpp.SteamManager.Initialized)
                status = "Steam not ready";
            else if (!SteamLobby.IsInLobby)
                status = "idle — press Host Lobby to start";
            else
            {
                memberCount = SteamMatchmaking.GetNumLobbyMembers(SteamLobby.Current);
                string role = SteamLobby.IsHost ? "host" : "client";
                status = $"lobby {SteamLobby.Current.m_SteamID}  ·  {memberCount} member{(memberCount == 1 ? "" : "s")}  ·  {role}";
            }
            if (_statusText != null) _statusText.text = status;

            // Member list
            if (_membersText != null)
            {
                if (!SteamLobby.IsInLobby) _membersText.text = "";
                else
                {
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < memberCount; i++)
                    {
                        var id = SteamMatchmaking.GetLobbyMemberByIndex(SteamLobby.Current, i);
                        string name = SteamFriends.GetFriendPersonaName(id);
                        bool isMe = id == SteamUser.GetSteamID();
                        bool isOwner = id == SteamMatchmaking.GetLobbyOwner(SteamLobby.Current);
                        sb.Append("• ").Append(name);
                        if (isMe) sb.Append(" (you)");
                        if (isOwner) sb.Append(" *host");
                        sb.AppendLine();
                    }
                    _membersText.text = sb.ToString();
                }
            }

            // Net stats
            if (_netText != null)
                _netText.text = $"net  rx {Transport.LastRxPackets}p / {Transport.LastRxBytes}B    tx {Transport.LastTxPackets}p / {Transport.LastTxBytes}B";

            // Button visibility — the action grid auto-reflows when
            // children toggle, so we don't have to reposition manually.
            if (_hostButtonGo != null) _hostButtonGo.SetActive(!SteamLobby.IsInLobby);
            if (_leaveButtonGo != null) _leaveButtonGo.SetActive(SteamLobby.IsInLobby);
            if (_inviteButtonGo != null) _inviteButtonGo.SetActive(SteamLobby.IsInLobby);
            if (_copyIdButtonGo != null) _copyIdButtonGo.SetActive(SteamLobby.IsInLobby);
            if (_pingButtonGo != null) _pingButtonGo.SetActive(SteamLobby.IsInLobby);

            // Mismatch banner — surface what was previously a HUD-only
            // visual in v0.0.7 (the lobby version + workshop diff is set
            // by SteamLobby.OnLobbyEntered when joining a host on a
            // different version or with a different workshop manifest).
            if (_mismatchText != null)
            {
                var lines = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(SteamLobby.VersionMismatch))
                    lines.Add($"!! version mismatch — {SteamLobby.VersionMismatch}");
                if (!string.IsNullOrEmpty(SteamLobby.WorkshopMismatch))
                    lines.Add($"!! workshop mismatch — {SteamLobby.WorkshopMismatch}");
                _mismatchText.text = string.Join("\n", lines);
            }
        }
        catch { /* TMP can NRE momentarily during scene tear-down */ }
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

# Data Center — Contexto para Desenvolvimento de Mod Multiplayer

> **Escopo deste workspace:** trabalhamos exclusivamente no jogo **Data Center** (Waseku).
> Mesmo que utilitários como o `FixCoreModule` consigam escanear toda a Steam library,
> aplicar correções/patches **apenas** nesta instalação
> (`K:\SteamLibrary\steamapps\common\Data Center\`). Nada de tocar em outros jogos.
>
> Este arquivo é o briefing técnico para qualquer IA/dev que trabalhar no mod multiplayer
> deste jogo. Atualize-o conforme novas descobertas forem feitas via dnSpy/Cpp2IL.

---

## 1. O Jogo

- **Nome:** Data Center
- **Estúdio:** Waseku (`Data Center_Data/app.info` → `Waseku` / `Data Center`)
- **Gênero:** Tycoon/simulação em primeira pessoa — o jogador monta e opera um data center
  (compra servidores, instala em racks, cabeia switches/patch panels, configura VLANs,
  atende customers, gerencia energia e dinheiro/XP).
- **Estado atual:** **single-player only.** Não há Mirror, Netcode for GameObjects,
  FishNet, Photon ou qualquer biblioteca de replicação no jogo. Steamworks está
  presente, mas usado apenas para Workshop e leaderboards.

### Plataforma técnica

| Item | Valor |
|------|-------|
| Engine | **Unity 6000.4.2f1** (HDRP + DOTS/Entities + Addressables + Cinemachine) |
| Scripting backend | **IL2CPP** (`GameAssembly.dll` na raiz) |
| Render pipeline | HDRP (`Unity.RenderPipelines.HighDefinition.Runtime.dll`) |
| Personagens | UMA (`Il2CppUMA_Core/Content/Examples.dll`) |
| Steam integration | Steamworks.NET (`Il2Cppcom.rlabrecque.steamworks.net.dll`) |
| Input | Unity Input System (`Unity.InputSystem.dll`) |
| Build GUID | `15a4ba4208634dfab0802ece0ae0044e` (`boot.config`) |
| **Steam AppID** | **4170200** (`steam://rungameid/4170200`) |

### Layout em disco (raiz: [./](./))

- [Data Center.exe](Data%20Center.exe) — entrypoint
- [GameAssembly.dll](GameAssembly.dll) — código IL2CPP nativo do jogo
- [UnityPlayer.dll](UnityPlayer.dll) — runtime Unity
- [Data Center_Data/](Data%20Center_Data/) — dados do build
  - `Managed/` não existe (IL2CPP); use proxies em `MelonLoader/Il2CppAssemblies/`
  - [StreamingAssets/aa/](Data%20Center_Data/StreamingAssets/aa/) — Addressables (`catalog.bin`/`catalog.hash`/`settings.json`)
  - [StreamingAssets/Mods/](Data%20Center_Data/StreamingAssets/Mods/) — conteúdo Workshop baixado (pastas `workshop_<id>/` com `config.json`, `model.obj`, `texture.png`, ícones)
  - [StreamingAssets/EntityScenes/](Data%20Center_Data/StreamingAssets/EntityScenes/) — DOTS subscenes
- [MelonLoader/](MelonLoader/) — loader já instalado (versão 0.7.2)
  - [MelonLoader/Il2CppAssemblies/](MelonLoader/Il2CppAssemblies/) — proxies gerados pelo Il2CppInterop. **Use estes como referências `<Reference>` ao compilar o mod.**
- [Mods/](Mods/) — destino dos `.dll` MelonLoader carregados em runtime
- [UserLibs/](UserLibs/) — bibliotecas auxiliares para mods (DLLs compartilhadas)
- [Plugins/](Plugins/) — plugins nativos
- [version.dll](version.dll) — bootstrap proxy do MelonLoader

---

## 2. Modding nesta build (estado conhecido)

A comunidade já tem mods funcionando. Inspecione o log mais recente:
[MelonLoader/Latest.log](MelonLoader/Latest.log).

Mods presentes hoje em `Mods/`:

- `DCAutoLoader.dll` (Joey, v1.1.0) — carrega
- `DCNotepad.dll` (Joey, v1.0.0) — carrega
- `DCMultiplayer.dll` — **falha ao carregar** com `BadImageFormatException: Duplicate type with name '<>O' in assembly 'UnityEngine.CoreModule'`
- [Mods/FixCoreModule-v1.0.2 (1)/](Mods/FixCoreModule-v1.0.2%20%281%29/) — utilitário externo (não é Melon) para corrigir o bug `<>O` do Il2CppInterop no `UnityEngine.CoreModule.dll` gerado.

### Pegadinhas já documentadas

1. **Bug `<>O` do MelonLoader 0.7.2 + Il2CppInterop em Unity 6000.4.x.** Em fresh install (e a cada update do Data Center que regenera os assemblies via Il2CppInterop) é gerado um tipo `<>O` duplicado dentro de `UnityEngine.CoreModule.dll`. Qualquer mod que referencie `UnityEngine.CoreModule` falha com `BadImageFormatException: Duplicate type with name '<>O'` antes mesmo do `MelonInfo` ser lido — é o que está acontecendo com o `DCMultiplayer.dll` no log atual. Issue upstream: [LavaGang/MelonLoader#1142](https://github.com/LavaGang/MelonLoader/issues/1142). Workaround oficial: rodar `FixCoreModule.exe` (V1ndicate1) — usa Mono.Cecil para ler o DLL corrompido, remover os `TypeDef` duplicados e reescrever o assembly, salvando um `.bak`. Repo: [V1ndicate1/FixCoreModule](https://github.com/V1ndicate1/FixCoreModule). Contexto adicional do mod base da comunidade: [V1ndicate1/DCIM-Mod releases](https://github.com/V1ndicate1/DCIM-Mod/releases). Já existe um `DCMultiplayer.dll` pré-existente que **só** falha por causa disso — vale descompilar para ver o que já foi tentado antes de começar do zero.
2. Os warnings `Some Melons are missing dependencies → UnityEngine.CoreModule v0.0.0.0` no log são consequência do mesmo bug; não são problema do mod em si.
3. Existe um `ModLoader` interno do **jogo** (não confundir com MelonLoader) com método `SyncWorkshopThenLoadAll` e `OnModLoad`/`OnModUnload`. Ele é o que consome `StreamingAssets/Mods/workshop_*` (modelos `.obj` + textura + `config.json`). Mods Workshop **não são código**, são assets — totalmente separados de mods MelonLoader/IL2CPP.

### Fluxo de build do mod (recomendado)

1. SDK: .NET 6 (MelonLoader/net6) — ver [MelonLoader/net6/](MelonLoader/net6/).
2. `<Reference>` para os DLLs em `MelonLoader/Il2CppAssemblies/` (especialmente `Assembly-CSharp.dll`, `Il2Cppmscorlib.dll`, `Il2CppSystem.dll`, `UnityEngine.CoreModule.dll`, `Il2Cppcom.rlabrecque.steamworks.net.dll`, `MelonLoader.dll` em `MelonLoader/net6/`).
3. Atributos obrigatórios: `[assembly: MelonInfo(...)]`, `[assembly: MelonGame("Waseku", "Data Center")]`.
4. Output do build → `Mods/SeuMod.dll`.
5. Antes de testar, garantir que `FixCoreModule` já foi rodado nesta instalação.

---

## 3. Mapa do `Assembly-CSharp.dll` (extração de símbolos IL2CPP)

> Os símbolos abaixo foram extraídos automaticamente dos `NativeMethodInfoPtr_*`
> e `NativeFieldInfoPtr_*` do proxy IL2CPP. **1475 métodos / 1921 campos**
> identificados. São pontos de entrada confirmados (existem em runtime), mas as
> assinaturas exatas (tipos de parâmetro/retorno) ainda precisam ser
> confirmadas em dnSpy.

O DLL é grande (~1.8 MB). Use **dnSpy / dnSpyEx** ou **ILSpy** abrindo
`MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll` para confirmar assinaturas.
Os nomes abaixo foram extraídos via análise de strings IL2CPP — **considere-os
ponteiros para investigação**, não fonte de verdade.

### Classes e símbolos relevantes para multiplayer

**Loop principal e save**
- `MainGameManager` — orquestrador da partida; possui `AutoSaveCoroutine` (`<AutoSaveCoroutine>d__94`). Provável singleton — confirmar.
- Save system: `SaveGameData`, `AutoSave`, `SaveDataFile`, `LoadFromSave`, `RestartAutoSave`, `NewestSave`. Tipos de payload: `PlayerData`, `NetworkSaveData`, `CableSaveData`, `ModItemSaveData`.
- Cena: `LoadingScreen.LoadGameLoadScene`, `IsSceneLoaded`, `LoadGameScenesVoid`, `loadedScenes`, `distanceToLoadScene`, `numberOfScenesInBuild` — múltiplas cenas streamed por distância.

**Player**
- `Player` (classe) com `LoadPlayer(PlayerData)`, `PlayerStopMovement`, `GetPlayerInput`, `ButtonUnstuckPlayer`.
- Input map: `m_Player_Move`, `m_Player_Crouch`, `m_Player_Jump`, `m_UI_Inventory`, `Player.Inventory` (PlayerActions do Input System).
- Estados: `isPlayerCameraDisallowed`, `isPlayerDriving` (existe sistema de veículo, embora discreto — só `UnityEngine.VehiclesModule` aparece).

**Domínio do data center (o que precisa replicar)**
- `Server` — entidade central. `RegisterServer`, `GetServer(string)`, `AddBrokenServer`, `RemoveBrokenServer`, `GenerateUniqueServerId`, `ServerID`, `UpdateServerCustomerID`. Corrotinas de manuseio: `InsertItemInRack`, `ReplacingServer`, `ThrowingOutServer`, `GettingNewServer`.
- `Rack`, `RackDoor` (`OpenDoor`, `PlayRackDoorOpen`), `CustomerBase` (com `CustomerBaseDoor`, `DelayedAppDoorOpening`).
- Rede lógica: `NetworkSwitch` (`OpenConfig(NetworkSwitch)`), `LoadNetworkStateCoroutine`, `ClearNetworkState`, `PrintNetworkMap`, VLANs (`ToggleVLANMulti`, `CreateVLANButtonMulti`).
- Cabeamento: `cableConnections`, `Connections`, `RegisterCableConnection`, `RemoveCableConnection`, `IsAnyCableConnected`, `AddSwitchConnection`, `GetConnectedDevices`, `Connect(string,string)`, `SetConnectionSpeed`. Áudio: `audioClipSuccessfullyConnected`.
- Patch panel: `BuildPatchPanelCache`, `BuildLookup`, `BuildAssignments`.
- Itens/loja: `BuyItem`, `ButtonBuyItem`, `BuyAnotherItem`, `AddSpawnedItem`, `RemoveSpawnedItem`, `RemoveLastSpawnedItem`, `DropAllItems`, `DestroyAllSpawnedItems`, `ItemID`, `ItemType` (enum `ObjectInHand`), `IsAllowedItem`, `CustomerItem`, `GetCustomerItemByID`.
- Energia/UI: `PowerButton`, `ButtonPower`, `pushPower`, `imagePowerButton`, `SetPowerLightMaterial`.
- Modos de jogo: `ChangeMode(int)`, `SetAutoRepairMode(int)`, `StartGodMod`, `MakeModeOptions`, `InitOnPlayMode`. Multiplicadores de progressão: `xpMultiplyerPerSecond`, `moneyMultiplyerPerSecond`, `forcedIndirectMultilierOne`.

**Steam (já no jogo)**
- `Il2CppSteamworks` (rlabrecque/Steamworks.NET).
- `SteamManager`, `SteamLeaderboards`, `SteamStatsOnMainMenuTop`, hook de warning `SteamAPIWarningMessageHook_t`.
- **Não há código atualmente para `SteamMatchmaking`/`SteamNetworking` no Assembly-CSharp.** O mod vai instanciar isso por conta própria via Steamworks.NET (que já está presente em runtime, evita shippar a DLL de novo).

### IDs únicos / chaves de autoridade (todos com geradores próprios)

> **Crítica para multiplayer:** todos os IDs abaixo são gerados localmente pelo
> jogo. No modo multiplayer **só o host** pode gerar — clientes recebem o ID
> via mensagem antes de instanciar a entidade correspondente.

| Geração | Persistência | Uso |
|---------|--------------|-----|
| `GenerateUniqueServerId()` | `serverID`, `endServerID`, `startServerID`, `serverList`, `servers` | identifica server físico |
| `GenerateUniqueSwitchId()` | `SwitchID`, `endSwitchID`, `startSwitchID`, `switchList`, `switches` | identifica switch |
| `GenerateUniquePatchPanelId()` | `patchPanelId`, `patchPanels`, `patchPanelLinkCache` | identifica patch panel |
| `GetNextCustomerID()` | `CustomerID`, `customerBaseID`, `availableCustomerIndices` | identifica customer e a base alocada |
| `_rackPosGlobalUID_k__BackingField` / `lastUsedRackPositionGlobalUID` | — | identifica posição em rack |
| `cableID`, `cableIds`, `cableIDsOnLink` | — | identifica cabo (gerador exato a confirmar) |
| `loadLanguageUID` | — | irrelevante p/ MP |

### Tabela detalhada por subsistema

#### Player (input + locomoção + câmera)

```
Métodos:
  LoadPlayer(PlayerData)           LoadPlayerAndNPCDataWithDelay
  PlayerStopMovement()              ButtonUnstuckPlayer()
  WarpPlayer(...)                   ResetCameraPosition / UpdateCameraPosition
  GetPlayerInput / GetPlayerJoystickInput
  GetPlayerAButton / GetPlayerBButton
  LockedCursorForPlayerMovement     OnInventory / OnCrouch / OnJump / OnMove / OnCloseMenu
  Crouch / StopCrouching            HandleGroundedMovement / OnAnimatorMove

Input map (PlayerActions):
  m_Player_Move, m_Player_Crouch, m_Player_Jump, m_Player_Drop,
  m_Player_Interact, m_Player_Look, m_Player_LookPosition,
  m_Player_Label, m_Player_CloseMenu, m_UI_Inventory

Estado:
  isPlayerCameraDisallowed, isPlayerDriving, enabledPlayerMovement,
  enabledMouseMovement, lastPlayerPosition, unstuckPlayerPosition,
  m_Crouching / m_isCrouching, m_Jumping, m_JumpPower, m_JumpSpeed,
  m_MoveSpeedMultiplier, m_GravityMultiplier, m_AnimSpeedMultiplier
  CameraTarget, m_OriginalCameraPosition, virtualCamera, mainCamera, playerCamera
```

→ **`WarpPlayer`** + `enabledPlayerMovement` + `enabledMouseMovement` são as
ferramentas certas para syncar posição inicial e bloquear input do cliente
durante loading/transições. Cada `RemotePlayer` é um GameObject novo
controlado pelo mod, não reusa este `Player` singleton.

#### Server / Rack / SFP

```
Server:
  GetServer(string)            GetAllServers()             GetAllBrokenServers()
  RegisterServer(Server)       AddBrokenServer(Server)     RemoveBrokenServer(string)
  GenerateUniqueServerId()     ReturnServerNameFromType    GetServerPrefab
  GetServerProcessingSpeed     GetServerTypeForIP          UpdateServerCustomerID
  UpdateServerScreenUI         UpdateCustomerServerCountAndSpeed

Rack:
  InsertItemInRack (coroutine)        InitializeLoadedRack         InstallRack
  ServerInsertedInRack                SwitchInsertedInRack         InsertedInRack
  ReplacingServer / ThrowingOutServer / GettingNewServer
  UnmountRack                         ButtonUnmountRack            ButtonDisablePositionsInRack
  ValidateRackPosition                CheatInsertRack              InstantiateRack
  PlayRackDoorOpen                    SetRacksVolume / RacksVolume

SFP:
  InsertSFP / RemoveSFP / TakeSFPFromBox / InsertSFPBackIntoBox / RemoveSFPFromBox
  ReturnSFPDirectly / InsertedInSFPPort / LoadSFPsFromSave / CanAcceptSFP
  GetSfpBoxPrefab / GetSfpPrefab

Estado:
  servers, serverList, serverPrefabs, brokenServers, selectedServer,
  appIdToServerType, parentServer, serverType, connectedServersCount,
  avgServerProcessingSpeed
  rackMounts, rackMountObjectData, rackMount, currentRackPosition,
  firstRackMountPos, isRackInstantiated, lastUsedRackPositionGlobalUID,
  buttonRackPositionsRendererer
  sfpModules, sfpPositions, sfpType, sfpTypeInserted, insertedSFP,
  isSFPPort, sfpForwardOffset, sfpPrefabs, sfpsBoxedPrefab, emptySfpBox
```

#### Cabeamento, Switches, VLAN, PatchPanel, LACP

```
Cable:
  CreateNewCable / CreateNewReverseCable / CreateCableWithSpawners
  RegisterCableConnection / RemoveCableConnection / DisconnectCables / ReconnectCables
  Connect / Disconnect / SetConnectionSpeed / GetConnectedDevices
  IsAnyCableConnected / IsCableComplete / IsCableInRoute / IsCableLenghtEnough
  GetAllCables / GetCableInfo / GetCableMaterial / GetCablePositions /
  GetRawCablePositions / GetCableCurrentSpeed / GetCableSpinnerPrefab
  HandleNewCableWhileOff / DisconnectCablesWhenSwitchIsOff
  ClearAllCables / LoadCable(CableSaveData) / LowerAmountOfCable
  RegisterCableInNetworkMap / DoesCableServeMultipleCustomers
  GetCustomersUsingCable / GetLACPGroupForCable / RemoveCableFromLACPGroups
  ActivateSpawnerOnCable / ActivateSpawnersForCable

Switch:
  RegisterSwitch / GetSwitchById / GenerateUniqueSwitchId / GetSwitchId
  GetAllNetworkSwitches / GetAllBrokenSwitches / GetSwitchPrefab
  AddBrokenSwitch / RemoveBrokenSwitch / AddSwitchConnection
  ButtonShowNetworkSwitchConfig / ReturnSwitchNameFromType / PatchStaleSwitchId
  ShowNetworkConfigCanvas / CloseNetworkConfigCanvas

VLAN:
  GetFreeVlanId / RemoveUsedVlanId / ReturnVlanId / InitializeVlanPool
  GetAllDisallowedVlans / GetDisallowedVlans / GetVisibleVLANs / GetVlanIdsPerApp
  IsVlanAllowedOnCable / IsVlanAllowedOnPort / IsVlanAllowedOnRoute
  SetVlanAllowed / SetVlanDisallowed / SetDisallowedVlansPerPort
  CreateVLANButtonMulti / ToggleVLANMulti / ClearVLANDisplay /
  RefreshVLANDisplayForSelection

PatchPanel:
  BuildPatchPanelCache / CollectPatchPanelChainCables / IsPatchPanelPort
  ResolveThroughPatchPanel / TraversePatchPanels / GetPatchPanelPrefab
  GenerateUniquePatchPanelId

NetworkState:
  ButtonNetworkMap / PrintNetworkMap / RegisterCableInNetworkMap
  LoadNetworkState / LoadNetworkStateCoroutine / ClearNetworkState

Estado relevante:
  cableConnections, cableEntities, cableGameObjects, cableLinks, cableLinkPorts,
  cableLinkSwitchPorts, cableID, cableIds, cableIDsOnLink, cableEndPoints,
  cableLenght / cableLenghtInUse / _yourCableLength_5__4
  switches, switchList, switchConnections, networkSwitch, currentNetworkSwitch,
  selectedNetworkSwitch, networkData, networkMapScreen
  vlanIdsPerApp, availableVlanIds, disallowedVlanIds, disallowedVlansPerPort,
  portVlanFilters, vlanButtonPrefab, capturedVlan
  patchPanels, patchPanelLinkCache, patchPanelId, patchPanelType
  appsSpeedRequirements, appsSpeedCurrent, appSpeedAccumulator,
  lastFinalCableSpeeds, connectionSpeed, currentSpeed
```

#### Customer / Economia (tick contínuo — host-only)

```
Customer:
  GetCustomerBase / RegisterCustomerBase / GetCustomerID / GetNextCustomerID
  GetCustomerLogo / GetColorForCustomerId / GetColorForCustomerIdFloat4
  GetCustomerItemByID / SetCustomer(CustomerItem) / UpdateCustomer
  UpdateCustomerServerCountAndSpeed / UpdateSpeedOnCustomerBaseApp
  UpdateDeviceCustomerID / GetCustomerTotalRequirement
  GetCustomerRoutes / IsCustomerSuitableForBase / CreateFallbackCustomer
  ShuffleAvailableCustomers / ShowCustomerCardsCanvas
  ButtonCustomerChosen / ButtonCancelCustomerChoice / ButtonClickChangeCustomer

Economia / progressão:
  UpdateMoney / UpdateXP / GetReward / GetEffectiveMoneySpeed
  GetTotalAppSpeed / GetAppsSpeedRequirements / GetAggregatedSpeed
  ResetAllAppSpeeds / get_TotalPrice
  Buy: ButtonBuyItem / ButtonBuyShopItem / BuyItem / BuyAnotherItem / BuyNewItem /
       ButtonBuyWall / ButtonCancelBuying / ShowBuyWallCanvas
  Shop: ButtonShopScreen / CloseShop / CreateShopButton / CreateShopTemplate /
        LoadShopItem / SyncWorkshopThenLoadAll / CleanUpShop
  Mode: ChangeMode / SetAutoRepairMode / StartGodMod / GODMOD_delayed /
        InitOnPlayMode / MakeModeOptions / UpdateMode

Estado:
  money, currentSalaryExpense, salaryExpense, priceOfTechnician,
  costPerLevel, xpCostPerLevel, xpGainMultiplier,
  _moneyMultiplyerPerSecond_k__BackingField, _xpMultiplyerPerSecond_k__BackingField,
  topLeft_MoneyPerSecond, topLeft_XPPerSecond, topLeft_ExpensesPerSecond,
  customerBases, customer, customerItems, chosenCustomerItems,
  availableCustomerIndices, canvasCustomerChoice, customerCards,
  appsSpeedRequirements, appsSpeedCurrent, appSpeedAccumulator,
  autoRepairMode, commandCenterAutoRepairMode, mode, lastUpdatedMode,
  isBatchMode, CameraMode
```

→ Esse tick econômico é o que **mais** vai sangrar bandwidth se você tentar
replicar campo por campo. Estratégia: rodar 100% no host; broadcast de
1–2 Hz com totais (`money`, `xpGainMultiplier`, `salaryExpense`,
`appsSpeedCurrent` por customer). Cliente nunca chama `UpdateMoney` /
`UpdateXP` / `BuyItem` direto — sempre via intent ao host.

#### Save / Load

```
Save:
  Save(string,string) / SaveGameData / SaveDataFile / SaveConfirm
  AutoSave / AutoSaveCoroutine / RestartAutoSave
  SetAutoSaveEnabled / SetAutoSaveOnOff / SetAutoSaveInterval
  ButtonSaveInputTextOverlay / NotAllowedToSaveOverlayOff
  Listofsaves / NewestSave / GetRawSaveEntry / GetSaveData
  PopulateLoadSaveMenu / LoadSaveOnButtonClick / CloseLoadSaveOverlay
  DeleteSaveButtonClick / DeleteSaveConfirm / DeleteSaveFile
  SaveColor / LoadSavedColor / SaveBindingOverride / LoadAllBindingOverrides

Load:
  Load / LoadFromSave / LoadGame / LoadGameData / LoadGameLoadScene
  LoadGameScenesVoid / LoadDataFile / LoadCable(CableSaveData)
  LoadSFPsFromSave / LoadAllMods / LoadModPack / LoadDll
  AsynchronousLoad / AsynchronousUnLoad / DelayedLoad

Estado:
  gameSaves, listofsaves, loadSaveSlots, firstLoadSaveSlot, loadSaveName,
  loadSaveOverlay, deleteSaveConfirmOverlay, isAllowedToSave, isLoadSaveEnabled,
  autoSaveCoroutine, autoSaveEnabled, autoSaveIntervalMinutes, dropDownAutoSaveInterval,
  toggleAutoSave, isLoading, isLoadingObjectives, loadingFirstTime,
  onGameIsLoadedCallback, distanceToLoadScene, loadedScenes,
  numberOfScenesInBuild, sceneIndex, sceneToLoad
```

→ Cliente: patch `AutoSaveCoroutine`/`SaveGameData`/`SaveDataFile` para no-op.
→ Host: dispara `AutoSave` normalmente; fora isso, snapshot inicial → cliente
   reusa `LoadFromSave` ou um caminho próprio que reidrate via mensagens.

#### ModLoader interno do jogo (Workshop)

```
LoadAllMods / LoadModPack(string) / GetModPrefab / GetModPrefabByFolder /
OnModLoad / OnModUnload / SyncWorkshopThenLoadAll / ModifyLastChar
```

→ Para entrar em uma sessão multiplayer, host envia o manifesto de
`workshop_*` ids carregados; cliente compara e baixa o que faltar via
Steam Workshop antes do `SyncWorkshopThenLoadAll`. `ModItemSaveData` é o
formato usado para persistir esses itens nos saves.



### O que o jogo **não tem**

- Nenhuma camada de replicação, RPC, ownership, NetworkBehaviour, NetworkVariable, etc.
- Nenhum modelo cliente/servidor — tudo roda local.
- IDs únicos para entidades existem mas são gerados local (`GenerateUniqueServerId`) — bom para usar como chave de sync, mas precisa atenção se host e cliente gerarem IDs em paralelo.

---

## 4. Estratégia recomendada para o mod multiplayer

> Resumo: **host-authoritative** rodando o `MainGameManager` real, clientes recebendo
> snapshots/eventos por **Steamworks P2P** dentro de uma **Steam Lobby**.

### 4.1. Transporte

- **Steam Lobby** (`SteamMatchmaking.CreateLobby` → friends-only ou public).
- **Mensageria:** `SteamNetworkingMessages` (API moderna, datagrama confiável/opcional, NAT punch automático). Alternativa: `SteamNetworkingSockets`. Evite a API legacy `SteamNetworking.SendP2PPacket`.
- Enquadramento próprio (varint + msgId + payload). Prefira **MemoryPack** ou um serializador binário simples; evite JSON em hotpath.
- Tick fixo (ex.: 20 Hz) para snapshots de estado contínuo (posição do jogador remoto, energia, dinheiro, “customer satisfaction”) e eventos discretos (compra, conexão de cabo, abertura de porta) enviados imediatamente.

### 4.2. Modelo de autoridade

- **Host:** dono da partida. Carrega o save normalmente; `MainGameManager`, `AutoSave`,
  spawn de customers, lógica de receita/avaria — tudo continua rodando aqui.
- **Cliente:** carrega a mesma cena, mas suprime `MainGameManager` (Harmony patch
  para neutralizar updates autoritativos: `AutoSaveCoroutine` desligado, geração de
  customer/item desligada, money/XP só refletem o que vier do host). Renderiza o
  estado replicado e **só** envia *intents* (ex.: “quero conectar cabo de A em B”,
  “comprei item X”).
- ID das entidades: usar o `ServerID`/IDs gerados pelo host como autoridade. Cliente
  nunca chama `GenerateUniqueServerId` localmente em runtime — tudo passa pelo host.

### 4.3. Pontos de hook (Harmony / `MelonLoader.HarmonyLib`)

| Sistema | Métodos para patch | Direção |
|---------|--------------------|---------|
| Spawn/registro de servidor | `Server.RegisterServer`, `AddBrokenServer`, `RemoveBrokenServer` | broadcast |
| Inserção em rack | corrotinas `InsertItemInRack`, `ReplacingServer`, `ThrowingOutServer`, `GettingNewServer` | broadcast |
| Cabeamento | `RegisterCableConnection`, `RemoveCableConnection`, `Connect`, `AddSwitchConnection`, `SetConnectionSpeed` | broadcast |
| Compra | `BuyItem`, `ButtonBuyItem`, `BuyAnotherItem` | intent → host → broadcast |
| Energia | `ButtonPower` / `PowerButton` | intent → host → broadcast |
| Portas | `OpenDoor`, `RackDoor`, `CustomerBaseDoor.DelayedAppDoorOpening` | broadcast (cliente pode prever) |
| Save/load | `SaveGameData`, `LoadFromSave`, `AutoSave`, `RestartAutoSave` | só no host; bloquear no cliente |
| Loop principal | `MainGameManager.AutoSaveCoroutine` | desabilitar no cliente |
| Player local | `Player.GetPlayerInput`, `LoadPlayer`, `PlayerStopMovement`, flags `isPlayerDriving`, `isPlayerCameraDisallowed` | mover dados de input local; replicar transform de remotos via `Transform`/`Animator` próprio |
| Network state | `LoadNetworkStateCoroutine`, `ClearNetworkState` | só no host; cliente recebe snapshot |

Patches em IL2CPP via MelonLoader: usar `HarmonyLib.Harmony` exposto pelo Melon e o
helper `MelonMod.OnLateInitializeMelon` para garantir que os tipos IL2CPP já estão
registrados antes do patch.

### 4.4. Replicação de jogadores remotos

- O `Player` atual é singleton/local. Não tente fazê-lo `MonoBehaviour` rede-aware —
  crie um `RemotePlayer` próprio (UMA é caro: pode reusar prefab UMA do jogador local
  ou um modelo simplificado). Sincronizar: posição, rotação do corpo + câmera,
  estado de animação/`m_Player_Crouch`/`m_Player_Jump`, item na mão (`ObjectInHand`).
- Use compressão de quaternion (smallest-three) e delta de posição para baixo bandwidth.

### 4.5. Persistência

- Save é único (do host). Cliente nunca grava em disco. Quando entrar na lobby, o
  host envia um **snapshot inicial completo** (servers, racks, cabos, dinheiro/XP,
  estado de customers, posição dos jogadores), depois só deltas/eventos.
- `ModItemSaveData` indica que itens vindos de Workshop são serializados —
  garantir que ambos os pares têm o mesmo conjunto de Workshop mods (compare
  `StreamingAssets/Mods/workshop_*` ids e bata hash; se cliente não tiver um asset,
  baixar via Workshop API antes de entrar na partida).

### 4.6. Compatibilidade Workshop

- Mods Workshop são puramente assets (`.obj`/`.png`/`config.json`); não afetam
  determinismo. O que precisa estar igual é **a lista** de workshop_ids carregados,
  para que IDs de item batam entre os pares. `ModLoader.SyncWorkshopThenLoadAll`
  é o ponto onde o jogo monta esse índice — fazer o host emitir esse manifesto na
  conexão.

---

## 5. Riscos e perguntas em aberto (investigar antes de codar)

1. **Determinismo do tick econômico.** `xpMultiplyerPerSecond` / `moneyMultiplyerPerSecond` e geradores de customers são `Random`-based? Se sim, **só rodar no host** e replicar resultado, nunca tentar reproduzir nos clientes.
2. **DOTS/Entities subcenas.** O jogo carrega `EntityScenes`. Confirmar se há lógica de simulação em ECS (jobs) — replicar ECS é um inferno; provavelmente é só rendering/streaming, mas verificar.
3. **HDRP + UMA + segundo jogador.** UMA é pesado para gerar — pré-aquecer atlas e materiais ao entrar na lobby.
4. **Versão de Steamworks.NET no jogo.** Confirmar a versão exata (abrir `Il2Cppcom.rlabrecque.steamworks.net.dll` em ILSpy) — algumas APIs (`SteamNetworkingMessages`) só apareceram em SDK recente. Se necessário, shippar Steamworks.NET próprio em `UserLibs/`.
5. **AppID Steam.** AppID = **4170200** (descoberto via `appmanifest_4170200.acf`). Lançar **sempre via Steam** (`steam://rungameid/4170200`); rodar `Data Center.exe` direto dispara erro de licença porque o jogo não recebe o auth ticket do Steam client. Se precisar lançar fora do Steam um dia, criar `steam_appid.txt` com `4170200` na raiz do jogo.
6. **DCMultiplayer.dll já existente.** Antes de começar a escrever o mod do zero, **identificar se é o seu próprio binário antigo ou de outra pessoa** — descompilar e ver o que já está implementado para não duplicar trabalho.
7. **Anti-cheat.** Verificar se há EAC/BattlEye (não detectado nos arquivos atuais; nada em `Plugins/`), mas confirmar antes de distribuir.
8. **Bug `<>O` recorrente.** Cada update do jogo regenera os assemblies via Il2CppInterop e o bug volta. Documente para o usuário rodar `FixCoreModule.exe` após updates.

---

## 6. Convenções deste repositório de mod

- **Não modificar arquivos do jogo.** Tudo que for editar fica em `Mods/`, `UserLibs/`
  ou em mods Workshop. `GameAssembly.dll`, `UnityPlayer.dll` e companhia são
  intocáveis.
- **Backups antes de qualquer experimento com Cecil/Il2CppInterop** (já existe o
  padrão `.bak` que o `FixCoreModule` usa — siga-o).
- **Logs:** usar `MelonLogger.Msg/Warning/Error`. Eles caem em
  [MelonLoader/Latest.log](MelonLoader/Latest.log) e em
  [MelonLoader/Logs/](MelonLoader/Logs/).
- **Não commitar binários do jogo** se for versionar o mod fora desta pasta.

---

## 6.5. Estado atual do mod (v0.0.5)

| Camada | Status | Onde |
|--------|--------|------|
| MelonLoader 0.7.2 + IL2CPP setup | ✅ funcionando | jogo carrega o mod no startup |
| Fix `<>O` (LavaGang/MelonLoader#1142) | ✅ embarcado no installer | `Tools/DCInstaller/CecilFix.cs` |
| Steam Lobby (FriendsOnly, max=4) | ✅ validado com 2 humanos | `Tools/DCMultiplayer.Mod/Networking/SteamLobby.cs` |
| Transport P2P (`SteamNetworkingMessages`, 3 canais) | ✅ PING/PONG funcionando | `Networking/Transport.cs` |
| Replicação de transform (capsulas como avatares, 20 Hz) | ✅ spawn confirmado, posição é a do mundo do remoto | `Replication/PlayerSync.cs`, `Replication/RemotePlayers.cs` |
| Authority gates (suppressão no cliente) | ✅ código pronto, falta validar com 2 peers | `Networking/Authority.cs`, `Patches/ClientSuppression.cs` |
| Replicação econômica (money/xp/rep, 1 Hz) | ✅ código pronto, falta validar | `Replication/EconomySync.cs` |
| HUD overlay (canto inferior esquerdo) | ✅ funciona em IMGUI | `UI/Hud.cs` |
| Hotkeys F6–F12 | ✅ via `UnityEngine.InputSystem.Keyboard.current` | `Mod.cs` |
| Distribuição (single-file installer + zip) | ✅ embarca DLL + roda fix `<>O` + deploy | `Tools/DCInstaller/`, `Tools/dist/` |

### Detalhes de IL2CPP relevantes para futuras patches

- Tipos do jogo (sem namespace original) ficam em `Il2Cpp.*` (ex.: `Il2Cpp.Player`,
  `Il2Cpp.MainGameManager`, `Il2Cpp.Server`).
- Steamworks.NET fica em `Il2CppSteamworks.*` (`SteamMatchmaking`, `SteamNetworkingMessages`,
  `Callback<T>`, `CSteamID`, etc.). `SteamAPI.RunCallbacks()` já é chamado pelo `SteamManager`
  do jogo — Callbacks nossos disparam sem nada a mais.
- `Callback<SteamNetworkingMessagesSessionFailed_t>` **não funciona** (struct contém string
  fixa, Il2CppInterop rejeita conversão de delegate). Skipped.
- Input: `UnityEngine.Input` legacy lança exceção (jogo usa Input System novo). Use
  `UnityEngine.InputSystem.Keyboard.current[Key.X].wasPressedThisFrame`.
- `GameObject.CreatePrimitive(...)` funciona, mas custom MonoBehaviour exigiria
  `ClassInjector` — atualmente evitado mantendo tudo como gerência estática.
- Custom delegates passados pra Il2Cpp via `Action<T>` cast funcionam quando `T` é
  blittable. Para tipos não-blittable, falha em runtime.

### Hotkeys em uso

| Tecla | Ação |
|-------|------|
| F6 | Toggle `Authority.ForceClient` (debug — testar suppressions sozinho) |
| F7 | Warp local player até primeiro avatar remoto (debug) |
| F8 | Hospedar lobby Steam |
| F9 | Sair do lobby |
| F10 | Dump de membros no log |
| F11 | Abrir overlay Steam de convite |
| F12 | Broadcast de PING (texto) no canal de controle |

### Constraint de design importante

Saves do Data Center são single-player. Em multiplayer, cada peer carrega **o próprio save**.
A v0.0.5 só replica posição/dinheiro entre dois mundos paralelos — para experiência real de
multiplayer (o cliente vê os servidores/cabos/customers do host) será necessário um
**snapshot inicial completo** + replicação contínua de eventos discretos (próxima fase).

## 7. Próximos passos sugeridos (em ordem)

1. Abrir `Assembly-CSharp.dll` no dnSpyEx e mapear assinaturas reais de
   `MainGameManager`, `Server`, `Player`, `Cable*`, `NetworkSwitch`. Atualizar este arquivo.
2. Inspecionar `DCMultiplayer.dll` existente (quem fez? até onde foi?).
3. Criar projeto `DCMultiplayer.Mod` (.NET 6, MelonMod) com lobby Steam vazio,
   só logando entrada/saída de peers.
4. Replicar **transform do jogador** (mais simples e visível) antes de qualquer
   sincronização de mundo.
5. Em seguida, snapshot inicial + replicação de eventos discretos (compra de item,
   inserção em rack, conexão de cabo) — onde está o gameplay real do jogo.
6. Por último: simulação contínua (energia, dinheiro/XP, customers).

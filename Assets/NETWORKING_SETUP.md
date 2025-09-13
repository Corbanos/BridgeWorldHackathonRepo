# Island Networking Setup Guide

## Overview
This system allows multiple players to connect their islands with automatic bridge generation and synchronized building.

## Required Unity Packages

### 1. Install Unity Netcode for GameObjects
1. Open Unity Package Manager (Window > Package Manager)
2. Change dropdown to "Unity Registry"
3. Search for "Netcode for GameObjects" 
4. Install the latest version (1.7.1 or newer)

### 2. Install Unity Transport (if not automatically installed)
1. In Package Manager, search for "Unity Transport"
2. Install if not already present

## Scene Setup

### 1. Create Network Manager
1. Create an empty GameObject in your scene
2. Name it "NetworkManagerObject" 
3. Add Component > Search for "NetworkManager" (Unity's official component under Netcode)
4. **ALSO** add Component > Search for "IslandNetworkManager" (our custom script)
5. Configure the Unity NetworkManager component:
   - **Transport**: Should auto-select "Unity Transport"
   - **PlayerPrefab**: Create a player prefab and assign it
   - **NetworkPrefabs**: Leave empty for now
   - **Enable Scene Management**: Can leave checked

**Important**: You need BOTH components on the same GameObject - Unity's NetworkManager AND our IslandNetworkManager script.

### 2. Setup BuilderLite Integration
1. Find your existing BuilderLite GameObject
2. Add the `NetworkedBuilder.cs` component
3. In BuilderLite inspector:
   - Set **Networked Builder** field to the NetworkedBuilder component
4. In NetworkedBuilder inspector:
   - Set **Local Builder** field to your BuilderLite component

### 3. Create Bridge System
1. Create a simple bridge prefab (a stretched cube works)
2. Add the `IslandBridge.cs` component to it
3. In NetworkManager inspector:
   - Set **Bridge Prefab** to your bridge prefab
   - Set **Island Spacing** (default: 50)

### 4. Player Setup (SIMPLIFIED - NO PREFABS NEEDED!)

#### Runtime Player Spawning:
**Good news! No prefabs needed.** The system now creates players automatically at runtime.

1. **In Unity's NetworkManager component**:
   - **LEAVE "Player Prefab" EMPTY** (don't assign anything)
   - The RuntimePlayerSpawner will handle everything automatically

2. **Add PlayerSpawnManager** script to any GameObject in scene (optional):
   - This will disable your existing local player when networking starts
   - Keep **Disable Local Player On Connect** checked
   - Make sure your existing player has "Player" tag

3. **Configure player settings** (optional):
   - In the IslandNetworkManager component, you can adjust:
   - Move Speed, Mouse Sensitivity, Debug options

## How It Works

### Island Layout
- **Host player**: Gets the center island (0,0,0)
- **Other players**: Placed in a circle around the center
- **Bridges**: Automatically connect all islands to the center
- **Maximum**: 8 players in a circle (can be modified)

### Building Synchronization
- When you place an object, it appears on all connected islands
- When you delete an object, it disappears from all islands
- New players joining get a full sync of existing buildings

## Testing Locally

### Host Mode
1. Press Play in Unity
2. Click "Host Island (Server + Client)"
3. You're now hosting and can build

### Client Mode
1. Open a second Unity Editor or build the game
2. Click "Join Island (Client)" 
3. You'll connect to the host and see their island

## Network UI Controls
The NetworkManager shows these buttons:
- **Host Island**: Start as both server and client
- **Join Island**: Connect as client to existing host
- **Disconnect**: Leave the network

## Building Across Islands

### What Gets Synced:
- ✅ Object placement (instant)
- ✅ Object deletion (instant)  
- ✅ Full island sync when joining
- ✅ Player positions on bridges

### What Doesn't Sync:
- ❌ Ghost object preview (local only)
- ❌ Menu states (local only)
- ❌ Camera positions (local only)

## Troubleshooting

### "NetworkManager component not found"
- Make sure you installed "Netcode for GameObjects" from Package Manager
- Try Window > Package Manager > In Project > Make sure Netcode is listed
- If still not showing, try restarting Unity Editor
- Search for "Network Manager" (with space) not "NetworkManager"

### "NetworkObject not found" errors
- Make sure the Unity NetworkManager component is in the scene
- Ensure all networked objects have NetworkObject component
- Check that NetworkManager is enabled and active

### Players can't connect
- Check if port 7777 is blocked by firewall
- Try hosting on 127.0.0.1 for local testing
- Make sure Unity Transport is installed and selected
- Verify both players have identical NetworkManager setup

### Buildings don't sync
- Verify NetworkedBuilder is assigned in BuilderLite
- Check console for network errors
- Ensure items have proper ID fields
- Make sure NetworkedBuilder has NetworkObject component

### Bridges don't appear
- Check Bridge Prefab is assigned in IslandNetworkManager
- Verify IslandBridge component is on bridge prefab
- Look for errors in console during connection
- Ensure bridge prefab has NetworkObject if it needs to be networked

## Customization

### Adding More Islands
In NetworkManager.cs, modify the `AssignIslandToPlayer` method:
```csharp
float angle = (360f / 12f) * (clientId % 12); // 12 islands instead of 8
```

### Changing Island Distance
In NetworkManager inspector, adjust **Island Spacing** value.

### Custom Bridge Behavior
Modify IslandBridge.cs:
- `segmentLength`: Distance between bridge segments
- `autoConnect`: Whether bridges appear automatically
- Materials for active/inactive states

## Advanced Features

### Manual Bridge Control
```csharp
// Get bridge component and toggle
IslandBridge bridge = FindObjectOfType<IslandBridge>();
bridge.ToggleBridgeServerRpc();
```

### Getting Player Islands
```csharp
NetworkManager netManager = FindObjectOfType<NetworkManager>();
List<ulong> playerIds = netManager.GetConnectedPlayerIds();
Vector3 myIsland = netManager.GetMyIslandPosition();
```

## Next Steps
- Add player names/avatars
- Implement voice chat
- Add island customization options
- Create mini-games between islands
- Add resource trading system
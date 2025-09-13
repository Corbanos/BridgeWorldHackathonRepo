# Simple Multiplayer Setup Guide

## IMPORTANT: Complete Setup Required in Unity Editor

### Step 1: Prepare Your Player GameObject
1. Select your **Player** GameObject in the Hierarchy
2. Add Component → **NetworkObject**
3. Add Component → **NetworkPlayer** (the script we just created)
4. Save it as a Prefab (drag it to Project window)
5. Delete the Player from the scene (we'll spawn it via network)

### Step 2: Create NetworkManager GameObject
1. Create Empty GameObject, name it "NetworkManager"
2. Add Component → **NetworkManager** (Unity's built-in)
3. Add Component → **Unity Transport** (for networking)
4. Add Component → **SimpleNetworkManager** (our UI script)

### Step 3: Configure NetworkManager
On the NetworkManager component:
1. **Player Prefab**: Drag your Player prefab here
2. **Network Prefabs List**:
   - Size: 1
   - Element 0: Drag your Player prefab here too
3. **Spawn Method**: Set to "Round Robin"

### Step 4: Configure Unity Transport
On the Unity Transport component:
1. **Protocol Type**: Unity Transport
2. **Network Configuration**:
   - Max Connect Attempts: 60
   - Connect Timeout MS: 1000
   - Disconnect Timeout MS: 30000
3. **Connection Data**:
   - Address: 127.0.0.1
   - Port: 7777
   - Server Listen Address: 0.0.0.0

### Step 5: Test
1. Build your project (File → Build Settings → Build)
2. Run the built version as Player 1
3. Run Unity Editor as Player 2
4. Player 1: Press H to Host
5. Player 2: Press J to Join
6. Both players should see each other!

## Controls
- **H** - Start as Host (server + client)
- **J** - Join as Client
- **ESC** - Toggle menu

## Troubleshooting
- If players can't see each other, check:
  1. Player prefab has NetworkObject component
  2. Player prefab has NetworkPlayer component
  3. NetworkManager's Player Prefab slot is filled
  4. NetworkManager's Network Prefabs List contains the player prefab
  5. Both builds are using the same Unity version

## What This System Does
- Host creates a server and spawns their player
- Clients connect and get their player spawned
- Position and rotation sync automatically
- Local player sees through their camera
- Remote players appear as red capsules
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.DataStructures;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace cctgPlugin
{
    [ApiVersion(2, 1)]
    public class CctgPlugin : TerrariaPlugin
    {
        public override string Name => "CctgPlugin";
        public override string Author => "stardust";
        public override string Description => "CCTG Plugin - Paint world and build houses centered at spawn";
        public override Version Version => new Version(1, 0, 0);

        // Module instances
        private HouseBuilder houseBuilder = new HouseBuilder();
        private WorldPainter worldPainter = new WorldPainter();
        private BoundaryChecker boundaryChecker = new BoundaryChecker();
        private TeleportManager teleportManager = new TeleportManager();

        // Scoreboard update counter
        private int scoreboardUpdateCounter = 0;
        private const int SCOREBOARD_UPDATE_INTERVAL = 60; // Update every 60 frames (~1 second)

        // House mob clearing counter
        private int mobClearCounter = 0;
        private const int MOB_CLEAR_INTERVAL = 30; // Check every 30 frames (~0.5 seconds)

        // Game state
        private bool gameStarted = false;

        // Player timers (player name -> start time)
        private Dictionary<string, DateTime> playerTimers = new Dictionary<string, DateTime>();

        public CctgPlugin(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            // Register events
            ServerApi.Hooks.GamePostInitialize.Register(this, OnPostInitialize);

            // Register Tile edit event to protect houses
            GetDataHandlers.TileEdit += OnTileEdit;

            // Register network data event to listen for team changes
            ServerApi.Hooks.NetGetData.Register(this, OnGetData);

            // Register game update event for delayed teleport handling
            ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);

            // Register player join event
            ServerApi.Hooks.NetGreetPlayer.Register(this, OnPlayerJoin);

            // Register player leave event
            ServerApi.Hooks.ServerLeave.Register(this, OnPlayerLeave);

            // Register commands
            Commands.ChatCommands.Add(new Command(PaintWorldCommand, "paintworld"));
            Commands.ChatCommands.Add(new Command(BuildHousesCommand, "buildhouses"));
            Commands.ChatCommands.Add(new Command(StartCommand, "start"));
            Commands.ChatCommands.Add(new Command(EndCommand, "end"));
            Commands.ChatCommands.Add(new Command(DebugBoundaryCommand, "debugbound"));
            Commands.ChatCommands.Add(new Command(TimerCommand, "timer", "t"));

            TShock.Log.ConsoleInfo("CctgPlugin loaded!");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Deregister events
                ServerApi.Hooks.GamePostInitialize.Deregister(this, OnPostInitialize);
                ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);
                ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
                ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnPlayerJoin);
                GetDataHandlers.TileEdit -= OnTileEdit;
            }
            base.Dispose(disposing);
        }

        private void OnPostInitialize(EventArgs args)
        {
            TShock.Log.ConsoleInfo("[CCTG] Plugin loaded");
        }

        // Command: Manually trigger world painting
        private void PaintWorldCommand(CommandArgs args)
        {
            args.Player.SendInfoMessage("Starting to paint world...");
            worldPainter.PaintWorld();
            args.Player.SendSuccessMessage("Painting complete!");
        }

        // Command: Manually build houses
        private void BuildHousesCommand(CommandArgs args)
        {
            args.Player.SendInfoMessage("Starting to build houses...");
            houseBuilder.BuildHouses();
            args.Player.SendSuccessMessage("House building complete!");
        }

        // Command: Start game
        private void StartCommand(CommandArgs args)
        {
            if (gameStarted)
            {
                args.Player.SendErrorMessage("Game already started, cannot start again!");
                return;
            }

            args.Player.SendInfoMessage("Starting game initialization...");

            // 1. Build houses
            houseBuilder.BuildHouses();
            TSPlayer.All.SendSuccessMessage("[Game Start] Houses built!");

            // 2. Paint world
            worldPainter.PaintWorld();
            TSPlayer.All.SendSuccessMessage("[Game Start] World painted!");

            // 3. Set time to 10:30
            SetTime(10, 30);
            TSPlayer.All.SendSuccessMessage("[Game Start] Time set to 10:30");

            // 4. Reset player inventory/stats (except players with ignoresse permission)
            foreach (var player in TShock.Players)
            {
                if (player != null && player.Active)
                {
                    // Check if player has ignoresse permission
                    if (!player.HasPermission("ignoresse"))
                    {
                        ResetPlayerInventoryAndStats(player);
                        player.SendSuccessMessage("[Game Start] Your inventory, equipment and stats have been reset");
                        TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} inventory, equipment and stats reset");
                    }
                    else
                    {
                        player.SendInfoMessage("[Game Start] You have ignoresse permission, inventory unchanged");
                        TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} has ignoresse permission, skipping reset");
                    }
                }
            }

            // 5. Randomly assign all players to Red or Blue team
            Random random = new Random();
            foreach (var player in TShock.Players)
            {
                if (player != null && player.Active)
                {
                    // Randomly choose Red Team (1) or Blue Team (3)
                    int team = random.Next(2) == 0 ? 1 : 3;
                    player.SetTeam(team);

                    string teamName = team == 1 ? "Red Team" : "Blue Team";
                    player.SendSuccessMessage($"You have been assigned to {teamName}!");

                    // Record team assignment for delayed teleport
                    if (!teleportManager.PlayerTeamStates.ContainsKey(player.Index))
                    {
                        teleportManager.PlayerTeamStates[player.Index] = new PlayerTeamState();
                    }

                    var state = teleportManager.PlayerTeamStates[player.Index];
                    state.LastTeam = team;
                    state.LastTeamChangeTime = DateTime.Now;

                    TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} assigned to {teamName}, preparing teleport");
                }
            }

            gameStarted = true;

            // Start boundary checking
            boundaryChecker.StartBoundaryCheck();

            TSPlayer.All.SendSuccessMessage("════════════════════════════");
            TSPlayer.All.SendSuccessMessage("    Game Started! Good Luck!    ");
            TSPlayer.All.SendSuccessMessage("  Do not cross spawn for 18 minutes!  ");
            TSPlayer.All.SendSuccessMessage("════════════════════════════");

            // Announce biome information
            AnnounceBiomeSides();

            // Replace Starfury with Enchanted Sword in Skyware Chests
            ReplaceStarfuryInSkywareChests();
        }

        // Command: End game
        private void EndCommand(CommandArgs args)
        {
            args.Player.SendInfoMessage("Ending game...");

            // 1. Set time to 10:30
            SetTime(10, 30);
            TSPlayer.All.SendSuccessMessage("[Game End] Time set to 10:30");

            // 2. Clear all NPCs (monsters and bosses)
            int killedCount = 0;
            for (int i = 0; i < Main.npc.Length; i++)
            {
                if (Main.npc[i].active && !Main.npc[i].townNPC)
                {
                    Main.npc[i].active = false;
                    Main.npc[i].type = 0;
                    TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", i);
                    killedCount++;
                }
            }
            TSPlayer.All.SendSuccessMessage($"[Game End] Cleared {killedCount} hostile NPCs");

            // 3. Clear houses
            if (houseBuilder.HousesBuilt)
            {
                houseBuilder.ClearHouses();
                TSPlayer.All.SendSuccessMessage("[Game End] Houses cleared");
            }

            // 4. Reset game state
            gameStarted = false;

            // Stop boundary checking
            boundaryChecker.StopBoundaryCheck();

            // Clear teleport manager states
            teleportManager.ClearAllStates();

            TSPlayer.All.SendSuccessMessage("════════════════════════════");
            TSPlayer.All.SendSuccessMessage("       Game Ended!         ");
            TSPlayer.All.SendSuccessMessage("════════════════════════════");

            TShock.Log.ConsoleInfo("[CCTG] Game ended!");
        }

        // Debug command: Check boundary detection status
        private void DebugBoundaryCommand(CommandArgs args)
        {
            var player = args.Player;
            string debugInfo = boundaryChecker.GetDebugInfo(player);
            player.SendInfoMessage(debugInfo);
            TShock.Log.ConsoleInfo($"[CCTG] {player.Name} used boundary check debug command");
        }

        // Set game time
        private void SetTime(int hour, int minute)
        {
            // Calculate time (game time starts at 4:30 AM)
            double targetMinutes = hour * 60 + minute;
            double startMinutes = 4 * 60 + 30; // 4:30 AM

            if (targetMinutes < startMinutes)
                targetMinutes += 24 * 60; // Add a day

            double gameMinutes = targetMinutes - startMinutes;
            Main.time = gameMinutes * 60; // Convert to game ticks (1 minute = 60 ticks)

            // Set day/night
            Main.dayTime = hour >= 4 && hour < 19; // Day: 4:30-19:30

            // Sync to all players
            TSPlayer.All.SendData(PacketTypes.TimeSet, "", 0, 0, Main.sunModY, Main.moonModY);
        }

        // Reset player inventory and stats
        private void ResetPlayerInventoryAndStats(TSPlayer player)
        {
            // Reset to SSC configured new player state
            player.PlayerData.CopyCharacter(player);
            TShock.CharacterDB.InsertPlayerData(player);
            player.IgnoreSSCPackets = false;

            // Set to SSC starting stats
            player.TPlayer.statLife = TShock.ServerSideCharacterConfig.Settings.StartingHealth;
            player.TPlayer.statLifeMax = TShock.ServerSideCharacterConfig.Settings.StartingHealth;
            player.TPlayer.statMana = TShock.ServerSideCharacterConfig.Settings.StartingMana;
            player.TPlayer.statManaMax = TShock.ServerSideCharacterConfig.Settings.StartingMana;

            // Clear inventory
            for (int i = 0; i < NetItem.InventorySlots; i++)
            {
                player.TPlayer.inventory[i].SetDefaults(0);
            }

            // Clear equipment (armor and accessories)
            for (int i = 0; i < NetItem.ArmorSlots; i++)
            {
                player.TPlayer.armor[i].SetDefaults(0);
            }

            // Clear dyes
            for (int i = 0; i < NetItem.DyeSlots; i++)
            {
                player.TPlayer.dye[i].SetDefaults(0);
            }

            // Clear misc equipment (pets, mounts, etc)
            for (int i = 0; i < NetItem.MiscEquipSlots; i++)
            {
                player.TPlayer.miscEquips[i].SetDefaults(0);
            }

            // Clear misc dyes
            for (int i = 0; i < NetItem.MiscDyeSlots; i++)
            {
                player.TPlayer.miscDyes[i].SetDefaults(0);
            }

            // Give starting items
            var startingItems = TShock.ServerSideCharacterConfig.Settings.StartingInventory;
            for (int i = 0; i < startingItems.Count && i < NetItem.InventorySlots; i++)
            {
                player.TPlayer.inventory[i] = startingItems[i].ToItem();
            }

            // Sync to client
            player.SendData(PacketTypes.PlayerHp, "", player.Index);
            player.SendData(PacketTypes.PlayerMana, "", player.Index);
            player.SendData(PacketTypes.PlayerInfo, "", player.Index);

            // Sync inventory
            for (int i = 0; i < NetItem.InventorySlots; i++)
            {
                player.SendData(PacketTypes.PlayerSlot, "", player.Index, i);
            }

            // Sync equipment slots (armor and accessories)
            for (int i = 0; i < NetItem.ArmorSlots; i++)
            {
                player.SendData(PacketTypes.PlayerSlot, "", player.Index, NetItem.InventorySlots + i);
            }

            // Sync dye slots
            for (int i = 0; i < NetItem.DyeSlots; i++)
            {
                player.SendData(PacketTypes.PlayerSlot, "", player.Index, NetItem.InventorySlots + NetItem.ArmorSlots + i);
            }

            // Sync misc equipment slots
            for (int i = 0; i < NetItem.MiscEquipSlots; i++)
            {
                player.SendData(PacketTypes.PlayerSlot, "", player.Index, NetItem.InventorySlots + NetItem.ArmorSlots + NetItem.DyeSlots + i);
            }

            // Sync misc dye slots
            for (int i = 0; i < NetItem.MiscDyeSlots; i++)
            {
                player.SendData(PacketTypes.PlayerSlot, "", player.Index, NetItem.InventorySlots + NetItem.ArmorSlots + NetItem.DyeSlots + NetItem.MiscEquipSlots + i);
            }
        }

        // Tile edit event handler - protect houses
        private void OnTileEdit(object sender, GetDataHandlers.TileEditEventArgs e)
        {
            if (!houseBuilder.HousesBuilt || e.Handled)
                return;

            // Check if edit position is in any protected area
            foreach (var area in houseBuilder.ProtectedHouseAreas)
            {
                if (area.Contains(e.X, e.Y))
                {
                    // Block destruction if player doesn't have admin permission
                    if (!e.Player.HasPermission("cctg.edit"))
                    {
                        e.Handled = true;
                        e.Player.SendErrorMessage("CCTG house is protected, cannot destroy!");

                        // Restore original tile state
                        e.Player.SendTileRect((short)e.X, (short)e.Y, 1, 1);
                    }
                    break;
                }
            }
        }

        // Player joined event
        private void OnPlayerJoin(GreetPlayerEventArgs e)
        {
            var player = TShock.Players[e.Who];
            if (player != null)
            {
                TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} joined game");
            }
        }

        // Network data event handler - monitor item usage and team changes
        private void OnGetData(GetDataEventArgs e)
        {
            if (e.Handled)
                return;

            var player = TShock.Players[e.Msg.whoAmI];
            if (player == null)
                return;

            // === Monitor Recall item usage ===
            if (e.MsgID == PacketTypes.PlayerUpdate)
            {
                using (var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(e.Msg.readBuffer, e.Index, e.Length)))
                {
                    byte playerId = reader.ReadByte();
                    byte control = reader.ReadByte();
                    byte pulley = reader.ReadByte();
                    byte miscFlags = reader.ReadByte();
                    byte sleepingInfo = reader.ReadByte();
                    byte selectedItem = reader.ReadByte();

                    var selectedItemType = player.TPlayer.inventory[selectedItem].type;

                    // Check if player used a recall item
                    if (teleportManager.IsRecallItem(selectedItemType))
                    {
                        // Check if player is using the item (control flags)
                        bool isUsingItem = (control & 32) != 0; // Bit 5 = using item

                        if (!houseBuilder.HousesBuilt)
                            return;

                        if (isUsingItem)
                        {
                            // Record recall state
                            if (!teleportManager.PlayerRecallStates.ContainsKey(player.Index))
                            {
                                teleportManager.PlayerRecallStates[player.Index] = new RecallTeleportState();
                            }

                            var recallState = teleportManager.PlayerRecallStates[player.Index];

                            if (!recallState.WaitingForTeleport && !recallState.WaitingToTeleportToTeamHouse)
                            {
                                recallState.WaitingForTeleport = true;
                                recallState.LastItemUseTime = DateTime.Now;
                                recallState.LastKnownPosition = player.TPlayer.position;
                                TShock.Log.ConsoleInfo($"[CCTG] Used recall item, waiting for teleport");
                            }
                        }
                    }
                }
            }

            // === Monitor team changes ===
            if (e.MsgID == PacketTypes.PlayerTeam)
            {
                if (!houseBuilder.HousesBuilt)
                    return;

                using (var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(e.Msg.readBuffer, e.Index, e.Length)))
                {
                    byte playerId = reader.ReadByte();
                    byte newTeam = reader.ReadByte();

                    if (!teleportManager.PlayerTeamStates.ContainsKey(player.Index))
                    {
                        teleportManager.PlayerTeamStates[player.Index] = new PlayerTeamState();
                    }

                    var state = teleportManager.PlayerTeamStates[player.Index];

                    // Only trigger teleport if team actually changed
                    if (state.LastTeam != newTeam)
                    {
                        state.LastTeam = newTeam;
                        state.LastTeamChangeTime = DateTime.Now;

                        string teamName = newTeam == 1 ? "Red Team" : newTeam == 3 ? "Blue Team" : "No Team";
                        TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} changed to {teamName}");
                    }
                }
            }

            // === Monitor respawn events ===
            if (e.MsgID == PacketTypes.PlayerSpawn)
            {
                if (!houseBuilder.HousesBuilt)
                    return;

                using (var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(e.Msg.readBuffer, e.Index, e.Length)))
                {
                    byte playerId = reader.ReadByte();
                    short spawnX = reader.ReadInt16();
                    short spawnY = reader.ReadInt16();
                    int respawnTimeRemaining = reader.ReadInt32();
                    byte playerSpawnContext = reader.ReadByte();

                    int playerTeam = player.TPlayer.team;

                    if (!teleportManager.PlayerTeamStates.ContainsKey(player.Index))
                    {
                        teleportManager.PlayerTeamStates[player.Index] = new PlayerTeamState();
                    }

                    var state = teleportManager.PlayerTeamStates[player.Index];
                    state.LastTeam = playerTeam;
                    state.LastTeamChangeTime = DateTime.Now;

                    string teamName = playerTeam == 1 ? "Red Team" : playerTeam == 3 ? "Blue Team" : "No Team";
                    TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} respawned, team {teamName}");

                    player.SendInfoMessage($"Respawning, teleporting to {teamName} house in 0.5s");
                }
            }
        }

        // Game update event handler
        private void OnGameUpdate(EventArgs args)
        {
            // Update scoreboard (every second)
            scoreboardUpdateCounter++;
            if (scoreboardUpdateCounter >= SCOREBOARD_UPDATE_INTERVAL)
            {
                scoreboardUpdateCounter = 0;
                UpdateScoreboard();
            }

            if (!houseBuilder.HousesBuilt)
                return;

            // Clear mobs in houses (every 0.5s, only after game starts)
            if (gameStarted)
            {
                mobClearCounter++;
                if (mobClearCounter >= MOB_CLEAR_INTERVAL)
                {
                    mobClearCounter = 0;
                    houseBuilder.ClearMobsInHouses();
                }
            }

            foreach (var player in TShock.Players)
            {
                if (player == null || !player.Active)
                    continue;

                // === Handle teleport after team change ===
                if (teleportManager.PlayerTeamStates.ContainsKey(player.Index))
                {
                    var state = teleportManager.PlayerTeamStates[player.Index];

                    // Check if teleport needed (0.5s after team change)
                    if (state.LastTeamChangeTime != DateTime.MinValue)
                    {
                        var timeSinceChange = (DateTime.Now - state.LastTeamChangeTime).TotalSeconds;

                        if (timeSinceChange >= 0.5)
                        {
                            // Execute teleport
                            teleportManager.TeleportToTeamHouse(player, houseBuilder.LeftHouseSpawn, houseBuilder.RightHouseSpawn);

                            // Clear teleport marker
                            state.LastTeamChangeTime = DateTime.MinValue;
                        }
                    }
                }

                // === Handle recall teleport ===
                if (teleportManager.PlayerRecallStates.ContainsKey(player.Index))
                {
                    var recallState = teleportManager.PlayerRecallStates[player.Index];

                    // Stage 1: Wait for vanilla teleport
                    if (recallState.WaitingForTeleport)
                    {
                        var timeSinceItemUse = (DateTime.Now - recallState.LastItemUseTime).TotalSeconds;

                        if (timeSinceItemUse < 3.0)
                        {
                            float distance = Vector2.Distance(player.TPlayer.position, recallState.LastKnownPosition);

                            // If position changed > 200 pixels, teleport occurred
                            if (distance > 200f)
                            {
                                TShock.Log.ConsoleInfo($"[CCTG] Recall teleport detected (distance: {distance}), 0.5s teleport to team house");

                                recallState.WaitingForTeleport = false;
                                recallState.WaitingToTeleportToTeamHouse = true;
                                recallState.TeleportDetectedTime = DateTime.Now;
                                continue;
                            }

                            recallState.LastKnownPosition = player.TPlayer.position;
                        }
                        else
                        {
                            TShock.Log.ConsoleInfo($"[CCTG] Teleport wait timeout, cancelled");
                            recallState.WaitingForTeleport = false;
                        }
                    }

                    // Stage 2: Teleport to team house
                    if (recallState.WaitingToTeleportToTeamHouse)
                    {
                        var timeSinceDetect = (DateTime.Now - recallState.TeleportDetectedTime).TotalSeconds;

                        if (timeSinceDetect >= 0.5)
                        {
                            teleportManager.TeleportToTeamHouse(player, houseBuilder.LeftHouseSpawn, houseBuilder.RightHouseSpawn);

                            recallState.WaitingToTeleportToTeamHouse = false;
                        }
                    }
                }

                // === Boundary violation check (only during first 18 minutes of game) ===
                if (gameStarted)
                {
                    boundaryChecker.CheckBoundaryViolation(player);
                }
            }
        }

        // Update scoreboard
        private void UpdateScoreboard()
        {
            // Calculate current game time
            double currentTime = Main.time;
            bool isDayTime = Main.dayTime;

            // Calculate current hour and minute
            int hour, minute;
            if (isDayTime)
            {
                // Day time: 4:30 AM (0) to 7:30 PM (54000)
                // 54000 ticks = 15 hours (4:30 to 19:30)
                double dayProgress = currentTime / 54000.0;
                double totalMinutes = dayProgress * 15 * 60; // 15 hours in minutes
                hour = 4 + (int)(totalMinutes / 60);
                minute = (int)(totalMinutes % 60);
                if (hour >= 12)
                {
                    hour = hour > 12 ? hour - 12 : 12;
                }
            }
            else
            {
                // Night time: 7:30 PM (0) to 4:30 AM (32400)
                // 32400 ticks = 9 hours (19:30 to 4:30)
                double nightProgress = currentTime / 32400.0;
                double totalMinutes = nightProgress * 9 * 60; // 9 hours in minutes
                hour = 7 + (int)(totalMinutes / 60);
                minute = 30 + (int)(totalMinutes % 60);
                if (minute >= 60)
                {
                    minute -= 60;
                    hour++;
                }
                if (hour >= 12)
                {
                    hour = hour > 12 ? hour - 12 : 12;
                }
            }

            // Calculate time until next 4:30 AM
            double timeUntilNextDay;
            if (isDayTime)
            {
                // Time remaining in day + full night
                timeUntilNextDay = (54000 - currentTime) + 32400;
            }
            else
            {
                // Time remaining in night
                timeUntilNextDay = 32400 - currentTime;
            }

            // Convert to minutes
            int minutesUntilNextDay = (int)((timeUntilNextDay / 86400.0) * 24 * 60);
            int hoursUntil = minutesUntilNextDay / 60;
            int minutesUntil = minutesUntilNextDay % 60;

            // Format time strings
            string ampm = (hour >= 12 || hour < 4) ? "PM" : "AM";
            string currentTimeStr = $"{hour}:{minute:D2} {ampm}";
            string untilNextDayStr = $"{hoursUntil}:{minutesUntil:D2}";

            // Build scoreboard as single line (no newlines to avoid percentage bug)
            string scoreboardText = $"       Time: {currentTimeStr}  |  Next Day: {untilNextDayStr}";

            // Send to all players
            foreach (var player in TShock.Players)
            {
                if (player != null && player.Active && player.ConnectionAlive)
                {
                    // Use NetworkText to send clean text without formatting issues
                    NetMessage.SendData((int)PacketTypes.Status, player.Index, -1,
                        Terraria.Localization.NetworkText.FromLiteral(scoreboardText), 0, 0f, 0f);
                }
            }
        }

        // Announce which side has which biome based on dungeon position
        private void AnnounceBiomeSides()
        {
            int spawnX = Main.spawnTileX;
            int dungeonX = Main.dungeonX;

            // Determine which side is which based on dungeon position
            // Jungle is always opposite to dungeon, Snow is on same side as dungeon
            bool dungeonOnLeft = dungeonX < spawnX;

            string leftBiome, rightBiome;

            if (dungeonOnLeft)
            {
                // Dungeon on left = Snow on left, Jungle on right
                leftBiome = "Snow";
                rightBiome = "Jungle";
            }
            else
            {
                // Dungeon on right = Snow on right, Jungle on left
                leftBiome = "Jungle";
                rightBiome = "Snow";
            }

            // Announce to all players
            TSPlayer.All.SendInfoMessage($"[Map Info] Red Team (Left): {leftBiome} side");
            TSPlayer.All.SendInfoMessage($"[Map Info] Blue Team (Right): {rightBiome} side");

            TShock.Log.ConsoleInfo($"[CCTG] Biome sides - Left: {leftBiome}, Right: {rightBiome} (Dungeon at X={dungeonX}, Spawn at X={spawnX})");
        }

        // Replace Starfury with Enchanted Sword in all Skyware Chests
        private void ReplaceStarfuryInSkywareChests()
        {
            int chestsChecked = 0;
            int starfuryReplaced = 0;

            // Iterate through all chests in the world
            for (int i = 0; i < Main.chest.Length; i++)
            {
                var chest = Main.chest[i];
                if (chest == null)
                    continue;

                // Get the tile at chest position
                int chestX = chest.x;
                int chestY = chest.y;

                if (chestX < 0 || chestX >= Main.maxTilesX || chestY < 0 || chestY >= Main.maxTilesY)
                    continue;

                var tile = Main.tile[chestX, chestY];
                if (tile == null || !tile.active() || tile.type != TileID.Containers)
                    continue;

                // Check if this is a Skyware Chest (style 13)
                int chestStyle = tile.frameX / 36;
                if (chestStyle != 13)
                    continue;

                chestsChecked++;

                // Check items in this chest
                for (int itemSlot = 0; itemSlot < Chest.maxItems; itemSlot++)
                {
                    var item = chest.item[itemSlot];
                    if (item == null || item.type != ItemID.Starfury)
                        continue;

                    // Replace Starfury with Enchanted Sword
                    item.SetDefaults(ItemID.EnchantedSword);
                    starfuryReplaced++;
                }
            }

            if (starfuryReplaced > 0)
            {
                TShock.Log.ConsoleInfo($"[CCTG] Starfury replacement: {chestsChecked} Skyware Chests checked, {starfuryReplaced} items replaced");
            }
        }

        // Timer command: /timer or /t
        private void TimerCommand(CommandArgs args)
        {
            string playerName = args.Player.Name;

            if (playerTimers.ContainsKey(playerName))
            {
                // Timer is running, stop it and show elapsed time
                DateTime startTime = playerTimers[playerName];
                TimeSpan elapsed = DateTime.Now - startTime;
                playerTimers.Remove(playerName);

                string timeString = FormatTimeSpan(elapsed);
                args.Player.SendSuccessMessage($"Timer stopped: {timeString}");
                TSPlayer.All.SendInfoMessage($"{playerName}: {timeString}");
            }
            else
            {
                // Start timer
                playerTimers[playerName] = DateTime.Now;
                args.Player.SendSuccessMessage("Timer started!");
            }
        }

        // Player leave event handler - show timer if running
        private void OnPlayerLeave(LeaveEventArgs args)
        {
            var player = TShock.Players[args.Who];
            if (player == null)
                return;

            string playerName = player.Name;

            if (playerTimers.ContainsKey(playerName))
            {
                // Timer was running, show elapsed time
                DateTime startTime = playerTimers[playerName];
                TimeSpan elapsed = DateTime.Now - startTime;
                playerTimers.Remove(playerName);

                string timeString = FormatTimeSpan(elapsed);
                TSPlayer.All.SendInfoMessage($"{playerName} left - Timer: {timeString}");
            }
        }

        // Format timespan as readable string
        private string FormatTimeSpan(TimeSpan elapsed)
        {
            if (elapsed.TotalHours >= 1)
            {
                return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m {elapsed.Seconds}s";
            }
            else if (elapsed.TotalMinutes >= 1)
            {
                return $"{elapsed.Minutes}m {elapsed.Seconds}s";
            }
            else
            {
                return $"{elapsed.Seconds}.{elapsed.Milliseconds / 100}s";
            }
        }
    }
}

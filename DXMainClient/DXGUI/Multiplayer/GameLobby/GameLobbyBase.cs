﻿using ClientCore;
using ClientCore.Statistics;
using ClientGUI;
using DTAClient.Domain;
using DTAClient.Domain.Multiplayer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Updater;

namespace DTAClient.DXGUI.Multiplayer.GameLobby
{
    [Flags]
    public enum GameOptionFlags
    {
        None = 0,

        /// <summary>
        /// If set, the client will remove all starting waypoints from the map
        /// before launching it.
        /// </summary>
        RandomizeStartingLocations = 1,

        /// <summary>
        /// If set, the NoRNG spawner flag will be set.
        /// For debugging sync errors.
        /// </summary>
        NoRNG = 2
    }

    /// <summary>
    /// A generic base for all game lobbies (Skirmish, LAN and CnCNet).
    /// Contains the common logic for parsing game options and handling player info.
    /// </summary>
    public abstract class GameLobbyBase : INItializableWindow
    {
        protected const int MAX_PLAYER_COUNT = 8;
        protected const int PLAYER_OPTION_VERTICAL_MARGIN = 12;
        protected const int PLAYER_OPTION_HORIZONTAL_MARGIN = 3;
        protected const int PLAYER_OPTION_CAPTION_Y = 6;
        private const int DROP_DOWN_HEIGHT = 21;

        private const int RANK_NONE = 0;
        private const int RANK_EASY = 1;
        private const int RANK_MEDIUM = 2;
        private const int RANK_HARD = 3;
        private const int RANK_BRUTAL = 4;

        /// <summary>
        /// Creates a new instance of the game lobby base.
        /// </summary>
        /// <param name="game">The game.</param>
        /// <param name="iniName">The name of the lobby in GameOptions.ini.</param>
        public GameLobbyBase(WindowManager windowManager, string iniName,
            List<GameMode> GameModes, bool isMultiplayer, DiscordHandler discordHandler) : base(windowManager)
        {
            _iniSectionName = iniName;
            this.GameModes = GameModes;
            this.isMultiplayer = isMultiplayer;
            this.discordHandler = discordHandler;
        }

        private string _iniSectionName;

        protected XNAPanel PlayerOptionsPanel;

        protected List<MultiplayerColor> MPColors;

        public List<GameLobbyCheckBox> CheckBoxes = new List<GameLobbyCheckBox>();
        public List<GameLobbyDropDown> DropDowns = new List<GameLobbyDropDown>();

        protected DiscordHandler discordHandler;

        /// <summary>
        /// The list of multiplayer game modes.
        /// </summary>
        protected List<GameMode> GameModes;

        private GameMode gameMode;

        /// <summary>
        /// The currently selected game mode.
        /// </summary>
        protected GameMode GameMode
        {
            get => gameMode;
            set
            {
                var oldGameMode = gameMode;
                gameMode = value;
                if (value != null && oldGameMode != value)
                    UpdateDiscordPresence();
            }
        }

        private Map map;

        /// <summary>
        /// The currently selected map.
        /// </summary>
        protected Map Map
        {
            get => map;
            set
            {
                var oldMap = map;
                map = value;
                if (value != null && oldMap != value)
                    UpdateDiscordPresence();
            }
        }

        protected XNAClientDropDown[] ddPlayerNames;
        protected XNAClientDropDown[] ddPlayerSides;
        protected XNAClientDropDown[] ddPlayerColors;
        protected XNAClientDropDown[] ddPlayerStarts;
        protected XNAClientDropDown[] ddPlayerTeams;

        protected XNAClientButton btnLeaveGame;
        protected GameLaunchButton btnLaunchGame;
        protected XNAClientButton btnPickRandomMap;
        protected XNALabel lblMapName;
        protected XNALabel lblMapAuthor;
        protected XNALabel lblGameMode;
        protected XNALabel lblMapSize;

        protected MapPreviewBox MapPreviewBox;

        protected XNAMultiColumnListBox lbMapList;
        protected XNAClientDropDown ddGameMode;
        protected XNALabel lblGameModeSelect;
        protected XNAContextMenu mapContextMenu;

        protected XNASuggestionTextBox tbMapSearch;

        protected List<PlayerInfo> Players = new List<PlayerInfo>();
        protected List<PlayerInfo> AIPlayers = new List<PlayerInfo>();

        protected virtual PlayerInfo FindLocalPlayer() => Players.Find(p => p.Name == ProgramConstants.PLAYERNAME);

        protected bool PlayerUpdatingInProgress { get; set; }

        protected Texture2D[] RankTextures;

        /// <summary>
        /// The seed used for randomizing player options.
        /// </summary>
        protected int RandomSeed { get; set; }

        /// <summary>
        /// An unique identifier for this game.
        /// </summary>
        protected int UniqueGameID { get; set; }
        protected int SideCount { get; private set; }
        protected int RandomSelectorCount { get; private set; } = 1;

        protected List<int[]> RandomSelectors = new List<int[]>();

        private readonly bool isMultiplayer = false;

        private MatchStatistics matchStatistics;

        private bool disableGameOptionUpdateBroadcast = false;

        protected GameOptionFlags GameOptionFlags { get; set; } = GameOptionFlags.None;

        protected IniFile GameOptionsIni { get; private set; }

        private GameModeDescriptionPanel gameModeDescriptionPanel;
        private ToolTip gameModeLabelTooltip;

        public bool IsGameOptionFlagEnabled(GameOptionFlags flagToCheck, GameOptionFlags flagsContainer)
        {
            return (flagsContainer & flagToCheck) == flagToCheck;
        }

        public override void Initialize()
        {
            Name = _iniSectionName;
            //if (WindowManager.RenderResolutionY < 800)
            //    ClientRectangle = new Rectangle(0, 0, WindowManager.RenderResolutionX, WindowManager.RenderResolutionY);
            //else
            ClientRectangle = new Rectangle(0, 0, WindowManager.RenderResolutionX - 60, WindowManager.RenderResolutionY - 32);
            WindowManager.CenterControlOnScreen(this);
            BackgroundTexture = AssetLoader.LoadTexture("gamelobbybg.png");

            RankTextures = new Texture2D[]
            {
                AssetLoader.LoadTexture("rankNone.png"),
                AssetLoader.LoadTexture("rankEasy.png"),
                AssetLoader.LoadTexture("rankNormal.png"),
                AssetLoader.LoadTexture("rankHard.png"),
                AssetLoader.LoadTexture("rankBrutal.png"),
                AssetLoader.LoadTexture("rankBrutal.png")
            };

            MPColors = MultiplayerColor.LoadColors();

            GameOptionsIni = new IniFile(ProgramConstants.GetBaseResourcePath() + "GameOptions.ini");

            base.Initialize();

            PlayerOptionsPanel = FindChild<XNAPanel>(nameof(PlayerOptionsPanel));

            btnLeaveGame = FindChild<XNAClientButton>(nameof(btnLeaveGame));
            btnLeaveGame.LeftClick += BtnLeaveGame_LeftClick;

            btnLaunchGame = FindChild<GameLaunchButton>(nameof(btnLaunchGame));
            btnLaunchGame.LeftClick += BtnLaunchGame_LeftClick;
            btnLaunchGame.InitStarDisplay(RankTextures);

            MapPreviewBox = FindChild<MapPreviewBox>("MapPreviewBox");
            MapPreviewBox.SetFields(Players, AIPlayers, MPColors, GameOptionsIni.GetStringValue("General", "Sides", String.Empty).Split(','), GameOptionsIni);

            lblMapName = FindChild<XNALabel>(nameof(lblMapName));
            lblMapAuthor = FindChild<XNALabel>(nameof(lblMapAuthor));
            lblGameMode = FindChild<XNALabel>(nameof(lblGameMode));
            lblMapSize = FindChild<XNALabel>(nameof(lblMapSize));

            lbMapList = FindChild<XNAMultiColumnListBox>(nameof(lbMapList));
            lbMapList.AllowKeyboardInput = !isMultiplayer;
            lbMapList.SelectedIndexChanged += LbMapList_SelectedIndexChanged;
            lbMapList.RightClick += LbMapList_RightClick;

            mapContextMenu = new XNAContextMenu(WindowManager);
            mapContextMenu.Name = nameof(mapContextMenu);
            mapContextMenu.Width = 100;
            mapContextMenu.AddItem("Delete Map", DeleteMapConfirmation, () => Map != null && !Map.Official);
            AddChild(mapContextMenu);

            XNAPanel rankHeader = new XNAPanel(WindowManager);
            rankHeader.BackgroundTexture = AssetLoader.LoadTexture("rank.png");
            rankHeader.ClientRectangle = new Rectangle(0, 0, rankHeader.BackgroundTexture.Width,
                19);

            XNAListBox rankListBox = new XNAListBox(WindowManager);
            rankListBox.TextBorderDistance = 2;

            lbMapList.AddColumn(rankHeader, rankListBox);
            lbMapList.AddColumn("MAP NAME", lbMapList.Width - RankTextures[1].Width - 3);

            ddGameMode = FindChild<XNAClientDropDown>(nameof(ddGameMode));
            ddGameMode.SelectedIndexChanged += DdGameMode_SelectedIndexChanged;
            
            foreach (GameMode gm in GameModes)
                ddGameMode.AddItem(gm.UIName);

            lblGameModeSelect = FindChild<XNALabel>(nameof(lblGameModeSelect));

            tbMapSearch = FindChild<XNASuggestionTextBox>(nameof(tbMapSearch));
            tbMapSearch.InputReceived += TbMapSearch_InputReceived;

            btnPickRandomMap = FindChild<XNAClientButton>(nameof(btnPickRandomMap));
            btnPickRandomMap.LeftClick += BtnPickRandomMap_LeftClick;

            CheckBoxes.ForEach(chk => chk.CheckedChanged += ChkBox_CheckedChanged);
            DropDowns.ForEach(dd => dd.SelectedIndexChanged += Dropdown_SelectedIndexChanged);

            gameModeDescriptionPanel = new GameModeDescriptionPanel(WindowManager);
            gameModeDescriptionPanel.Name = nameof(gameModeDescriptionPanel);
            AddChild(gameModeDescriptionPanel);
            gameModeDescriptionPanel.X = ddGameMode.Right;
            gameModeDescriptionPanel.Y = ddGameMode.Y;
            gameModeDescriptionPanel.Disable();

            gameModeLabelTooltip = new ToolTip(WindowManager, lblGameMode);
        }

        private void BtnPickRandomMap_LeftClick(object sender, EventArgs e) => PickRandomMap();

        private void TbMapSearch_InputReceived(object sender, EventArgs e) => ListMaps();

        private void Dropdown_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (disableGameOptionUpdateBroadcast)
                return;

            var dd = (GameLobbyDropDown)sender;
            dd.HostSelectedIndex = dd.SelectedIndex;
            OnGameOptionChanged();
        }

        private void ChkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (disableGameOptionUpdateBroadcast)
                return;

            var checkBox = (GameLobbyCheckBox)sender;
            checkBox.HostChecked = checkBox.Checked;
            OnGameOptionChanged();
        }

        protected virtual void OnGameOptionChanged()
        {
            CheckDisallowedSides();

            btnLaunchGame.SetRank(GetRank());
        }

        protected void DdGameMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            GameMode = GameModes[ddGameMode.SelectedIndex];

            tbMapSearch.Text = string.Empty;
            tbMapSearch.OnSelectedChanged();

            ListMaps();

            if (lbMapList.SelectedIndex == -1)
                lbMapList.SelectedIndex = 0; // Select default map
            else
                ChangeMap(GameMode, Map);
        }

        private int GetRankIndexForCoopDifficultyLevel(int coopDifficultyLevel) 
        {
            switch (coopDifficultyLevel)
            {
                case 0: return RANK_HARD;
                case 1: return RANK_MEDIUM;
                case 2: return RANK_EASY;
                case 3: return RANK_BRUTAL;
                default: return RANK_NONE;
            }
        } 

        private void ListMaps()
        {
            lbMapList.SelectedIndexChanged -= LbMapList_SelectedIndexChanged;

            lbMapList.ClearItems();
            lbMapList.SetTopIndex(0);

            lbMapList.SelectedIndex = -1;

            int mapIndex = -1;
            int skippedMapsCount = 0;

            for (int i = 0; i < GameMode.Maps.Count; i++)
            {
                if (tbMapSearch.Text != tbMapSearch.Suggestion)
                {
                    if (!GameMode.Maps[i].Name.ToUpper().Contains(tbMapSearch.Text.ToUpper()))
                    {
                        skippedMapsCount++;
                        continue;
                    }
                }

                XNAListBoxItem rankItem = new XNAListBoxItem();
                if (GameMode.Maps[i].IsCoop)
                {
                    if (StatisticsManager.Instance.HasBeatCoOpMap(GameMode.Maps[i].Name, GameMode.UIName))
                        rankItem.Texture = RankTextures[GetRankIndexForCoopDifficultyLevel(GameMode.CoopDifficultyLevel)];
                    else
                        rankItem.Texture = RankTextures[0];
                }
                else
                    rankItem.Texture = RankTextures[GetDefaultMapRankIndex(GameMode.Maps[i]) + 1];

                XNAListBoxItem mapNameItem = new XNAListBoxItem();
                mapNameItem.Text = Renderer.GetSafeString(GameMode.Maps[i].Name, lbMapList.FontIndex);
                if ((GameMode.Maps[i].MultiplayerOnly || GameMode.MultiplayerOnly) && !isMultiplayer)
                    mapNameItem.TextColor = UISettings.ActiveSettings.DisabledItemColor;
                mapNameItem.Tag = GameMode.Maps[i];

                XNAListBoxItem[] mapInfoArray = new XNAListBoxItem[]
                {
                    rankItem,
                    mapNameItem,
                };

                lbMapList.AddItem(mapInfoArray);

                if (GameMode.Maps[i] == Map)
                    mapIndex = i - skippedMapsCount;
            }

            if (mapIndex > -1)
            {
                lbMapList.SelectedIndex = mapIndex;
                while (mapIndex > lbMapList.LastIndex)
                    lbMapList.TopIndex++;
            }

            lbMapList.SelectedIndexChanged += LbMapList_SelectedIndexChanged;
        }

        protected abstract int GetDefaultMapRankIndex(Map map);

        private void LbMapList_RightClick(object sender, EventArgs e)
        {
            if (isMultiplayer || lbMapList.SelectedIndex < 0 || lbMapList.SelectedIndex >= lbMapList.ItemCount)
                return;

            mapContextMenu.Open(GetCursorPoint());
        }

        private void DeleteMapConfirmation()
        {
            if (Map == null)
                return;

            var messageBox = XNAMessageBox.ShowYesNoDialog(WindowManager, "Delete Confirmation",
                "Are you sure you wish to delete the custom map \"" + Map.Name + "\"?");
            messageBox.YesClickedAction = DeleteSelectedMap;
        }

        private void DeleteSelectedMap(XNAMessageBox messageBox)
        {
            try
            {
                Logger.Log("Deleting map " + Map.BaseFilePath);
                File.Delete(Map.CompleteFilePath);
                foreach (GameMode gameMode in GameModes)
                {
                    gameMode.Maps.Remove(Map);
                }

                tbMapSearch.Text = string.Empty;
                GameMode newGameMode = GameMode;
                if (newGameMode.Maps.Count == 0)
                    newGameMode = GameModes.Find(gm => gm.Maps.Count > 0);

                Map = newGameMode?.Maps[0];

                ListMaps();
                ChangeMap(newGameMode, Map);
            }
            catch (IOException ex)
            {
                Logger.Log($"Deleting map {Map.BaseFilePath} failed! Message: {ex.Message}");
                XNAMessageBox.Show(WindowManager, "Deleting Map Failed", "Deleting map failed! Reason: " + ex.Message);
            }
        }

        private void LbMapList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lbMapList.SelectedIndex < 0 || lbMapList.SelectedIndex >= lbMapList.ItemCount)
                return;

            XNAListBoxItem item = lbMapList.GetItem(1, lbMapList.SelectedIndex);

            Map map = (Map)item.Tag;

            ChangeMap(GameMode, map);
        }

        private void PickRandomMap()
        {
            int totalPlayerCount = Players.Count(p => p.SideId < ddPlayerSides[0].Items.Count - 1)
                   + AIPlayers.Count;
            List<Map> maps = GetMapList(totalPlayerCount);
            if (maps.Count < 1)
                return;

            int random = new Random().Next(0, maps.Count);
            Map = maps[random];

            Logger.Log("PickRandomMap: Rolled " + random + " out of " + maps.Count + ". Picked map: " + Map.Name);

            ChangeMap(GameMode, Map);
            tbMapSearch.Text = string.Empty;
            tbMapSearch.OnSelectedChanged();
            ListMaps();
        }

        private List<Map> GetMapList(int playerCount)
        {
            List<Map> mapList = new List<Map>(GameMode.Maps.Where(x => x.MaxPlayers == playerCount));
            if (mapList.Count < 1 && playerCount <= MAX_PLAYER_COUNT)
                return GetMapList(playerCount + 1);
            else
                return mapList;
        }

        /// <summary>
        /// Refreshes the map selection UI to match the currently selected map
        /// and game mode.
        /// </summary>
        protected void RefreshMapSelectionUI()
        {
            if (GameMode == null)
                return;

            int gameModeIndex = ddGameMode.Items.FindIndex(i => i.Text == GameMode.UIName);

            if (gameModeIndex == -1)
                return;

            if (ddGameMode.SelectedIndex == gameModeIndex)
                DdGameMode_SelectedIndexChanged(this, EventArgs.Empty);

            ddGameMode.SelectedIndex = gameModeIndex;
        }

        /// <summary>
        /// Initializes the player option drop-down controls.
        /// </summary>
        protected void InitPlayerOptionDropdowns()
        {
            ddPlayerNames = new XNAClientDropDown[MAX_PLAYER_COUNT];
            ddPlayerSides = new XNAClientDropDown[MAX_PLAYER_COUNT];
            ddPlayerColors = new XNAClientDropDown[MAX_PLAYER_COUNT];
            ddPlayerStarts = new XNAClientDropDown[MAX_PLAYER_COUNT];
            ddPlayerTeams = new XNAClientDropDown[MAX_PLAYER_COUNT];

            int playerOptionVecticalMargin = ConfigIni.GetIntValue(Name, "PlayerOptionVerticalMargin", PLAYER_OPTION_VERTICAL_MARGIN);
            int playerOptionHorizontalMargin = ConfigIni.GetIntValue(Name, "PlayerOptionHorizontalMargin", PLAYER_OPTION_HORIZONTAL_MARGIN);
            int playerOptionCaptionLocationY = ConfigIni.GetIntValue(Name, "PlayerOptionCaptionLocationY", PLAYER_OPTION_CAPTION_Y);
            int playerNameWidth = ConfigIni.GetIntValue(Name, "PlayerNameWidth", 136);
            int sideWidth = ConfigIni.GetIntValue(Name, "SideWidth", 91);
            int colorWidth = ConfigIni.GetIntValue(Name, "ColorWidth", 79);
            int startWidth = ConfigIni.GetIntValue(Name, "StartWidth", 49);
            int teamWidth = ConfigIni.GetIntValue(Name, "TeamWidth", 46);
            int locationX = ConfigIni.GetIntValue(Name, "PlayerOptionLocationX", 25);
            int locationY = ConfigIni.GetIntValue(Name, "PlayerOptionLocationY", 24);

            // InitPlayerOptionDropdowns(136, 91, 79, 49, 46, new Point(25, 24));

            string[] sides = ClientConfiguration.Instance.Sides.Split(',');
            SideCount = sides.Length;

            List<string> selectorNames = new List<string>();
            GetRandomSelectors(selectorNames, RandomSelectors);
            RandomSelectorCount = RandomSelectors.Count + 1;
            MapPreviewBox.RandomSelectorCount = RandomSelectorCount;

            string randomColor = GameOptionsIni.GetStringValue("General", "RandomColor", "255,255,255");

            for (int i = MAX_PLAYER_COUNT - 1; i > -1; i--)
            {
                var ddPlayerName = new XNAClientDropDown(WindowManager);
                ddPlayerName.Name = "ddPlayerName" + i;
                ddPlayerName.ClientRectangle = new Rectangle(locationX,
                    locationY + (DROP_DOWN_HEIGHT + playerOptionVecticalMargin) * i,
                    playerNameWidth, DROP_DOWN_HEIGHT);
                ddPlayerName.AddItem(string.Empty);
                ddPlayerName.AddItem(AILevelToName(0));
                ddPlayerName.AddItem(AILevelToName(1));
                ddPlayerName.AddItem(AILevelToName(2));
                ddPlayerName.AddItem(AILevelToName(3));
                ddPlayerName.AddItem(AILevelToName(4));
                ddPlayerName.AllowDropDown = true;
                ddPlayerName.SelectedIndexChanged += CopyPlayerDataFromUI;
                ddPlayerName.Tag = true;

                var ddPlayerSide = new XNAClientDropDown(WindowManager);
                ddPlayerSide.Name = "ddPlayerSide" + i;
                ddPlayerSide.ClientRectangle = new Rectangle(
                    ddPlayerName.Right + playerOptionHorizontalMargin,
                    ddPlayerName.Y, sideWidth, DROP_DOWN_HEIGHT);
                ddPlayerSide.AddItem("Random", LoadTextureOrNull("randomicon.png"));
                foreach (string randomSelector in selectorNames)
                    ddPlayerSide.AddItem(randomSelector, LoadTextureOrNull(randomSelector + "icon.png"));
                foreach (string sideName in sides)
                    ddPlayerSide.AddItem(sideName, LoadTextureOrNull(sideName + "icon.png"));
                ddPlayerSide.AllowDropDown = false;
                ddPlayerSide.SelectedIndexChanged += CopyPlayerDataFromUI;
                ddPlayerSide.Tag = true;

                var ddPlayerColor = new XNAClientDropDown(WindowManager);
                ddPlayerColor.Name = "ddPlayerColor" + i;
                ddPlayerColor.ClientRectangle = new Rectangle(
                    ddPlayerSide.Right + playerOptionHorizontalMargin,
                    ddPlayerName.Y, colorWidth, DROP_DOWN_HEIGHT);
                ddPlayerColor.AddItem("Random", AssetLoader.GetColorFromString(randomColor));
                foreach (MultiplayerColor mpColor in MPColors)
                    ddPlayerColor.AddItem(mpColor.Name, mpColor.XnaColor);
                ddPlayerColor.AllowDropDown = false;
                ddPlayerColor.SelectedIndexChanged += CopyPlayerDataFromUI;
                ddPlayerColor.Tag = false;

                var ddPlayerTeam = new XNAClientDropDown(WindowManager);
                ddPlayerTeam.Name = "ddPlayerTeam" + i;
                ddPlayerTeam.ClientRectangle = new Rectangle(
                    ddPlayerColor.Right + playerOptionHorizontalMargin,
                    ddPlayerName.Y, teamWidth, DROP_DOWN_HEIGHT);
                ddPlayerTeam.AddItem("-");
                ddPlayerTeam.AddItem("A");
                ddPlayerTeam.AddItem("B");
                ddPlayerTeam.AddItem("C");
                ddPlayerTeam.AddItem("D");
                ddPlayerTeam.AllowDropDown = false;
                ddPlayerTeam.SelectedIndexChanged += CopyPlayerDataFromUI;
                ddPlayerTeam.Tag = true;

                var ddPlayerStart = new XNAClientDropDown(WindowManager);
                ddPlayerStart.Name = "ddPlayerStart" + i;
                ddPlayerStart.ClientRectangle = new Rectangle(
                    ddPlayerTeam.Right + playerOptionHorizontalMargin,
                    ddPlayerName.Y, startWidth, DROP_DOWN_HEIGHT);
                for (int j = 1; j < 9; j++)
                    ddPlayerStart.AddItem(j.ToString());
                ddPlayerStart.AllowDropDown = false;
                ddPlayerStart.SelectedIndexChanged += CopyPlayerDataFromUI;
                ddPlayerStart.Visible = false;
                ddPlayerStart.Enabled = false;
                ddPlayerStart.Tag = true;

                ddPlayerNames[i] = ddPlayerName;
                ddPlayerSides[i] = ddPlayerSide;
                ddPlayerColors[i] = ddPlayerColor;
                ddPlayerStarts[i] = ddPlayerStart;
                ddPlayerTeams[i] = ddPlayerTeam;

                PlayerOptionsPanel.AddChild(ddPlayerName);
                PlayerOptionsPanel.AddChild(ddPlayerSide);
                PlayerOptionsPanel.AddChild(ddPlayerColor);
                PlayerOptionsPanel.AddChild(ddPlayerStart);
                PlayerOptionsPanel.AddChild(ddPlayerTeam);

                ReadINIForControl(ddPlayerName);
                ReadINIForControl(ddPlayerSide);
                ReadINIForControl(ddPlayerColor);
                ReadINIForControl(ddPlayerStart);
                ReadINIForControl(ddPlayerTeam);
            }

            var lblName = GeneratePlayerOptionCaption("lblName", "PLAYER", ddPlayerNames[0].X, playerOptionCaptionLocationY);
            var lblSide = GeneratePlayerOptionCaption("lblSide", "SIDE", ddPlayerSides[0].X, playerOptionCaptionLocationY);
            var lblColor = GeneratePlayerOptionCaption("lblColor", "COLOR", ddPlayerColors[0].X, playerOptionCaptionLocationY);

            var lblStart = GeneratePlayerOptionCaption("lblStart", "START", ddPlayerStarts[0].X, playerOptionCaptionLocationY);
            lblStart.Visible = false;

            var lblTeam = GeneratePlayerOptionCaption("lblTeam", "TEAM", ddPlayerTeams[0].X, playerOptionCaptionLocationY);

            ReadINIForControl(lblName);
            ReadINIForControl(lblSide);
            ReadINIForControl(lblColor);
            ReadINIForControl(lblStart);
            ReadINIForControl(lblTeam);

            CheckDisallowedSides();
        }

        private XNALabel GeneratePlayerOptionCaption(string name, string text, int x, int y)
        {
            var label = new XNALabel(WindowManager);
            label.Name = name;
            label.Text = text;
            label.FontIndex = 1;
            label.ClientRectangle = new Rectangle(x, y, 0, 0);
            PlayerOptionsPanel.AddChild(label);

            return label;
        }

        private Texture2D LoadTextureOrNull(string name) =>
            AssetLoader.AssetExists(name) ? AssetLoader.LoadTexture(name) : null;

        /// <summary>
        /// Loads random side selectors from GameOptions.ini
        /// </summary>
        /// <param name="selectorNames">TODO comment</param>
        /// <param name="selectorSides">TODO comment</param>
        private void GetRandomSelectors(List<string> selectorNames, List<int[]> selectorSides)
        {
            List<string> keys = GameOptionsIni.GetSectionKeys("RandomSelectors");

            if (keys == null)
                return;

            foreach (string randomSelector in keys)
            {
                List<int> randomSides = new List<int>();
                try
                {
                    string[] tmp = GameOptionsIni.GetStringValue("RandomSelectors", randomSelector, string.Empty).Split(',');
                    randomSides = Array.ConvertAll(tmp, int.Parse).Distinct().ToList();
                    randomSides.RemoveAll(x => (x >= SideCount || x < 0));
                }
                catch (FormatException) { }

                if (randomSides.Count > 1)
                {
                    selectorNames.Add(randomSelector);
                    selectorSides.Add(randomSides.ToArray());
                }
            }
        }

        protected abstract void BtnLaunchGame_LeftClick(object sender, EventArgs e);

        protected abstract void BtnLeaveGame_LeftClick(object sender, EventArgs e);

        /// <summary>
        /// Updates Discord Rich Presence with actual information.
        /// </summary>
        /// <param name="resetTimer">Whether to restart the "Elapsed" timer or not</param>
        protected abstract void UpdateDiscordPresence(bool resetTimer = false);

        /// <summary>
        /// Resets Discord Rich Presence to default state.
        /// </summary>
        protected void ResetDiscordPresence() => discordHandler?.UpdatePresence();

        protected void LoadDefaultMap()
        {
            if (ddGameMode.Items.Count > 0)
            {
                ddGameMode.SelectedIndex = 0;

                lbMapList.SelectedIndex = 0;
            }
        }

        private int GetSpectatorSideIndex() => SideCount + RandomSelectorCount;

        /// <summary>
        /// Applies disallowed side indexes to the side option drop-downs
        /// and player options.
        /// </summary>
        protected void CheckDisallowedSides()
        {
            var disallowedSideArray = GetDisallowedSides();
            int defaultSide = 0;
            int allowedSideCount = disallowedSideArray.Count(b => b == false);

            if (allowedSideCount == 1)
            {
                // Disallow Random

                for (int i = 0; i < disallowedSideArray.Length; i++)
                {
                    if (!disallowedSideArray[i])
                        defaultSide = i + RandomSelectorCount;
                }

                foreach (XNADropDown dd in ddPlayerSides)
                {
                    //dd.Items[0].Selectable = false;
                    for (int i = 0; i < RandomSelectorCount; i++)
                        dd.Items[i].Selectable = false;
                }
            }
            else
            {
                foreach (XNADropDown dd in ddPlayerSides)
                {
                    //dd.Items[0].Selectable = true;
                    for (int i = 0; i < RandomSelectorCount; i++)
                        dd.Items[i].Selectable = true;
                }
            }

            var concatPlayerList = Players.Concat(AIPlayers);

            // Disable custom random groups if all or all except one of included sides are unavailable.
            int c = 0;
            foreach (int[] randomSides in RandomSelectors)
            {
                int disableCount = 0;

                foreach (int side in randomSides)
                {
                    if (disallowedSideArray[side])
                        disableCount++;
                }

                bool disabled = false;
                if (disableCount >= randomSides.Length - 1) disabled = true;

                foreach (XNADropDown dd in ddPlayerSides)
                    dd.Items[1 + c].Selectable = !disabled;

                foreach (PlayerInfo pInfo in concatPlayerList)
                {
                    if (pInfo.SideId == 1 + c && disabled)
                        pInfo.SideId = defaultSide;
                }

                c++;
            }

            // Go over the side array and either disable or enable the side
            // dropdown options depending on whether the side is available
            for (int i = 0; i < disallowedSideArray.Length; i++)
            {
                bool disabled = disallowedSideArray[i];

                if (disabled)
                {
                    foreach (XNADropDown dd in ddPlayerSides)
                        dd.Items[i + RandomSelectorCount].Selectable = false;

                    // Change the sides of players that use the disabled 
                    // side to the default side
                    foreach (PlayerInfo pInfo in concatPlayerList)
                    {
                        if (pInfo.SideId == i + RandomSelectorCount)
                            pInfo.SideId = defaultSide;
                    }
                }
                else
                {
                    foreach (XNADropDown dd in ddPlayerSides)
                        dd.Items[i + RandomSelectorCount].Selectable = true;
                }
            }

            // If only 1 side is allowed, change all players' sides to that
            if (allowedSideCount == 1)
            {
                foreach (PlayerInfo pInfo in concatPlayerList)
                {
                    if (pInfo.SideId == 0)
                        pInfo.SideId = defaultSide;
                }
            }

            if (Map != null && Map.CoopInfo != null)
            {
                // Disallow spectator

                foreach (PlayerInfo pInfo in concatPlayerList)
                {
                    if (pInfo.SideId == GetSpectatorSideIndex())
                        pInfo.SideId = defaultSide;
                }

                foreach (XNADropDown dd in ddPlayerSides)
                {
                    if (dd.Items.Count > GetSpectatorSideIndex())
                        dd.Items[SideCount + RandomSelectorCount].Selectable = false;
                }
            }
            else
            {
                foreach (XNADropDown dd in ddPlayerSides)
                {
                    if (dd.Items.Count > SideCount + RandomSelectorCount)
                        dd.Items[SideCount + RandomSelectorCount].Selectable = true;
                }
            }
        }

        /// <summary>
        /// Gets a list of side indexes that are disallowed.
        /// </summary>
        /// <returns>A list of disallowed side indexes.</returns>
        protected bool[] GetDisallowedSides()
        {
            var returnValue = new bool[SideCount];

            if (Map != null && Map.CoopInfo != null)
            {
                // Co-Op map disallowed side logic

                foreach (int disallowedSideIndex in Map.CoopInfo.DisallowedPlayerSides)
                    returnValue[disallowedSideIndex] = true;
            }

            if (GameMode != null)
            {
                foreach (int disallowedSideIndex in GameMode.DisallowedPlayerSides)
                    returnValue[disallowedSideIndex] = true;
            }

            foreach (var checkBox in CheckBoxes)
                checkBox.ApplyDisallowedSideIndex(returnValue);

            return returnValue;
        }

        /// <summary>
        /// Randomizes options of both human and AI players
        /// and returns the options as an array of PlayerHouseInfos.
        /// </summary>
        /// <returns>An array of PlayerHouseInfos.</returns>
        protected virtual PlayerHouseInfo[] Randomize()
        {
            int totalPlayerCount = Players.Count + AIPlayers.Count;
            PlayerHouseInfo[] houseInfos = new PlayerHouseInfo[totalPlayerCount];

            for (int i = 0; i < totalPlayerCount; i++)
                houseInfos[i] = new PlayerHouseInfo();

            // Gather list of spectators
            for (int i = 0; i < Players.Count; i++)
                houseInfos[i].IsSpectator = Players[i].SideId == GetSpectatorSideIndex();

            // Gather list of available colors

            List<int> freeColors = new List<int>();

            for (int cId = 0; cId < MPColors.Count; cId++)
                freeColors.Add(cId);

            if (Map.CoopInfo != null)
            {
                foreach (int colorIndex in Map.CoopInfo.DisallowedPlayerColors)
                    freeColors.Remove(colorIndex);
            }

            foreach (PlayerInfo player in Players)
                freeColors.Remove(player.ColorId - 1); // The first color is Random

            foreach (PlayerInfo aiPlayer in AIPlayers)
                freeColors.Remove(aiPlayer.ColorId - 1);

            // Gather list of available starting locations

            List<int> freeStartingLocations = new List<int>();
            List<int> takenStartingLocations = new List<int>();

            for (int i = 0; i < Map.MaxPlayers; i++)
                freeStartingLocations.Add(i);

            for (int i = 0; i < Players.Count; i++)
            {
                if (!houseInfos[i].IsSpectator)
                {
                    freeStartingLocations.Remove(Players[i].StartingLocation - 1);
                    //takenStartingLocations.Add(Players[i].StartingLocation - 1);
                    // ^ Gives everyone with a selected location a completely random
                    // location in-game, because PlayerHouseInfo.RandomizeStart already
                    // fills the list itself
                }
            }

            for (int i = 0; i < AIPlayers.Count; i++)
                freeStartingLocations.Remove(AIPlayers[i].StartingLocation - 1);

            // Randomize options

            Random random = new Random(RandomSeed);

            for (int i = 0; i < totalPlayerCount; i++)
            {
                PlayerInfo pInfo;
                PlayerHouseInfo pHouseInfo = houseInfos[i];

                if (i < Players.Count)
                    pInfo = Players[i];
                else
                    pInfo = AIPlayers[i - Players.Count];

                pHouseInfo.RandomizeSide(pInfo, SideCount, random, GetDisallowedSides(), RandomSelectors, RandomSelectorCount);

                pHouseInfo.RandomizeColor(pInfo, freeColors, MPColors, random);
                pHouseInfo.RandomizeStart(pInfo, Map, freeStartingLocations, random, takenStartingLocations);
            }

            return houseInfos;
        }

        /// <summary>
        /// Writes spawn.ini. Returns the player house info returned from the randomizer.
        /// </summary>
        private PlayerHouseInfo[] WriteSpawnIni()
        {
            Logger.Log("Writing spawn.ini");

            File.Delete(ProgramConstants.GamePath + ProgramConstants.SPAWNER_SETTINGS);

            if (Map.IsCoop)
            {
                foreach (PlayerInfo pInfo in Players)
                    pInfo.TeamId = 1;

                foreach (PlayerInfo pInfo in AIPlayers)
                    pInfo.TeamId = 1;
            }

            PlayerHouseInfo[] houseInfos = Randomize();

            IniFile spawnIni = new IniFile(ProgramConstants.GamePath + ProgramConstants.SPAWNER_SETTINGS);

            IniSection settings = new IniSection("Settings");

            settings.SetStringValue("Name", ProgramConstants.PLAYERNAME);
            settings.SetStringValue("Scenario", ProgramConstants.SPAWNMAP_INI);
            settings.SetStringValue("UIGameMode", GameMode.UIName);
            settings.SetStringValue("UIMapName", Map.Name);
            settings.SetIntValue("PlayerCount", Players.Count);
            int myIndex = Players.FindIndex(c => c.Name == ProgramConstants.PLAYERNAME);
            settings.SetIntValue("Side", houseInfos[myIndex].SideIndex);
            settings.SetBooleanValue("IsSpectator", houseInfos[myIndex].IsSpectator);
            settings.SetIntValue("Color", houseInfos[myIndex].ColorIndex);
            settings.SetStringValue("CustomLoadScreen", LoadingScreenController.GetLoadScreenName(houseInfos[myIndex].SideIndex));
            settings.SetIntValue("AIPlayers", AIPlayers.Count);
            settings.SetIntValue("Seed", RandomSeed);
            if (GetPvPTeamCount() > 1)
                settings.SetBooleanValue("CoachMode", true);
            if (GetGameType() == GameType.Coop)
                settings.SetBooleanValue("AutoSurrender", false);
            if (IsGameOptionFlagEnabled(GameOptionFlags.NoRNG, GameOptionFlags))
                settings.SetBooleanValue("NoRNG", true);
            spawnIni.AddSection(settings);
            WriteSpawnIniAdditions(spawnIni);

            foreach (GameLobbyCheckBox chkBox in CheckBoxes)
                chkBox.ApplySpawnINICode(spawnIni);

            foreach (GameLobbyDropDown dd in DropDowns)
                dd.ApplySpawnIniCode(spawnIni);

            // Apply forced options from GameOptions.ini

            List<string> forcedKeys = GameOptionsIni.GetSectionKeys("ForcedSpawnIniOptions");

            if (forcedKeys != null)
            {
                foreach (string key in forcedKeys)
                {
                    spawnIni.SetStringValue("Settings", key,
                        GameOptionsIni.GetStringValue("ForcedSpawnIniOptions", key, String.Empty));
                }
            }

            GameMode.ApplySpawnIniCode(spawnIni); // Forced options from the game mode
            Map.ApplySpawnIniCode(spawnIni, Players.Count + AIPlayers.Count,
                AIPlayers.Count, GameMode.CoopDifficultyLevel); // Forced options from the map

            // Player options

            int otherId = 1;

            for (int pId = 0; pId < Players.Count; pId++)
            {
                PlayerInfo pInfo = Players[pId];
                PlayerHouseInfo pHouseInfo = houseInfos[pId];

                if (pInfo.Name == ProgramConstants.PLAYERNAME)
                    continue;

                string sectionName = "Other" + otherId;

                spawnIni.SetStringValue(sectionName, "Name", pInfo.Name);
                spawnIni.SetIntValue(sectionName, "Side", pHouseInfo.SideIndex);
                spawnIni.SetBooleanValue(sectionName, "IsSpectator", pHouseInfo.IsSpectator);
                spawnIni.SetIntValue(sectionName, "Color", pHouseInfo.ColorIndex);
                spawnIni.SetStringValue(sectionName, "Ip", GetIPAddressForPlayer(pInfo));
                spawnIni.SetIntValue(sectionName, "Port", pInfo.Port);

                otherId++;
            }

            List<int> multiCmbIndexes = new List<int>();

            for (int cId = 0; cId < MPColors.Count; cId++)
            {
                for (int pId = 0; pId < Players.Count; pId++)
                {
                    if (houseInfos[pId].ColorIndex == MPColors[cId].GameColorIndex)
                        multiCmbIndexes.Add(pId);
                }
            }

            if (AIPlayers.Count > 0)
            {
                for (int aiId = 0; aiId < AIPlayers.Count; aiId++)
                {
                    int multiId = multiCmbIndexes.Count + aiId + 1;

                    string keyName = "Multi" + multiId;

                    spawnIni.SetIntValue("HouseHandicaps", keyName, AIPlayers[aiId].GetHouseHandicapAILevel());
                    spawnIni.SetIntValue("HouseCountries", keyName, houseInfos[Players.Count + aiId].SideIndex);
                    spawnIni.SetIntValue("HouseColors", keyName, houseInfos[Players.Count + aiId].ColorIndex);
                }
            }

            for (int multiId = 0; multiId < multiCmbIndexes.Count; multiId++)
            {
                int pIndex = multiCmbIndexes[multiId];
                if (houseInfos[pIndex].IsSpectator)
                    spawnIni.SetBooleanValue("IsSpectator", "Multi" + (multiId + 1), true);
            }

            // Write alliances, the code is pretty big so let's take it to another class
            AllianceHolder.WriteInfoToSpawnIni(Players, AIPlayers, multiCmbIndexes, spawnIni);

            for (int pId = 0; pId < Players.Count; pId++)
            {
                int startingWaypoint = houseInfos[multiCmbIndexes[pId]].StartingWaypoint;

                // -1 means no starting location at all - let the game itself pick the starting location
                // using its own logic
                if (startingWaypoint > -1)
                {
                    int multiIndex = pId + 1;
                    spawnIni.SetIntValue("SpawnLocations", "Multi" + multiIndex,
                        startingWaypoint);
                }
            }

            for (int aiId = 0; aiId < AIPlayers.Count; aiId++)
            {
                int startingWaypoint = houseInfos[Players.Count + aiId].StartingWaypoint;

                if (startingWaypoint > -1)
                {
                    int multiIndex = Players.Count + aiId + 1;
                    spawnIni.SetIntValue("SpawnLocations", "Multi" + multiIndex,
                        startingWaypoint);
                }
            }

            spawnIni.WriteIniFile();

            return houseInfos;
        }

        /// <summary>
        /// Returns the number of teams with human players in them.
        /// Does not count spectators and human players that don't have a team set.
        /// </summary>
        /// <returns>The number of human player teams in the game.</returns>
        private int GetPvPTeamCount()
        {
            int[] teamPlayerCounts = new int[4];
            int playerTeamCount = 0;

            foreach (PlayerInfo pInfo in Players)
            {
                if (pInfo.IsAI || IsPlayerSpectator(pInfo))
                    continue;

                if (pInfo.TeamId > 0)
                {
                    teamPlayerCounts[pInfo.TeamId - 1]++;
                    if (teamPlayerCounts[pInfo.TeamId - 1] == 2)
                        playerTeamCount++;
                }
            }

            return playerTeamCount;
        }

        /// <summary>
        /// Checks whether the specified player has selected Spectator as their side.
        /// </summary>
        /// <param name="pInfo">The player.</param>
        /// <returns>True if the player is a spectator, otherwise false.</returns>
        private bool IsPlayerSpectator(PlayerInfo pInfo)
        {
            if (pInfo.SideId == GetSpectatorSideIndex())
                return true;

            return false;
        }

        protected virtual string GetIPAddressForPlayer(PlayerInfo player) => "0.0.0.0";

        /// <summary>
        /// Override this in a derived class to write game lobby specific code to
        /// spawn.ini. For example, CnCNet game lobbies should write tunnel info
        /// in this method.
        /// </summary>
        /// <param name="iniFile">The spawn INI file.</param>
        protected virtual void WriteSpawnIniAdditions(IniFile iniFile)
        {
            // Do nothing by default
        }

        private void InitializeMatchStatistics(PlayerHouseInfo[] houseInfos)
        {
            matchStatistics = new MatchStatistics(ProgramConstants.GAME_VERSION, UniqueGameID,
                Map.Name, GameMode.UIName, Players.Count, Map.IsCoop);

            bool isValidForStar = true;
            foreach (GameLobbyCheckBox checkBox in CheckBoxes)
            {
                if ((checkBox.MapScoringMode == CheckBoxMapScoringMode.DenyWhenChecked && checkBox.Checked) ||
                    (checkBox.MapScoringMode == CheckBoxMapScoringMode.DenyWhenUnchecked && !checkBox.Checked))
                {
                    // Don't prevent scoring due to forced options
                    if (!Map.ForcedCheckBoxValues.Exists(f => f.Key == checkBox.Name))
                    {
                        isValidForStar = false;
                        break;
                    }
                }
            }

            matchStatistics.IsValidForStar = isValidForStar;

            for (int pId = 0; pId < Players.Count; pId++)
            {
                PlayerInfo pInfo = Players[pId];
                matchStatistics.AddPlayer(pInfo.Name, pInfo.Name == ProgramConstants.PLAYERNAME,
                    false, pInfo.SideId == SideCount + RandomSelectorCount, houseInfos[pId].SideIndex + 1, pInfo.TeamId,
                    MPColors.FindIndex(c => c.GameColorIndex == houseInfos[pId].ColorIndex), 10);
            }

            for (int aiId = 0; aiId < AIPlayers.Count; aiId++)
            {
                var pHouseInfo = houseInfos[Players.Count + aiId];
                PlayerInfo aiInfo = AIPlayers[aiId];

                string aiName = GameMode.DifficultyBasedAINames ? aiInfo.Name : "Computer";

                matchStatistics.AddPlayer(aiName, false, true, false,
                    pHouseInfo.SideIndex + 1, aiInfo.TeamId,
                    MPColors.FindIndex(c => c.GameColorIndex == pHouseInfo.ColorIndex),
                    aiInfo.AILevel);
            }
        }

        /// <summary>
        /// Writes spawnmap.ini.
        /// </summary>
        private void WriteMap(PlayerHouseInfo[] houseInfos)
        {
            File.Delete(ProgramConstants.GamePath + ProgramConstants.SPAWNMAP_INI);

            Logger.Log("Writing map.");

            Logger.Log("Loading map INI from " + Map.CompleteFilePath);

            IniFile mapIni = Map.GetMapIni();

            IniFile globalCodeIni = new IniFile(ProgramConstants.GamePath + "INI/Map Code/GlobalCode.ini");

            MapCodeHelper.ApplyMapCode(mapIni, GameMode.GetMapRulesIniFile());
            MapCodeHelper.ApplyMapCode(mapIni, globalCodeIni);

            foreach (GameLobbyCheckBox checkBox in CheckBoxes)
                checkBox.ApplyMapCode(mapIni, GameMode);

            foreach (GameLobbyDropDown dropDown in DropDowns)
                dropDown.ApplyMapCode(mapIni, GameMode);

            mapIni.MoveSectionToFirst("MultiplayerDialogSettings"); // Required by YR

            ManipulateStartingLocations(mapIni, houseInfos);

            mapIni.WriteIniFile(ProgramConstants.GamePath + ProgramConstants.SPAWNMAP_INI);
        }

        private void ManipulateStartingLocations(IniFile mapIni, PlayerHouseInfo[] houseInfos)
        {
            if (IsGameOptionFlagEnabled(GameOptionFlags.RandomizeStartingLocations, GameOptionFlags))
            {
                if (Map.EnforceMaxPlayers)
                    return;

                // All random starting locations given by the game
                IniSection waypointSection = mapIni.GetSection("Waypoints");
                if (waypointSection == null)
                    return;

                for (int i = 0; i <= 7; i++)
                {
                    waypointSection.RemoveKey(i.ToString(CultureInfo.InvariantCulture));
                }
            }

            // Multiple players cannot properly share the same starting location
            // without breaking the SpawnX house logic that pre-placed objects depend on

            // To work around this, we add new starting locations that just point
            // to the same cell coordinates as existing stacked starting locations
            // and make additional players in the same start loc start from the new
            // starting locations instead.

            // As an additional restriction, players can only start from waypoints 0 to 7.
            // That means that if the map already has too many starting waypoints,
            // we need to move existing (but un-occupied) starting waypoints to point 
            // to the stacked locations so we can spawn the players there.


            // Check for stacked starting locations (locations with more than 1 player on it)
            bool[] startingLocationUsed = new bool[MAX_PLAYER_COUNT];
            bool stackedStartingLocations = false;
            foreach (PlayerHouseInfo houseInfo in houseInfos)
            {
                if (houseInfo.RealStartingWaypoint > -1)
                {
                    startingLocationUsed[houseInfo.RealStartingWaypoint] = true;

                    // If assigned starting waypoint is unknown while the real 
                    // starting location is known, it means that
                    // the location is shared with another player
                    if (houseInfo.StartingWaypoint == -1)
                    {
                        stackedStartingLocations = true;
                    }
                }
            }

            // If any starting location is stacked, re-arrange all starting locations
            // so that unused starting locations are removed and made to point at used
            // starting locations
            if (!stackedStartingLocations)
                return;

            // We also need to modify spawn.ini because WriteSpawnIni
            // doesn't handle stacked positions.
            // We could move this code there, but then we'd have to process
            // the stacked locations in two places (here and in WriteSpawnIni)
            // because we'd need to modify the map anyway.
            // Not sure whether having it like this or in WriteSpawnIni
            // is better, but this implementation is quicker to write for now.
            IniFile spawnIni = new IniFile(ProgramConstants.GamePath + ProgramConstants.SPAWNER_SETTINGS);

            // For each player, check if they're sharing the starting location
            // with someone else
            // If they are, find an unused waypoint and assign their 
            // starting location to match that
            for (int pId = 0; pId < houseInfos.Length; pId++)
            {
                PlayerHouseInfo houseInfo = houseInfos[pId];

                if (houseInfo.RealStartingWaypoint > -1 &&
                    houseInfo.StartingWaypoint == -1)
                {
                    // Find first unused starting location index
                    int unusedLocation = -1;
                    for (int i = 0; i < startingLocationUsed.Length; i++)
                    {
                        if (!startingLocationUsed[i])
                        {
                            unusedLocation = i;
                            startingLocationUsed[i] = true;
                            break;
                        }
                    }

                    houseInfo.StartingWaypoint = unusedLocation;
                    mapIni.SetIntValue("Waypoints", unusedLocation.ToString(),
                        mapIni.GetIntValue("Waypoints", houseInfo.RealStartingWaypoint.ToString(), 0));
                    spawnIni.SetIntValue("SpawnLocations", $"Multi{pId + 1}", unusedLocation);
                }
            }

            spawnIni.WriteIniFile();
        }

        /// <summary>
        /// Writes spawn.ini, writes the map file, initializes statistics and
        /// starts the game process.
        /// </summary>
        protected virtual void StartGame()
        {
            PlayerHouseInfo[] houseInfos = WriteSpawnIni();
            InitializeMatchStatistics(houseInfos);
            WriteMap(houseInfos);

            GameProcessLogic.GameProcessExited += GameProcessExited_Callback;

            GameSessionType sessionType = isMultiplayer && Players.Count > 1 ? GameSessionType.MULTIPLAYER : GameSessionType.SKIRMISH;
            var gameSessionInfo = new GameSessionManager(new GameSessionInfo(sessionType, DateTime.Now.Ticks), WindowManager.AddCallback);
            gameSessionInfo.StartSession(false);
            GameProcessLogic.StartGameProcess(gameSessionInfo);
            UpdateDiscordPresence(true);
        }

        private void GameProcessExited_Callback() => AddCallback(new Action(GameProcessExited), null);

        protected virtual void GameProcessExited()
        {
            GameProcessLogic.GameProcessExited -= GameProcessExited_Callback;

            Logger.Log("GameProcessExited: Parsing statistics.");

            matchStatistics.ParseStatistics(ProgramConstants.GamePath, ClientConfiguration.Instance.LocalGame, false);

            Logger.Log("GameProcessExited: Adding match to statistics.");

            StatisticsManager.Instance.AddMatchAndSaveDatabase(true, matchStatistics);

            ClearReadyStatuses();

            CopyPlayerDataToUI();

            UpdateDiscordPresence(true);
        }

        /// <summary>
        /// "Copies" player information from the UI to internal memory,
        /// applying users' player options changes.
        /// </summary>
        protected virtual void CopyPlayerDataFromUI(object sender, EventArgs e)
        {
            if (PlayerUpdatingInProgress)
                return;

            var senderDropDown = (XNADropDown)sender;
            if ((bool)senderDropDown.Tag)
                ClearReadyStatuses();

            var oldSideId = Players.Find(p => p.Name == ProgramConstants.PLAYERNAME)?.SideId;

            for (int pId = 0; pId < Players.Count; pId++)
            {
                PlayerInfo pInfo = Players[pId];

                pInfo.ColorId = ddPlayerColors[pId].SelectedIndex;
                pInfo.SideId = ddPlayerSides[pId].SelectedIndex;
                pInfo.StartingLocation = ddPlayerStarts[pId].SelectedIndex;
                pInfo.TeamId = ddPlayerTeams[pId].SelectedIndex;

                if (pInfo.SideId == SideCount + RandomSelectorCount)
                    pInfo.StartingLocation = 0;

                XNADropDown ddName = ddPlayerNames[pId];

                switch (ddName.SelectedIndex)
                {
                    case 0:
                        break;
                    case 1:
                    case 2:
                        ddName.SelectedIndex = 0;
                        break;
                    case 3:
                        KickPlayer(pId);
                        break;
                    case 4:
                        BanPlayer(pId);
                        break;
                }
            }

            AIPlayers.Clear();
            for (int cmbId = Players.Count; cmbId < 8; cmbId++)
            {
                XNADropDown dd = ddPlayerNames[cmbId];
                dd.Items[0].Text = "-";

                if (dd.SelectedIndex < 1)
                    continue;

                PlayerInfo aiPlayer = new PlayerInfo
                {
                    Name = dd.Items[dd.SelectedIndex].Text,
                    AILevel = dd.SelectedIndex - 1,
                    SideId = Math.Max(ddPlayerSides[cmbId].SelectedIndex, 0),
                    ColorId = Math.Max(ddPlayerColors[cmbId].SelectedIndex, 0),
                    StartingLocation = Math.Max(ddPlayerStarts[cmbId].SelectedIndex, 0),
                    TeamId = Map != null && Map.IsCoop ? 1 : Math.Max(ddPlayerTeams[cmbId].SelectedIndex, 0),
                    IsAI = true
                };

                AIPlayers.Add(aiPlayer);
            }

            CopyPlayerDataToUI();
            btnLaunchGame.SetRank(GetRank());

            if (oldSideId != Players.Find(p => p.Name == ProgramConstants.PLAYERNAME)?.SideId)
                UpdateDiscordPresence();
        }

        /// <summary>
        /// Sets the ready status of all non-host human players to false.
        /// </summary>
        protected void ClearReadyStatuses()
        {
            for (int i = 1; i < Players.Count; i++)
            {
                if (!Players[i].AutoReady)
                    Players[i].Ready = false;
            }
        }

        /// <summary>
        /// Applies player information changes done in memory to the UI.
        /// </summary>
        protected virtual void CopyPlayerDataToUI()
        {
            PlayerUpdatingInProgress = true;

            bool allowOptionsChange = AllowPlayerOptionsChange();
            
            // Human players
            for (int pId = 0; pId < Players.Count; pId++)
            {
                PlayerInfo pInfo = Players[pId];

                pInfo.Index = pId;

                XNADropDown ddPlayerName = ddPlayerNames[pId];
                ddPlayerName.Items[0].Text = pInfo.Name;
                ddPlayerName.Items[1].Text = string.Empty;
                ddPlayerName.Items[2].Text = string.Empty;
                ddPlayerName.Items[3].Text = "Kick";
                ddPlayerName.Items[4].Text = "Ban";
                ddPlayerName.SelectedIndex = 0;
                ddPlayerName.AllowDropDown = false;

                bool allowPlayerOptionsChange = allowOptionsChange || pInfo.Name == ProgramConstants.PLAYERNAME;

                ddPlayerSides[pId].SelectedIndex = pInfo.SideId;
                ddPlayerSides[pId].AllowDropDown = allowPlayerOptionsChange;

                ddPlayerColors[pId].SelectedIndex = pInfo.ColorId;
                ddPlayerColors[pId].AllowDropDown = allowPlayerOptionsChange;

                ddPlayerStarts[pId].SelectedIndex = pInfo.StartingLocation;
                //ddPlayerStarts[pId].AllowDropDown = allowPlayerOptionsChange;

                ddPlayerTeams[pId].SelectedIndex = pInfo.TeamId;
                if (Map != null && GameMode != null)
                {
                    ddPlayerTeams[pId].AllowDropDown = allowPlayerOptionsChange && !Map.IsCoop && !Map.ForceNoTeams && !GameMode.ForceNoTeams;
                    ddPlayerStarts[pId].AllowDropDown = allowPlayerOptionsChange && (Map.IsCoop || !Map.ForceRandomStartLocations && !GameMode.ForceRandomStartLocations);
                }
            }

            // AI players
            for (int aiId = 0; aiId < AIPlayers.Count; aiId++)
            {
                PlayerInfo aiInfo = AIPlayers[aiId];

                int index = Players.Count + aiId;

                aiInfo.Index = index;

                XNADropDown ddPlayerName = ddPlayerNames[index];
                ddPlayerName.Items[0].Text = "-";
                ddPlayerName.Items[1].Text = AILevelToName(0);
                ddPlayerName.Items[2].Text = AILevelToName(1);
                ddPlayerName.Items[3].Text = AILevelToName(2);
                ddPlayerName.Items[4].Text = AILevelToName(3);
                ddPlayerName.SelectedIndex = aiInfo.AILevel + 1;
                ddPlayerName.AllowDropDown = allowOptionsChange;

                ddPlayerSides[index].SelectedIndex = aiInfo.SideId;
                ddPlayerSides[index].AllowDropDown = allowOptionsChange;

                ddPlayerColors[index].SelectedIndex = aiInfo.ColorId;
                ddPlayerColors[index].AllowDropDown = allowOptionsChange;

                ddPlayerStarts[index].SelectedIndex = aiInfo.StartingLocation;
                //ddPlayerStarts[index].AllowDropDown = allowOptionsChange;

                ddPlayerTeams[index].SelectedIndex = aiInfo.TeamId;

                if (Map != null && GameMode != null)
                {
                    ddPlayerTeams[index].AllowDropDown = allowOptionsChange && !Map.IsCoop && !Map.ForceNoTeams && !GameMode.ForceNoTeams;
                    ddPlayerStarts[index].AllowDropDown = allowOptionsChange && (Map.IsCoop || !Map.ForceRandomStartLocations && !GameMode.ForceRandomStartLocations);
                }
            }

            // Unused player slots
            for (int ddIndex = Players.Count + AIPlayers.Count; ddIndex < MAX_PLAYER_COUNT; ddIndex++)
            {
                XNADropDown ddPlayerName = ddPlayerNames[ddIndex];
                ddPlayerName.AllowDropDown = false;
                ddPlayerName.Items[0].Text = string.Empty;
                ddPlayerName.Items[1].Text = AILevelToName(0);
                ddPlayerName.Items[2].Text = AILevelToName(1);
                ddPlayerName.Items[3].Text = AILevelToName(2);
                ddPlayerName.Items[4].Text = AILevelToName(3);
                ddPlayerName.SelectedIndex = 0;

                ddPlayerSides[ddIndex].SelectedIndex = -1;
                ddPlayerSides[ddIndex].AllowDropDown = false;

                ddPlayerColors[ddIndex].SelectedIndex = -1;
                ddPlayerColors[ddIndex].AllowDropDown = false;

                ddPlayerStarts[ddIndex].SelectedIndex = -1;
                ddPlayerStarts[ddIndex].AllowDropDown = false;

                ddPlayerTeams[ddIndex].SelectedIndex = -1;
                ddPlayerTeams[ddIndex].AllowDropDown = false;
            }

            if (allowOptionsChange && Players.Count + AIPlayers.Count < MAX_PLAYER_COUNT)
                ddPlayerNames[Players.Count + AIPlayers.Count].AllowDropDown = true;

            MapPreviewBox.UpdateStartingLocationTexts();
            UpdateMapPreviewBoxEnabledStatus();

            PlayerUpdatingInProgress = false;
        }

        /// <summary>
        /// Updates the enabled status of starting location selectors
        /// in the map preview box.
        /// </summary>
        protected abstract void UpdateMapPreviewBoxEnabledStatus();

        /// <summary>
        /// Override this in a derived class to kick players.
        /// </summary>
        /// <param name="playerIndex">The index of the player that should be kicked.</param>
        protected virtual void KickPlayer(int playerIndex)
        {
            // Do nothing by default
        }

        /// <summary>
        /// Override this in a derived class to ban players.
        /// </summary>
        /// <param name="playerIndex">The index of the player that should be banned.</param>
        protected virtual void BanPlayer(int playerIndex)
        {
            // Do nothing by default
        }

        /// <summary>
        /// Changes the current map and game mode.
        /// </summary>
        /// <param name="gameMode">The new game mode.</param>
        /// <param name="map">The new map.</param>
        protected virtual void ChangeMap(GameMode gameMode, Map map)
        {
            var oldGameMode = GameMode;
            GameMode = gameMode;

            Map = map;

            if (GameMode == null || Map == null)
            {
                lblMapName.Text = "Map: Unknown";
                lblMapAuthor.Text = "By Unknown Author";
                lblGameMode.Text = "Game mode: Unknown";
                lblMapSize.Text = "Unknown Size";

                gameModeLabelTooltip.Blocked = true;

                lblMapAuthor.X = MapPreviewBox.Right - lblMapAuthor.Width;

                MapPreviewBox.Map = null;

                return;
            }

            gameModeLabelTooltip.Blocked = false;
            gameModeLabelTooltip.Text = gameMode.Description;

            lblMapName.Text = "Map: " + Renderer.GetSafeString(map.Name, lblMapName.FontIndex);
            lblMapAuthor.Text = "By " + Renderer.GetSafeString(map.Author, lblMapAuthor.FontIndex);
            lblGameMode.Text = "Game mode: " + gameMode.UIName;
            lblMapSize.Text = map.GetSizeString();

            disableGameOptionUpdateBroadcast = true;

            // Clear forced options
            foreach (var ddGameOption in DropDowns)
                ddGameOption.AllowDropDown = true;

            foreach (var checkBox in CheckBoxes)
                checkBox.AllowChecking = true;

            // Apply default options if we should

            //if (GameMode.LoadDefaultSettingsOnMapChange ||
            //    (oldGameMode != null && oldGameMode.LoadDefaultSettingsOnMapChange))
            //{
            //    foreach (var ddGameOption in DropDowns)
            //        ddGameOption.SetDefaultValue();

            //    foreach (var checkBox in CheckBoxes)
            //        checkBox.SetDefaultValue();
            //}

            // We could either pass the CheckBoxes and DropDowns of this class
            // to the Map and GameMode instances and let them apply their forced
            // options, or we could do it in this class with helper functions.
            // The second approach is probably clearer.

            // We use these temp lists to determine which options WERE NOT forced
            // by the map. We then return these to user-defined settings.
            // This prevents forced options from one map getting carried
            // to other maps.

            var checkBoxListClone = new List<GameLobbyCheckBox>(CheckBoxes);
            var dropDownListClone = new List<GameLobbyDropDown>(DropDowns);

            ApplyForcedCheckBoxOptions(checkBoxListClone, gameMode.ForcedCheckBoxValues);
            ApplyForcedCheckBoxOptions(checkBoxListClone, map.ForcedCheckBoxValues);

            ApplyForcedDropDownOptions(dropDownListClone, gameMode.ForcedDropDownValues);
            ApplyForcedDropDownOptions(dropDownListClone, map.ForcedDropDownValues);

            foreach (var chkBox in checkBoxListClone)
                chkBox.Checked = chkBox.HostChecked;

            foreach (var dd in dropDownListClone)
                dd.SelectedIndex = dd.HostSelectedIndex;

            // Enable all sides by default
            foreach (var ddSide in ddPlayerSides)
            {
                ddSide.Items.ForEach(item => item.Selectable = true);
            }

            // Enable all colors by default
            foreach (var ddColor in ddPlayerColors)
            {
                ddColor.Items.ForEach(item => item.Selectable = true);
            }

            // Apply starting locations
            foreach (var ddStart in ddPlayerStarts)
            {
                ddStart.Items.Clear();

                ddStart.AddItem("???");

                for (int i = 1; i <= Map.MaxPlayers; i++)
                    ddStart.AddItem(i.ToString());
            }


            // Check if AI players allowed
            bool AIAllowed = !(Map.MultiplayerOnly || GameMode.MultiplayerOnly) || !(Map.HumanPlayersOnly || GameMode.HumanPlayersOnly);
            foreach (var ddName in ddPlayerNames)
            {
                if (ddName.Items.Count > 3)
                {
                    ddName.Items[1].Selectable = AIAllowed;
                    ddName.Items[2].Selectable = AIAllowed;
                    ddName.Items[3].Selectable = AIAllowed;
                }
            }

            if (!AIAllowed) AIPlayers.Clear();
            IEnumerable<PlayerInfo> concatPlayerList = Players.Concat(AIPlayers);

            foreach (PlayerInfo pInfo in concatPlayerList)
            {
                if (pInfo.StartingLocation > Map.StartingLocationCount || (!Map.IsCoop && (Map.ForceRandomStartLocations || GameMode.ForceRandomStartLocations)))
                    pInfo.StartingLocation = 0;
                if (!Map.IsCoop && (Map.ForceNoTeams || GameMode.ForceNoTeams))
                    pInfo.TeamId = 0;
            }

            CheckDisallowedSides();


            if (map.CoopInfo != null)
            {
                // Co-Op map disallowed color logic
                foreach (int disallowedColorIndex in map.CoopInfo.DisallowedPlayerColors)
                {
                    if (disallowedColorIndex >= MPColors.Count)
                        continue;

                    foreach (XNADropDown ddColor in ddPlayerColors)
                        ddColor.Items[disallowedColorIndex + 1].Selectable = false;

                    foreach (PlayerInfo pInfo in concatPlayerList)
                    {
                        if (pInfo.ColorId == disallowedColorIndex + 1)
                            pInfo.ColorId = 0;
                    }
                }

                // Force teams
                foreach (PlayerInfo pInfo in concatPlayerList)
                    pInfo.TeamId = 1;
            }

            OnGameOptionChanged();

            MapPreviewBox.Map = map;
            CopyPlayerDataToUI();

            disableGameOptionUpdateBroadcast = false;
        }

        private void ApplyForcedCheckBoxOptions(List<GameLobbyCheckBox> optionList,
            List<KeyValuePair<string, bool>> forcedOptions)
        {
            foreach (KeyValuePair<string, bool> option in forcedOptions)
            {
                GameLobbyCheckBox checkBox = CheckBoxes.Find(chk => chk.Name == option.Key);
                if (checkBox != null)
                {
                    checkBox.Checked = option.Value;
                    checkBox.AllowChecking = false;
                    optionList.Remove(checkBox);
                }
            }
        }

        private void ApplyForcedDropDownOptions(List<GameLobbyDropDown> optionList,
            List<KeyValuePair<string, int>> forcedOptions)
        {
            foreach (KeyValuePair<string, int> option in forcedOptions)
            {
                GameLobbyDropDown dropDown = DropDowns.Find(dd => dd.Name == option.Key);
                if (dropDown != null)
                {
                    dropDown.SelectedIndex = option.Value;
                    dropDown.AllowDropDown = false;
                    optionList.Remove(dropDown);
                }
            }
        }

        protected string AILevelToName(int aiLevel)
        {
            return ProgramConstants.GetAILevelName(aiLevel);
        }

        protected GameType GetGameType()
        {
            int teamCount = GetPvPTeamCount();

            if (teamCount == 0)
                return GameType.FFA;

            if (teamCount == 1)
                return GameType.Coop;

            return GameType.TeamGame;
        }

        protected int GetRank()
        {
            if (GameMode == null || Map == null)
                return RANK_NONE;

            foreach (GameLobbyCheckBox checkBox in CheckBoxes)
            {
                if ((checkBox.MapScoringMode == CheckBoxMapScoringMode.DenyWhenChecked && checkBox.Checked) ||
                    (checkBox.MapScoringMode == CheckBoxMapScoringMode.DenyWhenUnchecked && !checkBox.Checked))
                {
                    // Don't prevent scoring due to forced options
                    if (!Map.ForcedCheckBoxValues.Exists(f => f.Key == checkBox.Name))
                        return RANK_NONE;
                }
            }

            PlayerInfo localPlayer = Players.Find(p => p.Name == ProgramConstants.PLAYERNAME);

            if (localPlayer == null)
                return RANK_NONE;

            if (IsPlayerSpectator(localPlayer))
                return RANK_NONE;

            if (Map.IsCoop)
                return GetRankIndexForCoopDifficultyLevel(GameMode.CoopDifficultyLevel);

            // These variables are used by both the skirmish and multiplayer code paths
            int[] teamMemberCounts = new int[5];
            int lowestEnemyAILevel = 3;
            int highestAllyAILevel = 0;

            foreach (PlayerInfo aiPlayer in AIPlayers)
            {
                teamMemberCounts[aiPlayer.TeamId]++;

                if (aiPlayer.TeamId > 0 && aiPlayer.TeamId == localPlayer.TeamId)
                {
                    if (aiPlayer.AILevel > highestAllyAILevel)
                        highestAllyAILevel = aiPlayer.AILevel;
                }
                else
                {
                    if (aiPlayer.AILevel < lowestEnemyAILevel)
                        lowestEnemyAILevel = aiPlayer.AILevel;
                }
            }

            if (isMultiplayer)
            {
                if (Players.Count == 1)
                    return RANK_NONE;

                // PvP stars for 2-player and 3-player maps
                if (Map.MaxPlayers <= 3)
                {
                    List<PlayerInfo> filteredPlayers = Players.Where(p => !IsPlayerSpectator(p)).ToList();

                    if (AIPlayers.Count > 0)
                        return RANK_NONE;

                    if (filteredPlayers.Count != Map.MaxPlayers)
                        return RANK_NONE;

                    int localTeamIndex = localPlayer.TeamId;
                    if (localTeamIndex > 0 && filteredPlayers.Count(p => p.TeamId == localTeamIndex) > 1)
                        return RANK_NONE;

                    return RANK_HARD;
                }

                // Coop stars for maps with 4 or more players
                // See the code in StatisticsManager.GetRankForCoopMatch for the conditions

                if (Players.Find(p => IsPlayerSpectator(p)) != null)
                    return RANK_NONE;

                if (AIPlayers.Count == 0)
                    return RANK_NONE;

                if (Players.Find(p => p.TeamId != localPlayer.TeamId) != null)
                    return RANK_NONE;

                if (Players.Find(p => p.TeamId == 0) != null)
                    return RANK_NONE;

                if (AIPlayers.Find(p => p.TeamId == 0) != null)
                    return RANK_NONE;

                teamMemberCounts[localPlayer.TeamId] += Players.Count;

                if (lowestEnemyAILevel < highestAllyAILevel)
                {
                    // Check that the player's AI allies aren't stronger 
                    return RANK_NONE;
                }

                // Check that all teams have at least as many players
                // as the human players' team
                int allyCount = teamMemberCounts[localPlayer.TeamId];

                for (int i = 1; i < 5; i++)
                {
                    if (i == localPlayer.TeamId)
                        continue;

                    if (teamMemberCounts[i] > 0)
                    {
                        if (teamMemberCounts[i] < allyCount)
                            return RANK_NONE;
                    }
                }

                return lowestEnemyAILevel + 1;
            }

            // *********
            // Skirmish!
            // *********

            if (AIPlayers.Count != Map.MaxPlayers - 1)
                return RANK_NONE;

            teamMemberCounts[localPlayer.TeamId]++;

            if (lowestEnemyAILevel < highestAllyAILevel)
            {
                // Check that the player's AI allies aren't stronger 
                return RANK_NONE;
            }

            if (localPlayer.TeamId > 0)
            {
                // Check that all teams have at least as many players
                // as the local player's team
                int allyCount = teamMemberCounts[localPlayer.TeamId];

                for (int i = 1; i < 5; i++)
                {
                    if (i == localPlayer.TeamId)
                        continue;

                    if (teamMemberCounts[i] > 0)
                    {
                        if (teamMemberCounts[i] < allyCount)
                            return RANK_NONE;
                    }
                }

                // Check that there is a team other than the players' team that is at least as large
                bool pass = false;
                for (int i = 1; i < 5; i++)
                {
                    if (i == localPlayer.TeamId)
                        continue;

                    if (teamMemberCounts[i] >= allyCount)
                    {
                        pass = true;
                        break;
                    }
                }

                if (!pass)
                    return RANK_NONE;
            }

            return lowestEnemyAILevel + 1;
        }

        protected string AddGameOptionPreset(string name)
        {
            string error = GameOptionPreset.IsNameValid(name);
            if (!string.IsNullOrEmpty(error))
                return error;

            GameOptionPreset preset = new GameOptionPreset(name);
            foreach (GameLobbyCheckBox checkBox in CheckBoxes)
            {
                preset.AddCheckBoxValue(checkBox.Name, checkBox.Checked);
            }

            foreach (GameLobbyDropDown dropDown in DropDowns)
            {
                preset.AddDropDownValue(dropDown.Name, dropDown.SelectedIndex);
            }

            GameOptionPresets.Instance.AddPreset(preset);

            return null;
        }

        public bool LoadGameOptionPreset(string name)
        {
            GameOptionPreset preset = GameOptionPresets.Instance.GetPreset(name);
            if (preset == null)
                return false;

            disableGameOptionUpdateBroadcast = true;

            var checkBoxValues = preset.GetCheckBoxValues();
            foreach (var kvp in checkBoxValues)
            {
                GameLobbyCheckBox checkBox = CheckBoxes.Find(c => c.Name == kvp.Key);
                if (checkBox != null && checkBox.AllowChanges && checkBox.AllowChecking)
                    checkBox.Checked = kvp.Value;
            }

            var dropDownValues = preset.GetDropDownValues();
            foreach (var kvp in dropDownValues)
            {
                GameLobbyDropDown dropDown = DropDowns.Find(d => d.Name == kvp.Key);
                if (dropDown != null && dropDown.AllowDropDown)
                    dropDown.SelectedIndex = kvp.Value;
            }

            disableGameOptionUpdateBroadcast = false;
            OnGameOptionChanged();
            return true;
        }

        protected string GetFMVsHash()
        {
            if (ClientConfiguration.Instance.ModMode)
                return string.Empty;

            var fmvsCustomComponent = CUpdater.CustomComponents.ToList().Find(cc => cc.ININame == "FMVs");
            if (fmvsCustomComponent == null)
                return string.Empty;

            if (!File.Exists(ProgramConstants.GamePath + fmvsCustomComponent.LocalPath))
                return string.Empty;

            const int MaxLength = 8;

            string value = fmvsCustomComponent.LocalIdentifier;
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            if (value.Length < MaxLength)
                return value;

            return value.Substring(0, MaxLength);
        }

        /// <summary>
        /// Checks whether there is an in-game videos game option in the game lobby.
        /// If there is, checks that it is allowed to be enabled (all players
        /// have the same FMV custom component hash).
        /// </summary>
        protected bool IsFMVGameOptionStateOK()
        {
            var checkBox = CheckBoxes.Find(chk => chk.DependsOnCustomComponent == "FMVs");
            if (checkBox == null || !checkBox.Checked)
                return true;

            // The check box is checked, make sure that all players have the same FMV hash
            string hostHash = Players[0].FMVHash;
            if (string.IsNullOrWhiteSpace(hostHash))
                return false;

            if (Players.Exists(p => p.FMVHash != hostHash))
                return false;

            return true;
        }

        private void UpdateGameModeDescriptionPanel()
        {
            if (ddGameMode.IsActive && ddGameMode.HoveredIndex > -1 && ddGameMode.DropDownState != DropDownState.CLOSED)
            {
                GameMode hoveredGameMode = GameModes[ddGameMode.HoveredIndex];
                gameModeDescriptionPanel.GameMode = hoveredGameMode;

                if (!string.IsNullOrEmpty(hoveredGameMode.Description))
                {
                    gameModeDescriptionPanel.Enable();
                }
                else
                {
                    gameModeDescriptionPanel.Disable();
                }
            }
            else
            {
                if (gameModeDescriptionPanel.Enabled)
                    gameModeDescriptionPanel.Disable();
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            UpdateGameModeDescriptionPanel();
        }

        protected abstract bool AllowPlayerOptionsChange();
    }
}

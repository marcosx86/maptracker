using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Tibia.Objects;
using Tibia.Packets;
using Tibia.Util;
using Tibia.Packets.Incoming;

namespace MapTracker.NET
{
    // Todo:
    // Many invalid items
    // Show map boundaries under statistics
    // Splashes are the wrong color
    public partial class MainForm : Form
    {
        #region Variables
        Client client;
        List<Client> clientList;
        Dictionary<ushort, ushort> clientToServer;
        Dictionary<Location, OtMapTile> mapTiles;
        Location mapBoundsNW;
        Location mapBoundsSE;
        Location currentLocation;
        bool processing;
        bool tracking;
        int trackedTileCount;
        int trackedItemCount;
        short staticSkipTiles;
        #endregion

        #region SplitPacket
        struct SplitPacket
        {
            public IncomingPacketType Type;
            public byte[] Packet;

            public SplitPacket(IncomingPacketType type, byte[] packet)
            {
                this.Type = type;
                this.Packet = packet;
            }
        }
        Queue<SplitPacket> packetQueue;
        #endregion

        #region Form Controls
        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            clientToServer = new OtbReader().GetClientToServerDictionary();
            mapTiles = new Dictionary<Location, OtMapTile>();
            packetQueue = new Queue<SplitPacket>();

            Reset();

            ReloadClients();

            if (clientList.Count > 0)
            {
                client = clientList[uxClients.SelectedIndex];
            }
            else
            {
                MessageBox.Show("MapTracker requires at least one running client.");
                Application.Exit();
            }
        }

        private void uxStart_Click(object sender, EventArgs e)
        {
            if (tracking)
            {
                Stop();
            }
            else
            {
                Start();
            }
        }

        private void uxWrite_Click(object sender, EventArgs e)
        {
            if (mapTiles.Count > 0)
            {
                OtbmMapWriter.WriteMapTilesToFile(mapTiles.Values);
            }
        }

        private void uxClients_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (uxStart.Text == "Stop Map Tracking")
            {
                Stop();
                client = clientList[uxClients.SelectedIndex];
            }
        }

        private void uxReset_Click(object sender, EventArgs e)
        {
            Reset();
        }
        #endregion

        #region Control
        private void ReloadClients()
        {
            uxClients.Items.Clear();
            uxClients.Text = "";
            clientList = Client.GetClients();
            if (clientList.Count > 0)
            {
                foreach (Client c in clientList)
                {
                    uxClients.Items.Add(c.ToString());
                }
                uxClients.SelectedIndex = 0;
            }
        }

        private void Start()
        {
            uxLog.Clear();
            uxStart.Text = "Stop Map Tracking";

            if (client.LoggedIn)
            {
                currentLocation = GetPlayerLocation();
            }

            client.StopRawSocket();
            client.StartRawSocket();
            //client.RawSocket.IncomingSplitPacket -= IncomingSplitPacket;
            //client.RawSocket.IncomingSplitPacket += IncomingSplitPacket;

            client.RawSocket.ReceivedMapDescriptionIncomingPacket -= ReceivedMapPacket;
            client.RawSocket.ReceivedMoveNorthIncomingPacket -= ReceivedMapPacket;
            client.RawSocket.ReceivedMoveEastIncomingPacket -= ReceivedMapPacket;
            client.RawSocket.ReceivedMoveSouthIncomingPacket -= ReceivedMapPacket;
            client.RawSocket.ReceivedMoveWestIncomingPacket -= ReceivedMapPacket;
            client.RawSocket.ReceivedFloorChangeDownIncomingPacket -= ReceivedMapPacket;
            client.RawSocket.ReceivedFloorChangeUpIncomingPacket -= ReceivedMapPacket;

            client.RawSocket.ReceivedMapDescriptionIncomingPacket += ReceivedMapPacket;
            client.RawSocket.ReceivedMoveNorthIncomingPacket += ReceivedMapPacket;
            client.RawSocket.ReceivedMoveEastIncomingPacket += ReceivedMapPacket;
            client.RawSocket.ReceivedMoveSouthIncomingPacket += ReceivedMapPacket;
            client.RawSocket.ReceivedMoveWestIncomingPacket += ReceivedMapPacket;
            client.RawSocket.ReceivedFloorChangeDownIncomingPacket += ReceivedMapPacket;
            client.RawSocket.ReceivedFloorChangeUpIncomingPacket += ReceivedMapPacket;

            tracking = true;
            processing = false;
            uxReset.Enabled = false;
        }

        private void Stop()
        {
            uxStart.Text = "Start Map Tracking";
            client.StopRawSocket();
            tracking = false;
            uxReset.Enabled = true;
        }

        private void Reset()
        {
            mapTiles.Clear();
            mapBoundsNW = Tibia.Objects.Location.Invalid;
            mapBoundsSE = Tibia.Objects.Location.Invalid;
            trackedTileCount = 0;
            trackedItemCount = 0;
            UpdateStats();
        }

        private void UpdateStats()
        {
            Invoke(new EventHandler(delegate
            {
                uxTrackedTiles.Text = trackedTileCount.ToString("0,0");
                uxTrackedItems.Text = trackedItemCount.ToString("0,0");
            }));
        }

        private Location GetPlayerLocation()
        {
            return client.GetPlayer().Location;
        }
        #endregion

        #region Process Packets
        private bool ReceivedMapPacket(IncomingPacket packet)
        {
            lock (this)
            {
                MapPacket p = (MapPacket)packet;
                foreach (Tile tile in p.Tiles)
                {
                    if (!mapTiles.ContainsKey(tile.Location))
                    {
                        OtMapTile mapTile = new OtMapTile();
                        mapTile.Location = tile.Location;
                        mapTile.TileId = (ushort)tile.Id;
                        foreach (Item item in tile.Items)
                        {
                            OtMapItem mapItem = new OtMapItem();
                            mapItem.AttrType = AttrType.None;

                            if (clientToServer.ContainsKey((ushort)item.Id))
                            {
                                mapItem.ItemId = clientToServer[(ushort)item.Id];
                            }
                            else
                            {
                                Log("ClientId not in items.otb: " + item.Id.ToString());
                                break;
                            }

                            if (item.HasExtraByte)
                            {
                                byte extra = item.Count;
                                if (item.GetFlag(Tibia.Addresses.DatItem.Flag.IsRune))
                                {
                                    mapItem.AttrType = AttrType.Charges;
                                    mapItem.Extra = extra;
                                }
                                else if (item.GetFlag(Tibia.Addresses.DatItem.Flag.IsStackable) ||
                                    item.GetFlag(Tibia.Addresses.DatItem.Flag.IsSplash))
                                {
                                    mapItem.AttrType = AttrType.Count;
                                    mapItem.Extra = extra;
                                }
                            }
                            mapTile.Items.Add(mapItem);
                        }
                        mapTiles.Add(tile.Location, mapTile);
                        trackedTileCount++;
                        trackedItemCount += mapTile.Items.Count;
                        UpdateStats();
                    }
                }
            }
            return true;
        }
        #endregion

        #region Helpers
        private void SetNewMapBounds(Location loc)
        {
            if (mapBoundsNW.Equals(Tibia.Objects.Location.Invalid))
            {
                mapBoundsNW = loc;
                mapBoundsSE = loc;
            }
            else
            {
                if (loc.X < mapBoundsNW.X)
                    mapBoundsNW.X = loc.X;
                if (loc.Y < mapBoundsNW.Y)
                    mapBoundsNW.Y = loc.Y;
                if (loc.Z < mapBoundsNW.Z)
                    mapBoundsNW.Z = loc.Z;

                if (loc.X > mapBoundsSE.X)
                    mapBoundsSE.X = loc.X;
                if (loc.Y > mapBoundsNW.Y)
                    mapBoundsSE.Y = loc.Y;
                if (loc.Z > mapBoundsNW.Z)
                    mapBoundsSE.Z = loc.Z;
            }
        }

        private Location GetMapSize()
        {
            Tibia.Objects.Location size = new Location();
            size.X = mapBoundsSE.X - mapBoundsNW.X;
            size.Y = mapBoundsSE.Y - mapBoundsNW.Y;
            size.Z = mapBoundsSE.Z - mapBoundsNW.Z;
            return size;
        }

        private void Log(string text)
        {
            Invoke(new EventHandler(delegate
            {
                uxLog.AppendText(text + Environment.NewLine);
            }));
        }
        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Tibia.Objects;
using Tibia.Packets;
using Tibia.Util;

namespace MapTracker.NET
{
    // Todo:
    // Going subterranean doesnt work
    // Parse floor change packets
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
            client.RawSocket.IncomingSplitPacket -= IncomingSplitPacket;
            client.RawSocket.IncomingSplitPacket += IncomingSplitPacket;

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
            Application.DoEvents();
        }

        private Location GetPlayerLocation()
        {
            return client.GetPlayer().Location;
        }
        #endregion

        #region Process Packets
        private void IncomingSplitPacket(byte type, byte[] packet)
        {
            packetQueue.Enqueue(new SplitPacket((IncomingPacketType)type, packet));
            ProcessPacketQueue();
        }

        private void ProcessPacketQueue()
        {
            if (!processing && packetQueue.Count > 0)
            {
                StartProcessing();
            }
        }

        private void StartProcessing()
        {
            processing = true;
            ProcessPacket(packetQueue.Dequeue());
        }

        private void DoneProcessing()
        {
            processing = false;
            ProcessPacketQueue();
        }

        private void ProcessPacket(SplitPacket splitPacket)
        {
            IncomingPacketType type = splitPacket.Type;
            byte[] packet = splitPacket.Packet;

            NetworkMessage msg = new NetworkMessage(packet);
            type = (IncomingPacketType)msg.GetByte();

            if (type == IncomingPacketType.MapDescription)
            {
                Log("MapDescription");
                currentLocation = msg.GetLocation();
                ParseMapDescription(msg, 
                    currentLocation.X - 8, 
                    currentLocation.Y - 6, 
                    currentLocation.Z, 18, 14);
                DoneProcessing();
                return;
            }
            else if (type == IncomingPacketType.MoveNorth)
            {
                Log("MoveNorth");
                currentLocation.Y--;
                ParseMapDescription(msg, 
                    currentLocation.X - 8, 
                    currentLocation.Y - 6, 
                    currentLocation.Z, 18, 1);
            }
            else if (type == IncomingPacketType.MoveEast)
            {
                Log("MoveEast");
                currentLocation.X++;
                ParseMapDescription(msg, 
                    currentLocation.X + 9, 
                    currentLocation.Y - 6, 
                    currentLocation.Z, 1, 14);
            }
            else if (type == IncomingPacketType.MoveSouth)
            {
                Log("MoveSouth");
                currentLocation.Y++;
                ParseMapDescription(msg, 
                    currentLocation.X - 8, 
                    currentLocation.Y + 7, 
                    currentLocation.Z, 18, 1);
            }
            else if (type == IncomingPacketType.MoveWest)
            {
                Log("MoveWest");
                currentLocation.X--;
                ParseMapDescription(msg, 
                    currentLocation.X - 8, 
                    currentLocation.Y - 6, 
                    currentLocation.Z, 1, 14);
            }
            else if (type == IncomingPacketType.FloorChangeDown)
            {
                Log("FloorChangeDown");
                currentLocation.X++; // East
                currentLocation.Y++; // South
                currentLocation.Z--;
            }
            else if (type == IncomingPacketType.FloorChangeUp)
            {
                Log("FloorChangeUp");
                currentLocation.X--; // West
                currentLocation.Y--; // North
                currentLocation.Z++;
            }
            DoneProcessing();
        }

        private bool ParseMapDescription(NetworkMessage msg, int x, int y, int z, int width, int height)
        {
            int startz, endz, zstep;
            //calculate map limits
            if (z > 7)
            {
                startz = z - 2;
                endz = System.Math.Min(16 - 1, z + 2);
                zstep = 1;
            }
            else
            {
                startz = 7;
                endz = 0;
                zstep = -1;
            }

            for (int nz = startz; nz != endz + zstep; nz += zstep)
            {
                //parse each floor
                if (!ParseFloorDescription(msg, x, y, nz, width, height, z - nz))
                    return false;
            }

            return true;
        }

        private bool ParseFloorDescription(NetworkMessage msg, int x, int y, int z, int width, int height, int offset)
        {
            ushort skipTiles;

            for (int nx = 0; nx < width; nx++)
            {
                for (int ny = 0; ny < height; ny++)
                {
                    if (staticSkipTiles == 0)
                    {
                        ushort tileOpt = msg.PeekUInt16();
                        if (tileOpt >= 0xFF00)
                        {
                            skipTiles = msg.GetUInt16();                            
                            staticSkipTiles = (short)(skipTiles & 0xFF);
                        }
                        else
                        {
                            Location pos = new Location(x + nx + offset, y + ny + offset, z);

                            if (!ParseTileDescription(msg, pos))
                            {
                                return false;
                            }
                            skipTiles = msg.GetUInt16();
                            staticSkipTiles = (short)(skipTiles & 0xFF);
                        }
                    }
                    else
                    {
                        staticSkipTiles--;
                    }
                }
            }
            return true;
        }

        private bool ParseTileDescription(NetworkMessage msg, Location pos)
        {
            bool ret = false;
            OtMapTile mapTile = null;

            SetNewMapBounds(pos);
            mapTile = new OtMapTile();
            mapTile.Location = pos;

            int n = 0;
            while (true)
            {
                n++;

                ushort inspectTileId = msg.PeekUInt16();

                if (inspectTileId >= 0xFF00)
                {
                    ret = true;
                    break;
                }
                else
                {
                    if (n > 10)
                    {
                        ret = false;
                        break;
                    }

                    InternalGetThing(msg, pos, n, mapTile);
                }
            }

            if (!mapTiles.ContainsKey(pos))
            {
                mapTiles.Add(pos, mapTile);
                trackedTileCount++;
                trackedItemCount += mapTile.Items.Count;
                UpdateStats();
            }

            return ret;
        }

        private bool InternalGetThing(NetworkMessage msg, Location pos, int n, OtMapTile mapTile)
        {
            ushort thingId;
            try
            {
                thingId = msg.GetUInt16();
            }
            catch (Exception e)
            {
                Log("Error: " + e.Message);
                return false;
            }

            if (thingId == 0x0061 || thingId == 0x0062)
            {

                if (thingId == 0x0062)
                {
                    msg.Position += 4;
                }
                else if (thingId == 0x0061)
                {
                    msg.Position += 8;
                    int len = msg.GetUInt16();
                    msg.Position += len;
                }

                msg.Position += 2;
                int outfit = msg.GetUInt16();
                if (outfit == 0)
                    msg.Position += 2;
                else
                    msg.Position += 5;
                msg.Position += 6;

                return true;
            }
            else if (thingId == 0x0063)
            {
                msg.Position += 5;

                return true;
            }
            else
            {
                Item item = new Item(client, thingId);
                ushort oldThingId = 0;
                if (clientToServer.ContainsKey(thingId))
                {
                    oldThingId = thingId;
                    thingId = clientToServer[thingId];
                }
                else
                {
                    Log("ClientId not in items.otb: " + thingId.ToString());
                }

                if (n == 1)
                {
                    mapTile.TileId = thingId;
                }
                else
                {
                    OtMapItem mapItem = null;
                    mapItem = new OtMapItem();
                    mapItem.ItemId = thingId;
                    mapItem.AttrType = AttrType.None;

                    if (item.HasExtraByte)
                    {
                        byte extra = msg.GetByte();
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

                return true;
            }
        }

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
            Application.DoEvents();
        }
        #endregion
    }
}

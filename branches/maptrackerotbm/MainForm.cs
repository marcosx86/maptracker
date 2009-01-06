using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Tibia.Objects;
using Tibia.Packets;
using System.IO;
using Tibia.Util;

namespace MapTracker.NET
{
    // Todo:
    // Instead of writing everything out to an otbm file right away,
    // have some sort of intermediate state, either in memory or in a file
    public partial class MainForm : Form
    {
        #region Variables
        Client client;
        HashSet<Location> trackedTiles;
        List<Client> clientList;
        Dictionary<ushort, ushort> clientToServer;
        Location mapBoundsNW;
        Location mapBoundsSE;
        MapWriter mapWriter;
        bool processing;

        struct SplitPacket
        {
            public byte Type;
            public byte[] Packet;

            public SplitPacket(byte type, byte[] packet)
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
            ItemsReader ir = new ItemsReader();
            clientToServer = ir.GetClientToServerDictionary();
            packetQueue = new Queue<SplitPacket>();

            ReloadClients();

            client = clientList[uxClients.SelectedIndex];
        }

        private void uxStart_Click(object sender, EventArgs e)
        {
            StartStop();
        }

        private void uxClients_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (uxStart.Text == "Stop Map Tracking")
            {
                Stop();
                client = clientList[uxClients.SelectedIndex];
            }
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

        private void Stop()
        {
            uxStart.Text = "Start Map Tracking";
            client.StopRawSocket();
            if (mapWriter != null)
            {
                mapWriter.WriteNodeEnd(); // Map Data node
                mapWriter.WriteNodeEnd(); // Root node
                //Tibia.Objects.Location size = GetMapSize();
                mapWriter.Close();
            }
        }

        private void Start()
        {
            textBox1.Clear();
            trackedTiles = new HashSet<Location>();
            uxStart.Text = "Stop Map Tracking";

            string fn = Directory.GetCurrentDirectory() + "\\mapdump_" + DateTime.Now.ToString("dd-MM-yyyy_HH-mm-ss.ffff") + ".otbm";
            mapWriter = new MapWriter(fn);

            if (mapWriter.CanWrite)
            {
                mapBoundsNW = Tibia.Objects.Location.Invalid;
                mapBoundsSE = Tibia.Objects.Location.Invalid;

                mapWriter.WriteHeader();
                mapWriter.WriteMapStart();

                client.StopRawSocket();
                client.StartRawSocket();
                client.RawSocket.IncomingSplitPacket += IncomingSplitPacket;

                processing = false;
            }
        }

        private void StartStop()
        {
            if (uxStart.Text == "Stop Map Tracking")
            {
                Stop();
            }
            else if (uxStart.Text == "Start Map Tracking")
            {
                Start();
            }
        }
        #endregion

        #region Process Packets
        private void IncomingSplitPacket(byte type, byte[] packet)
        {
            if (type < 0x64 || type > 0x68)
                return;
            packetQueue.Enqueue(new SplitPacket(type, packet));
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
            Invoke(new EventHandler(delegate
            {
                uxStatus.Text = "Processing...";
                uxStatus.ForeColor = Color.Red;
            }));
            ProcessPacket(packetQueue.Dequeue());
        }

        private void DoneProcessing()
        {
            processing = false;
            Invoke(new EventHandler(delegate
            {
                uxStatus.Text = "Done";
                uxStatus.ForeColor = Color.Black;
            }));
            ProcessPacketQueue();
        }

        private void ProcessPacket(SplitPacket splitPacket)
        {
            byte type = splitPacket.Type;
            byte[] packet = splitPacket.Packet;
            if (type < 0x64 || type > 0x68)
                return;

            NetworkMessage msg = new NetworkMessage(packet);
            Location pos;

            type = msg.GetByte();

            if (type == 0x64)
            {
                pos = msg.GetLocation();
                ParseMapDescription(msg, pos.X - 8, pos.Y - 6, pos.Z, 18, 14);
                DoneProcessing();
                return;
            }

            pos = client.GetPlayer().Location;

            if (type == 0x65)
            {
                pos.Y--;
                ParseMapDescription(msg, pos.X - 8, pos.Y - 6, pos.Z, 18, 1);
            }

            if (type == 0x66)
            {
                pos.X++;
                ParseMapDescription(msg, pos.X + 9, pos.Y - 6, pos.Z, 1, 14);
            }

            if (type == 0x67)
            {
                pos.Y++;
                ParseMapDescription(msg, pos.X - 8, pos.Y + 7, pos.Z, 18, 1);
            }

            if (type == 0x68)
            {
                pos.X--;
                ParseMapDescription(msg, (int)(pos.X - 8), (int)(pos.Y - 6), (int)(pos.Z), 1, 14);
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

        private short staticSkipTiles;

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
            bool doTrack = true;

            if (trackedTiles.Contains(pos))
            {
                doTrack = false;
            }
            else
            {
                trackedTiles.Add(pos);
            }

            if (doTrack)
            {
                SetNewMapBounds(pos);
                mapWriter.WriteNodeStart(NodeType.TileArea);
                mapWriter.WriteTileAreaCoords(pos);
                mapWriter.WriteNodeStart(NodeType.Tile);
                mapWriter.WriteTileCoords(pos);
            }

            int n = 0;
            while (true)
            {
                n++;

                ushort inspectTileId = msg.PeekUInt16();

                if (inspectTileId >= 0xFF00)
                {
                    // there are skip tiles
                    //WriteByte(0xFF);
                    //WriteByte(0xFF);
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

                    InternalGetThing(msg, pos, n, doTrack);
                }
            }

            if (doTrack)
            {
                mapWriter.WriteNodeEnd();
                mapWriter.WriteNodeEnd();
            }
            return ret;
        }

        private bool InternalGetThing(NetworkMessage msg, Location pos, int n, bool doTrack)
        {
            ushort thingId = msg.GetUInt16();

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
                    doTrack = false;
                    Invoke(new EventHandler(delegate
                    {
                        textBox1.AppendText("ClientId not in items.otb: " + thingId.ToString() + Environment.NewLine);
                    }));
                }

                //if (pos.Equals(new Location(32092, 32211, 7)))
                //{
                //    int i = 0;
                //}

                if (n == 1)
                {
                    // write as the ground tile
                    if (doTrack)
                    {
                        mapWriter.WriteAttrType(AttrType.Item);
                        mapWriter.WriteUInt16(thingId);
                    }
                }
                else
                {
                    if (doTrack)
                    {
                        mapWriter.WriteNodeStart(NodeType.Item);
                        mapWriter.WriteUInt16(thingId);
                    }

                    if (item.HasExtraByte)
                    {
                        byte extra = msg.GetByte();
                        if (doTrack)
                        {
                            if (item.GetFlag(Tibia.Addresses.DatItem.Flag.IsRune))
                            {
                                mapWriter.WriteAttrType(AttrType.Charges);
                                mapWriter.WriteByte(extra);
                            }
                            else if (item.GetFlag(Tibia.Addresses.DatItem.Flag.IsStackable) ||
                                item.GetFlag(Tibia.Addresses.DatItem.Flag.IsSplash))
                            {
                                mapWriter.WriteAttrType(AttrType.Count);
                                mapWriter.WriteByte(extra);
                            }
                        }
                    }
                    if (doTrack)
                    {
                        mapWriter.WriteNodeEnd();
                    }
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
        #endregion
    }
}

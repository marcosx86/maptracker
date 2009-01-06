using System;
using System.Collections.Generic;
using Tibia;
using Tibia.Objects;

namespace MapTracker.NET
{
    public class OTMapTile
    {
        public Location Location;
        public ushort TileId;
        public List<OTMapItem> Items = new List<OTMapItem>();
    }

    public class OTMapItem
    {
        public ushort ItemId;
        public AttrType AttrType;
        public byte Extra;
    }
}

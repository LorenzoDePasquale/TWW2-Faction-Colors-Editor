using System.IO;
using Filetypes;

namespace TWW2_Faction_Colors_Editor
{
    static class DBDecoder
    {
        public static DBFile Decode(string typeName, byte[] data)
        {
            DBFile decoded = null;
            using (BinaryReader reader = new BinaryReader(new MemoryStream(data)))
            {
                DBFileHeader header = PackedFileDbCodec.readHeader(reader);
                if (data != null && header != null)
                {
                    decoded = new PackedFileDbCodec(typeName).Decode(data);
                }
            }
            return decoded;
        }

        public static byte[] Encode(string typeName, DBFile data)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                if (DBTypeMap.Instance.IsSupported(typeName))
                {
                    new PackedFileDbCodec(typeName).Encode(stream, data);
                }
                return stream.ToArray();
            }
        }
    }
}

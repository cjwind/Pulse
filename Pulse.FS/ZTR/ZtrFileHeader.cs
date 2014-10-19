using System;
using System.IO;
using Pulse.Core;

namespace Pulse.FS
{
    public sealed class ZtrFileHeader
    {
        public int Version;
        public int Count;
        public int KeysUnpackedSize;
        public int TextBlocksCount;
        public int[] TextBlockTable;
        public ZtrFileHeaderLineInfo[] TextLinesTable;

        public unsafe void ReadFromStream(Stream input)
        {
            byte[] buff = input.EnsureRead(0x10);
            fixed (byte* b = &buff[0])
            {
                Version = Endian.ToLittleInt32(b + 0);
                Count = Endian.ToLittleInt32(b + 4);
                KeysUnpackedSize = Endian.ToLittleInt32(b + 8);
                TextBlocksCount = Endian.ToLittleInt32(b + 12);
            }

            if (Version != 1)
                throw new NotImplementedException();

            TextBlockTable = new int[TextBlocksCount];
            if (TextBlocksCount > 0)
            {
                buff = input.EnsureRead(TextBlocksCount * 4);
                fixed (byte* b = &buff[0])
                {
                    for (int i = 0; i < TextBlocksCount; i++)
                        TextBlockTable[i] = Endian.ToLittleInt32(b + i * 4);
                }
            }

            TextLinesTable = new ZtrFileHeaderLineInfo[Count];
            if (Count > 0)
            {
                buff = input.EnsureRead(Count * 4);
                fixed (byte* b = &buff[0])
                {
                    for (int i = 0; i < Count; i++)
                    {
                        TextLinesTable[i].Block = *(b + i * 4);
                        TextLinesTable[i].BlockOffset = *(b + i * 4 + 1);
                        TextLinesTable[i].PackedOffset = Endian.ToLittleUInt16(b + i * 4 + 2);
                    }
                }
            }
        }
    }
}
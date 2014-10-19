﻿using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pulse.Core;

namespace Pulse.FS
{
    public sealed class ArchiveListingReader
    {
        public static ArchiveListing[] Read(string gameDataPath, ArchiveAccessor accessor)
        {
            ArchiveListingReader reader = new ArchiveListingReader(gameDataPath, accessor);
            reader.Read();
            return reader._listings.ToArray();
        }

        private readonly ConcurrentBag<ArchiveAccessor> _accessors = new ConcurrentBag<ArchiveAccessor>();
        private readonly ConcurrentBag<ArchiveListing> _listings = new ConcurrentBag<ArchiveListing>();
        private readonly string _gameDataPath;
        private long _counter;

        private ArchiveListingReader(string gameDataPath, ArchiveAccessor accessor)
        {
            _gameDataPath = gameDataPath;
            _accessors.Add(accessor);
        }

        public void Read()
        {
            int count = Math.Max(Environment.ProcessorCount, 1);
            using (Semaphore semaphore = new Semaphore(count, count))
            {
                do
                {
                    ArchiveAccessor acc;
                    while (_accessors.TryTake(out acc) && semaphore.WaitOne())
                    {
                        Interlocked.Increment(ref _counter);
                        ArchiveAccessor accessor = acc;
                        Task.Run(() => ReadInternal(accessor, semaphore));
                    }
                    Thread.Sleep(200);
                } while (Interlocked.Read(ref _counter) > 0 || _accessors.Count > 0);
            }
        }

        private void ReadInternal(ArchiveAccessor accessor, Semaphore semaphore)
        {
            using (DisposableStack stack = new DisposableStack(2))
            {
                Stream input = stack.Add(accessor.OpenListing());
                if (accessor.ListingEntry.Size != accessor.ListingEntry.UncompressedSize)
                {
                    SafeHGlobalHandle buffer = stack.Add(new SafeHGlobalHandle((int)accessor.ListingEntry.UncompressedSize));
                    Stream output = stack.Add(buffer.OpenStream(FileAccess.ReadWrite));
                    ArchiveEntryExtractor extractor = new ArchiveEntryExtractor(accessor.ListingEntry, input, output);
                    extractor.Extract();
                    output.Position = 0;
                    input = output;
                }

                ArchiveListingHeader header = input.ReadStruct<ArchiveListingHeader>();
                ArchiveListingEntryInfo[] entries = input.ReadStructs<ArchiveListingEntryInfo>(header.EntriesCount);

                input.Position = header.BlockOffset;
                ArchiveListingBlockInfo[] blocks = input.ReadStructs<ArchiveListingBlockInfo>(header.BlocksCount);

                Encoding encoding = Encoding.GetEncoding(1252);
                
                byte[] buff = new byte[0];
                int blockLength = 0;

                ArchiveListing result = new ArchiveListing(accessor, header.EntriesCount);
                for (int currentBlock = -1, i = 0; i < header.EntriesCount; i++)
                {
                    ArchiveListingEntryInfo entryInfo = entries[i];
                    if (entryInfo.BlockNumber != currentBlock)
                    {
                        currentBlock = entryInfo.BlockNumber;
                        ArchiveListingBlockInfo block = blocks[currentBlock];
                        blockLength = block.UncompressedSize;

                        input.Position = header.InfoOffset + block.Offset;
                        buff = ZLibHelper.Uncompress(input, blockLength);
                    }

                    int infoLength;
                    if (i < header.EntriesCount - 1)
                    {
                        ArchiveListingEntryInfo next = entries[i + 1];
                        infoLength = next.BlockNumber == currentBlock ? next.Offset : blockLength - 4;
                    }
                    else
                    {
                        infoLength = blockLength - 4;
                    }
                    infoLength = infoLength - entryInfo.Offset - 1;

                    string[] info = encoding.GetString(buff, entryInfo.Offset, infoLength).Split(':');
                    long sector = long.Parse(info[0], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                    long uncompressedSize = long.Parse(info[1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                    long compressedSize = long.Parse(info[2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                    string name = info[3];

                    ArchiveListingEntry entry = new ArchiveListingEntry(name, sector, compressedSize, uncompressedSize)
                    {
                        UnknownNumber = entryInfo.UnknownNumber,
                        UnknownValue = entryInfo.UnknownValue
                    };
                    result.Add(entry);

                    if (name.StartsWith("zone/filelist"))
                    {
                        //string binaryName = Path.Combine(_gameDataPath, String.Format("zone/white_{0}_img{1}.win32.bin", name.Substring(14, 5), name.EndsWith("2") ? "2" : string.Empty));
                        //_accessors.Add(accessor.CreateChild(binaryName, entry));
                    }
                }
                _listings.Add(result);
            }
            semaphore.Release();
            Interlocked.Decrement(ref _counter);
        }
    }
}
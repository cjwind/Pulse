﻿using System.IO;
using Pulse.Core;

namespace Pulse.FS
{
    public sealed class ArchiveAccessor
    {
        private int _level = 0;

        private readonly SharedMemoryMappedFile _binaryFile;
        private readonly SharedMemoryMappedFile _listingFile;
        public readonly ArchiveListingEntry ListingEntry;

        public ArchiveAccessor(string binaryFile, string listingFile)
        {
            _binaryFile = new SharedMemoryMappedFile(binaryFile);
            _listingFile = new SharedMemoryMappedFile(listingFile);

            FileInfo listingFileInfo = new FileInfo(listingFile);
            ListingEntry = new ArchiveListingEntry(listingFileInfo.Name, 0, listingFileInfo.Length, listingFileInfo.Length);
        }

        private ArchiveAccessor(SharedMemoryMappedFile binaryFile, SharedMemoryMappedFile listingFile, ArchiveListingEntry entry)
        {
            _binaryFile = binaryFile;
            _listingFile = listingFile;
            ListingEntry = entry;
        }

        public ArchiveAccessor CreateChild(ArchiveListingEntry listingEntry)
        {
            ArchiveAccessor result = new ArchiveAccessor(_binaryFile, _binaryFile, listingEntry);
            result._level++;
            return result;
        }

        public ArchiveAccessor CreateChild(string binaryFile, ArchiveListingEntry listingEntry)
        {
            ArchiveAccessor result = new ArchiveAccessor(new SharedMemoryMappedFile(binaryFile), _binaryFile, listingEntry);
            result._level++;
            return result;
        }

        public Stream OpenListing()
        {
            return _listingFile.CreateViewStream(ListingEntry.Offset, ListingEntry.Size);
        }

        public Stream OpenCapacityListing()
        {
            if (_level == 0)
                return _listingFile.RecreateFile();

            long capacity = ((ListingEntry.Size / 0x800) + 1) * 0x800;
            return _listingFile.CreateViewStream(ListingEntry.Offset, capacity);
        }

        public Stream OpenBinary(ArchiveListingEntry entry)
        {
            return _binaryFile.CreateViewStream(entry.Offset, entry.Size);
        }
    }
}
﻿/*
 * This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
 * If a copy of the MPL was not distributed with this file, You can obtain one at
 * http://mozilla.org/MPL/2.0/. 
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using FSO.Files.Utils;
using SU2.Files.Formats.DIR;
using SU2.Utils;

namespace FSO.Files.Formats.DBPF
{
    public class DBPFReference
    {
        public byte[] fileBytes;
        public DBPFFile file;

        public DBPFReference(byte[] bytes, DBPFFile file)
        {
            this.fileBytes = bytes;
            this.file = file;
        }
    }
    /// <summary>
    /// The database-packed file (DBPF) is a format used to store data for pretty much all Maxis games after The Sims, 
    /// including The Sims Online (the first appearance of this format), SimCity 4, The Sims 2, Spore, The Sims 3, and 
    /// SimCity 2013.
    /// </summary>
    public class DBPFFile : IDisposable
    {
        public bool hasCompression = false;

        public string fname = "";
        public DIRFile DIR = null;
        public uint IndexMinorVersion;
        public uint groupID;

        public int DateCreated;
        public int DateModified;

        private uint IndexMajorVersion;
        public uint NumEntries;
        private IoBuffer m_Reader;

        private List<DBPFEntry> m_EntriesList = new List<DBPFEntry>();
        public Dictionary<int, DBPFEntry> m_EntryByID = new Dictionary<int, DBPFEntry>();
        private Dictionary<int, DBPFEntry> m_EntryByFullID = new Dictionary<int, DBPFEntry>();
        private Dictionary<uint, List<DBPFEntry>> m_EntriesByType = new Dictionary<uint, List<DBPFEntry>>();
        private Dictionary<string, DBPFEntry> m_EntryByName = new Dictionary<string, DBPFEntry>();

        private IoBuffer Io;

        /// <summary>
        /// Constructs a new DBPF instance.
        /// </summary>
        public DBPFFile()
        {
        }

        /// <summary>
        /// Creates a DBPF instance from a path.
        /// </summary>
        /// <param name="file">The path to an DBPF archive.</param>
        public DBPFFile(string file)
        {
            // var stream = Delimon.Win32.IO.File.OpenRead(file);
            var stream = File.OpenRead(file);
            fname = file;
            Read(stream);
        }

        /// <summary>
        /// Reads a DBPF archive from a stream.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        public void Read(Stream stream)
        {
            groupID = Hash.GroupHash(Path.GetFileNameWithoutExtension(fname));
            m_EntryByFullID = new Dictionary<int, DBPFEntry>();
            m_EntriesList = new List<DBPFEntry>();

            var io = IoBuffer.FromStream(stream, ByteOrder.LITTLE_ENDIAN);
            m_Reader = io;
            this.Io = io;

            var magic = io.ReadCString(4);
            if (magic != "DBPF")
            {
                throw new Exception("Not a DBPF file");
            }

            var majorVersion = io.ReadUInt32();
            var minorVersion = io.ReadUInt32();
            var version = majorVersion + (((double)minorVersion) / 10.0);

            /** Unknown, set to 0 **/
            io.Skip(12);
            if (version <= 1.2)
            {
                this.DateCreated = io.ReadInt32();
                this.DateModified = io.ReadInt32();
            }

            if (version < 2.0)
            {
                IndexMajorVersion = io.ReadUInt32();
            }

            NumEntries = io.ReadUInt32();
            uint indexOffset = 0;
            if (version < 2.0)
            {
                indexOffset = io.ReadUInt32();
            }
            var indexSize = io.ReadUInt32();
            //uint indexMinor = 0;
            if (version < 2.0)
            {
                var trashEntryCount = io.ReadUInt32();
                var trashIndexOffset = io.ReadUInt32();
                var trashIndexSize = io.ReadUInt32();
                IndexMinorVersion = io.ReadUInt32();
            }
            else if (version == 2.0)
            {
                IndexMinorVersion = io.ReadUInt32();
                indexOffset = io.ReadUInt32();
                io.Skip(4);
            }

            /** Padding **/
            io.Skip(32);

            io.Seek(SeekOrigin.Begin, indexOffset);
            var nameMaps = new List<DBPFEntry>();
            var rcols = new List<DBPFEntry>();
            for (int i = 0; i < NumEntries; i++)
            {
                var entry = new DBPFEntry();
                entry.file = this;
                entry.TypeID = io.ReadUInt32();
                entry.GroupID = io.ReadUInt32();
                if (entry.GroupID == 0xFFFFFFFF)
                    entry.GroupID = groupID;
                entry.InstanceID = io.ReadUInt32();
                if (IndexMinorVersion >= 2)
                    entry.InstanceID2 = io.ReadUInt32();
                entry.FileOffset = io.ReadUInt32();
                entry.FileSize = io.ReadUInt32();
                var id = Hash.TGIHash(entry.InstanceID, entry.TypeID, entry.GroupID);
                var fullID = Hash.TGIRHash(entry.InstanceID, entry.InstanceID2, entry.TypeID, entry.GroupID);
                m_EntryByID[id] = entry;
                m_EntryByFullID[fullID] = entry;
                if (!m_EntriesByType.ContainsKey(entry.TypeID))
                    m_EntriesByType.Add(entry.TypeID, new List<DBPFEntry>());
                m_EntriesByType[entry.TypeID].Add(entry);
            }
            var dirEntry = GetItemByFullID(Hash.TGIRHash((uint)0x286B1F03, (uint)0x00000000, (uint)0xE86B1EEF, (uint)0xE86B1EEF));
            if (dirEntry != null)
            {
                hasCompression = true;
                DIRFile.Read(this, dirEntry);
            }
            foreach(var element in nameMaps)
            {
                var b1g = GetEntry(element);
                var stream1 = new MemoryStream(b1g);
                var read = new IoBuffer(stream1);
                read.ByteOrder = ByteOrder.LITTLE_ENDIAN;
                var numNames = read.ReadUInt32();
                for (var j = 0; j < numNames; j++)
                {
                    var gID = read.ReadUInt32();
                    if (gID == 0xFFFFFFFF)
                        gID = groupID;
                    var iID = read.ReadUInt32();
                    var nameLength = read.ReadUInt32();
                    var fileNam = read.ReadCString((int)nameLength).ToLower();
                    DBPFEntry hash = null;
                        hash = GetEntryByID(Hash.TGIHash(iID, element.InstanceID, gID));
                    var gNam = "##0x" + gID.ToString("x8") + "!" + fileNam;

                    if (hash != null)
                    {
                        m_EntryByName[fileNam] = hash;
                        m_EntryByName[gNam] = hash;

                    }
                }
                read.Dispose();
                stream1.Dispose();
            }
        }

        /// <summary>
        /// Gets a DBPFEntry's data from this DBPF instance.
        /// </summary>
        /// <param name="entry">Entry to retrieve data for.</param>
        /// <returns>Data for entry.</returns>
        public byte[] GetEntry(DBPFEntry entry)
        {
            m_Reader.Seek(SeekOrigin.Begin, entry.FileOffset);
            return m_Reader.ReadBytes((int)entry.FileSize);
        }

        /// <summary>
        /// Gets an entry from its Name.
        /// </summary>
        /// <param name="Name">The name of the entry.</param>
        /// <returns>The entry's data.</returns>
        public byte[] GetItemByName(string name)
        {
            if (m_EntryByName.ContainsKey(name))
                return GetEntry(m_EntryByName[name]);
            else
                return null;
        }

        /// <summary>
        /// Gets an entry from its ID (TypeID + FileID).
        /// </summary>
        /// <param name="ID">The ID of the entry.</param>
        /// <returns>The entry's data.</returns>
        public byte[] GetItemByID(int tgi)
        {
            if (m_EntryByID.ContainsKey(tgi))
                return GetEntry(m_EntryByID[tgi]);
            else
                return null;
        }

        /// <summary>
        /// Gets an entry from its ID (TypeID + FileID).
        /// </summary>
        /// <param name="ID">The ID of the entry.</param>
        /// <returns>The entry's data.</returns>
        public DBPFEntry GetEntryByID(int tgi)
        {
            if (m_EntryByID.ContainsKey(tgi))
                return m_EntryByID[tgi];
            else
                return null;
        }

        /// <summary>
        /// Gets an entry from its ID (TypeID + FileID).
        /// </summary>
        /// <param name="ID">The ID of the entry.</param>
        /// <returns>The entry's data.</returns>
        public byte[] GetItemByFullID(int tgir)
        {
            if (m_EntryByFullID.ContainsKey(tgir))
                return GetEntry(m_EntryByFullID[tgir]);
            else
                return null;
        }

        /// <summary>
        /// Gets an entry from its ID (TypeID + FileID).
        /// </summary>
        /// <param name="ID">The ID of the entry.</param>
        /// <returns>The entry's data.</returns>
        public DBPFEntry GetEntryByFullID(int tgir)
        {
            if (m_EntryByFullID.ContainsKey(tgir))
                return m_EntryByFullID[tgir];
            else
                return null;
        }

        public DBPFEntry GetEntryByName(string name)
        {
            if (m_EntryByName.ContainsKey(name))
                return m_EntryByName[name];
            else
                return null;
        }

        /// <summary>
        /// Gets an entry from its ID (TypeID + FileID + GroupID).
        /// </summary>
        /// <param name="ID">The ID of the entry.</param>
        /// <returns>The entry's data.</returns>
        /*
        public byte[] GetItemByID(GroupEntryRef ID)
        {
            if (m_EntryByFullID.ContainsKey(ID))
                return GetEntry(m_EntryByFullID[ID]);
            else
                return null;
        }*/

        /// <summary>
        /// Gets all entries of a specific type.
        /// </summary>
        /// <param name="Type">The Type of the entry.</param>
        /// <returns>The entry data, paired with its instance id.</returns>
        public List<KeyValuePair<uint, byte[]>> GetItemsByType(uint Type)
        {

            var result = new List<KeyValuePair<uint, byte[]>>();
            if (!m_EntriesByType.ContainsKey(Type))
                return null;
            var entries = m_EntriesByType[Type];
            for (int i = 0; i < entries.Count; i++)
            {
                result.Add(new KeyValuePair<uint, byte[]>(entries[i].InstanceID, GetEntry(entries[i])));
            }
            return result;
        }

        #region IDisposable Members

        /// <summary>
        /// Disposes this DBPF instance.
        /// </summary>
        public void Dispose()
        {
            Io.Dispose();
        }

        #endregion
    }
}

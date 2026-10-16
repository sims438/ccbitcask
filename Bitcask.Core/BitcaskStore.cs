using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;


namespace Bitcask.Core
{
    public class BitcaskStore
    {
        public readonly struct RecordMetadata
        {
            public readonly int FileId;
            public readonly long ValuePosition;
            public readonly int ValueSize;
            public readonly long Timestamp;
            

            public RecordMetadata(int fileId, long valuePosition, int valueSize, long timestamp)
            {
                FileId = fileId;
                ValuePosition = valuePosition;
                ValueSize = valueSize;
                Timestamp = timestamp;
            }
        }
        Dictionary<string , RecordMetadata> _index = new Dictionary<string, RecordMetadata>();
        private string _activePath;
        private readonly string _dbPath;
        private readonly int _maxFileSize;
        private int _nextFileId;
        private int _tombstone = -1;
        int _headerSize = 4 + 8 + 4 + 4; // CRC32 (4 bytes) + Timestamp (8 bytes) + Key Size (4 bytes) + Value Size (4 bytes)

        public BitcaskStore(string dbPath, int maxFileSize =60)
        {
            _maxFileSize=maxFileSize;
            _dbPath= dbPath;
            Directory.CreateDirectory(dbPath);
            List<string> files = Directory.GetFiles(dbPath, "cask.*")
                      .OrderBy(f => ExtractFileId(f)).ToList();
            //for segmentation
            _nextFileId = files.Count == 0?1:files.Max(f =>ExtractFileId(f)+1);
            

            foreach (string file in files)
            {

                _activePath = file;
                var fi = new FileInfo(_activePath);
                int fileId = ExtractFileId(file);
                if (fi.Exists && fi.Length > 0)
                {
                    using (var stream = File.OpenRead(_activePath))
                    using (var reader = new BinaryReader(stream))
                    {
                        while (stream.Position < stream.Length)
                        {
                            byte[] storedCrc = reader.ReadBytes(4);
                            long timeStamp = reader.ReadInt64();
                            int keySize = reader.ReadInt32();
                            int valSize =  reader.ReadInt32();                      
                            byte[] keyBytes = reader.ReadBytes(keySize);
                           
                            long valPosition = stream.Position; 
                            string key = Encoding.UTF8.GetString(keyBytes);

                            if (valSize == _tombstone)
                            {
                                byte[] payloadDel = BitConverter.GetBytes(timeStamp)
                                .Concat(BitConverter.GetBytes(keySize))                               
                                .Concat(BitConverter.GetBytes(valSize))
                                .Concat(keyBytes)
                                .ToArray();
                                byte[] computedCrcDel = System.IO.Hashing.Crc32.Hash(payloadDel);
                                if (storedCrc.SequenceEqual(computedCrcDel))
                                {
                                    _index.Remove(key);
                                }
                                continue;

                            }
                            else
                            {
                                byte[] valueBytes = reader.ReadBytes(valSize);
                                byte[] payload = BitConverter.GetBytes(timeStamp)
                                    .Concat(BitConverter.GetBytes(keySize))
                                    .Concat(BitConverter.GetBytes(valSize))
                                    .Concat(keyBytes)
                                    .Concat(valueBytes).ToArray();

                                byte[] computedCrc = System.IO.Hashing.Crc32.Hash(payload);
                                if (!storedCrc.SequenceEqual(computedCrc))
                                {
                                    continue;
                                }
                                var recordMetaData = new RecordMetadata(fileId, valPosition, valSize, timeStamp);

                                _index[key] = recordMetaData;
                            }

                        }
                    }

                }
            }
            if (files.Count == 0)
            {
                _activePath = Path.Combine(dbPath, "cask.0");
                _nextFileId = 1;
            }
        }

        private int ExtractFileId(string path)
        {
            string fileName = Path.GetFileName(path);
            return int.Parse(fileName.Split('.')[1]); 
        }

        public void Set(string key,string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Byte[] record = Encode(key, value, out long timestamp, out int valueOffsetInRecord, false);       
            long recordStart = Append(record); 
            _index[key] = new RecordMetadata(ExtractFileId(_activePath),recordStart +valueOffsetInRecord,record.Length-valueOffsetInRecord,timestamp);
        }

        private void EnsureActiveFileHasRoom()
        {
            var fileInfo = new FileInfo(_activePath);
            if (fileInfo.Exists && fileInfo.Length > _maxFileSize)
            {
                _activePath = Path.Combine(_dbPath, $"cask.{_nextFileId++}");
            }
        }
        public long Append(byte[] record)
        {
            EnsureActiveFileHasRoom();
            using var fs = new FileStream(_activePath, FileMode.Append);
            long start = fs.Length;
            fs.Write(record);
            return start;

        }

        public string Get(string key)
        {
            if (!_index.TryGetValue(key, out var metaData))
                return null;           
            int fileId = metaData.FileId;
            string fileName = $"cask.{fileId}";
            string fileDirectory  = Path.Combine(_dbPath, fileName);
            var fi = new FileInfo(fileDirectory);
            string strValue = "";
            if (fi.Exists && fi.Length > 0)
            {
                using var fs = new FileStream(fileDirectory, FileMode.Open, FileAccess.Read);
                using var br = new BinaryReader(fs);
                fs.Seek(metaData.ValuePosition, SeekOrigin.Begin);
                byte[] byteValue = br.ReadBytes(metaData.ValueSize);
                strValue = Encoding.UTF8.GetString(byteValue);

            }
            return strValue;
        }   
        public void Delete(string key)
        {
            if (!_index.ContainsKey(key))
                return;

            long start = Append(Encode(key,null, out long timestamp, out _, true));           
            var fileInfo = new FileInfo(_activePath);
            _index.Remove(key);

        }
        private byte[] Encode(string key, string value, out long timestamp, out int valueOffsetInRecord,bool _isDeleted)
        {
            //Timestamp,Key Size, Value Size,Key,Value

            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            byte[] timestampBytes = BitConverter.GetBytes(timestamp);
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[] keySizeByte = BitConverter.GetBytes(keyBytes.Length);
            byte[] valueBytes = _isDeleted ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(value);
            valueOffsetInRecord = _headerSize + keyBytes.Length;
            byte[] valueSizeByte = BitConverter.GetBytes(_isDeleted?_tombstone : valueBytes.Length);
            byte[] payload;
            payload = timestampBytes
                                 .Concat(keySizeByte)
                                .Concat(valueSizeByte)
                                .Concat(keyBytes)
                                .Concat(valueBytes)
                                .ToArray();
               
            byte[] crcBytes = System.IO.Hashing.Crc32.Hash(payload);

            byte[] record = crcBytes.Concat(payload).ToArray();

            return record;
        }
    }
}

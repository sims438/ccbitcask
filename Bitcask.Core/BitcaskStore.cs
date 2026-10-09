using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Threading.Tasks;
using System.IO.Hashing;

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
        Dictionary<string , RecordMetadata> dict = new Dictionary<string, RecordMetadata>();
        private string filePath;
        private string _dbPath;
        public int _maxFileSize;
        int fileCount;
        
        public BitcaskStore(string dbPath, int maxFileSize =256)
        {
            _maxFileSize=maxFileSize;
            _dbPath= dbPath;
            Directory.CreateDirectory(dbPath);
            List<string> files = Directory.GetFiles(dbPath, "cask.*")
                      .OrderBy(f => ExtractFileId(f)).ToList();
            fileCount= files.Count;
            

            foreach (string file in files)
            {

                filePath = file;
                var fi = new FileInfo(filePath);
                int fileId = ExtractFileId(file);
                if (fi.Exists && fi.Length > 0)
                {
                    using (var stream = File.OpenRead(filePath))
                    using (var reader = new BinaryReader(stream))
                    {
                        while (stream.Position < stream.Length)
                        {
                            byte[] storedCrc = reader.ReadBytes(4);
                            long timeStamp = reader.ReadInt64();
                            int keySize = reader.ReadInt32();
                            int valSize = reader.ReadInt32();
                            byte[] keyBytes = reader.ReadBytes(keySize);
                            long valPosition = stream.Position; 
                            byte[] valueBytes = reader.ReadBytes(valSize);

                            string key = Encoding.UTF8.GetString(keyBytes);
                            string value = Encoding.UTF8.GetString(valueBytes);
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
                            var recordMetaData = new RecordMetadata(fileId,valPosition,valSize,timeStamp);

                            dict[key] = recordMetaData;

                        }
                    }

                }
            }
        }

        private int ExtractFileId(string path)
        {
            string fileName = Path.GetFileName(path);
            return int.Parse(fileName.Split('.')[1]); ;
        }

        public void Set(string key,string value)
        {
            Byte[] encodedByte = Encode(key,value,out long timestamp,out int valueOffsetInRecord);
            int fileId = ExtractFileId(filePath);
            long recordStart = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;
            using (var writer = new BinaryWriter(File.Open(filePath, FileMode.Append)))
            {
                writer.Write(encodedByte);
            }
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Exists && fileInfo.Length > _maxFileSize)
            {
                filePath = Path.Combine(_dbPath, $"cask.{fileCount++}");
            }
            int valueSize = encodedByte.Length - valueOffsetInRecord;

            dict[key] = new RecordMetadata(fileId,recordStart+valueOffsetInRecord,valueSize,timestamp);
        }
        public string Get(string key)
        {
            return dict.TryGetValue(key, out var value) ? value : null;
        }

        public byte[] Encode(string key , string value,out long timestamp, out int valueOffsetInRecord)
        {
            //Timestamp,Key Size, Value Size,Key,Value

            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            byte[] timestampBytes = BitConverter.GetBytes(timestamp);
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[] keySizeByte = BitConverter.GetBytes(keyBytes.Length);
            byte[] valueBytes = Encoding.UTF8.GetBytes(value);
            valueOffsetInRecord = 4 + 8 + 4 + 4 + keyBytes.Length;
            byte[] valueSizeByte = BitConverter.GetBytes(valueBytes.Length);
            

            int payloadLength = timestampBytes.Length + keySizeByte.Length
                       + valueSizeByte.Length + keyBytes.Length + valueBytes.Length;
            byte[] payload = timestampBytes
    .Concat(keySizeByte)
    .Concat(valueSizeByte)
    .Concat(keyBytes)
    .Concat(valueBytes)
    .ToArray();
            byte[] crcBytes = System.IO.Hashing.Crc32.Hash(payload);

            byte[] record = crcBytes.Concat(payload).ToArray();

            return record;
        }
        public int Decode(byte[] bytes)
        {
            int val; 
            val = BinaryPrimitives.ReadInt32LittleEndian(bytes);
            return val;
        }

    }
}

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
        Dictionary<string ,string> dict = new Dictionary<string, string>();
        private string filePath;
        public BitcaskStore(string dbpath)
        {
            Directory.CreateDirectory(dbpath);
            filePath = Path.Combine(dbpath , "KeyValueStore.Txt");
            if (File.Exists(filePath) && !string.IsNullOrEmpty(File.ReadAllText(filePath)))
            {
                foreach (var line in File.ReadAllLines(filePath))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length != 2) continue;
                    string lineKey = parts[0];
                    string lineValue = parts[1];
                    dict[lineKey] = lineValue;

                }

            }
        }
        
        public void Set(string key,string value)
        {               
            dict[key] = value;
            File.WriteAllText(filePath, "");
            var builder = new StringBuilder();
            foreach(var kvp in dict)
            {
                builder.AppendLine($"{kvp.Key}:{kvp.Value}");
            }
            File.WriteAllText(filePath, builder.ToString());          

        }
        public string Get(string key)
        {
            return dict.TryGetValue(key, out var value) ? value : null;
        }

        public byte[] Encode(string key , string value)
        {
            //Timestamp,Key Size, Value Size,Key,Value
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            byte[] timestampBytes = BitConverter.GetBytes(timestamp);
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[] keySizeByte = BitConverter.GetBytes(keyBytes.Length);
            byte[] valueBytes = Encoding.UTF8.GetBytes(value);
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

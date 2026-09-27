using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

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
                builder.AppendLine($"{kvp.Key} : {kvp.Value}");
            }
            File.WriteAllText(filePath, builder.ToString());          

        }
        public string Get(string key)
        {
            return dict.TryGetValue(key, out var value) ? value : null;
        }


    }
}

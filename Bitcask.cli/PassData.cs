using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bitcask.Core;

namespace Bitcask.cli
{
    public class PassData
    {
        static void Main(string[] args)
        {
            string dbPath = args[1];
            string command = args[2];
            string key = args[3];

            var store = new BitcaskStore(dbPath);

            if(command == "set")
            {
                string value = args[4];
                store.Set(key,value);
            }
            if (command == "get")
            {
                var result = store.Get(key);
                Console.WriteLine(result ?? "(nil)");
            }

        }
        

    }
}

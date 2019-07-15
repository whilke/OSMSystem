using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Newtonsoft.Json;

namespace OSMSystem
{
    class Program
    {
        private static string naUrl = "https://download.geofabrik.de/north-america-latest.osm.pbf";
        private static string osmPath = "/tmp";
        private static string downloadUrl = "https://download.geofabrik.de/north-america/us/{0}";
        private static string stateList =
            "['Alaska', 'Alabama', 'Arkansas', 'Arizona', 'California', 'Colorado', 'Connecticut', 'District of Columbia', 'Delaware', 'Florida', 'Georgia', 'Hawaii', 'Iowa', 'Idaho', 'Illinois', 'Indiana', 'Kansas', 'Kentucky', 'Louisiana', 'Massachusetts', 'Maryland', 'Maine', 'Michigan', 'Minnesota', 'Missouri', 'Mississippi', 'Montana', 'North Carolina', 'North Dakota', 'Nebraska', 'New Hampshire', 'New Jersey', 'New Mexico', 'Nevada', 'New York', 'Ohio', 'Oklahoma', 'Oregon', 'Pennsylvania', 'Rhode Island', 'South Carolina', 'South Dakota', 'Tennessee', 'Texas', 'Utah', 'Virginia', 'Vermont', 'Washington', 'Wisconsin', 'West Virginia', 'Wyoming']";
        static void Main(string[] args)
        {
            osmPath = args[0];
            string cs = args[2];
            string container = args[1];
            var account = CloudStorageAccount.Parse(cs);
            var blobClient = account.CreateCloudBlobClient();
            var cont = blobClient.GetContainerReference(container);
            cont.CreateIfNotExists();

            List<string> states = JsonConvert.DeserializeObject<List<string>>(stateList);

            WebClient client = new WebClient();

            var naFile = Path.Combine(osmPath, "north-america-latest.osm.pbf");
            if (!File.Exists(naFile))
            {
                Console.WriteLine("Downloading NA OSM file");
                client.DownloadFile(naUrl, naFile);
            }

            foreach (var state in states)
            {
                var st = state.ToLower().Replace(" ", "-") + ".poly";
                var osm = state.ToLower().Replace(" ", "-") + ".osm.pbf";
                var polyFile = Path.Combine(osmPath, st);
                if (!File.Exists(polyFile))
                {
                    try
                    {
                        Console.WriteLine("Downloading poly file " + st);
                        client.DownloadFile(string.Format(downloadUrl, st), polyFile);
                    }
                    catch
                    {
                    }
                }

                if (!File.Exists(Path.Combine(osmPath, osm)))
                {
                    Console.WriteLine("Processing..." + osm);
                    var dockerArgs = $"extract -p {polyFile} -o {Path.Combine(osmPath, osm)} {naFile}";
                    var p = Process.Start("osmium", dockerArgs);
                    p.WaitForExit();

                    //upload to blob
                    Console.WriteLine("Uploading " + osm);
                    var blob = cont.GetBlockBlobReference(osm);
                    blob.UploadFromFile(Path.Combine(osmPath, osm));
                }

            }
            
        }
    }
}

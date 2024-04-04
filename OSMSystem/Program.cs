using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using Itinero;
using Itinero.IO.Osm;
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
            "['California','Arizona','Alaska', 'Alabama', 'Arkansas', 'Colorado', 'Connecticut', 'District of Columbia', 'Delaware', 'Florida', 'Georgia', 'Hawaii', 'Iowa', 'Idaho', 'Illinois', 'Indiana', 'Kansas', 'Kentucky', 'Louisiana', 'Massachusetts', 'Maryland', 'Maine', 'Michigan', 'Minnesota', 'Missouri', 'Mississippi', 'Montana', 'North Carolina', 'North Dakota', 'Nebraska', 'New Hampshire', 'New Jersey', 'New Mexico', 'Nevada', 'New York', 'Ohio', 'Oklahoma', 'Oregon', 'Pennsylvania', 'Rhode Island', 'South Carolina', 'South Dakota', 'Tennessee', 'Texas', 'Utah', 'Virginia', 'Vermont', 'Washington', 'Wisconsin', 'West Virginia', 'Wyoming']";
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
                var st        = state.ToLower().Replace(" ", "-") + ".poly";
                var osm       = state.ToLower().Replace(" ", "-") + ".osm.pbf";
                var osmFilter = state.ToLower().Replace(" ", "-") + ".osm.filter.pbf";
                var routerdb  = state.ToLower().Replace(" ", "-") + ".routerdb";
                var polyFile  = Path.Combine(osmPath, st);
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
                    var blockBlobReference = cont.GetBlockBlobReference(routerdb);
                    if (blockBlobReference.Exists())
                    {
                        blockBlobReference.FetchAttributes();
                        DateTimeOffset  utcNow       = DateTimeOffset.UtcNow;
                        DateTimeOffset? lastModified = blockBlobReference.Properties.LastModified;
                        TimeSpan?       diff         = lastModified.HasValue ? new TimeSpan?(utcNow - lastModified.Value) : new TimeSpan?();
                        if ((diff.HasValue ? (diff.Value.TotalDays < 1.0 ? 1 : 0) : 0) != 0)
                        {
                            Console.WriteLine("routerFile is too recent, skipping");
                            continue;
                        }
                    }

                    Console.WriteLine("Processing..." + osm);
                    var dockerArgs = $"extract -p {polyFile} -o {Path.Combine(osmPath, osm)} {naFile}";
                    var p = Process.Start("osmium", dockerArgs);
                    p.WaitForExit();
                }

                if (!File.Exists(Path.Combine(osmPath, osmFilter)))
                {
                    Console.WriteLine("Filtering..." + osm);
                    var dockerArgs = $"tags-filter {Path.Combine(osmPath, osm)} w/highway w/junction w/barrier -o {Path.Combine(osmPath, osmFilter)}";
                    var p          = Process.Start("osmium", dockerArgs);
                    p.WaitForExit();

                    //upload to blob
                    Console.WriteLine("Uploading " + osmFilter);
                    var blob = cont.GetBlockBlobReference(osm);
                    blob.UploadFromFile(Path.Combine(osmPath, osmFilter));
                }

                if (!File.Exists(Path.Combine(osmPath, routerdb)))
                {
                    try
                    {
                        LoadSettings settings = new LoadSettings
                        {
                            AllCore = true,
                        };

                        Console.WriteLine("Loading OSM..." + osmFilter);
                        Stopwatch sw         = Stopwatch.StartNew();
                        RouterDb  db         = new RouterDb();
                        using var fileStream = File.OpenRead(Path.Combine(osmPath, osmFilter));
                        db.LoadOsmData(fileStream, settings,Itinero.Osm.Vehicles.Vehicle.Car);
                        Console.WriteLine("OSM loaded in..." + sw.Elapsed);
                        sw = Stopwatch.StartNew();
                        Console.WriteLine("Contracting..."   + routerdb);
                        db.AddContracted(Itinero.Osm.Vehicles.Vehicle.Car.Fastest());
                        Console.WriteLine("Contracted in..."      + sw.Elapsed);
                        Console.WriteLine("Uploading routerDB..." + routerdb);
                        fileStream.Dispose();

                        using var fileStreamDb = File.OpenWrite(Path.Combine(osmPath, routerdb));
                        db.Serialize(fileStreamDb, true);
                        fileStreamDb.Close();
                        fileStreamDb.Dispose();
                        cont.GetBlockBlobReference(routerdb).UploadFromFile(Path.Combine(osmPath, routerdb));
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error in RouterDB..." + e.ToString());
                    }
                }

                
                if (!File.Exists(Path.Combine(osmPath, osm)))
                {
                    File.Delete(Path.Combine(osmPath, osm));
                }
                if (!File.Exists(Path.Combine(osmPath, osmFilter)))
                {
                    File.Delete(Path.Combine(osmPath, osmFilter));
                }
                if (!File.Exists(Path.Combine(osmPath, routerdb)))
                {
                    File.Delete(Path.Combine(osmPath, routerdb));
                }
                

                Console.WriteLine("Finished..." + osm);
            }
            
        }
    }
}

namespace GeoDocDB
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.Client;
    using Microsoft.Azure.Documents.Linq;
    using Microsoft.Azure.Documents.Spatial;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;
    using geo = GeoJSON.Net.Geometry;
    using GeoJSON.Net.Feature;
    /// <summary>
    /// This sample demonstrates the use of geospatial indexing and querying with Azure DocumentDB. We 
    /// look at how to store Points using the classes in the Microsoft.Azure.Documents.Spatial namespace,
    /// how to enable a collection for geospatial indexing, and how to query for WITHIN and DISTANCE 
    /// using SQL and LINQ.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Gets the database ID to use for the demo.
        /// </summary>
        private static readonly string DatabaseId = ConfigurationManager.AppSettings["DatabaseId"];

        /// <summary>
        /// Gets the collection ID to use for the demo.
        /// </summary>
        private static readonly string CollectionId = ConfigurationManager.AppSettings["CollectionId"];

        /// <summary>
        /// Gets the DocumentDB endpoint to use for the demo.
        /// </summary>
        private static readonly string EndpointUrl = ConfigurationManager.AppSettings["EndPointUrl"];

        /// <summary>
        /// Gets the DocumentDB authorization key to use for the demo.
        /// </summary>
        private static readonly string AuthorizationKey = ConfigurationManager.AppSettings["AuthorizationKey"];

        /// <summary>
        /// Gets an indexing policy with spatial enabled. You can also configure just certain paths for spatial indexing, e.g. Path = "/location/?"
        /// </summary>
        private static readonly IndexingPolicy IndexingPolicyWithSpatialEnabled = new IndexingPolicy
        {
            IncludedPaths = new System.Collections.ObjectModel.Collection<IncludedPath>()
            {
                new IncludedPath
                {
                    Path = "/*",
                    Indexes = new System.Collections.ObjectModel.Collection<Index>()
                    {
                        new SpatialIndex(DataType.Point),
                        new RangeIndex(DataType.Number) { Precision = -1 },
                        new RangeIndex(DataType.String) { Precision = -1 }
                    }
                }
            }
        };

        /// <summary>
        /// Gets the client to use.
        /// </summary>
        private static DocumentClient client;

        private static SnappedPoints _sps;
        /// <summary>
        /// The main method to use.
        /// </summary>
        /// <param name="args">The command line arguments.</param>
        public static void Main(string[] args)
        {
            try
            {
                // Get a Document client
                using (client = new DocumentClient(new Uri(EndpointUrl), AuthorizationKey))
                {
                    RunDemoAsync(DatabaseId, CollectionId).Wait();
                }

                Console.WriteLine("DONE");
                Console.ReadLine();
            }
            catch (DocumentClientException de)
            {
                Exception baseException = de.GetBaseException();
                Console.WriteLine("{0} error occurred: {1}, Message: {2}", de.StatusCode, de.Message, baseException.Message);
            }
            catch (Exception e)
            {
                Exception baseException = e.GetBaseException();
                Console.WriteLine("Error: {0}, Message: {1}", e.Message, baseException.Message);
            }
            finally
            {
                Console.WriteLine("End of demo, press any key to exit.");
                Console.ReadKey();
            }
        }

        private static IEnumerable<GPXData> GetOptimizedDataPoints(List<GPXData> allDataPoints)
        {
            return allDataPoints;//.Where(a => (a.StdDevYaw > 0.2) && (a.StdDevRoll > 0.2) && (a.StdDevPitch > 0.2)).OrderByDescending(x => x.Time);
        }

        private static List<GPXData> GetSnappedRoads(List<GPXData> allDataPoints)
        {
            var optimalPoints = GetOptimizedDataPoints(allDataPoints);
            Point prevNearestPoint;
            double prevdis = 0;
            List<GPXData> snapedPointsGPX = new List<GPXData>();

            foreach (var item in optimalPoints)
            {
                prevNearestPoint = item.MidPoint;
                prevdis = 20;
                foreach (var sp in _sps.snappedPoints)
                {
                    double dis = Utils.distance(sp.point, item.MidPoint, 'K') * 1000;
                    if (dis < prevdis)
                    {
                        prevdis = dis;
                        prevNearestPoint = sp.point;
                    }
                }
                if (prevdis != 20)
                {
                    item.SnapPoint = prevNearestPoint;
                    snapedPointsGPX.Add(item);
                }
                //var point = _sps.snappedPoints.Where(a => a.point.Distance(item.MidPoint) < 1000);
            }

            return snapedPointsGPX;
        }

        private static GPXData NormalizeGPXPoint(GPXData oldData,GPXData newData)
        {
            if(newData.TripID == oldData.TripID && newData.Time.Subtract(oldData.Time).Seconds < 2)
            {
                return null;
            }
            newData.Id = oldData.Id;
            newData.Id = oldData.Id;
            newData.L0 = oldData.L0;
            newData.L1 = oldData.L1;
            newData.L2 = oldData.L2;
            switch (newData.RoadCondition)
            {
                case RoadType.Good:
                    newData.L0++;
                    break;
                case RoadType.SlightyBumpy:
                    break;
                case RoadType.Bumpy:
                    newData.L1++;
                    break;
                case RoadType.Worst:
                    newData.L1++;
                    break;
                case RoadType.RandomAction:
                    break;
                case RoadType.Idle:
                    break;
                default:
                    break;
            }

            if((newData.L0 + newData.L1 + newData.L2) > 5)
            {
                if(newData.L0 > newData.L1)
                {
                    newData.L0 = 2;
                }
                else
                {
                    newData.L1 = 2;
                }
            }

            if(newData.L0 >= newData.L1)
            {
                newData.RoadCondition = RoadType.Good;
            }
            else
            {
                newData.RoadCondition = RoadType.Bumpy;
            }

            //oldData.RoadCondition = newData.RoadCondition;
            //oldData.StdDevPitch = newData.StdDevPitch;
            //oldData.StdDevRoll = newData.StdDevRoll;
            //oldData.StdDevYaw = newData.StdDevYaw;
            //oldData.Speed = newData.Speed;
            //TODO: do averaging
            return newData;
        }

        private static List<GPXData> ProcessDataPoints(List<GPXData> dps)
        {
            //var pointsLessthan10Speed = dps.Where(a => a.Speed < 10 && a.Speed > 0.1);

            foreach (var item in dps)
            {
                if (item.StdDevPitch + item.StdDevYaw + item.StdDevRoll > 0.6)
                {
                    item.RoadCondition = RoadType.Bumpy;
                }
                else
                {
                    item.RoadCondition = RoadType.Good;
                }
            }

            return dps;
        }
        private static List<GPXData> ProcessDataPointsV1(List<GPXData> dps)
        {
            List<GPXData> processedList = new List<GPXData>();

            var pointsLessthan10Speed = dps.Where(a => a.Speed < 10 && a.Speed > 0.1);


            foreach (var item in pointsLessthan10Speed)
            {
                if(item.StdDevPitch > 0.2 && item.StdDevYaw > 0.2 && item.StdDevRoll > 0.2)
                {
                    item.RoadCondition = RoadType.Bumpy;
                }
                else
                {
                    item.RoadCondition = RoadType.Good;
                }

                processedList.Add(item);
            }

            var pointsMorethan10Speed = dps.Where(a => a.Speed > 10);

            foreach (var item in pointsMorethan10Speed)
            {
                if (item.StdDevPitch > 0.5 || item.StdDevYaw > 0.5 && item.StdDevRoll > 0.5)
                {
                    item.RoadCondition = RoadType.Bumpy;
                }
                else
                {
                    item.RoadCondition = RoadType.Good;
                }
                processedList.Add(item);
            }

            return dps;
        }

        /// <summary>
        /// Run the geospatial demo.
        /// </summary>
        /// <param name="databaseId">The database Id.</param>
        /// <param name="collectionId">The collection Id.</param>
        /// <returns>The Task for asynchronous execution.</returns>
        private static async Task RunDemoAsync(string databaseId, string collectionId)
        {
            Database database = await GetDatabaseAsync(databaseId);

            // Create a new collection, or modify an existing one to enable spatial indexing.
            DocumentCollection collection = await GetCollectionWithSpatialIndexingAsync(database.SelfLink, collectionId);

            ////await Cleanup(collection);

            int MAX_SKIP = 3;
            int MAX_COUNT = 100;
            int counter = 0;

            var docs = await client.ReadDocumentFeedAsync(collection.SelfLink, new FeedOptions { MaxItemCount = 1000 });
            //foreach (var item in docs)
            //{
            //    GPXData dp = (dynamic)item;

            //}

            List<geo.Point> geopoints1 = new List<geo.Point>();

            List<geo.Point> geobadpoints = new List<geo.Point>();

            List<Point> points1 = new List<Point>();
            List<Point> badpoints = new List<Point>();
            Feature f;
            foreach (var d in docs)
            {
                GPXData dp = (dynamic)d;
                geopoints1.Add(new geo.Point(new geo.GeographicPosition(dp.SnapPoint.Position.Latitude, dp.SnapPoint.Position.Longitude)));
                points1.Add(dp.SnapPoint);
               //geo.LineString l =  new geo.LineString(dp.StartPoint)
                //f = new Feature()



                if(dp.RoadCondition == RoadType.Bumpy)
                {
                    geobadpoints.Add(new geo.Point(new geo.GeographicPosition(dp.SnapPoint.Position.Latitude, dp.SnapPoint.Position.Longitude)));
                    badpoints.Add(dp.SnapPoint);
                }
                //Console.WriteLine(dp);
            }

            var multiPoint = new geo.MultiPoint(geopoints1);


            string geoJsonroute = JsonConvert.SerializeObject(new geo.MultiPoint(geopoints1));
            string geoJsonBadroute = JsonConvert.SerializeObject(new geo.MultiPoint(geobadpoints));

            
            //Feature f = new Feature(multiPoint,)

            string s = string.Empty;


            foreach (var item in points1)
            {
                ++counter;
                if (counter % MAX_SKIP == 0)
                {
                    s += item.Position.Latitude + "," + item.Position.Longitude + "|";
                }
                if (counter > MAX_COUNT)
                {
                    break;
                }
            }
            s = s.Remove(s.Length - 1, 1);


            //_sps = Utils.GetSnapedRoad(s);
            List<GPXData> dataPoints = ReadCSVcs.GetGPXDataFromCSVFile(@"d:\temp\EXCEL_180418_data.csv"); 
            //List<GPXData> dataPoints = ReadCSVcs.GetGPXDataFromCSVFile(@"d:\temp\EXCEL_120836_data.csv");
            //List<GPXData> dataPoints = ReadCSVcs.GetGPXDataFromCSVFile(@"d:\temp\EXCEL_192333_data.csv");
            //List<GPXData> dataPoints = ReadCSVcs.GetGPXDataFromCSVFile(@"d:\temp\EXCEL_205339_data.csv");
            //List<GPXData> dataPoints = ReadCSVcs.GetGPXDataFromCSVFile(@"d:\temp\EXCEL_162418_data.csv");
            //List<GPXData> dataPoints = ReadCSVcs.GetGPXDataFromCSVFile(@"d:\temp\EXCEL_163043_data.csv");
            //List<GPXData> dataPoints = ReadCSVcs.GetGPXDataFromCSVFile(@"d:\temp\EXCEL_160551_data.csv");
            //List<GPXData> dataPoints =  ReadCSVcs.GetGPXDataFromCSVFile(@"d:\temp\EXCEL_134919_data.csv");
            MAX_SKIP = 3;
            MAX_COUNT = 150;
            counter = 0;
           
            var points =
                from x in dataPoints select new {  p = x.MidPoint };
            string s1 = string.Empty;
            
           
            foreach (var item in points)
            {
                ++counter;
                if(counter%MAX_SKIP == 0)
                {
                    s1 += item.p.Position.Latitude + "," + item.p.Position.Longitude + "|";
                }
                if(counter > MAX_COUNT)
                {
                    break;
                }
            }
             s1 = s1.Remove(s1.Length-1, 1);


            _sps = Utils.GetSnapedRoad(s1);

            List<GPXData> snappedDataPoints = GetSnappedRoads(ProcessDataPoints(dataPoints));

            GPXData normalizedGPX;
            foreach (var item in snappedDataPoints)
            {
                GPXData neareastGpxPoint = GetNeareastGPXDataFromDB(collection, item);

                if (neareastGpxPoint == null)
                {
                    switch (item.RoadCondition)
                    {
                        case RoadType.Good:
                            item.L0++;
                            break;
                        case RoadType.SlightyBumpy:
                            break;
                        case RoadType.Bumpy:
                            item.L1++;
                            break;
                        case RoadType.Worst:
                            item.L1++;
                            break;
                        case RoadType.RandomAction:
                            break;
                        case RoadType.Idle:
                            break;
                        default:
                            break;
                    }
                    Document created = await client.CreateDocumentAsync(collection.SelfLink, item);
                    //add this gpx point
                    Console.WriteLine("CREATED - Condition:{0} L0:{1} L1:{2} ", item.RoadCondition.ToString(), item.L0.ToString(), item.L1.ToString());
                }
                else
                {
                    //if (neareastGpxPoint.RoadCondition == item.RoadCondition) continue;
                    normalizedGPX = NormalizeGPXPoint(neareastGpxPoint, item);
                    if(normalizedGPX == null)
                    {
                        Console.WriteLine("SKIP as same trip" + item.TripID);
                        continue;
                    }
                    var response = await client.ReplaceDocumentAsync(UriFactory.CreateDocumentUri(databaseId, collectionId, normalizedGPX.Id), normalizedGPX);
                    //update this gpx point
                    Console.WriteLine("UPDATED - Condition:{0} L0:{1} L1:{2} ", item.RoadCondition.ToString(), item.L0.ToString(), item.L1.ToString());

                    //if (normalizedGPX.RoadCondition == RoadType.Good)
                    //{
                    //    var response = await client.DeleteDocumentAsync(UriFactory.CreateDocumentUri(databaseId, collectionId, normalizedGPX.Id));
                    //    //delete the record
                    //    Console.WriteLine("DELETED - " + item.RoadCondition.ToString() + " - " + item.SnapPoint.Position.Latitude.ToString() + "," + item.SnapPoint.Position.Longitude.ToString());
                    //}
                    //else
                    //{
                    //    var response = await client.ReplaceDocumentAsync(UriFactory.CreateDocumentUri(databaseId, collectionId, normalizedGPX.Id), normalizedGPX);
                    //    //update this gpx point
                    //    Console.WriteLine("UPDATED - " + item.RoadCondition.ToString() + " - " + item.SnapPoint.Position.Latitude.ToString() + "," + item.SnapPoint.Position.Longitude.ToString());
                    //}
                }
            }

           

            //NOTE: In GeoJSON, longitude comes before latitude.
            //DocumentDB uses the WGS-84 coordinate reference standard.Longitudes are between - 180 and 180 degrees, and latitudes between - 90 and 90 degrees.
            //Console.WriteLine("Inserting some spatial data");
            //int count = 0;
            //foreach (var item in dataPoints)
            //{
            //    count++;
            //    if (count > 100)
            //    {
            //        break;
            //    }
            //    await client.CreateDocumentAsync(collection.SelfLink, item);
            //}

            // RunQ(collection);


            //Animal you = new Animal { Name = "you", Species = "Human", Location = new Point(31.9, -4.8) };
            //Animal dragon1 = new Animal { Name = "dragon1", Species = "Dragon", Location = new Point(31.87, -4.55) };
            //Animal dragon2 = new Animal { Name = "dragon2", Species = "Dragon", Location = new Point(32.33, -4.66) };


            // Insert documents with GeoJSON spatial data.
            //await client.CreateDocumentAsync(collection.SelfLink, you);
            //await client.CreateDocumentAsync(collection.SelfLink, dragon1);
            //await client.CreateDocumentAsync(collection.SelfLink, dragon2);

            // Check for points within a circle/radius relative to another point. Common for "What's near me?" queries.
            //RunDistanceQuery(collection, you.Location);

            // Check for points within a polygon. Cities/states/natural formations are all commonly represented as polygons.
            //RunWithinPolygonQuery(collection);

            // How to check for valid geospatial objects. Checks for valid latitude/longtiudes and if polygons are well-formed, etc.
            //CheckIfPointOrPolygonIsValid(collection);
        }

        /// <summary>
        /// Cleanup data from previous runs.
        /// </summary>
        /// <param name="collection">The DocumentDB collection.</param>
        /// <returns>The Task for asynchronous execution.</returns>
        private static async Task Cleanup(DocumentCollection collection)
        {
            Console.WriteLine("Cleaning up");
            foreach (Document d in await client.ReadDocumentFeedAsync(collection.SelfLink))
            {
                await client.DeleteDocumentAsync(d.SelfLink);
            }
        }
       
        private static GPXData GetNeareastGPXDataFromDB(DocumentCollection collection,GPXData orginGpx)
        {
            GPXData neareastGPX = null;
            foreach (GPXData gpxItem in client.CreateDocumentQuery<GPXData>(collection.SelfLink).Where(a => a.SnapPoint.Distance(orginGpx.SnapPoint) < 5))
            {
                neareastGPX = gpxItem;
            }
            return neareastGPX;
        }

        private static void RunQ(DocumentCollection collection)
        {
            Console.WriteLine("Performing a ST_DISTANCE proximity query in SQL");

            //var result = client.CreateDocumentQuery<dynamic>(collection.SelfLink,
            //       "SELECT e.EndPoint as distance " + // ST_DISTANCE(e.EndPoint,e.StartPoint) 
            //       "FROM everything e ");
            //foreach (var item in result)
            //{
            //    Console.WriteLine(item);
            //}

            Point p = new Point(78.3681344, 17.3920687);


            //var results1 = client.CreateDocumentQuery<GPXData>(collection.SelfLink).Where(a => a.StartPoint.Distance(p) > 1500).ToList();
            var results1 = client.CreateDocumentQuery<GPXData>(collection.SelfLink).Where(a => (a.StdDevYaw > 0.4) || (a.StdDevRoll > 0.4) || (a.StdDevPitch > 0.4)).ToList();

            foreach (GPXData animal in client.CreateDocumentQuery<GPXData>(collection.SelfLink).Where(a => a.StartPoint.Distance(a.EndPoint) > 50000))
            {
                Console.WriteLine("\t" + animal);
            }
           

            var results = client.CreateDocumentQuery<GPXData>(
                collection.SelfLink,
                new SqlQuerySpec
                {
                    QueryText = "SELECT * FROM everything e  WHERE ST_DISTANCE(e.startpoint, e.endpoint) > 50000"//,
                    //Parameters = new SqlParameterCollection(new[] { new SqlParameter { Name = "@me", Value = null  } })
                }).ToList<GPXData>();


            foreach (GPXData animal in client.CreateDocumentQuery<GPXData>(
                collection.SelfLink,
                new SqlQuerySpec
                {
                    QueryText = "SELECT * FROM everything e  WHERE ST_DISTANCE(e.startpoint, e.endpoint) > 10"//,
                    //Parameters = new SqlParameterCollection(new[] { new SqlParameter { Name = "@me", Value = null  } })
                }))
            {
                Console.WriteLine("\t" + animal);
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Run a distance query using SQL, LINQ and parameterized SQL.
        /// </summary>
        /// <param name="collection">The DocumentDB collection.</param>
        /// <param name="from">The position to measure distance from.</param>
        private static void RunDistanceQuery(DocumentCollection collection, Point from)
        {
            Console.WriteLine("Performing a ST_DISTANCE proximity query in SQL");

            // DocumentDB uses the WGS-84 coordinate reference system (CRS). In this reference system, distance is measured in meters. So 30km = 3000m.
            // There are several built-in SQL functions that follow the OGC naming standards and start with the "ST_" prefix for "spatial type".
            foreach (Animal animal in client.CreateDocumentQuery<Animal>(
                collection.SelfLink,
                "SELECT * FROM everything e WHERE e.species ='Dragon' AND ST_DISTANCE(e.location, {'type': 'Point', 'coordinates':[31.9, -4.8]}) < 30000"))
            {
                Console.WriteLine("\t" + animal);
            }

            Console.WriteLine();

            // Geometry.Distance is a stub method in the DocumentDB SDK that can be used within LINQ expressions to build spatial queries.
            Console.WriteLine("Performing a ST_DISTANCE proximity query in LINQ");
            foreach (Animal animal in client.CreateDocumentQuery<Animal>(collection.SelfLink).Where(a => a.Species == "Dragon" && a.Location.Distance(from) < 30000))
            {
                Console.WriteLine("\t" + animal);
            }

            Console.WriteLine();

            Console.WriteLine("Performing a ST_DISTANCE proximity query in parameterized SQL");
            foreach (Animal animal in client.CreateDocumentQuery<Animal>(
                collection.SelfLink,
                new SqlQuerySpec
                {
                    QueryText = "SELECT * FROM everything e WHERE e.species ='Dragon' AND ST_DISTANCE(e.location, @me) < 30000",
                    Parameters = new SqlParameterCollection(new[] { new SqlParameter { Name = "@me", Value = from } })
                }))
            {
                Console.WriteLine("\t" + animal);
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Run a within query (get points within a box/polygon) using SQL and LINQ.
        /// </summary>
        /// <param name="collection">The DocumentDB collection.</param>
        private static void RunWithinPolygonQuery(DocumentCollection collection)
        {
            Console.WriteLine("Performing a ST_WITHIN proximity query in SQL");

            foreach (Animal animal in client.CreateDocumentQuery<Animal>(
                collection.SelfLink,
                "SELECT * FROM everything e WHERE ST_WITHIN(e.location, {'type':'Polygon', 'coordinates': [[[31.8, -5], [32, -5], [32, -4.7], [31.8, -4.7], [31.8, -5]]]})"))
            {
                Console.WriteLine("\t" + animal);
            }

            Console.WriteLine();
            Console.WriteLine("Performing a ST_WITHIN proximity query in LINQ");

            foreach (Animal animal in client.CreateDocumentQuery<Animal>(collection.SelfLink)
                .Where(a => a.Location.Within(new Polygon(new[] { new LinearRing(new[] { new Position(31.8, -5), new Position(32, -5), new Position(32, -4.7), new Position(31.8, -4.7), new Position(31.8, -5) }) }))))
            {
                Console.WriteLine("\t" + animal);
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Check if a point or polygon is valid using built-in functions. An important thing to note is that since DocumentDB's query is designed to handle heterogeneous types, 
        /// bad input parameters will evaluate to "undefined" and get skipped over instead of returning an error. For debugging and fixing malformed geospatial objects, please 
        /// use the built-in functions shown below.
        /// </summary>
        /// <param name="collection">The DocumentDB collection.</param>
        private static void CheckIfPointOrPolygonIsValid(DocumentCollection collection)
        {
            Console.WriteLine("Checking if a point is valid ...");

            // Here we pass a latitude that's invalid (they can be only between -90 and 90 degrees).
            QueryScalar(
                collection.SelfLink,
                new SqlQuerySpec
                {
                    QueryText = "SELECT ST_ISVALID(@point), ST_ISVALIDDETAILED(@point)",
                    Parameters = new SqlParameterCollection(new[] { new SqlParameter { Name = "@point", Value = new Point(31.9, -132.8) } })
                });

            // Here we pass a polygon that's not closed. GeoJSON and DocumentDB require that polygons must include the first point repeated at the end.
            // DocumentDB does not support polygons with holes within queries, so a polygon used in a query must have only a single LinearRing.
            Console.WriteLine("Checking if a polygon is valid ...");
            QueryScalar(
                collection.SelfLink,
                new SqlQuerySpec
                {
                    QueryText = "SELECT ST_ISVALID(@polygon), ST_ISVALIDDETAILED(@polygon)",
                    Parameters = new SqlParameterCollection(new[]
                     {
                         new SqlParameter
                         {
                             Name = "@polygon",
                             Value = new Polygon(new[]
                             {
                                 new LinearRing(new[]
                                 {
                                     new Position(31.8, -5), new Position(32, -5), new Position(32, -4.7), new Position(31.8, -4.7)
                                 })
                             })
                         }
                     })
                });
        }

        /// <summary>
        /// Get a Database for this id. Delete if it already exists.
        /// </summary>
        /// <param name="id">The id of the Database to create.</param>
        /// <returns>The created Database object</returns>
        private static async Task<Database> GetDatabaseAsync(string id)
        {
            Database database = client.CreateDatabaseQuery().Where(c => c.Id == id).ToArray().FirstOrDefault();
            if (database != null)
            {
                return database;
            }

            Console.WriteLine("Creating new database...");
            database = await client.CreateDatabaseAsync(new Database { Id = id });
            return database;
        }

        /// <summary>
        /// Get a DocumentCollection by id, or create a new one if one with the id provided doesn't exist. 
        /// If it exists, update the indexing policy to use string range and spatial indexes.
        /// </summary>
        /// <param name="databaseLink">The database self-link to use.</param>
        /// <param name="id">The id of the DocumentCollection to search for, or create.</param>
        /// <returns>The matched, or created, DocumentCollection object</returns>
        private static async Task<DocumentCollection> GetCollectionWithSpatialIndexingAsync(string databaseLink, string id)
        {
            DocumentCollection collection = client.CreateDocumentCollectionQuery(databaseLink).Where(c => c.Id == id).ToArray().FirstOrDefault();

            if (collection == null)
            {
                DocumentCollection collectionDefinition = new DocumentCollection { Id = id };
                collectionDefinition.IndexingPolicy = IndexingPolicyWithSpatialEnabled;

                Console.WriteLine("Creating new collection...");
                collection = await client.CreateDocumentCollectionAsync(databaseLink, collectionDefinition);
            }
            else
            {
                await ModifyCollectionWithSpatialIndexing(collection, IndexingPolicyWithSpatialEnabled);
            }

            return collection;
        }

        /// <summary>
        /// Modify a collection to use spatial indexing policy and wait for it to complete.
        /// </summary>
        /// <param name="collection">The DocumentDB collection.</param>
        /// <param name="indexingPolicy">The indexing policy to use.</param>
        /// <returns>The Task for asynchronous execution.</returns>
        private static async Task ModifyCollectionWithSpatialIndexing(DocumentCollection collection, IndexingPolicy indexingPolicy)
        {
            Console.WriteLine("Updating collection with spatial indexing enabled in indexing policy...");

            collection.IndexingPolicy = indexingPolicy;
            await client.ReplaceDocumentCollectionAsync(collection);

            Console.WriteLine("waiting for indexing to complete...");

            long indexTransformationProgress = 0;

            while (indexTransformationProgress < 100)
            {
                ResourceResponse<DocumentCollection> response = await client.ReadDocumentCollectionAsync(collection.SelfLink);
                indexTransformationProgress = response.IndexTransformationProgress;

                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        /// <summary>
        /// Run a query that returns a single document, and display it
        /// </summary>
        /// <param name="collectionLink">The collection self-link</param>
        /// <param name="query">The query to run</param>
        private static void QueryScalar(string collectionLink, SqlQuerySpec query)
        {
            dynamic result = client.CreateDocumentQuery(collectionLink, query).AsDocumentQuery().ExecuteNextAsync().Result.First();
            Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.None));
        }

        /// <summary>
        /// Register a user defined function to extend geospatial functionality, e.g. introduce ST_AREA for calculating the area of a polygon.
        /// </summary>
        /// <param name="collection">The DocumentDB collection.</param>
        /// <returns>The Task for asynchronous execution.</returns>
        private static async Task RegisterAreaUserDefinedFunction(DocumentCollection collection)
        {
            string areaJavaScriptBody = File.ReadAllText(@"STArea.js");

            UserDefinedFunction areaUserDefinedFunction = client.CreateUserDefinedFunctionQuery(collection.SelfLink).Where(u => u.Id == "ST_AREA").AsEnumerable().FirstOrDefault();

            if (areaUserDefinedFunction == null)
            {
                await client.CreateUserDefinedFunctionAsync(
                    collection.SelfLink,
                    new UserDefinedFunction
                    {
                        Id = "ST_AREA",
                        Body = areaJavaScriptBody
                    });
            }
            else
            {
                areaUserDefinedFunction.Body = areaJavaScriptBody;
                await client.ReplaceUserDefinedFunctionAsync(areaUserDefinedFunction);
            }
        }

        /// <summary>
        /// Describes an animal.
        /// </summary>
        internal class Animal
        {
            /// <summary>
            /// Gets or sets the name of the animal.
            /// </summary>
            [JsonProperty("name")]
            public string Name { get; set; }

            /// <summary>
            /// Gets or sets the species of the animal.
            /// </summary>
            [JsonProperty("species")]
            public string Species { get; set; }

            /// <summary>
            /// Gets or sets the location of the animal.
            /// </summary>
            [JsonProperty("location")]
            public Point Location { get; set; }



            /// <summary>
            /// Returns the JSON string representation.
            /// </summary>
            /// <returns>The string representation.</returns>
            public override string ToString()
            {
                return JsonConvert.SerializeObject(this, Formatting.None);
            }
        }
    }
}
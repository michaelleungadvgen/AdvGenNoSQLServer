// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.Geospatial;

namespace AdvGenNoSqlServer.Example.ConsoleApp
{
    /// <summary>
    /// Geospatial query examples demonstrating location-based data operations.
    /// 
    /// Features demonstrated:
    /// - Creating geospatial indexes
    /// - Finding nearby locations ($near query)
    /// - Finding locations within a bounding box ($withinBox query)
    /// - Finding locations within a circle ($withinCircle query)
    /// - Finding locations within a polygon ($withinPolygon query)
    /// - Distance calculations
    /// </summary>
    public static class GeospatialExamples
    {
        /// <summary>
        /// Run all geospatial examples.
        /// </summary>
        public static async Task RunAllExamples()
        {
            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("  GEOSPATIAL EXAMPLES");
            Console.WriteLine(new string('═', 60));

            await Example1_BasicGeospatialIndex();
            await Example2_FindNearbyLocations();
            await Example3_FindWithinBoundingBox();
            await Example4_FindWithinCircle();
            await Example5_FindWithinPolygon();
            await Example6_DeliveryServiceScenario();
        }

        /// <summary>
        /// Example 1: Creating a geospatial index on location data.
        /// </summary>
        static async Task Example1_BasicGeospatialIndex()
        {
            Console.WriteLine("\n📍 Example 1: Basic Geospatial Index");
            Console.WriteLine("-".PadRight(50, '-'));

            // Create a document store with geospatial support
            var innerStore = new DocumentStore();
            var geoStore = new GeospatialDocumentStore(innerStore);

            // Insert some sample locations (coffee shops)
            var coffeeShops = new[]
            {
                new Document
                {
                    Id = "shop1",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "Central Perk",
                        ["type"] = "Coffee Shop",
                        ["location"] = new[] { -74.006, 40.7128 }, // New York City
                        ["rating"] = 4.5
                    }
                },
                new Document
                {
                    Id = "shop2",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "Blue Bottle",
                        ["type"] = "Coffee Shop",
                        ["location"] = new[] { -118.2437, 34.0522 }, // Los Angeles
                        ["rating"] = 4.8
                    }
                },
                new Document
                {
                    Id = "shop3",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "Intelligentsia",
                        ["type"] = "Coffee Shop",
                        ["location"] = new[] { -87.6298, 41.8781 }, // Chicago
                        ["rating"] = 4.6
                    }
                }
            };

            foreach (var shop in coffeeShops)
            {
                await geoStore.InsertAsync("shops", shop);
            }

            Console.WriteLine($"✓ Inserted {coffeeShops.Length} coffee shops");

            // Create a geospatial index on the 'location' field
            var index = geoStore.CreateGeospatialIndex("shops", "location");
            Console.WriteLine($"✓ Created geospatial index on 'shops.location'");

            // Get index statistics
            var stats = index.GetStats();
            Console.WriteLine($"  - Indexed documents: {stats.TotalDocuments}");
            Console.WriteLine($"  - Field: {stats.FieldName}");
            Console.WriteLine($"  - Collection: {stats.CollectionName}");
            Console.WriteLine($"  - Last updated: {stats.LastUpdated:HH:mm:ss}");

            if (stats.BoundingBox.HasValue)
            {
                var bbox = stats.BoundingBox.Value;
                Console.WriteLine($"  - Bounding box: [{bbox.MinLongitude:F2}, {bbox.MinLatitude:F2}] to [{bbox.MaxLongitude:F2}, {bbox.MaxLatitude:F2}]");
            }
        }

        /// <summary>
        /// Example 2: Finding nearby locations using $near query.
        /// </summary>
        static async Task Example2_FindNearbyLocations()
        {
            Console.WriteLine("\n📍 Example 2: Find Nearby Locations ($near)");
            Console.WriteLine("-".PadRight(50, '-'));

            // Create store with sample restaurant data
            var innerStore = new DocumentStore();
            var geoStore = new GeospatialDocumentStore(innerStore);

            // Insert restaurants in San Francisco area
            var restaurants = new[]
            {
                new Document
                {
                    Id = "rest1",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "The French Laundry",
                        ["cuisine"] = "French",
                        ["location"] = new[] { -122.56, 38.29 }, // Yountville
                        ["price"] = "$$$$"
                    }
                },
                new Document
                {
                    Id = "rest2",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "In-N-Out Burger",
                        ["cuisine"] = "Burgers",
                        ["location"] = new[] { -122.42, 37.78 }, // San Francisco
                        ["price"] = "$"
                    }
                },
                new Document
                {
                    Id = "rest3",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "Chez Panisse",
                        ["cuisine"] = "California",
                        ["location"] = new[] { -122.27, 37.86 }, // Berkeley
                        ["price"] = "$$$"
                    }
                },
                new Document
                {
                    Id = "rest4",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "Ghirardelli Square",
                        ["cuisine"] = "Dessert",
                        ["location"] = new[] { -122.42, 37.81 }, // San Francisco
                        ["price"] = "$$"
                    }
                },
                new Document
                {
                    Id = "rest5",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "Fisherman's Wharf",
                        ["cuisine"] = "Seafood",
                        ["location"] = new[] { -122.42, 37.81 }, // San Francisco
                        ["price"] = "$$"
                    }
                }
            };

            foreach (var restaurant in restaurants)
            {
                await geoStore.InsertAsync("restaurants", restaurant);
            }

            // Create geospatial index
            geoStore.CreateGeospatialIndex("restaurants", "location");
            Console.WriteLine($"✓ Inserted {restaurants.Length} restaurants");

            // Find restaurants near Union Square, San Francisco
            var unionSquare = new GeoPoint(-122.41, 37.79);
            var maxDistanceKm = 5.0;

            Console.WriteLine($"\n🔍 Finding restaurants within {maxDistanceKm}km of Union Square, SF");
            Console.WriteLine($"   Coordinates: {unionSquare}");

            var nearby = geoStore.FindNear("restaurants", "location", unionSquare, maxDistanceKm, new GeospatialQueryOptions
            {
                DistanceUnit = DistanceUnit.Kilometers,
                IncludeDistance = true,
                SortByDistance = true,
                Limit = 10
            }).ToList();

            Console.WriteLine($"\n   Found {nearby.Count} restaurants:\n");

            foreach (var result in nearby)
            {
                var doc = await geoStore.GetAsync("restaurants", result.DocumentId);
                if (doc != null)
                {
                    var name = doc.Data["name"];
                    var cuisine = doc.Data["cuisine"];
                    var price = doc.Data["price"];
                    var distance = result.Distance;

                    Console.WriteLine($"   📍 {name}");
                    Console.WriteLine($"      Cuisine: {cuisine}, Price: {price}");
                    Console.WriteLine($"      Distance: {distance:F2} km, Location: {result.Location}");
                    Console.WriteLine();
                }
            }
        }

        /// <summary>
        /// Example 3: Finding locations within a bounding box.
        /// </summary>
        static async Task Example3_FindWithinBoundingBox()
        {
            Console.WriteLine("\n📍 Example 3: Find Within Bounding Box ($withinBox)");
            Console.WriteLine("-".PadRight(50, '-'));

            var innerStore = new DocumentStore();
            var geoStore = new GeospatialDocumentStore(innerStore);

            // Insert airports across the US
            var airports = new[]
            {
                new Document { Id = "jfk", Data = new Dictionary<string, object> { ["name"] = "JFK Airport", ["city"] = "New York", ["location"] = new[] { -73.78, 40.64 } } },
                new Document { Id = "lax", Data = new Dictionary<string, object> { ["name"] = "LAX Airport", ["city"] = "Los Angeles", ["location"] = new[] { -118.41, 33.94 } } },
                new Document { Id = "ord", Data = new Dictionary<string, object> { ["name"] = "O'Hare Airport", ["city"] = "Chicago", ["location"] = new[] { -87.90, 41.97 } } },
                new Document { Id = "mia", Data = new Dictionary<string, object> { ["name"] = "Miami Airport", ["city"] = "Miami", ["location"] = new[] { -80.29, 25.79 } } },
                new Document { Id = "sea", Data = new Dictionary<string, object> { ["name"] = "Seattle Airport", ["city"] = "Seattle", ["location"] = new[] { -122.31, 47.45 } } },
                new Document { Id = "den", Data = new Dictionary<string, object> { ["name"] = "Denver Airport", ["city"] = "Denver", ["location"] = new[] { -104.67, 39.86 } } },
                new Document { Id = "bos", Data = new Dictionary<string, object> { ["name"] = "Boston Airport", ["city"] = "Boston", ["location"] = new[] { -71.01, 42.37 } } },
                new Document { Id = "atl", Data = new Dictionary<string, object> { ["name"] = "Atlanta Airport", ["city"] = "Atlanta", ["location"] = new[] { -84.43, 33.64 } } }
            };

            foreach (var airport in airports)
            {
                await geoStore.InsertAsync("airports", airport);
            }

            geoStore.CreateGeospatialIndex("airports", "location");
            Console.WriteLine($"✓ Inserted {airports.Length} airports");

            // Define a bounding box for the Northeast US
            // Bottom-left: near Washington DC
            // Top-right: near Boston
            var boundingBox = new GeoBoundingBox(
                new GeoPoint(-77.0, 38.9),   // Washington DC
                new GeoPoint(-70.0, 43.0)    // Near Boston
            );

            Console.WriteLine($"\n🔍 Finding airports in Northeast US bounding box:");
            Console.WriteLine($"   Bottom-left: [-77.0, 38.9] (Washington DC)");
            Console.WriteLine($"   Top-right: [-70.0, 43.0] (Boston area)");

            var results = geoStore.FindWithinBox("airports", "location", boundingBox).ToList();

            Console.WriteLine($"\n   Found {results.Count} airports:\n");

            foreach (var doc in results)
            {
                var name = doc.Data["name"];
                var city = doc.Data["city"];
                Console.WriteLine($"   ✈️  {name} ({city})");
            }
        }

        /// <summary>
        /// Example 4: Finding locations within a circle.
        /// </summary>
        static async Task Example4_FindWithinCircle()
        {
            Console.WriteLine("\n📍 Example 4: Find Within Circle ($withinCircle)");
            Console.WriteLine("-".PadRight(50, '-'));

            var innerStore = new DocumentStore();
            var geoStore = new GeospatialDocumentStore(innerStore);

            // Insert tech company headquarters
            var companies = new[]
            {
                new Document { Id = "apple", Data = new Dictionary<string, object> { ["name"] = "Apple", ["location"] = new[] { -122.03, 37.33 } } },      // Cupertino
                new Document { Id = "google", Data = new Dictionary<string, object> { ["name"] = "Google", ["location"] = new[] { -122.08, 37.42 } } },    // Mountain View
                new Document { Id = "meta", Data = new Dictionary<string, object> { ["name"] = "Meta", ["location"] = new[] { -122.15, 37.48 } } },       // Menlo Park
                new Document { Id = "netflix", Data = new Dictionary<string, object> { ["name"] = "Netflix", ["location"] = new[] { -122.33, 37.93 } } },  // Los Gatos
                new Document { Id = "microsoft", Data = new Dictionary<string, object> { ["name"] = "Microsoft", ["location"] = new[] { -122.13, 47.64 } } }, // Seattle
                new Document { Id = "amazon", Data = new Dictionary<string, object> { ["name"] = "Amazon", ["location"] = new[] { -122.33, 47.61 } } },   // Seattle
                new Document { Id = "tesla", Data = new Dictionary<string, object> { ["name"] = "Tesla", ["location"] = new[] { -122.06, 37.41 } } }      // Palo Alto
            };

            foreach (var company in companies)
            {
                await geoStore.InsertAsync("companies", company);
            }

            geoStore.CreateGeospatialIndex("companies", "location");
            Console.WriteLine($"✓ Inserted {companies.Length} tech companies");

            // Find companies within 25km of Stanford University
            var stanford = new GeoPoint(-122.17, 37.43);
            var searchRadius = new GeoCircle(stanford, 25, DistanceUnit.Kilometers);

            Console.WriteLine($"\n🔍 Finding tech companies within 25km of Stanford University");
            Console.WriteLine($"   Center: {stanford}");

            var results = geoStore.FindWithinCircle("companies", "location", searchRadius, new GeospatialQueryOptions
            {
                DistanceUnit = DistanceUnit.Kilometers,
                IncludeDistance = true,
                SortByDistance = true
            }).ToList();

            Console.WriteLine($"\n   Found {results.Count} companies:\n");

            foreach (var result in results)
            {
                var doc = await geoStore.GetAsync("companies", result.DocumentId);
                if (doc != null)
                {
                    var name = doc.Data["name"];
                    var distance = result.Distance;
                    Console.WriteLine($"   🏢 {name,-12} - {distance:F1} km away");
                }
            }

            // Also show companies outside the radius
            var allDocs = await geoStore.GetAllAsync("companies");
            var outsideRadius = allDocs.Where(d => !results.Any(r => r.DocumentId == d.Id)).ToList();
            Console.WriteLine($"\n   Companies outside 25km radius: {outsideRadius.Count}");
            foreach (var doc in outsideRadius)
            {
                var name = doc.Data["name"];
                Console.WriteLine($"   🏢 {name,-12} - Outside search radius");
            }
        }

        /// <summary>
        /// Example 5: Finding locations within a polygon.
        /// </summary>
        static async Task Example5_FindWithinPolygon()
        {
            Console.WriteLine("\n📍 Example 5: Find Within Polygon ($withinPolygon)");
            Console.WriteLine("-".PadRight(50, '-'));

            var innerStore = new DocumentStore();
            var geoStore = new GeospatialDocumentStore(innerStore);

            // Insert landmarks in the UK
            var landmarks = new[]
            {
                new Document { Id = "bigben", Data = new Dictionary<string, object> { ["name"] = "Big Ben", ["city"] = "London", ["location"] = new[] { -0.12, 51.50 } } },
                new Document { Id = "tower", Data = new Dictionary<string, object> { ["name"] = "Tower of London", ["city"] = "London", ["location"] = new[] { -0.08, 51.51 } } },
                new Document { Id = "edin", Data = new Dictionary<string, object> { ["name"] = "Edinburgh Castle", ["city"] = "Edinburgh", ["location"] = new[] { -3.20, 55.95 } } },
                new Document { Id = "stonehenge", Data = new Dictionary<string, object> { ["name"] = "Stonehenge", ["city"] = "Wiltshire", ["location"] = new[] { -1.83, 51.18 } } },
                new Document { Id = "oxford", Data = new Dictionary<string, object> { ["name"] = "Oxford University", ["city"] = "Oxford", ["location"] = new[] { -1.25, 51.75 } } },
                new Document { Id = "manchester", Data = new Dictionary<string, object> { ["name"] = "Manchester Town Hall", ["city"] = "Manchester", ["location"] = new[] { -2.24, 53.48 } } },
                new Document { Id = "cardiff", Data = new Dictionary<string, object> { ["name"] = "Cardiff Castle", ["city"] = "Cardiff", ["location"] = new[] { -3.18, 51.48 } } },
                new Document { Id = "belfast", Data = new Dictionary<string, object> { ["name"] = "Belfast City Hall", ["city"] = "Belfast", ["location"] = new[] { -5.93, 54.60 } } }
            };

            foreach (var landmark in landmarks)
            {
                await geoStore.InsertAsync("landmarks", landmark);
            }

            geoStore.CreateGeospatialIndex("landmarks", "location");
            Console.WriteLine($"✓ Inserted {landmarks.Length} UK landmarks");

            // Define a polygon covering southern England
            // (roughly a pentagon around London and surrounding areas)
            var southernEnglandPolygon = new GeoPolygon(
                new GeoPoint(-2.0, 52.5),   // West
                new GeoPoint(-0.5, 52.5),   // North-West
                new GeoPoint(0.5, 51.8),    // North-East
                new GeoPoint(0.0, 51.0),    // South-East
                new GeoPoint(-2.0, 51.0)    // South-West
            );

            Console.WriteLine($"\n🔍 Finding landmarks within Southern England polygon");

            var results = geoStore.FindWithinPolygon("landmarks", "location", southernEnglandPolygon).ToList();

            Console.WriteLine($"   Found {results.Count} landmarks in Southern England:\n");

            foreach (var doc in results)
            {
                var name = doc.Data["name"];
                var city = doc.Data["city"];
                Console.WriteLine($"   🏛️  {name} ({city})");
            }

            // Show landmarks outside the polygon
            var allDocs = await geoStore.GetAllAsync("landmarks");
            var outside = allDocs.Where(d => !results.Any(r => r.Id == d.Id)).ToList();
            Console.WriteLine($"\n   Landmarks outside Southern England: {outside.Count}");
            foreach (var doc in outside)
            {
                var name = doc.Data["name"];
                var city = doc.Data["city"];
                Console.WriteLine($"   🏛️  {name} ({city})");
            }
        }

        /// <summary>
        /// Example 6: Real-world scenario - Delivery service finding nearby restaurants.
        /// </summary>
        static async Task Example6_DeliveryServiceScenario()
        {
            Console.WriteLine("\n📍 Example 6: Delivery Service Scenario");
            Console.WriteLine("-".PadRight(50, '-'));

            var innerStore = new DocumentStore();
            var geoStore = new GeospatialDocumentStore(innerStore);

            // Simulate a food delivery service in downtown San Francisco
            Console.WriteLine("\n🚚 Scenario: Food delivery service in downtown San Francisco");
            Console.WriteLine("   Customer wants food delivered to their office\n");

            // Insert restaurants in downtown SF
            var restaurants = new[]
            {
                new Document
                {
                    Id = "r1",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "Tony's Pizza",
                        ["cuisine"] = "Italian",
                        ["rating"] = 4.5,
                        ["delivery_time_min"] = 25,
                        ["location"] = new[] { -122.40, 37.79 }
                    }
                },
                new Document
                {
                    Id = "r2",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "Golden Dragon",
                        ["cuisine"] = "Chinese",
                        ["rating"] = 4.2,
                        ["delivery_time_min"] = 35,
                        ["location"] = new[] { -122.41, 37.79 }
                    }
                },
                new Document
                {
                    Id = "r3",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "Burger Palace",
                        ["cuisine"] = "American",
                        ["rating"] = 4.0,
                        ["delivery_time_min"] = 20,
                        ["location"] = new[] { -122.40, 37.80 }
                    }
                },
                new Document
                {
                    Id = "r4",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "Sushi Master",
                        ["cuisine"] = "Japanese",
                        ["rating"] = 4.8,
                        ["delivery_time_min"] = 40,
                        ["location"] = new[] { -122.42, 37.78 }
                    }
                },
                new Document
                {
                    Id = "r5",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "Taco Express",
                        ["cuisine"] = "Mexican",
                        ["rating"] = 4.3,
                        ["delivery_time_min"] = 15,
                        ["location"] = new[] { -122.39, 37.79 }
                    }
                },
                new Document
                {
                    Id = "r6",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = "Curry House",
                        ["cuisine"] = "Indian",
                        ["rating"] = 4.4,
                        ["delivery_time_min"] = 45,
                        ["location"] = new[] { -122.43, 37.77 } // Further away
                    }
                }
            };

            foreach (var restaurant in restaurants)
            {
                await geoStore.InsertAsync("restaurants", restaurant);
            }

            geoStore.CreateGeospatialIndex("restaurants", "location");
            Console.WriteLine($"✓ {restaurants.Length} restaurants in the database");

            // Customer location (Union Square, SF)
            var customerLocation = new GeoPoint(-122.41, 37.79);
            Console.WriteLine($"\n📍 Customer location: Union Square {customerLocation}");

            // Find restaurants within 1km (walking/delivery distance)
            var maxDeliveryDistance = 1.0; // 1km
            var nearby = geoStore.FindNear("restaurants", "location", customerLocation, maxDeliveryDistance, new GeospatialQueryOptions
            {
                DistanceUnit = DistanceUnit.Kilometers,
                IncludeDistance = true,
                SortByDistance = true
            }).ToList();

            Console.WriteLine($"\n🍽️  Restaurants within {maxDeliveryDistance}km (sorted by distance):\n");
            Console.WriteLine("   {0,-18} {1,-10} {2,-8} {3,-10} {4,-10}", "Name", "Cuisine", "Rating", "Est.Time", "Distance");
            Console.WriteLine($"   {new string('-', 60)}");

            foreach (var result in nearby)
            {
                var doc = await geoStore.GetAsync("restaurants", result.DocumentId);
                if (doc != null)
                {
                    var name = doc.Data["name"].ToString()!;
                    var cuisine = doc.Data["cuisine"].ToString()!;
                    var rating = Convert.ToDouble(doc.Data["rating"]);
                    var deliveryTime = Convert.ToInt32(doc.Data["delivery_time_min"]);
                    var distance = result.Distance;

                    // Truncate long names
                    name = name.Length > 16 ? name[..16] : name;

                    Console.WriteLine($"   {name,-18} {cuisine,-10} {rating,-8:F1} {deliveryTime + " min",-10} {distance * 1000:F0}m");
                }
            }

            // Calculate statistics
            var avgDistance = nearby.Average(r => r.Distance) * 1000; // in meters
            var avgRating = nearby.Select(async r =>
            {
                var d = await geoStore.GetAsync("restaurants", r.DocumentId);
                return d != null ? Convert.ToDouble(d.Data["rating"]) : 0;
            }).Select(t => t.Result).Average();

            Console.WriteLine($"\n📊 Delivery Zone Statistics:");
            Console.WriteLine($"   - Restaurants available: {nearby.Count}");
            Console.WriteLine($"   - Average distance: {avgDistance:F0}m");
            Console.WriteLine($"   - Average rating: {avgRating:F1} ⭐");
            Console.WriteLine($"   - Fastest delivery: {nearby.Select(async r =>
            {
                var d = await geoStore.GetAsync("restaurants", r.DocumentId);
                return d != null ? Convert.ToInt32(d.Data["delivery_time_min"]) : 999;
            }).Select(t => t.Result).Min()} min");
        }
    }
}

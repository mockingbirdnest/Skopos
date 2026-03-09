using Microsoft.VisualStudio.TestTools.UnitTesting;
using RealAntennas;
using RealAntennas.Antenna;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace σκοπός {
  [TestClass]
  public class AStarTesting {
    [TestInitialize]
    public void Initialize() {
      BandInfo.All["C"] = new RealAntennas.Antenna.BandInfo {
        name = "C",
        TechLevel = 7,
        Frequency = 6e9f,
        ChannelWidth = 4e9f, };
      BandInfo.initialized = true;
      TechLevelInfo.initialized = true;
      TechLevelInfo.All[0] = new TechLevelInfo {
        name = "TL0",
        Level = 0,
        Description = "WW2-era",
        PowerEfficiency = 0.0555f,
        ReflectorEfficiency = 0.5f,
        MinDataRate = 4,
        MaxDataRate = 4,
        MaxPower = 20,
        MassPerWatt = 1.6f,
        BaseMass = 1,
        BasePower = 2,
        BaseCost = 2,
        CostPerWatt = 5,
        ReceiverNoiseTemperature = 27000
      };
      TechLevelInfo.All[7] = new TechLevelInfo {
        name = "TL7",
        Level = 7,
        Description = "High Data Rate Comms, 1976-1980 [...]",
        PowerEfficiency = 0.3f,
        ReflectorEfficiency = 0.64f,
        MinDataRate = 16,
        MaxDataRate = 262144,
        MaxPower = 46,
        MassPerWatt = 0.6f,
        BaseMass = 21.3f,
        BasePower = 18.3f,
        BaseCost = 125,
        CostPerWatt = 1.2f,
        ReceiverNoiseTemperature = 1100
      };
      TechLevelInfo.MaxTL = 9;
      Encoder.All["Concatenated Reed-Solomon,Convolutional"] = new Encoder {
        name = "Concatenated Reed-Solomon,Convolutional",
        TechLevel = 7,
        CodingRate = 0.43725f,
        RequiredEbN0 = 3.3f
      };
      Encoder.initialized = true;

      
      filler_antenna = new ConfigNode();
      filler_antenna.AddValue("TechLevel", "7");
      filler_antenna.AddValue("RFBand", "C");
      filler_antenna.AddValue("referenceGain", "37.5");
      filler_antenna.AddValue("referenceFrequency", "4768");
      filler_antenna.AddValue("TxPower", "120");
      filler_antenna.AddValue("AMWTemp", "80"); // Taken from Yuzhno-Sakhalinsk. Should be RX only, but whatever.

      routing_astar = new Routing();
      routing_astar.use_apsp_heuristic = true;

      routing_dijkstras = new Routing();
      routing_dijkstras.use_apsp_heuristic = false;
    }

    RACommNode MakeNode(string name, double x, double y, double z) {
      RACommNode node = new RACommNode();
      node.name = name;
      node.precisePosition = new Vector3d(x, y, z);
      node.RAAntennaList = new List<RealAntenna>();
      RealAntennaDigital antenna = new RealAntennaDigital($"{name} C-band horn");
      antenna.LoadFromConfigNode(filler_antenna);
      node.RAAntennaList.Add(antenna);
      RealAntennaDigital antenna2 = new RealAntennaDigital($"{name} C-band horn");
      antenna2.LoadFromConfigNode(filler_antenna);
      node.RAAntennaList.Add(antenna2); // Everyone gets 2 antennas! The second one is for inter-satellite links.
      nodes.Add(name, node);
      return node;
    }

    RACommNode MakeNodeLatLong(string name, double lat, double lon, double height) {
      // I don't know the KSP coordinate system so I'm going to use ECEF (https://en.wikipedia.org/wiki/Earth-centered,_Earth-fixed_coordinate_system)
      // +x towards 0lat 0long
      // -x towards 0lat 180long
      // +y towards 0lat 90long
      // -y towards 0lat -90long
      // +z towards north pole
      // -z towards south pole
      double r = height / 1000 + RADIUS; // unit is km
      double x = r * Math.Sin((90-lat)*Math.PI/180) * Math.Cos(lon*Math.PI/180);
      double y = r * Math.Sin((90-lat)*Math.PI/180) * Math.Sin(lon*Math.PI/180);
      double z = r * Math.Cos((90-lat)*Math.PI/180);
      return MakeNode(name, x, y, z);
    }

    RACommNode MakeNodeOrbit(string name, double period, double ecc, double inc_deg, double argp_deg, double lan_deg, double mean_anomaly_deg) {
      // https://downloads.rene-schwarz.com/download/M001-Keplerian_Orbit_Elements_to_Cartesian_State_Vectors.pdf
      double mean_anomaly = mean_anomaly_deg * Math.PI / 180; 
      double argp = argp_deg * Math.PI / 180;
      double lan = lan_deg * Math.PI / 180;
      double inc = inc_deg * Math.PI / 180;
      double sma = SMAFromPeriod(period); // kilometres
      double E = EccentricAnomaly(ecc, mean_anomaly);
      double true_anomaly = 2 * Math.Atan2(Math.Sqrt(1 + ecc) * Math.Sin(E / 2), Math.Sqrt(1 + ecc) * Math.Cos(E / 2));
      double distance = sma * (1 - ecc * Math.Cos(E));
      double orbit_x = distance * Math.Cos(true_anomaly);
      double orbit_y = distance * Math.Sin(true_anomaly);

      // rotate to our coordinates D:
      double sin_w = Math.Sin(argp);
      double cos_w = Math.Cos(argp);
      double sin_om = Math.Sin(lan);
      double cos_om = Math.Cos(lan);
      double sin_i = Math.Sin(inc);
      double cos_i = Math.Cos(inc);

      double x = orbit_x * (cos_w * cos_om - sin_w * cos_i * sin_om) - orbit_y * (sin_w * cos_om + cos_w * cos_i * sin_om);
      double y = orbit_x * (cos_w * sin_om - sin_w * cos_i * cos_om) + orbit_y * (cos_w * cos_i * cos_om - sin_w * sin_om);
      double z = orbit_x * sin_w * sin_i + orbit_y * cos_w * sin_i;

      return MakeNode($"SAT_{name}", x, y, z);
    }

    private void LoadTestStations() {
      // Loads test stations from CSV. Consists of *almost* every station currently configured in telecom.cfg. I think I'm missing two.

      string path = "./test_stations.csv";
      Assert.IsTrue(File.Exists(path));
      using (StreamReader sr = File.OpenText(path)) {
        string s;
        while ((s = sr.ReadLine()) != null) {
          string[] parts = s.Split(',');
          Assert.AreEqual(3, parts.Length);
          string name = parts[0];
          double lat = double.Parse(parts[1]);
          double lon = double.Parse(parts[2]);
          MakeNodeLatLong(name, lat, lon, 0);
        }
      }
    }

    double SMAFromPeriod(double period) {
      // returns in kilometers
      const double mu = 3.9860043543609598e14;
      return Math.Pow(mu * Math.Pow(period / 2 / Math.PI, 2), 1.0 / 3) / 1000;
    }


    double EccentricAnomaly(double ecc, double mean_anomaly) {
      // Newton-Raphson
      const double epsilon = 1e-12;
      const int max_iters = 100;
      double estimate = mean_anomaly;
      double prev_estimate = double.PositiveInfinity;
      int iters_left = max_iters;
      while (iters_left > 0 && Math.Abs(prev_estimate - estimate) < epsilon) {
        prev_estimate = estimate;
        estimate -= (estimate - ecc * Math.Sin(estimate) - mean_anomaly) / (1 - ecc * Math.Cos(estimate));
        --iters_left;
      }
      return estimate;
    }

    RACommLink MakeLink(
      RACommNode tx,
      RACommNode rx,
      double forward_data_rate,
      double backward_data_rate, int antenna_num = 0) {
      if (!tx.TryGetValue(rx, out CommNet.CommLink link)) {
        link = new RACommLink();
        link.Set(tx, rx, 0, 0);
        tx.Add(rx, link);
        rx.Add(tx, link);
      }
      var ra_link = (RACommLink)link;
      ra_link.FwdAntennaTx = tx.RAAntennaList[antenna_num];
      ra_link.FwdAntennaRx = rx.RAAntennaList[antenna_num];
      ra_link.RevAntennaTx = rx.RAAntennaList[antenna_num];
      ra_link.RevAntennaRx = tx.RAAntennaList[antenna_num];
      ra_link.FwdDataRate = forward_data_rate;
      ra_link.RevDataRate = backward_data_rate;
      return ra_link;
    }

    int LinkNodes(double base_distance) {
      // base_distance is the maximum distance at which a link still achieves maximum data rate.
      int links = 0;
      foreach (string uname in nodes.Keys) {
        foreach (string vname in nodes.Keys) {
          if (uname == vname) continue;
          RACommNode u = nodes[uname];
          RACommNode v = nodes[vname]; // KeyValuePair Deconstruct isn't in this version of .NET D:

          Vector3d distance_vec = (v.precisePosition - u.precisePosition);
          double t = Vector3d.Dot(-u.precisePosition, distance_vec) / Vector3d.Dot(distance_vec, distance_vec);
          if (t < 0) t = 0;
          if (t > 1) t = 1;
          Vector3d closest_point_to_earth = Vector3d.Lerp(u.precisePosition, v.precisePosition, t);
          if (closest_point_to_earth.magnitude < RADIUS) continue; // No links inside Earth.
          double distance = distance_vec.magnitude;
          // Just quarter tx rate per doubling in distance. Let's say we get full strength at 30Mm. That's quite unrealistic for vessel-vessel links, but whatever.
          double link_rate = 4e9;
          bool is_intersatellite = uname.StartsWith("SAT_") && vname.StartsWith("SAT_");
          if (is_intersatellite) { // Awful workaround, but whatever.
            link_rate /= 4; // Relaying is expensive.
          }
          while (distance > base_distance) {
            distance /= 2;
            link_rate /= 4;
          }
          if (link_rate < 1000) continue; // Ignore small links

          MakeLink(u, v, link_rate, link_rate, (is_intersatellite) ? 1 : 0);
          links++;
        }
      }
      return links / 2; // because we make each link twice, whoops!
    }

    private RACommNode MakeGeostationary(string name, double lon) {
      const double sidereal_day = 86164.098903691;
      double geostationary_sma = SMAFromPeriod(sidereal_day);

      return MakeNodeOrbit(name, period: sidereal_day, ecc: 0, inc_deg: 0, argp_deg: 0, lan_deg: 0, mean_anomaly_deg: lon);
    }

    private bool MakeMolniyaThree(string name, double ecc, double lan1, double mean_anomaly, out RACommNode[] nodes) {
      // lan1 is the LAN of the first satellite. mean_anomaly offsets that satellite's mean anomaly in degrees.
      // The other two are offset to match.
      // Fails if the Pe of the constellation would be inside the Earth.
      nodes = null;
      
      const double sidereal_day = 86164.098903691;
      double molniya_sma = SMAFromPeriod(sidereal_day / 2);
      double pe = molniya_sma * (1 - ecc);
      if (pe < RADIUS) return false;

      nodes = new RACommNode[3];
      nodes[0] = MakeNodeOrbit($"{name}-1", period: sidereal_day / 2, ecc: ecc, inc_deg: 63.4, argp_deg: 270, lan_deg: lan1, mean_anomaly_deg: mean_anomaly);
      nodes[1] = MakeNodeOrbit($"{name}-2", period: sidereal_day / 2, ecc: ecc, inc_deg: 63.4, argp_deg: 270, lan_deg: lan1 + 120, mean_anomaly_deg: mean_anomaly + 120);
      nodes[2] = MakeNodeOrbit($"{name}-3", period: sidereal_day / 2, ecc: ecc, inc_deg: 63.4, argp_deg: 270, lan_deg: lan1 + 240, mean_anomaly_deg: mean_anomaly + 240);
      return true;
    }


    [TestMethod]
    public void OrbitConversion() {
      // A test for the test.
      const double eps = 1e-9;
      const double sidereal_day = 86164.098903691;

      double geostationary_sma = SMAFromPeriod(sidereal_day);
      Assert.AreEqual(42164.1721412429, geostationary_sma, eps);
      // This should match the SMA for Earth geostationary.
      // Note that the *altitude* is higher than Earth, since Earth is oblate and its equatorial radius is ~7km greater than the radius specified in Kopernicus.
      // This makes "geostationary altitude" on RSS-Earth equal to 35793.17214 km.

      double offset_deg = 13.5;
      RACommNode geostationary = MakeGeostationary("geos", offset_deg);
      Assert.AreEqual(geostationary_sma * Math.Cos(offset_deg * Math.PI / 180), geostationary.precisePosition.x, eps);
      Assert.AreEqual(geostationary_sma * Math.Sin(offset_deg * Math.PI / 180), geostationary.precisePosition.y, eps);
      Assert.AreEqual(0, geostationary.precisePosition.z, eps); // I think this argument order is stupid.

      double molniya_sma = SMAFromPeriod(sidereal_day / 2);
      double molniya_pe = molniya_sma * (1 - 0.737);
      RACommNode[] molniya1, molniya2, molniya3;
      Assert.IsTrue(MakeMolniyaThree("molniya_pe", 0.737, 90, 0, out molniya1));
      Assert.AreEqual(molniya_pe * Math.Cos(-63.4 * Math.PI / 180), molniya1[0].precisePosition.x, eps);
      Assert.AreEqual(0, molniya1[0].precisePosition.y, eps);
      Assert.AreEqual(molniya_pe * Math.Sin(-63.4 * Math.PI / 180), molniya1[0].precisePosition.z, eps);

      Assert.IsTrue(MakeMolniyaThree("molniya_ap", 0.737, 90, 180, out molniya2));
      double molniya_ap = molniya_sma * (1 + 0.737);
      Assert.AreEqual(molniya_ap * Math.Cos((180 - 63.4) * Math.PI / 180), molniya2[0].precisePosition.x, eps);
      Assert.AreEqual(0, molniya2[0].precisePosition.y, eps);
      Assert.AreEqual(molniya_ap * Math.Sin((180 - 63.4) * Math.PI / 180), molniya2[0].precisePosition.z, eps);

      Assert.IsTrue(MakeMolniyaThree("molniya_offset", 0.737, 210, 120, out molniya3));
      Assert.AreEqual(molniya_pe * Math.Cos((- 63.4) * Math.PI / 180), molniya3[2].precisePosition.x, eps);
      Assert.AreEqual(0, molniya3[2].precisePosition.y, eps);
      Assert.AreEqual(molniya_pe * Math.Sin((- 63.4) * Math.PI / 180), molniya3[2].precisePosition.z, eps);
    }

    [TestMethod]
    public void NodeLinking() {
      for (int offset = 0; offset < 360; offset += 90) {
        MakeGeostationary($"geos-{offset}", offset);
      }
      LinkNodes(1000000);
      Assert.IsFalse(nodes["SAT_geos-0"].ContainsKey(nodes["SAT_geos-180"]));
      Assert.IsFalse(nodes["SAT_geos-90"].ContainsKey(nodes["SAT_geos-270"]));
      Assert.IsTrue(nodes["SAT_geos-0"].ContainsKey(nodes["SAT_geos-90"]));
      Assert.IsTrue(nodes["SAT_geos-0"].ContainsKey(nodes["SAT_geos-270"]));
      Assert.IsTrue(nodes["SAT_geos-180"].ContainsKey(nodes["SAT_geos-90"]));
      Assert.IsTrue(nodes["SAT_geos-180"].ContainsKey(nodes["SAT_geos-270"]));
    }
    
    private void CompareConnections(Routing[] routings, out int connection_count, out int connected) {
      // Tests that all routings yield the same route.

      // Test connections are taken from my save, and consist of almost every connection configured.
      string path = "./test_connections.csv";
      Assert.IsTrue(File.Exists(path));
      connection_count = 0;
      connected = 0;

      using (StreamReader sr = File.OpenText(path)) {
        string s;
        while ((s = sr.ReadLine()) != null) {
          string[] parts = s.Split(',');
          RACommNode tx = nodes[parts[0]];
          RACommNode[] rxs = new RACommNode[parts.Length - 3];
          for (int i = 1; i < parts.Length - 2; ++i) {
            rxs[i-1] = nodes[parts[i]];
          }
          double data_rate = double.Parse(parts[parts.Length - 2]);
          double latency_limit = double.Parse(parts[parts.Length - 1]) / 1000; // all distances are currently shortened by 1000x

          Routing.Channel[] baseline;
          var baseline_result = routings[0].FindAndUseAvailableChannels(tx, rxs, latency_limit, data_rate, out baseline, connection: null);

          for (int i = routings.Length - 1; i >= 1; --i) {
            Routing.Channel[] channels;
            var result = routings[i].FindAndUseAvailableChannels(tx, rxs, latency_limit, data_rate, out channels, connection: null);
            Assert.AreEqual(baseline_result, result, $"Different availability for {s}");
            Assert.AreEqual(baseline?.Length, channels?.Length);

            for (int j = baseline.Length - 1; j >= 0; --j) {
              Routing.Channel baseline_channel = baseline[j];
              Routing.Channel this_channel = channels[j];
              if (baseline_channel is null || this_channel is null) {
                string culpable = (!(baseline_channel is null)) ? "baseline" : $"routing {i}";
                Assert.IsTrue(baseline_channel is null && this_channel is null, $"Channel {j} for {tx.name} to {rxs[j].name} exists on {culpable}");
                break;
              }
              Assert.AreEqual(baseline_channel.latency, this_channel.latency, 1e-12, $"Channel {j} for {tx.name} to {rxs[j].name} has different latency");
              Assert.AreEqual(baseline_channel.links.Count, this_channel.links.Count, $"Channel {j} for {tx.name} to {rxs[j].name} uses different link count");
              for (int k = baseline_channel.links.Count - 1; k >= 0; --k) {
                Assert.AreEqual(baseline_channel.links[k].ra_link, this_channel.links[k].ra_link,
                  $"{tx.name} to {rxs[j].name} uses different intermediate links");
              }
            }
          }
          if (baseline_result == Routing.PointToMultipointAvailability.Available) connected++;
          connection_count++;
        }
      }
    }
    

    private long TestConnections(Routing routing, out int connection_count) {
      // Connects the connections in the file, and display how long it takes in ticks.

      // Test connections are taken from my save, and consist of almost every connection configured.
      string path = "./test_connections.csv";
      Assert.IsTrue(File.Exists(path));
      connection_count = 0;

      System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();

      using (StreamReader sr = File.OpenText(path)) {
        string s;
        while ((s = sr.ReadLine()) != null) {
          string[] parts = s.Split(',');
          RACommNode tx = nodes[parts[0]];
          RACommNode[] rxs = new RACommNode[parts.Length - 3];
          for (int i = 1; i < parts.Length - 2; ++i) {
            rxs[i-1] = nodes[parts[i]];
          }
          double data_rate = double.Parse(parts[parts.Length - 2]);
          double latency_limit = double.Parse(parts[parts.Length - 1]) / 1000; // all distances are currently shortened by 1000x

          Routing.Channel[] baseline;
          watch.Start();
          var baseline_result = routing.FindAndUseAvailableChannels(tx, rxs, latency_limit, data_rate, out baseline, connection: null);
          watch.Stop();
          connection_count++;
        }
      }
      return watch.ElapsedTicks;
    }
    private void SetupPrecompute() {
      routing_astar.heuristic.OverrideNodes(nodes.Values.ToList());
    }

    private void PrecomputeLinkAndTest(double link_distance) {
      SetupPrecompute();
      int links = LinkNodes(link_distance);
      int connection_count, connected;
      
      CompareConnections(new Routing[] { routing_dijkstras, routing_astar }, out connection_count, out connected);
      Console.WriteLine($"Vertex count: {nodes.Count}");
      Console.WriteLine($"Edge count: {links}");
      Console.WriteLine($"Connection status: {connected}/{connection_count} connected");

      routing_astar.Reset(new RACommNode[] { }, new RACommNode[] { }, new RACommNode[] { });
      SetupPrecompute();

      Console.WriteLine($"A* Runtime: {(double) TestConnections(routing_astar, out connection_count) / Stopwatch.Frequency * 1000:F2}ms");
      
      routing_astar.Reset(new RACommNode[] { }, new RACommNode[] { }, new RACommNode[] { });
      Console.WriteLine($"Dijkstra's Runtime: {(double) TestConnections(routing_dijkstras, out connection_count) / Stopwatch.Frequency * 1000:F2}ms");
    }

    [TestMethod]
    public void CompareLEOStrong() {
      LoadTestStations();
      // Approximate the Iridium network.
      for (int i = 0; i < 6; ++i) {
        for (int j = 0; j < 11; ++j) {
          MakeNodeOrbit($"iridium-{i}-{j}", period: 6000, ecc: i * 0.00001, inc_deg: 86.4, argp_deg: 0, lan_deg: i * 30, mean_anomaly_deg: j * 360.0 / 11);
        }
      }
      PrecomputeLinkAndTest(100000); // This doesn't approximate intersatellite links very well, since they eat up bandwidth on the same antenna everywhere.
    }

    [TestMethod]
    public void CompareLEOWeak() {
      LoadTestStations();
      // Approximate the Iridium network.
      for (int i = 0; i < 6; ++i) {
        for (int j = 0; j < 11; ++j) {
          MakeNodeOrbit($"iridium-{i}-{j}", period: 6000, ecc: i * 0.00001, inc_deg: 86.4, argp_deg: 0, lan_deg: i * 30, mean_anomaly_deg: j * 360.0 / 11);
        }
      }
      PrecomputeLinkAndTest(2000); // Enough to perfectly cover a decent area.
    }

    [TestMethod]
    public void CompareMEOStrong() {
      LoadTestStations();
      // Approximate the O3b network.
      for (int i = 0; i < 12; ++i) {
        MakeNodeOrbit($"o3b-{i}", period: 287.9 * 60, ecc: i * 0.00001, inc_deg: 0, argp_deg: 0, lan_deg: 0, mean_anomaly_deg: i * 360.0 / 12); 
      }
      PrecomputeLinkAndTest(1000000);
    }

    [TestMethod]
    public void CompareMEOWeak() {
      LoadTestStations();
      // Approximate the O3b network.
      for (int i = 0; i < 12; ++i) {
        MakeNodeOrbit($"o3b-{i}", period: 287.9 * 60, ecc: i * 0.00001, inc_deg: 0, argp_deg: 0, lan_deg: 0, mean_anomaly_deg: i * 360.0 / 12); 
        // Let's just. wiggle the nodes a bit so there's less ties
      }
      PrecomputeLinkAndTest(8000);
    }

    [TestMethod]
    public void CompareGEOStrong() {
      LoadTestStations();
      // This is my network! Kind of.
      double[] GEONodes = { -173.6793456, -61.608255, -16.36492257, 63.29080541, 89.31195831, -22.454397, 177.005682, 54.26406091, 95.7193, -82.2150155, -151.249734 };
      for (int i = 0; i < GEONodes.Length; ++i) {
        MakeGeostationary($"geos-{i}", GEONodes[i]);
      }
      PrecomputeLinkAndTest(1000000); // If they can see each other, they can connect with max data rate.
    }

    [TestMethod]
    public void CompareGEOWeak() {
      LoadTestStations();
      // This is my network! Kind of.
      double[] GEONodes = { -173.6793456, -61.608255, -16.36492257, 63.29080541, 89.31195831, -22.454397, 177.005682, 54.26406091, 95.7193, -82.2150155, -151.249734 };
      for (int i = 0; i < GEONodes.Length; ++i) {
        MakeGeostationary($"geos-{i}", GEONodes[i]);
      }
      PrecomputeLinkAndTest(10000); // Weak connections. All links probably have barely enough data rate to do contracts.
    }

    private Routing routing_astar;
    private Routing routing_dijkstras;
    private readonly Dictionary<string, RACommNode> nodes = new Dictionary<string, RACommNode>();

    private ConfigNode filler_antenna;

    private const double RADIUS = 6371;

  }
}

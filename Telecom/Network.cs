using RealAntennas;
using RealAntennas.Antenna;
using RealAntennas.MapUI;
using RealAntennas.Network;
using RealAntennas.Precompute;
using RealAntennas.Targeting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace σκοπός {
  public class Network {

    static void Log(string message,
                    [CallerFilePath] string file = "",
                    [CallerLineNumber] int line = 0) {
      UnityEngine.Debug.Log($"[Σκοπός Telecom]: {message} ({file}:{line})");
    }

    static ConfigNode GetStationDefinition(string name) {
      foreach (var block in GameDatabase.Instance.GetConfigs("σκοπός_telecom")) {
        foreach (var definition in block.config.GetNodes("station")) {
          if (definition.GetValue("name") == name) {
            return definition;
          }
        }
      }
      throw new KeyNotFoundException($"No definition for station {name}");
    }

    static ConfigNode GetCustomerDefinition(string name) {
      foreach (var block in GameDatabase.Instance.GetConfigs("σκοπός_telecom")) {
        foreach (var definition in block.config.GetNodes("customer")) {
          if (definition.GetValue("name") == name) {
            return definition;
          }
        }
      }
      throw new KeyNotFoundException($"No definition for customer {name}");
    }

    static ConfigNode GetConnectionDefinition(string name) {
      foreach (var block in GameDatabase.Instance.GetConfigs("σκοπός_telecom")) {
        foreach (var definition in block.config.GetNodes("connection")) {
          if (definition.GetValue("name") == name) {
            return definition;
          }
        }
      }
      throw new KeyNotFoundException($"No definition for service level {name}");
    }

    static CelestialBody GetConfiguredBody(ConfigNode node) {
      string body_name = node.GetValue("body");
      return body_name != null ? FlightGlobals.GetBodyByName(body_name)
                               : FlightGlobals.GetHomeBody();
    }

    public Network(ConfigNode network_specification) {
      if (network_specification == null) {
        return;
      }
      AddStations(network_specification.GetValues("station"));
      AddCustomers(network_specification.GetValues("customer"));
      ConfigNode[] connection_nodes = network_specification.GetNodes("connection");
      AddConnections(connection_nodes.Select(n => n.GetValue("name")));
      foreach (ConfigNode node in connection_nodes) {
        connections_[node.GetValue("name")].Load(node);
      }
    }

    public void Serialize(ConfigNode node) {
      foreach (string station in stations_.Keys) {
        node.AddValue("station", station);
      }
      foreach (string customer in customers_.Keys) {
        node.AddValue("customer", customer);
      }
      foreach (var name_connection in connections_) {
        string name = name_connection.Key;
        Connection connection = name_connection.Value;
        ConfigNode connection_node = node.AddNode("connection");
        connection_node.AddValue("name", name);
        connection.Serialize(connection_node);
      }
    }

    private void RebuildGraph() {
      int n = stations_.Count + customers_.Count;
      connection_graph_ = new Edge[n, n];
      for (int i = 0; i < n; i++) {
        for (int j = 0; j < n; ++j) {
          connection_graph_[i, j] = new Edge();
        }
      }
      names_ = new string[n];
      int k = 0;
      foreach (string name in stations_.Keys) {
        names_[k++] = name;
      }
      foreach (string name in customers_.Keys) {
        names_[k++] = name;
      }
      foreach (var connection in connections_.Values) {
        int tx = names_.IndexOf(connection.tx_name);
        int rx = names_.IndexOf(connection.rx_name);
        connection_graph_[tx, rx].monitors_.Add(connection);
      }
    }

    public void AddStations(IEnumerable<string> names) {
      foreach (var name in names) {
        if (stations_.ContainsKey(name)) {
          Log($"Station {name} already present");
          continue;
        }
        Log($"Adding station {name}");
        stations_.Add(name, null);
      }
      connection_graph_ = null;
    }

    RACommNetHome MakeStation(string name) {
      var node = GetStationDefinition(name);
      var body = GetConfiguredBody(node);
      var station =
        new UnityEngine.GameObject(body.name).AddComponent<RACommNetHome>();
      var station_node = new ConfigNode();
      foreach (string key in new[] { "objectName", "lat", "lon", "alt" }) {
        station_node.AddValue(key, node.GetValue(key));
      }
      station_node.AddValue("isKSC", false);
      station_node.AddValue("isHome", false);
      station_node.AddValue("icon", "RealAntennas/radio-antenna");
      foreach (var antenna in node.GetNodes("Antenna")) {
        Log($"antenna for {name}: {antenna}");
        Log($"Ground TL is {RACommNetScenario.GroundStationTechLevel}");
        station_node.AddNode(antenna);
      }
      station.Configure(station_node, body);

      if (node.GetValue("role") == "tx") {
        tx_.Add(station);
      } else if (node.GetValue("role") == "rx") {
        rx_.Add(station);
      } else {
        tx_.Add(station);
        rx_.Add(station);
      }
      return station;
    }

      public void AddCustomers(IEnumerable<string> names) {
      foreach (var name in names) {
        if (customers_.ContainsKey(name)) {
          Log($"Customer {name} already present");
          continue;
        }
        Log($"Adding customer {name}");
        customers_.Add(name, new Customer(GetCustomerDefinition(name), this));
      }
      connection_graph_ = null;
    }
    public void AddConnections(IEnumerable<string> names) {
      foreach (var name in names) {
        if (connections_.ContainsKey(name)) {
          Log($"Connection {name} already present");
          continue;
        }
        Log($"Adding connection {name}");
        connections_.Add(name, new Connection(GetConnectionDefinition(name)));
      }
      connection_graph_ = null;
    }

    public void RemoveStations(IEnumerable<string> names) { }
    public void RemoveCustomers(IEnumerable<string> names) { }
    public void RemoveConnections(IEnumerable<string> names) { }

    public void AddNominalLocation(Vessel v) {
      // TODO(egg): maybe this could be body-dependent.
      nominal_satellite_locations_.Add(
        UnityEngine.QuaternionD.Inverse(FlightGlobals.GetHomeBody().scaledBody.transform.rotation) *
          (v.GetWorldPos3D() - FlightGlobals.GetHomeBody().position));
      must_retarget_customers_ = true;
    }

    public Vector3d[] GetNominalLocationLatLonAlts() {
      var result = new List<Vector3d>(nominal_satellite_locations_.Count);
      foreach (var position in nominal_satellite_locations_) {
        FlightGlobals.GetHomeBody().GetLatLonAlt(
          FlightGlobals.GetHomeBody().scaledBody.transform.rotation * position +
          FlightGlobals.GetHomeBody().position,
          out double lat, out double lon, out double alt);
        result.Add(new Vector3d(lat, lon, alt));
      }
      return result.ToArray();
    }

    public void ClearNominalLocations() {
      nominal_satellite_locations_.Clear();
      must_retarget_customers_ = true;
    }

    private void OnUpdateGroundStationVisible(
        KSP.UI.Screens.Mapview.MapNode mapNode,
        KSP.UI.Screens.Mapview.MapNode.IconData iconData) {}

    private void OnUpdateOffNetworkStationVisible(
        KSP.UI.Screens.Mapview.MapNode mapNode,
        KSP.UI.Screens.Mapview.MapNode.IconData iconData) {
      iconData.visible &= !hide_off_network;
    }

    public void Refresh() {
      if (connection_graph_ == null) {
        RebuildGraph();
      }
      bool all_stations_good = true;;
      var station_names = stations_.Keys.ToArray();
      foreach (var name in station_names) {
        if (stations_[name] == null) {
          Log($"Making station {name}");
          stations_[name] = MakeStation(name);
        }
        if (stations_[name].Comm == null) {
          Log($"null Comm for {name}");
          all_stations_good = false;
        }
        string node_name = stations_[name].nodeName;
        if (!RACommNetScenario.GroundStations.ContainsKey(node_name)) {
          Log($"{name} not in GroundStations at {node_name}");
          RACommNetScenario.GroundStations.Add(node_name, stations_[name]);
        } else if (RACommNetScenario.GroundStations[node_name] != stations_[name]) {
          Log($"{name} is not GroundStations[{node_name}]");
          UnityEngine.Object.DestroyImmediate(RACommNetScenario.GroundStations[node_name]);
          RACommNetScenario.GroundStations[node_name] = stations_[name];
        }
      }
      var ui = RACommNetUI.Instance as RACommNetUI;
      if (!ground_stations_have_visibility_callback_ &&
          ui?.groundStationSiteNodes.Count > 0) {
        foreach (var site in ui.groundStationSiteNodes) {
          var station_comm = ((GroundStationSiteNode)site.siteObject).node;
          bool on_network = stations_.Values.Any(station => station.Comm == station_comm);
          if (on_network) {
            site.wayPoint.node.OnUpdateVisible += OnUpdateGroundStationVisible;
          } else {
            site.wayPoint.node.OnUpdateVisible += OnUpdateOffNetworkStationVisible;
          }
        }
      }
      if (!all_stations_good) {
        return;
      }
      foreach (var pair in stations_) {
        var station = pair.Value;
        if (station.Comm.RAAntennaList.Count == 0) {
          Log($"No antenna for {pair.Key}");
          Log($"Ground TL is {RACommNetScenario.GroundStationTechLevel}");
        }
        station.Comm.RAAntennaList[0].Target = null;
      }
      //CreateGroundSegmentNodesIfNeeded();
      foreach (var customer in customers_.Values) {
        customer.Cycle();
      }
      if (customers_.Values.Any(customer => customer.station == null)) {
        return;
      }
      if (must_retarget_customers_) {
        foreach (var customer in customers_.Values) {
          customer.Retarget();
        }
        must_retarget_customers_ = false;
      }
      UpdateConnections();
    }

    private void CreateGroundSegmentNodesIfNeeded() {
      // TODO(egg): Rewrite taking mutability into account.
      if (ground_segment_nodes_ == null && MapView.fetch != null) {
        ground_segment_nodes_ = new List<SiteNode>();
        foreach (var station in stations_.Values) {
          ground_segment_nodes_.Add(MakeSiteNode(station));
        }
      }
    }

    private static SiteNode MakeSiteNode(RACommNetHome station) {
      SiteNode site_node = SiteNode.Spawn(new GroundStationSiteNode(station.Comm));
      UnityEngine.Texture2D stationTexture = GameDatabase.Instance.GetTexture(station.icon, false);
      site_node.wayPoint.node.SetIcon(UnityEngine.Sprite.Create(
        stationTexture,
        new UnityEngine.Rect(0, 0, stationTexture.width, stationTexture.height),
        new UnityEngine.Vector2(0.5f, 0.5f),
        100f));
      site_node.wayPoint.node.OnUpdateVisible += station.OnUpdateVisible;
      return site_node;
    }

    private ConfigNode MakeTargetConfig(CelestialBody body, Vector3d station_world_position) {
      var config = new ConfigNode(AntennaTarget.nodeName);
      if (nominal_satellite_locations_.Count == 0) {
        return config;
      }
      Vector3d station_position =
          UnityEngine.QuaternionD.Inverse(body.scaledBody.transform.rotation) *
            (station_world_position - body.position);
      Vector3d station_zenith = station_position.normalized;
      Vector3d target = default;
      double max_cos_zenithal_angle = double.NegativeInfinity;
      foreach (var position in nominal_satellite_locations_) {
        double cos_zenithal_angle = Vector3d.Dot(station_zenith, position - station_position);
        if (cos_zenithal_angle > max_cos_zenithal_angle) {
          max_cos_zenithal_angle = cos_zenithal_angle;
          target = position;
        }
      }
      body.GetLatLonAlt(
        body.scaledBody.transform.rotation * target + body.position,
        out double lat, out double lon, out double alt);
      config.AddValue("name", $"{AntennaTarget.TargetMode.BodyLatLonAlt}");
      config.AddValue("bodyName", Planetarium.fetch.Home.name);
      config.AddValue("latLonAlt", new Vector3d(lat, lon, alt));
      return config;
    }

    private void UpdateConnections() {
      var network = CommNet.CommNetNetwork.Instance.CommNet as RACommNetwork;
      if (network == null) {
        Log("No RA comm network");
        return;
      }
      all_ground_ = stations_.Values.Concat(from customer in customers_.Values select customer.station).ToArray();
      min_rate_ = double.PositiveInfinity;
      active_links_.Clear();
      for (int tx = 0; tx < all_ground_.Length; ++tx) {
        for (int rx = 0; rx < all_ground_.Length; ++rx) {
          if (rx == tx || !tx_.Contains(all_ground_[tx]) || !rx_.Contains(all_ground_[rx])) {
            connection_graph_[tx, rx].AddMeasurement(double.NaN, double.NaN);
            continue;
          }
          var path = new CommNet.CommPath();
          network.FindClosestWhere(
            all_ground_[tx].Comm, path, (_, n) => n == all_ground_[rx].Comm);
          double rate = double.PositiveInfinity;
          double length = 0;
          foreach (var l in path) {
            active_links_.Add(l);
            RACommLink link = l as RACommLink;
            rate = Math.Min(rate, link.FwdDataRate);
            length += (l.a.position - l.b.position).magnitude;
            if ((l.end as RACommNode).ParentVessel is Vessel vessel) {
              space_segment_[vessel] = Planetarium.GetUniversalTime();
            }
          }
          if (path.IsEmpty()) {
            rate = 0;
          }
          connection_graph_[tx, rx].AddMeasurement(rate: rate, latency: length / 299792458);
          min_rate_ = Math.Min(min_rate_, rate);
        }
      }
    }

    public Connection Monitor(string name) {
      return connections_[name];
    }

    private class Customer {
      public Customer(ConfigNode template, Network network) {
        template_ = template;
        network_ = network;
        body_ = GetConfiguredBody(template_);
      }

      public void Cycle() {
        if (network_.freeze_customers_) {
          return;
        }
        if (imminent_station_ != null) {
          DestroyStation();
          station = imminent_station_;
          imminent_station_ = null;
        }
        if (imminent_station_ == null && upcoming_station_?.Comm != null) {
          imminent_station_ = upcoming_station_;
          upcoming_station_ = null;
          (RACommNetScenario.Instance as RACommNetScenario)?.Network?.InvalidateCache();
        }
        if (upcoming_station_ == null) {
          upcoming_station_ = MakeStation();
        }
      }
      public void Retarget() {
        var antenna = station.Comm.RAAntennaList[0];
        antenna.Target = AntennaTarget.LoadFromConfig(network_.MakeTargetConfig(body_, station.Comm.precisePosition), antenna);
      }

      private RACommNetHome MakeStation() {
        HashSet<string> biomes = template_.GetValues("biome").ToHashSet();
        const double degree = Math.PI / 180;
        double lat;
        double lon;
        int i = 0;
        do {
          ++i;
          double sin_lat_min =
            Math.Sin(double.Parse(template_.GetValue("lat_min")) * degree);
          double sin_lat_max =
            Math.Sin(double.Parse(template_.GetValue("lat_max")) * degree);
          double lon_min = double.Parse(template_.GetValue("lon_min")) * degree;
          double lon_max = double.Parse(template_.GetValue("lon_max")) * degree;
          lat = Math.Asin(sin_lat_min + network_.random_.NextDouble() * (sin_lat_max - sin_lat_min));
          lon = lon_min + network_.random_.NextDouble() * (lon_max - lon_min);
        } while (!biomes.Contains(body_.BiomeMap.GetAtt(lat, lon).name));
        var new_station =
          new UnityEngine.GameObject(body_.name).AddComponent<RACommNetHome>();
        var node = new ConfigNode();
        node.AddValue("objectName", $"{template_.GetValue("name")} @{lat / degree:F2}, {lon / degree:F2} ({i} tries)");
        node.AddValue("lat", lat / degree);
        node.AddValue("lon", lon / degree);
        double alt = body_.TerrainAltitude(lat / degree, lon / degree) + 10;
        node.AddValue("alt", alt);
        node.AddValue("isKSC", false);
        node.AddValue("isHome", false);
        node.AddValue("icon", "RealAntennas/DSN");
        Vector3d station_position = body_.GetWorldSurfacePosition(lat, lon, alt);
        foreach (var antenna in template_.GetNodes("Antenna")) {
          var targeted_antenna = antenna.CreateCopy();
          targeted_antenna.AddNode(network_.MakeTargetConfig(body_, station_position));
          node.AddNode(targeted_antenna);
        }
        new_station.Configure(node, body_);
        if (template_.GetValue("role") == "tx") {
          network_.tx_.Add(new_station);
        } else if (template_.GetValue("role") == "rx") {
          network_.rx_.Add(new_station);
        } else {
          network_.tx_.Add(new_station);
          network_.rx_.Add(new_station);
        }
        return new_station;
      }

      private void DestroyStation() {
        if (station == null) {
          return;
        }
        network_.tx_.Remove(station);
        network_.rx_.Remove(station);
        CommNet.CommNetNetwork.Instance.CommNet.Remove(station.Comm);
        if (node_ != null) {
          FinePrint.WaypointManager.RemoveWaypoint(node_.wayPoint);
          UnityEngine.Object.Destroy(node_.gameObject);
        }
        UnityEngine.Object.Destroy(station);
        station = null;
        node_ = null;
      }

      private RACommNetHome upcoming_station_;
      private RACommNetHome imminent_station_;
      public RACommNetHome station { get; private set; }
      private SiteNode node_;

      private ConfigNode template_;
      private CelestialBody body_;
      private Network network_;
    }

    public class Connection {
      public Connection(ConfigNode definition) {
        tx_name = definition.GetValue("tx");
        rx_name = definition.GetValue("rx");
        latency_threshold = double.Parse(definition.GetValue("latency"));
        rate_threshold = double.Parse(definition.GetValue("rate"));
        window = int.Parse(definition.GetValue("window"));
        daily_availability_ = new LinkedList<double>();
      }

      public void AddMeasurement(double latency, double rate, double t) {
        double day = KSPUtil.dateTimeFormatter.Day;
        double t_in_days = t / day;
        double new_day = Math.Floor(t_in_days);
        if (current_day_ == null) {
          current_day_ = new_day;
          return;
        }
        within_sla = latency <= latency_threshold && rate >= rate_threshold;

        if (new_day > current_day_) {
          if (within_sla) {
            daily_availability_.AddLast(day_fraction_within_sla_ + (1 - day_fraction_));
          } else {
            daily_availability_.AddLast(day_fraction_within_sla_);
          }
          for (int i = 0; i < new_day - current_day_ - 1; ++i) {
            daily_availability_.AddLast(within_sla ? 1 : 0);
          }
          day_fraction_ = t_in_days - new_day;
          day_fraction_within_sla_ = within_sla ? day_fraction_ : 0;
        } else {
          day_fraction_within_sla_ += within_sla ? (t_in_days - new_day) - day_fraction_ : 0;
          day_fraction_ = t_in_days - new_day;
        }
        while (daily_availability_.Count > window) {
          daily_availability_.RemoveFirst();
        }
        if (new_day > current_day_) {
          UpdateAvailability();
        }
        current_day_ = new_day;
      }

      public void Serialize(ConfigNode node) {
        foreach(var availability in daily_availability_) {
          node.AddValue("daily_availability", availability);
        }
        node.AddValue("current_day", current_day_);
        node.AddValue("day_fraction_within_sla", day_fraction_within_sla_);
        node.AddValue("day_fraction", day_fraction_);
      }

      public void Load(ConfigNode node) {
        daily_availability_ =
          new LinkedList<double>(node.GetValues("daily_availability").Select(double.Parse));
        current_day_ = double.Parse(node.GetValue("current_day"));
        day_fraction_within_sla_ = double.Parse(node.GetValue("day_fraction_within_sla"));
        day_fraction_ = double.Parse(node.GetValue("day_fraction"));
        UpdateAvailability();
      }

      private void UpdateAvailability() {
        availability = daily_availability_.Sum() / daily_availability_.Count;
      }

      public double latency_threshold { get; }
      public double rate_threshold { get; }
      public double availability { get; private set; }
      public double availability_yesterday => daily_availability_.Count == 0 ? 0 : daily_availability_.Last();

      public string tx_name { get; }
      public string rx_name { get; }

      public bool within_sla { get; private set; }
      public int window { get; private set; }
      public int days => daily_availability_.Count;

      private LinkedList<double> daily_availability_;
      private double? current_day_;
      private double day_fraction_within_sla_;
      private double day_fraction_;
    }

    public class Edge {
      public void AddMeasurement(double latency, double rate) {
        current_latency = latency;
        current_rate = rate;
        foreach (var monitor in monitors_) {
          monitor.AddMeasurement(latency, rate, Planetarium.GetUniversalTime());
        }
      }
      public double current_latency { get; private set; }
      public double current_rate { get; private set; }
      public List<Connection> monitors_ = new List<Connection>();
    }

    public Connection GetConnection(string name) {
      return connections_[name];
    }

    public RACommNetHome GetStation(string name) {
      return stations_[name];
    }

    public int customer_pool_size { get; set; }
    public bool hide_off_network { get; set; }

    private bool ground_stations_have_visibility_callback_ = false;
    private readonly SortedDictionary<string, Customer> customers_ =
        new SortedDictionary<string, Customer>();
    private readonly SortedDictionary<string, RACommNetHome> stations_ =
        new SortedDictionary<string, RACommNetHome>();
    private readonly SortedDictionary<string, Connection> connections_ =
        new SortedDictionary<string, Connection>();
    private List<SiteNode> ground_segment_nodes_;
    public readonly HashSet<RACommNetHome> tx_ = new HashSet<RACommNetHome>();
    public readonly HashSet<RACommNetHome> rx_ = new HashSet<RACommNetHome>();
    public readonly Dictionary<Vessel, double> space_segment_ = new Dictionary<Vessel, double>();
    private readonly List<Vector3d> nominal_satellite_locations_ = new List<Vector3d>();
    bool must_retarget_customers_ = false;
    private readonly Random random_ = new Random();
    public readonly List<CommNet.CommLink> active_links_ = new List<CommNet.CommLink>();
    public RACommNetHome[] all_ground_ = { };
    public Edge[,] connection_graph_ = { };
    public string[] names_ = { };
    public double min_rate_;
    public bool freeze_customers_;
  }
}

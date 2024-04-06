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
    static ConfigNode GetStationDefinition(string name) {
      foreach (var block in GameDatabase.Instance.GetConfigs("skopos_telecom")) {
        foreach (var definition in block.config.GetNodes("station")) {
          if (definition.GetValue("name") == name) {
            return definition;
          }
        }
      }
      throw new KeyNotFoundException($"No definition for station {name}");
    }

    static ConfigNode GetCustomerDefinition(string name) {
      foreach (var block in GameDatabase.Instance.GetConfigs("skopos_telecom")) {
        foreach (var definition in block.config.GetNodes("customer")) {
          if (definition.GetValue("name") == name) {
            return definition;
          }
        }
      }
      throw new KeyNotFoundException($"No definition for customer {name}");
    }

    static ConfigNode GetConnectionDefinition(string name) {
      foreach (var block in GameDatabase.Instance.GetConfigs("skopos_telecom")) {
        foreach (var definition in block.config.GetNodes("connection")) {
          if (definition.GetValue("name") == name) {
            return definition;
          }
        }
      }
      throw new KeyNotFoundException($"No definition for connection {name}");
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
        connection.Save(connection_node);
      }
    }

    private void RebuildGraph() {
      int n = stations_.Count + customers_.Count;
      names_ = new string[n];
      int k = 0;
      foreach (string name in stations_.Keys) {
        names_[k++] = name;
      }
      foreach (string name in customers_.Keys) {
        names_[k++] = name;
      }
    }

    public void AddStations(IEnumerable<string> names) {
      foreach (var name in names) {
        if (stations_.ContainsKey(name)) {
          Telecom.Log($"Station {name} already present");
          continue;
        }
        Telecom.Log($"Adding station {name}");
        stations_.Add(name, null);
      }
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
      foreach (string key in new[] { "isControlSource",
                                     "isControlSourceMultiHop" }) {
        string value = null;
        if (node.TryGetValue(key, ref value)) {
          station_node.AddValue(key, node.GetValue(key));
        }
      }
      station_node.AddValue("name", name);
      station_node.AddValue("isKSC", false);
      station_node.AddValue("isHome", false);
      station_node.AddValue("icon", "RealAntennas/radio-antenna");
      foreach (var antenna in node.GetNodes("Antenna")) {
        Telecom.Log($"antenna for {name}: {antenna}");
        Telecom.Log($"Ground TL is {RACommNetScenario.GroundStationTechLevel}");
        station_node.AddNode(antenna);
      }
      station.Configure(station_node, body);

      if (node.GetValue("role") == "tx") {
        tx_only_.Add(station);
      } else if (node.GetValue("role") == "rx") {
        rx_only_.Add(station);
      }
      return station;
    }

      public void AddCustomers(IEnumerable<string> names) {
      foreach (var name in names) {
        if (customers_.ContainsKey(name)) {
          Telecom.Log($"Customer {name} already present");
          continue;
        }
        Telecom.Log($"Adding customer {name}");
        customers_.Add(name, new Customer(GetCustomerDefinition(name), this));
      }
    }
    public void AddConnections(IEnumerable<string> names) {
      foreach (var name in names) {
        if (connections_.ContainsKey(name)) {
          Telecom.Log($"Connection {name} already present");
          continue;
        }
        Telecom.Log($"Adding connection {name}");
        connections_.Add(name, Connections.New(GetConnectionDefinition(name)));
      }
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
      bool all_stations_good = true;;
      var station_names = stations_.Keys.ToArray();
      foreach (var name in station_names) {
        if (stations_[name] == null) {
          Telecom.Log($"Making station {name}");
          stations_[name] = MakeStation(name);
        }
        if (stations_[name].Comm == null) {
          Telecom.Log($"null Comm for {name}");
          all_stations_good = false;
        }
        string node_name = stations_[name].nodeName;
        if (!RACommNetScenario.GroundStations.ContainsKey(node_name)) {
          Telecom.Log($"{name} not in GroundStations at {node_name}");
          RACommNetScenario.GroundStations.Add(node_name, stations_[name]);
        } else if (RACommNetScenario.GroundStations[node_name] != stations_[name]) {
          Telecom.Log($"{name} is not GroundStations[{node_name}]");
          UnityEngine.Object.DestroyImmediate(RACommNetScenario.GroundStations[node_name]);
          RACommNetScenario.GroundStations[node_name] = stations_[name];
        }
      }
      if (RACommNetUI.Instance is RACommNetUI ui) {
        foreach (var site in ui.groundStationSiteNodes) {
          var station_comm = ((GroundStationSiteNode)site.siteObject).node;
          bool on_network = stations_.Values.Any(station => station.Comm == station_comm);
          site.wayPoint.node.OnUpdateVisible -= OnUpdateGroundStationVisible;
          site.wayPoint.node.OnUpdateVisible -= OnUpdateOffNetworkStationVisible;
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
          Telecom.Log($"No antenna for {pair.Key}");
          Telecom.Log($"Ground TL is {RACommNetScenario.GroundStationTechLevel}");
        }
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
      foreach (RealAntennaDigital antenna in routing_.usage.Transmitters()) {
        if ((antenna?.ParentNode as RACommNode).ParentVessel is Vessel vessel) {
          Kerbalism.ConsumeResource(
              vessel,
              "ElectricCharge",
              // PowerDrawLinear is in mW, ElectricCharge is in kJ.
              routing_.usage.TxPowerUsage(antenna) * antenna.PowerDrawLinear *
              1e-6 * TimeWarp.fixedDeltaTime,
              "Σκοπός telecom");
        }
      }
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
      var network = CommNet.CommNetNetwork.Instance?.CommNet as RACommNetwork;
      if (network == null) {
        Telecom.Log("No RA comm network");
        return;
      }
      routing_.Reset(
          from station in tx_only_ select station.Comm,
          from station in rx_only_ select station.Comm,
          from station in stations_.Values select station.Comm);
      connections_by_contract.Clear();
      contracted_connections.Clear();
      foreach (var contract in Contracts.ContractSystem.Instance.Contracts) {
        if (contract.ContractState == Contracts.Contract.State.Active) {
          List<Connection> contract_connections = null;
          foreach (var parameter in contract.AllParameters) {
            if (parameter is ConnectionAvailability connection) {
              if (contract_connections == null &&
                  !connections_by_contract.TryGetValue(contract, out contract_connections)) {
                contract_connections = new List<Connection>();
                connections_by_contract.Add(contract, contract_connections);
              }
              contracted_connections.Add(GetConnection(connection.connection_name));
              contract_connections.Add(GetConnection(connection.connection_name));
            }
          }
        }
      }
      foreach (var connection in connections_.Values) {
        if (contracted_connections.Contains(connection)) {
          connection.AttemptConnection(routing_, this, Telecom.Instance.last_universal_time);
        }
      }
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
          if (!targeted_antenna.HasNode(AntennaTarget.nodeName)) {
            targeted_antenna.AddNode(network_.MakeTargetConfig(body_, station_position));
          }
          node.AddNode(targeted_antenna);
        }
        new_station.Configure(node, body_);
        if (template_.GetValue("role") == "tx") {
          network_.tx_only_.Add(new_station);
        } else if (template_.GetValue("role") == "rx") {
          network_.rx_only_.Add(new_station);
        }
        return new_station;
      }

      private void DestroyStation() {
        if (station == null) {
          return;
        }
        network_.tx_only_.Remove(station);
        network_.rx_only_.Remove(station);
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

    public IEnumerable<Connection> connections => connections_.Values;

    public Connection GetConnection(string name) {
      try {
        return connections_[name];
      } catch (KeyNotFoundException e) {
        Telecom.Log($"No connection {name}\n{e}");
        throw;
      }
    }

    public RACommNetHome GetStation(string name) {
      return stations_[name];
    }

    public IEnumerable<RACommNetHome> AllGround() {
      return stations_.Values.Concat(
          from customer in customers_.Values select customer.station);
    }

    public int customer_pool_size { get; set; }
    public bool hide_off_network { get; set; }

    private readonly SortedDictionary<string, Customer> customers_ =
        new SortedDictionary<string, Customer>();
    private readonly SortedDictionary<string, RACommNetHome> stations_ =
        new SortedDictionary<string, RACommNetHome>();
    private readonly SortedDictionary<string, Connection> connections_ =
        new SortedDictionary<string, Connection>();
    private List<SiteNode> ground_segment_nodes_;
    public readonly HashSet<RACommNetHome> tx_only_ = new HashSet<RACommNetHome>();
    public readonly HashSet<RACommNetHome> rx_only_ = new HashSet<RACommNetHome>();
    private readonly List<Vector3d> nominal_satellite_locations_ = new List<Vector3d>();
    bool must_retarget_customers_ = false;
    private readonly Random random_ = new Random();
    public string[] names_ = { };
    public bool freeze_customers_;
    public Routing routing_ = new Routing();

    public Dictionary<Contracts.Contract, List<Connection>> connections_by_contract  { get; } =
        new Dictionary<Contracts.Contract, List<Connection>>();
    public HashSet<Connection> contracted_connections { get; } = new HashSet<Connection>();
  }
}

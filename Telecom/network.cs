using RealAntennas;
using RealAntennas.MapUI;
using RealAntennas.Network;
using System.Collections.Generic;
using System.Linq;

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
      ConfigNode[] connection_nodes = network_specification.GetNodes("connection");
      AddConnections(connection_nodes.Select(n => n.GetValue("name")));
      foreach (ConfigNode node in connection_nodes) {
        connections_[node.GetValue("name")].Load(node);
      }
      (CommNet.CommNetScenario.Instance as RACommNetScenario).Network.InvalidateCache();    // Inform RA of changes to the node list.
    }

    public void Serialize(ConfigNode node) {
      foreach (string station in stations_.Keys) {
        node.AddValue("station", station);
      }
      foreach (var name_connection in connections_) {
        string name = name_connection.Key;
        Connection connection = name_connection.Value;
        ConfigNode connection_node = node.AddNode("connection");
        connection_node.AddValue("name", name);
        connection.Save(connection_node);
      }
    }

    public void AddStations(IEnumerable<string> names) {
      foreach (var name in names) {
        if (stations_.ContainsKey(name)) {
          Telecom.Log($"Station {name} already present");
          continue;
        }
        Telecom.Log($"Adding station {name}");
        RACommNetHome new_station = MakeStation(name);
        stations_.Add(name, new_station);
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
      Telecom.Log($"Ground TL is {RACommNetScenario.GroundStationTechLevel}");
      foreach (var antenna in node.GetNodes("Antenna")) {
        Telecom.Log($"antenna for {name}: {antenna}");
        station_node.AddNode(antenna);
      }
      station.Configure(station_node, body);
      if (RACommNetScenario.GroundStations.TryGetValue(station.nodeName, out RACommNetHome oldStation)) { 
        Telecom.Log($"Ground station {station.nodeName} was already registered in RA, deleting the old instance");
        RACommNetScenario.GroundStations.Remove(station.nodeName);
        UnityEngine.Object.Destroy(oldStation);
      }
      RACommNetScenario.GroundStations.Add(station.nodeName, station);

      if (node.GetValue("role") == "tx") {
        tx_only_.Add(station);
      } else if (node.GetValue("role") == "rx") {
        rx_only_.Add(station);
      }
      Telecom.Instance.StartCoroutine(Telecom.Instance.UpdateGroundStationNode(station));
      return station;
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

    private void OnUpdateGroundStationVisible(
        KSP.UI.Screens.Mapview.MapNode mapNode,
        KSP.UI.Screens.Mapview.MapNode.IconData iconData) {}

    private void OnUpdateOffNetworkStationVisible(
        KSP.UI.Screens.Mapview.MapNode mapNode,
        KSP.UI.Screens.Mapview.MapNode.IconData iconData) {
      iconData.visible &= !hide_off_network;
    }

    private void StationSanityChecker() { 
      foreach (var pair in stations_) {
        var station = pair.Value;
        if (station.Comm.RAAntennaList.Count == 0) {
          Telecom.Log($"No antenna for {pair.Key}; Ground TL is {RACommNetScenario.GroundStationTechLevel}");
        }
      }
    }

    internal void UpdateStationVisibilityHandler() { 
      if (RACommNetUI.Instance is RACommNetUI ui) {
        foreach (var site in ui.groundStationSiteNodes.Values) {
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
    }

    private System.Diagnostics.Stopwatch refresh_watch_ = new System.Diagnostics.Stopwatch();
    public void Refresh() {
      UnityEngine.Profiling.Profiler.BeginSample("Skopos.Network.FixedUpdate");
      var metrics = Telecom.Instance.runtimeMetrics_;
      refresh_watch_.Start();
      UpdateConnections();
      foreach (RealAntennaDigital antenna in routing_.usage.Transmitters()) {
        if ((antenna?.ParentNode as RACommNode).ParentVessel is Vessel vessel) {
          Kerbalism.ConsumeResource(
              vessel,
              "ElectricCharge",
              // PowerDrawLinear is in mW, ElectricCharge is in kJ.
              routing_.usage.TxPowerUsage(antenna) * antenna.PowerDrawLinear * 1e-6 * TimeWarp.fixedDeltaTime,
              "Σκοπός telecom");
        }
      }
      refresh_watch_.Stop();
      metrics.num_fixed_update_iterations_++;
      metrics.fixed_update_runtime_ = refresh_watch_.Elapsed.TotalMilliseconds;
      UnityEngine.Profiling.Profiler.EndSample();
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
      foreach (var connection in connections_.Values) {
        if (contracted_connections.Contains(connection)) {
          connection.AttemptConnection(routing_, this, Telecom.Instance.last_universal_time);
        }
      }
    }

    internal void ReloadContractConnections() {
      connections_by_contract.Clear();
      contracted_connections.Clear();
      foreach (var contract in Contracts.ContractSystem.Instance.Contracts.Where(c => c.ContractState == Contracts.Contract.State.Active)) {
        List<Connection> contract_connections = null;
        foreach (ConnectionAvailability connection in contract.AllParameters.Where(p => p is ConnectionAvailability)) {
          if (contract_connections == null &&
              !connections_by_contract.TryGetValue(contract, out contract_connections)) {
            contract_connections = new List<Connection>();
            connections_by_contract.Add(contract, contract_connections);
          }
          contracted_connections.Add(GetConnection(connection.connection_name));
          contract_connections.Add(GetConnection(connection.connection_name));
        }
      }
      (CommNet.CommNetScenario.Instance as RACommNetScenario).Network.InvalidateCache();    // Inform RA of changes to the node list.
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
      return stations_.Values;
    }

    public int customer_pool_size { get; set; }
    public bool hide_off_network { get; set; }

    private readonly SortedDictionary<string, RACommNetHome> stations_ =
        new SortedDictionary<string, RACommNetHome>();
    private readonly SortedDictionary<string, Connection> connections_ =
        new SortedDictionary<string, Connection>();
    public readonly HashSet<RACommNetHome> tx_only_ = new HashSet<RACommNetHome>();
    public readonly HashSet<RACommNetHome> rx_only_ = new HashSet<RACommNetHome>();
    public string[] names_ = { };
    public Routing routing_ = new Routing();

    public Dictionary<Contracts.Contract, List<Connection>> connections_by_contract  { get; } =
        new Dictionary<Contracts.Contract, List<Connection>>();
    public HashSet<Connection> contracted_connections { get; } = new HashSet<Connection>();
    public HashSet<RACommNetHome> needs_site_node { get;} = new HashSet<RACommNetHome>();
  }
}

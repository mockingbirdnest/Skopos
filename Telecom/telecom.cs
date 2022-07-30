using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RealAntennas;
using RealAntennas.Network;
using RealAntennas.MapUI;
using RealAntennas.Targeting;
using System.IO;
using System.Runtime.CompilerServices;

namespace σκοπός {
  [KSPScenario(
    ScenarioCreationOptions.AddToAllGames,
    new[] { GameScenes.SPACECENTER, GameScenes.TRACKSTATION, GameScenes.FLIGHT, GameScenes.EDITOR })]
  public sealed class Telecom : ScenarioModule {

    public static void Log(string message,
                           [CallerFilePath] string file = "",
                           [CallerLineNumber] int line = 0) {
      UnityEngine.Debug.Log($"[Σκοπός Telecom]: {message} ({file}:{line})");
    }

    public Telecom() {
      Log("Constructor");
      Instance = this;
    }

    public override void OnLoad(ConfigNode node) {
      Log("OnLoad");
      base.OnLoad(node);
      network = new Network(node.GetNode("network"));
      if (node.HasNode("window")) {
        var window = node.GetNode("window");
        show_window_ = Convert.ToBoolean(window.GetValue("show"));
        window_.x = Convert.ToSingle(window.GetValue("x"));
        window_.y = Convert.ToSingle(window.GetValue("y"));
      }
    }

    public override void OnSave(ConfigNode node) {
      Log("OnSave");
      base.OnSave(node);
      network.Serialize(node.AddNode("network"));
      var window = node.AddNode("window");
      window.SetValue("show", show_window_, createIfNotFound : true);
      window.SetValue("x", window_.x, createIfNotFound : true);
      window.SetValue("y", window_.y, createIfNotFound : true);
    }

    private void OnGUI() {
      if (KSP.UI.Screens.ApplicationLauncher.Ready && toolbar_button_ == null) {
        LoadTextureIfExists(out UnityEngine.Texture toolbar_button_texture,
                            "skopos_telecom.png");
        toolbar_button_ =
            KSP.UI.Screens.ApplicationLauncher.Instance.AddModApplication(
                onTrue          : () => show_window_ = true,
                onFalse         : () => show_window_ = false,
                onHover         : null,
                onHoverOut      : null,
                onEnable        : null,
                onDisable       : null,
                visibleInScenes : KSP.UI.Screens.ApplicationLauncher.AppScenes.
                    ALWAYS,
                texture         : toolbar_button_texture);
      }
      // Make sure the state of the toolbar button remains consistent with the
      // state of the window.
      if (show_window_) {
        toolbar_button_?.SetTrue(makeCall : false);
      } else {
        toolbar_button_?.SetFalse(makeCall : false);
      }

      if (show_window_) {
        window_ = UnityEngine.GUILayout.Window(
          GetHashCode(), window_, DrawWindow, "Σκοπός Telecom network overview");
      }
    }

    private void OnDisable() {
      Log("OnDisable");
      if (toolbar_button_ != null) {
        KSP.UI.Screens.ApplicationLauncher.Instance.RemoveModApplication(
            toolbar_button_);
      }
    }

    private bool LoadTextureIfExists(out UnityEngine.Texture texture,
                                     string path) {
      string full_path =
          KSPUtil.ApplicationRootPath + Path.DirectorySeparatorChar +
          "GameData" + Path.DirectorySeparatorChar +
          "Skopos" + Path.DirectorySeparatorChar +
          path;
      if (File.Exists(full_path)) {
        var texture2d = new UnityEngine.Texture2D(2, 2);
        UnityEngine.ImageConversion.LoadImage(
            texture2d,
            File.ReadAllBytes(full_path));
        texture = texture2d;
        return true;
      } else {
        texture = null;
        return false;
      }
    }

    private void FixedUpdate() {
      if (HighLogic.LoadedScene != GameScenes.EDITOR) {
        // Time does not advance in the VAB, but after a revert, it is incorrectly stuck in the past.
        ut_ = Planetarium.GetUniversalTime();
      }
      network?.Refresh();
    }

    private void DrawWindow(int id) {
      using (new UnityEngine.GUILayout.VerticalScope()) {
        show_network_ = UnityEngine.GUILayout.Toggle(show_network_, "Show network");
        if (false) {
          using (new UnityEngine.GUILayout.HorizontalScope()) {
            if (UnityEngine.GUILayout.Button("Add nominal location") && FlightGlobals.ActiveVessel != null) {
              network.AddNominalLocation(FlightGlobals.ActiveVessel);
              return;
            }
            if (UnityEngine.GUILayout.Button("Clear nominal locations")) {
              network.ClearNominalLocations();
              return;
            }
            network.freeze_customers_ = UnityEngine.GUILayout.Toggle(network.freeze_customers_, "Freeze customers");
          }
          foreach (Vector3d location in network.GetNominalLocationLatLonAlts()) {
            UnityEngine.GUILayout.Label($"{location.x:F2}°, {location.y:F2}°, {location.z / 1000:F0} km");
          }
          using (new UnityEngine.GUILayout.HorizontalScope()) {
            if (int.TryParse(UnityEngine.GUILayout.TextField(network.customer_pool_size.ToString()), out int pool_size)) {
              network.customer_pool_size = Math.Max(pool_size, 0);
            }
          }
        }
        foreach (var connection in network.connections) {
          RATools.PrettyPrintDataRate(connection.data_rate);
          if (connection is PointToMultipointConnection point_to_multipoint) {
            var tx = network.GetStation(point_to_multipoint.tx_name);
            UnityEngine.GUILayout.Label(
                $"{tx.displaynodeName} to",
                UnityEngine.GUILayout.Width(25 * 30));
            for (int i = 0; i < point_to_multipoint.rx_names.Length; ++i) {
              var services = point_to_multipoint.channel_services[i];
              bool available = services.basic.available;
              string status = available
                  ? $"Connected ({services.actual_latency} s)"
                  : "Disconnected";
              var rx = network.GetStation(point_to_multipoint.rx_names[i]);
              UnityEngine.GUILayout.Label(
                $@"{rx.displaynodeName}: {status}");
              if (!available) {
                network.routing_.FindChannelsInIsolation(
                    tx.Comm,
                    new[]{rx.Comm},
                    latency_limit: double.PositiveInfinity,
                    connection.data_rate,
                    out Routing.Channel[] channels);
                bool purely_latency_limited = channels[0] != null;
                if (purely_latency_limited) {
                  UnityEngine.GUILayout.Label(
                      $"Latency-limited: available at {channels[0].latency} s");
                }
                network.routing_.FindChannelsInIsolation(
                    tx.Comm,
                    new[]{rx.Comm},
                    connection.latency_limit,
                    data_rate: 0,
                    out channels);
                bool purely_rate_limited = channels[0] != null;
                if (purely_rate_limited) {
                  string max_data_rate = RATools.PrettyPrintDataRate(
                      (from link in channels[0].links
                       select link.max_data_rate).Min());
                  UnityEngine.GUILayout.Label(
                      $"Limited by data rate: available at {max_data_rate}");
                }
                if (!purely_rate_limited && !purely_latency_limited) {
                  network.routing_.FindChannelsInIsolation(
                      tx.Comm,
                      new[]{rx.Comm},
                      latency_limit: double.PositiveInfinity,
                      data_rate: 0,
                      out channels);
                  if (channels[0] != null) {
                    string max_data_rate = RATools.PrettyPrintDataRate(
                        (from link in channels[0].links
                         select link.max_data_rate).Min());
                    UnityEngine.GUILayout.Label(
                        "Limited by both latency and data rate: available at " +
                        $"{max_data_rate}, {channels[0].latency} s");
                  }
                }
                if (connection.exclusive) {
                  // TODO(egg): analyze capacity issues.
                }
              }
            }
          } else if (connection is DuplexConnection duplex) {
            var trx0 = network.GetStation(duplex.trx_names[0]);
            var trx1 = network.GetStation(duplex.trx_names[1]);
            bool available = duplex.basic_service.available;
            string status = available
                ? $"Connected ({duplex.actual_latency} s)"
                : "Disconnected";
            UnityEngine.GUILayout.Label(
                $"Duplex between {trx0.displaynodeName} and {trx1.displaynodeName}: {status}",
                UnityEngine.GUILayout.Width(25 * 30));
            if (!available) {
              var circuit = network.routing_.FindCircuitInIsolation(
                  trx0.Comm,
                  trx1.Comm,
                  round_trip_latency_limit: double.PositiveInfinity,
                  connection.data_rate);
              bool purely_latency_limited = circuit != null;
              if (purely_latency_limited) {
                UnityEngine.GUILayout.Label(
                    $@"Latency-limited: available at {
                        circuit.round_trip_latency} s");
              }
              circuit = network.routing_.FindCircuitInIsolation(
                  trx0.Comm,
                  trx1.Comm,
                  connection.latency_limit,
                  one_way_data_rate: 0);
              bool purely_rate_limited = circuit != null;
              if (purely_rate_limited) {
                string max_data_rate = RATools.PrettyPrintDataRate(
                    Math.Min(
                        (from link in circuit.forward.links
                          select link.max_data_rate).Min(),
                        (from link in circuit.backward.links
                          select link.max_data_rate).Min()));
                UnityEngine.GUILayout.Label(
                    $"Limited by data rate: available at {max_data_rate}");
              }
              if (!purely_rate_limited && !purely_latency_limited) {
                circuit = network.routing_.FindCircuitInIsolation(
                    trx0.Comm,
                    trx1.Comm,
                    round_trip_latency_limit: double.PositiveInfinity,
                    one_way_data_rate: 0);
                if (circuit != null) {
                  string max_data_rate = RATools.PrettyPrintDataRate(
                      Math.Min(
                          (from link in circuit.forward.links
                            select link.max_data_rate).Min(),
                          (from link in circuit.backward.links
                            select link.max_data_rate).Min()));
                  UnityEngine.GUILayout.Label(
                      "Limited by both latency and data rate: available at " +
                      $"{max_data_rate}, {circuit.round_trip_latency} s");
                }
              }
              if (connection.exclusive) {
                // TODO(egg): analyze capacity issues.
              }
            }
          }
        }
        network.hide_off_network = show_network_;
      }
      UnityEngine.GUI.DragWindow();
    }

    private void LateUpdate() {
      if (!show_network_) {
        return;
      }
      var ui = CommNet.CommNetUI.Instance as RACommNetUI;
      if (ui == null) {
        return;
      }
      HashSet<RACommNode> stations =
          (from station in network.AllGround() select station.Comm).ToHashSet();
      foreach (var station in stations) {
        ui.OverrideShownCones.Add(station);
      }
      foreach (var link in CommNet.CommNetNetwork.Instance.CommNet.Links) {
        if (link.a is RACommNode node_a &&
            (node_a.ParentVessel != null || stations.Contains(node_a)) &&
            link.b is RACommNode node_b &&
            (node_b.ParentVessel != null || stations.Contains(node_b))) {
          ui.OverrideShownLinks.Add(link);
        }
      }
    }


    public static Telecom Instance { get; private set; }

    public Network network { get; private set; }
    private bool show_network_ = true;
    private bool show_window_ = true;
    private UnityEngine.Rect window_;
    public double last_universal_time => ut_;
    [KSPField(isPersistant = true)]
    private double ut_;
    private KSP.UI.Screens.ApplicationLauncherButton toolbar_button_;
  }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RealAntennas;
using RealAntennas.Network;
using RealAntennas.MapUI;
using RealAntennas.Targeting;

namespace σκοπός {
  [KSPScenario(
    ScenarioCreationOptions.AddToAllGames,
    new[] { GameScenes.SPACECENTER, GameScenes.TRACKSTATION, GameScenes.FLIGHT, GameScenes.EDITOR })]
  public sealed class Telecom : ScenarioModule {

    public Telecom() {
      Instance = this;
    }

    public override void OnLoad(ConfigNode node) {
      base.OnLoad(node);
      network = new Network(node.GetNode("network"));
    }

    public override void OnSave(ConfigNode node) {
      base.OnSave(node);
      network.Serialize(node.AddNode("network"));
    }

    private void OnGUI() {
      window_ = UnityEngine.GUILayout.Window(
        GetHashCode(), window_, DrawWindow, "Σκοπός Telecom network overview");
    }

    private void FixedUpdate() {
      if (HighLogic.LoadedScene != GameScenes.EDITOR) {
        network?.Refresh();
      }
    }

    private void DrawWindow(int id) {
      using (new UnityEngine.GUILayout.VerticalScope()) {
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
        using (new UnityEngine.GUILayout.HorizontalScope()) {
          UnityEngine.GUILayout.Label(@"Tx\Rx", UnityEngine.GUILayout.Width(3 * 20));
          for (int rx = 0; rx < network.all_ground_.Length; ++rx) {
            if (!network.rx_.Contains(network.all_ground_[rx])) {
              continue;
            }
            UnityEngine.GUILayout.Label($"{rx + 1}", UnityEngine.GUILayout.Width(6 * 20));
          }
        }
        for (int tx = 0; tx < network.all_ground_.Length; ++tx) {
          if (!network.tx_.Contains(network.all_ground_[tx])) {
            continue;
          }
          using (new UnityEngine.GUILayout.HorizontalScope()) {
            UnityEngine.GUILayout.Label($"{tx + 1}", UnityEngine.GUILayout.Width(3 * 20));
            for (int rx = 0; rx < network.all_ground_.Length; ++rx) {
              if (!network.rx_.Contains(network.all_ground_[rx])) {
                continue;
              }
              double rate = network.ground_edges_[tx, rx].current_rate;
              double latency = network.ground_edges_[tx, rx].current_latency;
              UnityEngine.GUILayout.Label(
                double.IsNaN(latency) || double.IsNaN(latency)
                  ? "—"
                  : $"{RATools.PrettyPrintDataRate(rate)}\n" +
                    $"{latency * 1000:F0} ms\n",
                UnityEngine.GUILayout.Width(6 * 20));
            }
          }
        }
        for (int i = 0; i < network.all_ground_.Length; ++i) {
          var station = network.all_ground_[i];
          var antenna = station.Comm.RAAntennaList[0];
          string role =
            (network.tx_.Contains(station) ? "T" : "") +
            (network.rx_.Contains(station) ? "R" : "") + "x";
          UnityEngine.GUILayout.Label(
            $@"{i + 1}: {role} {station.nodeName} {
              (antenna.Target == null ? "Tracking" : "Fixed")}");
        }
        foreach (var vessel_time in network.space_segment_) {
          double age_s = Planetarium.GetUniversalTime() - vessel_time.Value;
          string age = null;
          if (age_s > 2 * KSPUtil.dateTimeFormatter.Day) {
            age = $"{age_s / KSPUtil.dateTimeFormatter.Day:F0} days ago";
          } else if (age_s > 2 * KSPUtil.dateTimeFormatter.Hour) {
            age = $"{age_s / KSPUtil.dateTimeFormatter.Hour:F0} hours ago";
          } else if (age_s > 2 * KSPUtil.dateTimeFormatter.Minute) {
            age = $"{age_s / KSPUtil.dateTimeFormatter.Minute:F0} minutes ago";
          } else if (age_s > 2) {
            age = $"{age_s:F0} seconds ago";
          }
          using (new UnityEngine.GUILayout.HorizontalScope()) {
            UnityEngine.GUILayout.Label($"{vessel_time.Key.name} {age}");
            if (age != null &&
              UnityEngine.GUILayout.Button("Remove", UnityEngine.GUILayout.Width(4 * 20))) {
              network.space_segment_.Remove(vessel_time.Key);
              return;
            }
          }
        }
        show_network_ = UnityEngine.GUILayout.Toggle(show_network_, "Show network");
        show_active_links_ = UnityEngine.GUILayout.Toggle(show_active_links_, "Active links only");
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
      foreach (var station in network.all_ground_) {
        ui.OverrideShownCones.Add(station.Comm);
      }
      foreach (var satellite in network.space_segment_.Keys) {
        ui.OverrideShownCones.Add(satellite.Connection.Comm as RACommNode);
      }
      if (show_active_links_) {
        ui.OverrideShownLinks.AddRange(network.active_links_);
      } else {
        foreach (var station in network.all_ground_) {
          ui.OverrideShownLinks.AddRange(station.Comm.Values);
        }
        foreach (var satellite in network.space_segment_.Keys) {
          foreach (var link in satellite.Connection.Comm.Values) {
            Vessel vessel = (link.b as RACommNode).ParentVessel;
            if (vessel != null && network.space_segment_.ContainsKey(vessel)) {
              ui.OverrideShownLinks.Add(link);
            }
          }
        }
      }
    }


    public static Telecom Instance { get; private set; }

    public Network network { get; private set; }
    private bool show_network_ = true;
    private bool show_active_links_ = true;
    private UnityEngine.Rect window_;
  }
}

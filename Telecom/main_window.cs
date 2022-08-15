using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RealAntennas;
using static VehiclePhysics.Block;

namespace σκοπός {

internal class MainWindow : principia.ksp_plugin_adapter.SupervisedWindowRenderer {
  public MainWindow(Telecom telecom) : base(telecom) {
    telecom_ = telecom;
  }

  public bool show_network { get; private set; } = true;

  protected override string Title => "Σκοπός Telecom network overview";

  protected override void RenderWindowContents(int window_id) {
    using (new UnityEngine.GUILayout.VerticalScope()) {
      show_network = UnityEngine.GUILayout.Toggle(show_network, "Show network");
      if (false) {
        using (new UnityEngine.GUILayout.HorizontalScope()) {
          if (UnityEngine.GUILayout.Button("Add nominal location") && FlightGlobals.ActiveVessel != null) {
            telecom_.network.AddNominalLocation(FlightGlobals.ActiveVessel);
            return;
          }
          if (UnityEngine.GUILayout.Button("Clear nominal locations")) {
            telecom_.network.ClearNominalLocations();
            return;
          }
          telecom_.network.freeze_customers_ =
              UnityEngine.GUILayout.Toggle(telecom_.network.freeze_customers_, "Freeze customers");
        }
        foreach (Vector3d location in telecom_.network.GetNominalLocationLatLonAlts()) {
          UnityEngine.GUILayout.Label($"{location.x:F2}°, {location.y:F2}°, {location.z / 1000:F0} km");
        }
        using (new UnityEngine.GUILayout.HorizontalScope()) {
          if (int.TryParse(UnityEngine.GUILayout.TextField(
                telecom_.network.customer_pool_size.ToString()), out int pool_size)) {
            telecom_.network.customer_pool_size = Math.Max(pool_size, 0);
          }
        }
      }
      foreach (var connection in telecom_.network.connections) {
        if (telecom_.network.connection_to_contracts[connection].Count == 0) {
          if (inspectors_.ContainsKey(connection)) {
            inspectors_[connection].DisposeWindow();
            inspectors_.Remove(connection);
          }
          continue;
        }
        if (!inspectors_.ContainsKey(connection)) {
          inspectors_[connection] = new ConnectionInspector(telecom_, connection);
        }

        if (connection is PointToMultipointConnection point_to_multipoint) {
          var tx = telecom_.network.GetStation(point_to_multipoint.tx_name);
          if (point_to_multipoint.channel_services.Length == 1) {
            var services = point_to_multipoint.channel_services[0];
            var rx = telecom_.network.GetStation(point_to_multipoint.rx_names[0]);
            bool available = services.basic.available;
            string status = available ? "OK" : "D/C";
            using (new UnityEngine.GUILayout.HorizontalScope()) {
              UnityEngine.GUILayout.Label(
                  $"From {tx.displaynodeName} to {rx.displaynodeName}: {status}",
                  GUILayoutWidth(15));
              inspectors_[connection].RenderButton();
            }
          } else {
            using (new UnityEngine.GUILayout.HorizontalScope()) {
              UnityEngine.GUILayout.Label(
                  $"Broadcast from {tx.displaynodeName} to:",
                  GUILayoutWidth(15));
              inspectors_[connection].RenderButton();
            }
          }
          for (int i = 0; i < point_to_multipoint.rx_names.Length; ++i) {
            var services = point_to_multipoint.channel_services[i];
            bool available = services.basic.available;
            string status = available ? "OK" : "D/C";
            var rx = telecom_.network.GetStation(point_to_multipoint.rx_names[i]);
            if (point_to_multipoint.rx_names.Length > 1) {
              UnityEngine.GUILayout.Label(
                $@"— {rx.displaynodeName}: {status}");
            }
          }
        } else if (connection is DuplexConnection duplex) {
          var trx0 = telecom_.network.GetStation(duplex.trx_names[0]);
          var trx1 = telecom_.network.GetStation(duplex.trx_names[1]);
          bool available = duplex.basic_service.available;
          string status = available ? "OK" : "D/C";
          using (new UnityEngine.GUILayout.HorizontalScope()) {
            UnityEngine.GUILayout.Label(
                $@"Duplex  between {trx0.displaynodeName} and {trx1.displaynodeName}: {status}",
                GUILayoutWidth(15));
            inspectors_[connection].RenderButton();
          }
        }
        string contracts = string.Join(
        ", ",
            from contract in telecom_.network.connection_to_contracts[connection]
            select contract.Title);
          UnityEngine.GUILayout.Label("• For " + contracts);
      }
      telecom_.network.hide_off_network = show_network;
    }
    UnityEngine.GUI.DragWindow();
  }

  private Telecom telecom_;
  private Dictionary<Connection, ConnectionInspector> inspectors_ =
      new Dictionary<Connection, ConnectionInspector>();
}

}

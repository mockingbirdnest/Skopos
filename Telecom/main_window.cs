using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RealAntennas;

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
        string contracts;
        if (telecom_.network.connection_to_contracts[connection].Count == 0) {
          continue;
        } else {
          contracts = string.Join(
              ", ",
              from contract in telecom_.network.connection_to_contracts[connection]
              select contract.Title);
        }
        UnityEngine.GUILayout.Label(contracts + ":");
        string desired_data_rate = RATools.PrettyPrintDataRate(connection.data_rate);
        if (connection is PointToMultipointConnection point_to_multipoint) {
          var tx = telecom_.network.GetStation(point_to_multipoint.tx_name);
          if (point_to_multipoint.channel_services.Length == 1) {
            var services = point_to_multipoint.channel_services[0];
            var rx = telecom_.network.GetStation(point_to_multipoint.rx_names[0]);
            bool available = services.basic.available;
            string status = available
                ? $"Connected ({services.actual_latency * 1000:N0} ms)"
                : "Disconnected";
            UnityEngine.GUILayout.Label(
                $"{desired_data_rate}, ≤ {connection.latency_limit * 1000:N0} ms from {tx.displaynodeName} to {rx.displaynodeName}: {status}",
              GUILayoutWidth(35));
          } else {
            UnityEngine.GUILayout.Label(
                $"Broadcast {desired_data_rate}, ≤ {connection.latency_limit * 1000:N0} ms from {tx.displaynodeName} to:",
              GUILayoutWidth(35));

          }
          for (int i = 0; i < point_to_multipoint.rx_names.Length; ++i) {
            var services = point_to_multipoint.channel_services[i];
            bool available = services.basic.available;
            string status = available
                ? $"Connected ({services.actual_latency * 1000:N0} ms)"
                : "Disconnected";
            var rx = telecom_.network.GetStation(point_to_multipoint.rx_names[i]);
            if (point_to_multipoint.rx_names.Length > 1) {
              UnityEngine.GUILayout.Label(
                $@"— {rx.displaynodeName}: {status}");
            }
            if (!available) {
              telecom_.network.routing_.FindChannelsInIsolation(
                  tx.Comm,
                  new[]{rx.Comm},
                  latency_limit: double.PositiveInfinity,
                  connection.data_rate,
                  out Routing.Channel[] channels);
              bool purely_latency_limited = channels[0] != null;
              if (purely_latency_limited) {
                UnityEngine.GUILayout.Label(
                    $"→ Latency-limited: available at {channels[0].latency * 1000:N0} ms");
              }
              telecom_.network.routing_.FindChannelsInIsolation(
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
                    $"→ Limited by data rate: available at {max_data_rate}");
              }
              if (!purely_rate_limited && !purely_latency_limited) {
                telecom_.network.routing_.FindChannelsInIsolation(
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
                      "→ Limited by both latency and data rate: available at " +
                      $"{max_data_rate}, {channels[0].latency * 1000:N0} ms");
                }
              }
              if (connection.exclusive) {
                // TODO(egg): analyze capacity issues.
              }
            }
          }
        } else if (connection is DuplexConnection duplex) {
          var trx0 = telecom_.network.GetStation(duplex.trx_names[0]);
          var trx1 = telecom_.network.GetStation(duplex.trx_names[1]);
          bool available = duplex.basic_service.available;
          string status = available
              ? $"Connected ({duplex.actual_latency * 1000:N0} ms)"
              : "Disconnected";
          UnityEngine.GUILayout.Label(
              $@"Duplex ({desired_data_rate} one-way, ≤ {connection.latency_limit * 1000:N0} ms round-trip) between {trx0.displaynodeName} and {trx1.displaynodeName}: {status}",
              GUILayoutWidth(35));
          if (!available) {
            var circuit = telecom_.network.routing_.FindCircuitInIsolation(
                trx0.Comm,
                trx1.Comm,
                round_trip_latency_limit: double.PositiveInfinity,
                connection.data_rate);
            bool purely_latency_limited = circuit != null;
            if (purely_latency_limited) {
              UnityEngine.GUILayout.Label(
                  $@"→ Latency-limited: available at {
                      circuit.round_trip_latency * 1000:N0} ms");
            }
            circuit = telecom_.network.routing_.FindCircuitInIsolation(
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
                  $"→ Limited by data rate: available at {max_data_rate}");
            }
            if (!purely_rate_limited && !purely_latency_limited) {
              circuit = telecom_.network.routing_.FindCircuitInIsolation(
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
                    "→ Limited by both latency and data rate: available at " +
                    $"{max_data_rate}, {circuit.round_trip_latency * 1000:N0} ms");
              }
            }
            if (connection.exclusive) {
              // TODO(egg): analyze capacity issues.
            }
          }
        }
      }
      telecom_.network.hide_off_network = show_network;
    }
    UnityEngine.GUI.DragWindow();
  }

  private Telecom telecom_;
}

}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommNet.Network;
using RealAntennas;

namespace σκοπός {

internal class ConnectionInspector : principia.ksp_plugin_adapter.SupervisedWindowRenderer {
    public ConnectionInspector(Telecom telecom, Connection connection)
      : base(telecom) {
    telecom_ = telecom;
    connection_ = connection;
  }

  protected override string Title => "Connection inspector";

  private void ShowChannel(Routing.Channel channel) {
    if (channel == null) {
        return;
      }
    UnityEngine.GUILayout.Label(channel.links[0].tx.displayName);
    foreach (var link in channel.links) {
      UnityEngine.GUILayout.Label(
          $"↓ {link.length / 299792458 * 1000:N0} ms");
      UnityEngine.GUILayout.Label(link.rx.displayName);
    }
  }

  protected override void RenderWindowContents(int window_id) {
    string desired_data_rate = RATools.PrettyPrintDataRate(connection_.data_rate);
    if (connection_ is PointToMultipointConnection point_to_multipoint) {
      UnityEngine.GUILayout.Label(
          $"Data rate: {desired_data_rate}");
      UnityEngine.GUILayout.Label(
          $"Latency limit: {connection_.latency_limit * 1000:N0} ms");
      foreach (double latency in
               point_to_multipoint.channel_services[0].improved_by_latency.Keys) {
        UnityEngine.GUILayout.Label(
            $"Improved service latency: {latency * 1000:N0} ms");
      }

      var tx = telecom_.network.GetStation(point_to_multipoint.tx_name);
      if (point_to_multipoint.channel_services.Length == 1) {
        var services = point_to_multipoint.channel_services[0];
        var rx = telecom_.network.GetStation(point_to_multipoint.rx_names[0]);
        bool available = services.basic.available;
        string status = available ? $"Connected" : "Disconnected";
        UnityEngine.GUILayout.Label(
          $"Transmission from {tx.displaynodeName} to {rx.displaynodeName}: {status}");
      } else {
        UnityEngine.GUILayout.Label($"Broadcast to:");

      }
      for (int i = 0; i < point_to_multipoint.rx_names.Length; ++i) {
        var services = point_to_multipoint.channel_services[i];
        bool available = services.basic.available;
        string status = available ? "Connected" : "Disconnected";
        var rx = telecom_.network.GetStation(point_to_multipoint.rx_names[i]);
        Action<Action> indent = (Action x) => x();
        if (point_to_multipoint.rx_names.Length > 1) {
          indent = (Action x) => {
            using (new UnityEngine.GUILayout.HorizontalScope()) {
              UnityEngine.GUILayout.Space(Width(1));
              x();
            }
          };
          UnityEngine.GUILayout.Label(
            $@"— {rx.displaynodeName}: {status}");
        }
        if (available) {
          ShowChannel(services.channel);
        } else {
          Routing.Channel[] channels;
          bool capacity_limited = false;
          if (connection_.exclusive) {
            telecom_.network.routing_.FindChannelsInIsolation(
                tx.Comm,
                new[]{rx.Comm},
                connection_.latency_limit,
                connection_.data_rate,
                out channels);
            if (channels[0] != null) {
              capacity_limited = true;
              indent(() =>
                UnityEngine.GUILayout.Label(
                    $"→ Limited by capacity: available in isolation:"));
              ShowChannel(channels[0]);
            }
          }
          if (!capacity_limited) {
            telecom_.network.routing_.FindChannelsInIsolation(
                tx.Comm,
                new[]{rx.Comm},
                latency_limit: double.PositiveInfinity,
                connection_.data_rate,
                out channels);
            bool purely_latency_limited = channels[0] != null;
            if (purely_latency_limited) {
              indent(() =>
                UnityEngine.GUILayout.Label(
                    $"→ Latency-limited: available at {channels[0].latency * 1000:N0} ms:"));
                ShowChannel(channels[0]);
            }
            telecom_.network.routing_.FindChannelsInIsolation(
                tx.Comm,
                new[]{rx.Comm},
                connection_.latency_limit,
                data_rate: 0,
                out channels);
            bool purely_rate_limited = channels[0] != null;
            if (purely_rate_limited) {
              string max_data_rate = RATools.PrettyPrintDataRate(
                  (from link in channels[0].links
                    select link.max_data_rate).Min());
              indent(() =>
                UnityEngine.GUILayout.Label(
                    $"→ Limited by data rate: available at {max_data_rate}"));
              ShowChannel(channels[0]);
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
                indent(() =>
                  UnityEngine.GUILayout.Label(
                      "→ Limited by both latency and data rate: available at " +
                      $"{max_data_rate}, {channels[0].latency * 1000:N0} ms"));
                ShowChannel(channels[0]);
              }
            }
          }
        }
        ShowChannel(services.channel);
      }
    } else if (connection_ is DuplexConnection duplex) {
      var trx0 = telecom_.network.GetStation(duplex.trx_names[0]);
      var trx1 = telecom_.network.GetStation(duplex.trx_names[1]);
      bool available = duplex.basic_service.available;
      string status = available
          ? $"Connected ({duplex.actual_latency * 1000:N0} ms)"
          : "Disconnected";
      UnityEngine.GUILayout.Label(
          $@"Duplex ({desired_data_rate} one-way, ≤ {connection_.latency_limit * 1000:N0} ms round-trip) between {trx0.displaynodeName} and {trx1.displaynodeName}: {status}",
          GUILayoutWidth(35));
      if (!available) {
        var circuit = telecom_.network.routing_.FindCircuitInIsolation(
            trx0.Comm,
            trx1.Comm,
            round_trip_latency_limit: double.PositiveInfinity,
            connection_.data_rate);
        bool purely_latency_limited = circuit != null;
        if (purely_latency_limited) {
          UnityEngine.GUILayout.Label(
              $@"→ Latency-limited: available at {
                  circuit.round_trip_latency * 1000:N0} ms");
        }
        circuit = telecom_.network.routing_.FindCircuitInIsolation(
            trx0.Comm,
            trx1.Comm,
            connection_.latency_limit,
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
        if (connection_.exclusive) {
          // TODO(egg): analyze capacity issues.
        }
      }
    }
    UnityEngine.GUI.DragWindow();
  }

  public void RenderButton() {
    if (UnityEngine.GUILayout.Button("Inspect…")) {
      Toggle();
    }
  }

    private Telecom telecom_;
    private Connection connection_;
}

}

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
    if (connection_ is PointToMultipointConnection point_to_multipoint &&
        point_to_multipoint.rx_names.Length > 1) {
      receiver_open_ = new bool[point_to_multipoint.rx_names.Length];
    }
  }

  protected override string Title => "Connection inspector";

  private void ShowCircuit(Routing.Circuit circuit) {
    if (circuit == null) {
      return;
    }
    using (new UnityEngine.GUILayout.HorizontalScope()) {
      using (new UnityEngine.GUILayout.VerticalScope()) { 
        ShowChannel(circuit.forward);
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        ShowChannel(circuit.backward);
      }
    }
  }

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
        if (receiver_open_ != null) {
          using (new UnityEngine.GUILayout.HorizontalScope()) {
            if (UnityEngine.GUILayout.Button(
                  receiver_open_[i] ? "−" : "+", GUILayoutWidth(1))) {
              receiver_open_[i] = !receiver_open_[i];
              Shrink();
              return;
            }
            UnityEngine.GUILayout.Label($"{rx.displaynodeName}: {status}");
          }
        }
        if (receiver_open_?[i] != false) {
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
                UnityEngine.GUILayout.Label(
                      $"→ Limited by capacity: available in isolation:");
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
                UnityEngine.GUILayout.Label(
                    $"→ Latency-limited: available at {channels[0].latency * 1000:N0} ms:");
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
                UnityEngine.GUILayout.Label(
                    $"→ Limited by data rate: available at {max_data_rate}");
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
                  UnityEngine.GUILayout.Label(
                      "→ Limited by both latency and data rate: available at " +
                      $"{max_data_rate}, {channels[0].latency * 1000:N0} ms");
                  ShowChannel(channels[0]);
                }
              }
            }
          }
        }
      }
    } else if (connection_ is DuplexConnection duplex) {
      UnityEngine.GUILayout.Label(
          $"One-way data rate: {desired_data_rate}");
      UnityEngine.GUILayout.Label(
          $"Round-trip latency limit: {connection_.latency_limit * 1000:N0} ms");
      foreach (double latency in
               duplex.improved_service_by_latency.Keys) {
        UnityEngine.GUILayout.Label(
            $"Improved service round-trip latency: {latency * 1000:N0} ms");
      }
      var trx0 = telecom_.network.GetStation(duplex.trx_names[0]);
      var trx1 = telecom_.network.GetStation(duplex.trx_names[1]);
      bool available = duplex.basic_service.available;
      string status = available
          ? $"Connected ({duplex.actual_latency * 1000:N0} ms)"
          : "Disconnected";
      UnityEngine.GUILayout.Label(
          $@"Duplex between {trx0.displaynodeName} and {trx1.displaynodeName}: {status}");
      if (available) {
        ShowCircuit(duplex.circuit);
      } else {
        Routing.Circuit circuit;
        bool capacity_limited = false;
        if (connection_.exclusive) {
          circuit = telecom_.network.routing_.FindCircuitInIsolation(
              trx0.Comm,
              trx1.Comm,
              connection_.latency_limit,
              connection_.data_rate);
          if (circuit != null) {
            capacity_limited = true;
            UnityEngine.GUILayout.Label(
                  $"→ Limited by capacity: available in isolation:");
            ShowCircuit(circuit);
          } else {
            telecom_.network.routing_.FindChannelsInIsolation(
                trx0.Comm,
                new[] { trx1.Comm },
                connection_.latency_limit,
                connection_.data_rate,
                out Routing.Channel[] forward);
            telecom_.network.routing_.FindChannelsInIsolation(
                trx1.Comm,
                new[] { trx0.Comm },
                connection_.latency_limit,
                connection_.data_rate,
                out Routing.Channel[] backward);
            if (forward[0] != null && backward[0] != null) {
              capacity_limited = true;
              UnityEngine.GUILayout.Label(
                    $"→ Limited by capacity: available in simplex:");
              ShowCircuit(new Routing.Circuit(forward[0], backward[0]));
            }
          }
        }
        if (!capacity_limited) {
          circuit = telecom_.network.routing_.FindCircuitInIsolation(
              trx0.Comm,
              trx1.Comm,
              round_trip_latency_limit: double.PositiveInfinity,
              connection_.data_rate);
          bool purely_latency_limited = circuit != null;
          if (purely_latency_limited) {
            UnityEngine.GUILayout.Label(
                $@"→ Latency-limited: available at {
                    circuit.round_trip_latency * 1000:N0} ms");
            ShowCircuit(circuit);
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
            ShowCircuit(circuit);
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
              ShowCircuit(circuit);
            }
          }
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
    private bool[] receiver_open_;
}

}

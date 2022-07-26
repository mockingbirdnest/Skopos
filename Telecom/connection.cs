using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RealAntennas;

namespace σκοπός {
  public static class Connections {
    public static Connection New(ConfigNode definition) {
      if (definition.HasValue("tx")) {
        return new PointToMultipointConnection(definition);
      } else {
        return new DuplexConnection(definition);
      }
    }
  }

  public interface Connection {
    void AttemptConnection(Routing routing, Network network, double t);
    void Load(ConfigNode node);
    void Save(ConfigNode node);
    double data_rate { get; }
    double latency_limit { get; }
    bool exclusive { get; }
  }

  // A DuplexConnection represents simultaneous connection between two stations.
  public class DuplexConnection : Connection {
    public DuplexConnection(ConfigNode definition) {
      trx_names = definition.GetValues("trx");
      if (trx_names.Length != 2) {
        throw new ArgumentException(
            $@"Duplex connection between {trx_names.Length} terminals {
            string.Join(", ", trx_names)})");
      }
      exclusive = bool.Parse(definition.GetValue("exclusive"));
      latency_limit = double.Parse(definition.GetValue("latency"));
      data_rate = double.Parse(definition.GetValue("rate"));
      window_size_ = int.Parse(definition.GetValue("window"));
      basic_service = new Service(window_size_);
    }

    public void AttemptConnection(Routing routing, Network network, double t) {
      Routing.Circuit circuit;
      if (exclusive) {
        circuit = routing.FindAndUseAvailableCircuit(
            network.GetStation(trx_names[0]).Comm,
            network.GetStation(trx_names[1]).Comm,
            latency_limit, data_rate);
      } else {
        circuit = routing.FindCircuitInIsolation(
            network.GetStation(trx_names[0]).Comm,
            network.GetStation(trx_names[1]).Comm,
            latency_limit, data_rate);
      }
      basic_service.ReportAvailability(circuit != null, t);
      foreach (var latency_service in improved_service_by_latency) {
        double latency = latency_service.Key;
        Service service = latency_service.Value;
        bool available =
            circuit?.forward.latency + circuit?.backward.latency <= latency;
        basic_service.ReportAvailability(available, t);
      }
    }

    public void Load(ConfigNode node) {
      basic_service.Load(node.GetNode("basic_service"));
      foreach (var improved_service_node in
               node.GetNodes("improved_service")) {
        var latency = double.Parse(improved_service_node.GetValue("latency"));
        var improved_service = new Service(window_size_);
        improved_service.Load(improved_service_node);
        improved_service_by_latency[latency] = improved_service;
      }
    }

    public void Save(ConfigNode node) {
      basic_service.Save(node.AddNode("basic_service"));
      foreach (var latency_service in improved_service_by_latency) {
        double latency = latency_service.Key;
        Service improved_service = latency_service.Value;
        var improved_service_node = node.AddNode("improved_service");
        improved_service_node.AddValue("latency", latency);
        improved_service.Save(improved_service_node);
      }
    }

    public double data_rate { get; }
    public double latency_limit { get; }

    public bool exclusive { get; }

    public string[] trx_names { get; }

    public Service basic_service;
    public SortedDictionary<double, Service> improved_service_by_latency {
      get;
    } = new SortedDictionary<double, Service>();

    private int window_size_;
  }

  // A PointToMultipointConnection represents a communication from one station
  // to multiple others.  The availabilities are tracked separately for each
  // receiver.
  public class PointToMultipointConnection : Connection {
    public PointToMultipointConnection(ConfigNode definition) {
      tx_name = definition.GetValue("tx");
      rx_names = definition.GetValues("rx");
      exclusive = bool.Parse(definition.GetValue("exclusive"));
      latency_limit = double.Parse(definition.GetValue("latency"));
      data_rate = double.Parse(definition.GetValue("rate"));
      window_size_ = int.Parse(definition.GetValue("window"));
      channel_services = (from rx in rx_names
                           select new ChannelService(window_size_)).ToArray();
    }

    public void AttemptConnection(Routing routing, Network network, double t) {
      RACommNode tx = network.GetStation(tx_name).Comm;
      RACommNode[] rx = (from name in rx_names
                         select network.GetStation(name).Comm).ToArray();
      Routing.Channel[] channels;
      if (exclusive) {
        routing.FindAndUseAvailableChannels(
            tx, rx, latency_limit, data_rate, out channels);
      } else {
        routing.FindChannelsInIsolation(
            tx, rx, latency_limit, data_rate, out channels);
      }
      for (int i = 0; i < channels.Length; ++i) {
        Routing.Channel channel = channels[i];
        channel_services[i].basic.ReportAvailability(channel != null, t);
        foreach (var latency_service in
                 channel_services[i].improved_by_latency) {
          double latency = latency_service.Key;
          Service improved_service = latency_service.Value;
          improved_service.ReportAvailability(channel?.latency <= latency, t);
        }
      }
    }

    public void Load(ConfigNode node) {
      var channel_service_nodes = node.GetNodes("channel_service");
      for (int i = 0; i < channel_services.Length; ++i) {
        channel_services[i].basic.Load(
            channel_service_nodes[i].GetNode("basic"));
        foreach (var improved_service_node in
                 channel_service_nodes[i].GetNodes("improved")) {
          var latency = double.Parse(improved_service_node.GetValue("latency"));
          var improved_service = new Service(window_size_);
          improved_service.Load(improved_service_node);
          channel_services[i].improved_by_latency[latency] = improved_service;
        }
      }
    }

    public void Save(ConfigNode node) {
      foreach (var service in channel_services) {
        var channel_service_node = node.AddNode("channel_service");
        service.basic.Save(channel_service_node.AddNode("basic"));
        foreach (var latency_service in service.improved_by_latency) {
          double latency = latency_service.Key;
          Service improved_service = latency_service.Value;
          var improved_service_node = channel_service_node.AddNode("improved");
          improved_service_node.AddValue("latency", latency);
          improved_service.Save(improved_service_node);
        }
      }
    }

    public double data_rate { get; }
    public double latency_limit { get; }

    public string tx_name { get; }
    public string[] rx_names { get; }

    public bool exclusive { get; }

    public class ChannelService {
      public ChannelService(int window_size) {
        basic = new Service(window_size);
      }

      public Service basic;
      public SortedDictionary<double, Service> improved_by_latency =
          new SortedDictionary<double, Service>();
    }

    public ChannelService[] channel_services { get; private set; }
    private int window_size_;
  }
}

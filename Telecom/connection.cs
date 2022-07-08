using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RealAntennas;

namespace σκοπός {

  // A PointToMultipointConnection represents a communication from one station
  // to multiple others.  The availabilities are tracked separately for each
  // receiver.
  public class PointToMultipointConnection {
    public PointToMultipointConnection(ConfigNode definition) {
      tx_name = definition.GetValue("tx");
      rx_names = definition.GetValues("rx");
      exclusive = bool.Parse(definition.GetValue("exclusive"));
      latency_limit = double.Parse(definition.GetValue("latency"));
      data_rate = double.Parse(definition.GetValue("rate"));
      window_size_ = int.Parse(definition.GetValue("window"));
      channel_services_ = (from rx in rx_names
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
        channel_services_[i].basic.ReportAvailability(channel != null, t);
        foreach (var latency_service in
                 channel_services_[i].improved_by_latency) {
          double latency = latency_service.Key;
          Service improved_service = latency_service.Value;
          improved_service.ReportAvailability(channel?.latency <= latency, t);
        }
      }
    }

    public void Load(ConfigNode node) {
      var channel_service_nodes = node.GetNodes("channel_service");
      for (int i = 0; i < channel_services_.Length; ++i) {
        channel_services_[i].basic.Load(
            channel_service_nodes[i].GetNode("basic"));
        foreach (var improved_service_node in
                 channel_service_nodes[i].GetNodes("improved")) {
          var latency = double.Parse(improved_service_node.GetValue("latency"));
          var improved_service = new Service(window_size_);
          improved_service.Load(improved_service_node);
          channel_services_[i].improved_by_latency[latency] = improved_service;
        }
      }
    }

    public void Save(ConfigNode node) {
      foreach (var service in channel_services_) {
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

    public double latency_limit { get; }
    public double data_rate { get; }

    public bool connected { get; private set; }

    public string tx_name { get; }
    public string[] rx_names { get; }

    public bool exclusive { get; }

    private class ChannelService {
      public ChannelService(int window_size) {
        basic = new Service(window_size);
      }

      public Service basic;
      public SortedDictionary<double, Service> improved_by_latency =
          new SortedDictionary<double, Service>();
    }

    private ChannelService[] channel_services_;
    private int window_size_;
  }
}

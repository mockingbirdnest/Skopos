using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RealAntennas;
using RealAntennas.Network;

using static σκοπός.Routing.PointToMultipointAvailability;

namespace σκοπός {

public class Routing {

  public enum PointToMultipointAvailability {
    Unavailable,
    Partial,
    Available,
  }

  public void Reset() {
    links_.Clear();
  }

  public class Channel {
    public readonly List<OrientedLink> links = new List<OrientedLink>();
    public double latency;
  }

  public class Circuit {
    public readonly Channel forward;
    public readonly Channel backward;

    public Circuit(Channel forward, Channel backward) {
      this.forward = forward;
      this.backward = backward;
    }
  }

  public PointToMultipointAvailability AvailabilityInIsolation(
      RACommNode source,
      RACommNode[] destinations,
      double latency_threshold,
      double data_rate,
      out Channel[] channels) {
    return FindChannels(source,
                        destinations,
                        latency_threshold,
                        data_rate,
                        (link) => link.capacity,
                        out channels);
  }

  public PointToMultipointAvailability ConsumeIfAvailable(
      RACommNode source,
      RACommNode[] destinations,
      double latency_threshold,
      double data_rate,
      out Channel[] channels) {
    PointToMultipointAvailability availability = FindChannels(
        source,
        destinations,
        latency_threshold,
        data_rate,
        (link) => link.available_capacity,
        out channels);
    if (availability != Unavailable) {
      HashSet<OrientedLink> links_used =
        (from channel in channels where channel != null
         from link in channel.links select link).ToHashSet();
      foreach (OrientedLink link in links_used) {
        link.ConsumeCapacity(data_rate);
      }
    }
    return availability;
  }

  private Circuit FindCircuit(RACommNode source,
                              RACommNode destination,
                              double latency_threshold,
                              double one_way_data_rate,
                              Func<OrientedLink, double> link_capacity) {
    if (FindChannels(source,
                     new[]{destination},
                     latency_threshold,
                     one_way_data_rate,
                     link_capacity,
                     out Channel[] forward) == Unavailable) {
      return null;
    }
    var forward_links = forward[0].links.ToHashSet();
    if (FindChannels(source,
                     new[]{destination},
                     latency_threshold,
                     one_way_data_rate,
                     link => link_capacity(link) -
                             (forward_links.Contains(link) ? one_way_data_rate
                                                           : 0),
                     out Channel[] backward) == Unavailable) {
      return null;
    }
    return new Circuit(forward[0], backward[0]);
  }

  private PointToMultipointAvailability FindChannels(
      RACommNode source,
      RACommNode[] destinations,
      double latency_threshold,
      double data_rate,
      Func<OrientedLink, double> link_capacity,
      out Channel[] channels) {
    const double c = 299792458;
    // TODO(egg): consider using the stock intrusive data structure.
    var distances = new Dictionary<RACommNode, double>();
    var previous = new Dictionary<RACommNode, OrientedLink>();
    var boundary = new SortedDictionary<double, RACommNode>();
    var interior = new HashSet<RACommNode>();

    distances[source] = 0;
    boundary[0] = source;
    previous[source] = null;
    int rx_found = 0;
    channels = new Channel[destinations.Length];

    while (boundary.Count > 0) {
      double tx_distance = boundary.First().Key;
      RACommNode tx = boundary.First().Value;
      boundary.Remove(tx_distance);

      if (tx_distance > latency_threshold * c) {
        // We have run out of latency, no need to keep searching.
        return rx_found == 0 ? Unavailable : Partial;
      } else if (destinations.Contains(tx)) {
        int i = destinations.IndexOf(tx);
        channels[i] = new Channel();
        for (OrientedLink link = previous[tx];
            link != null;
            link = previous[link.tx]) {
           channels[i].links.Add(link);
        }
        channels[i].links.Reverse();
        channels[i].latency = tx_distance / c;
        ++rx_found;
        if (rx_found == channels.Length) {
          return PointToMultipointAvailability.Available;
        }
      }

      interior.Add(tx);

      // TODO(egg): continue if tx is an rx-only station.

      foreach (var stock_rx in tx.Keys) {
        var rx = (RACommNode)stock_rx;

      // TODO(egg): continue if rx is a tx-only station.

        if (interior.Contains(rx)) {
          continue;
        }

        var link = OrientedLink.Get(this, from: tx, to: rx);

        if (link_capacity(link) < data_rate) {
          continue;
        }

        double tentative_distance = tx_distance + link.length;
        if (distances.TryGetValue(rx, out double d)) {
          if (d <= tentative_distance) {
            continue;
          } else {
            boundary.Remove(d);
          }
        }

        distances[rx] = tentative_distance;
        // NOTE(egg): this will fail if we have equidistant nodes.
        boundary.Add(tentative_distance, rx);
        previous[rx] = link;
      }
    }
    return rx_found == 0 ? Unavailable : Partial;
  }

  private class LinkUsage {
    public readonly DirectedLinkUsage forward = new DirectedLinkUsage();
    public readonly DirectedLinkUsage backward = new DirectedLinkUsage();
  }

  private class DirectedLinkUsage {
    public double data_rate = 0;
    public readonly List<Connection> connections = new List<Connection>();
  }

  public class OrientedLink {
    public static OrientedLink Get(
        Routing routing,
        RACommNode from,
        RACommNode to) {
      if (!routing.links_.TryGetValue((from, to), out OrientedLink link)) {
        var ra_link = (RACommLink)from[to];
        bool forward = ra_link.a == from;
        link = new OrientedLink(from, to, ra_link, forward, routing);
        routing.links_.Add((from, to), link);
      }
      return link;
    }
    
    public RealAntenna tx_antenna => forward ? ra_link.FwdAntennaTx
                                             : ra_link.RevAntennaTx;
    public RealAntenna rx_antenna => forward ? ra_link.FwdAntennaRx
                                             : ra_link.RevAntennaRx;
    public double capacity => forward ? ra_link.FwdDataRate
                                      : ra_link.RevDataRate;
    public double available_capacity =>
        capacity * Math.Min(1 - routing_.TxUsage(tx_antenna),
                            1 - routing_.RxUsage(rx_antenna));
    // TODO(egg): this needs to be adapted once we have support for landlines.
    public double length => (tx.position - tx.position).magnitude;

    public void ConsumeCapacity(double data_rate) {
      double link_usage = data_rate / capacity;
      if (!routing_.tx_usage_.ContainsKey(tx_antenna)) {
        routing_.tx_usage_[tx_antenna] = 0;
      }
      if (!routing_.tx_usage_.ContainsKey(rx_antenna)) {
        routing_.rx_usage_[rx_antenna] = 0;
      }
      routing_.tx_usage_[tx_antenna] += link_usage;
      routing_.rx_usage_[rx_antenna] += link_usage;
    }

    public readonly RACommNode tx;
    public readonly RACommNode rx;
    public readonly RACommLink ra_link;
    public readonly bool forward;

    private OrientedLink(RACommNode tx,
                         RACommNode rx,
                         RACommLink ra_link,
                         bool forward,
                         Routing routing) {
      this.tx = tx;
      this.rx = rx;
      this.ra_link = ra_link;
      this.forward = forward;
      this.routing_ = routing;
    }

    private Routing routing_;
  }
  
  public double TxUsage(RealAntenna antenna) {
    if (tx_usage_.TryGetValue(antenna, out double usage)) {
      return usage;
    } else {
      return 0.0;
    }
  }

  public double RxUsage(RealAntenna antenna) {
    if (rx_usage_.TryGetValue(antenna, out double usage)) {
      return usage;
    } else {
      return 0.0;
    }
  }

  private readonly Dictionary<(RACommNode, RACommNode), OrientedLink> links_ =
      new Dictionary<(RACommNode, RACommNode), OrientedLink>();
  private readonly Dictionary<RealAntenna, double> rx_usage_ =
      new Dictionary<RealAntenna, double>();
  private readonly Dictionary<RealAntenna, double> tx_usage_ =
      new Dictionary<RealAntenna, double>();
}

}

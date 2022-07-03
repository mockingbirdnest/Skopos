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

  public void Reset(IEnumerable<RACommNode> tx_only,
                    IEnumerable<RACommNode> rx_only,
                    IEnumerable<RACommNode> multiple_tracking_tx) {
    links_.Clear();
    rx_spectrum_usage_.Clear();
    tx_power_usage_.Clear();
    tx_only_ = new HashSet<RACommNode>(tx_only);
    rx_only_ = new HashSet<RACommNode>(rx_only);
    multiple_tracking_tx_ = new HashSet<RACommNode>(multiple_tracking_tx);
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

  public Circuit AvailabilityInIsolation(
      RACommNode source,
      RACommNode destination,
      double latency_limit,
      double data_rate) {
    return FindCircuit(source,
                       destination,
                       latency_limit,
                       data_rate,
                       (link) => link.max_data_rate);
  }

  public Circuit ConsumeIfAvailable(
      RACommNode source,
      RACommNode destination,
      double latency_limit,
      double data_rate) {
    Circuit circuit = FindCircuit(
        source,
        destination,
        latency_limit,
        data_rate,
        (link) => link.available_capacity);
    if (circuit != null) {
      foreach (OrientedLink link in circuit.forward.links) {
        link.ConsumeCapacity(data_rate);
      }
      foreach (OrientedLink link in circuit.backward.links) {
        link.ConsumeCapacity(data_rate);
      }
    }
    return circuit;
  }

  public PointToMultipointAvailability AvailabilityInIsolation(
      RACommNode source,
      RACommNode[] destinations,
      double latency_limit,
      double data_rate,
      out Channel[] channels) {
    return FindChannels(source,
                        destinations,
                        latency_limit,
                        data_rate,
                        (link) => link.max_data_rate,
                        out channels);
  }

  public PointToMultipointAvailability ConsumeIfAvailable(
      RACommNode source,
      RACommNode[] destinations,
      double latency_limit,
      double data_rate,
      out Channel[] channels) {
    PointToMultipointAvailability availability = FindChannels(
        source,
        destinations,
        latency_limit,
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
                              double one_way_latency_limit,
                              double one_way_data_rate,
                              NetworkUsage usage) {
    if (FindChannels(source,
                     new[]{destination},
                     one_way_latency_limit,
                     one_way_data_rate,
                     usage,
                     out Channel[] forward) == Unavailable) {
      return null;
    }
    var forward_tx_power_usage = forward[0].links.ToDictionary(
        link => link.tx_antenna,
        link => link.TxPowerUsageFromDataRate(one_way_data_rate));
    var forward_rx_spectrum_usage = forward[0].links.ToDictionary(
        link => link.rx_antenna,
        link => link.RxSpectrumUsageFromDataRate(one_way_data_rate));
    NetworkUsage usage_with_forward_channel = new NetworkUsage {
      tx_power_usage = tx => {
        forward_tx_power_usage.TryGetValue(tx, out double forward_usage);
        return  usage.tx_power_usage(tx) + forward_usage;
      },
      rx_spectrum_usage = rx => {
        forward_rx_spectrum_usage.TryGetValue(rx, out double forward_usage);
        return  usage.rx_spectrum_usage(rx) + forward_usage;
      }
    };
    if (FindChannels(source,
                     new[]{destination},
                     one_way_latency_limit,
                     one_way_data_rate,
                     usage_with_forward_channel,
                     out Channel[] backward) == Unavailable) {
      return null;
    }
    return new Circuit(forward[0], backward[0]);
  }

  private PointToMultipointAvailability FindChannels(
      RACommNode source,
      RACommNode[] destinations,
      double latency_limit,
      double data_rate,
      NetworkUsage usage,
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
    bool is_point_to_multipoint = destinations.Length > 1;

    while (boundary.Count > 0) {
      double tx_distance = boundary.First().Key;
      RACommNode tx = boundary.First().Value;
      boundary.Remove(tx_distance);

      if (tx_distance > latency_limit * c) {
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

      if (rx_only_.Contains(tx)) {
        continue;
      }

      foreach (var stock_rx in tx.Keys) {
        var rx = (RACommNode)stock_rx;

        if (tx_only_.Contains(tx) || interior.Contains(rx)) {
          continue;
        }

        var link = OrientedLink.Get(this, from: tx, to: rx);

        if (is_point_to_multipoint &&
            !multiple_tracking_tx_.Contains(tx) &&
            !link.is_at_tx_tech_level) {
          // DRVeyl says: RA kindly assumes higher-tech equipment can
          // automatically realize it should run an earlier encoding!
          // We are less kind than that when broadcasting, because then the
          // lower-tech links would be incompatible with the higher-tech ones.
          // We could get away with just using the links at a given tech level,
          // e.g., the max or the min available, but then existing links would
          // become ineligible when adding a receiver, which would be deeply
          // confusing.  Intsead we just assume that along a broadcast channel,
          // everything but fancy Earth stations simply transmits using an
          // encoding and a modulation predetermined at construction.
          // For point-to-point connections we retain the RA kindness.
          continue;
        }

        if (link.CapacityWithUsage(usage) < data_rate) {
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

  public class NetworkUsage {
    public delegate double TxPowerUsage(RealAntennaDigital tx);
    public delegate double RxSpectrumUsage(RealAntennaDigital rx);
    public TxPowerUsage tx_power_usage = _ => 0;
    public RxSpectrumUsage rx_spectrum_usage = _ => 0;
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

    public readonly RACommNode tx;
    public readonly RACommNode rx;
    public readonly RACommLink ra_link;
    public readonly bool forward;

    public RealAntennaDigital tx_antenna =>
        (RealAntennaDigital)(forward ? ra_link.FwdAntennaTx
                                     : ra_link.RevAntennaTx);
    public RealAntennaDigital rx_antenna =>
        (RealAntennaDigital)(forward ? ra_link.FwdAntennaRx
                                     : ra_link.RevAntennaRx);
    public double max_data_rate => forward ? ra_link.FwdDataRate
                                           : ra_link.RevDataRate;
    public int tech_level => Math.Min(tx_antenna.TechLevelInfo.Level,
                                      rx_antenna.TechLevelInfo.Level);
    public bool is_at_tx_tech_level =>
        tech_level == tx_antenna.TechLevelInfo.Level;
    public RealAntennaDigital lowest_tech_antenna => 
        is_at_tx_tech_level ? tx_antenna : rx_antenna;
    public RAModulator modulator => lowest_tech_antenna.modulator;
    public RealAntennas.Antenna.Encoder encoder => lowest_tech_antenna.Encoder;
    // TODO(egg): this needs to be adapted once we have support for landlines.
    public double length => (tx.position - tx.position).magnitude;

    public double CapacityWithUsage(NetworkUsage usage) {
      double limiting_usage = Math.Max(usage.tx_power_usage(tx_antenna),
                                       usage.rx_spectrum_usage(rx_antenna));
      return max_data_rate * (1 - limiting_usage);
    }

    public double TxPowerUsageFromDataRate(double data_rate) {
      return data_rate / max_data_rate;
    }

    public double RxSpectrumUsageFromDataRate(double data_rate) {
      return data_rate / max_data_rate;
    }

    private OrientedLink(RACommNode tx,
                         RACommNode rx,
                         RACommLink ra_link,
                         bool forward,
                         Routing routing) {
      this.tx = tx;
      this.rx = rx;
      this.ra_link = ra_link;
      this.forward = forward;
      routing_ = routing;
    }

    private double max_symbol_rate => max_data_rate / modulator.ModulationBits;

    private double max_bandwidth => max_symbol_rate * encoder.CodingRate;

    private readonly Routing routing_;
  }
  
  public double TxUsage(RealAntenna antenna) {
    tx_power_usage_.TryGetValue(antenna, out double usage);
    return usage;
  }

  public double RxUsage(RealAntenna antenna) {
    rx_spectrum_usage_.TryGetValue(antenna, out double usage);
    return usage;
  }

  private readonly Dictionary<(RACommNode, RACommNode), OrientedLink> links_ =
      new Dictionary<(RACommNode, RACommNode), OrientedLink>();
  private readonly Dictionary<RealAntenna, double> rx_spectrum_usage_ =
      new Dictionary<RealAntenna, double>();
  private readonly Dictionary<RealAntenna, double> tx_power_usage_ =
      new Dictionary<RealAntenna, double>();
  // Stations only capable of transmitting.
  private HashSet<RACommNode> tx_only_ = new HashSet<RACommNode>();
  // Station only capable of receiving.
  private HashSet<RACommNode> rx_only_ = new HashSet<RACommNode>();
  // Station modelled as capable of tracking multiple targets simultaneously,
  // so that each of its antennas really represents multiple independent
  // antennas.  Their transmitted power does not get used up.
  private HashSet<RACommNode> multiple_tracking_tx_ = new HashSet<RACommNode>();
}

}

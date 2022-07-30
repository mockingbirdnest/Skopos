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

      public double round_trip_latency => forward.latency + backward.latency;
    }

  public class NetworkUsage {
    public static NetworkUsage None = new NetworkUsage();
    public virtual double TxPowerUsage(RealAntennaDigital tx) { return 0; }
    public virtual double SpectrumUsage(RealAntennaDigital trx) { return 0; }
    protected NetworkUsage() {}
  }

  public Routing() {
    current_network_usage_ = new RoutingNetworkUsage(this);
  }

  public void Reset(IEnumerable<RACommNode> tx_only,
                    IEnumerable<RACommNode> rx_only,
                    IEnumerable<RACommNode> multiple_tracking_tx) {
    links_.Clear();
    current_network_usage_.Clear();

    tx_only_ = new HashSet<RACommNode>(tx_only);
    rx_only_ = new HashSet<RACommNode>(rx_only);
    multiple_tracking_ = new HashSet<RACommNode>(multiple_tracking_tx);
  }
  public NetworkUsage usage => current_network_usage_;

  public Circuit FindCircuitInIsolation(
      RACommNode source,
      RACommNode destination,
      double round_trip_latency_limit,
      double one_way_data_rate) {
    return FindCircuit(source,
                       destination,
                       round_trip_latency_limit,
                       one_way_data_rate,
                       NetworkUsage.None);
  }

  public Circuit FindAndUseAvailableCircuit(
      RACommNode source,
      RACommNode destination,
      double round_trip_latency_limit,
      double one_way_data_rate) {
    Circuit circuit = FindCircuit(
        source,
        destination,
        round_trip_latency_limit,
        one_way_data_rate,
        current_network_usage_);
    if (circuit != null) {
      foreach (OrientedLink link in circuit.forward.links) {
        current_network_usage_.UseLinks(new[] {link}, one_way_data_rate);
      }
      foreach (OrientedLink link in circuit.backward.links) {
        current_network_usage_.UseLinks(new[] {link}, one_way_data_rate);
      }
    }
    return circuit;
  }

  public PointToMultipointAvailability FindChannelsInIsolation(
      RACommNode source,
      IList<RACommNode> destinations,
      double latency_limit,
      double data_rate,
      out Channel[] channels) {
    return FindChannels(source,
                        destinations,
                        latency_limit,
                        data_rate,
                        NetworkUsage.None,
                        out channels);
  }

  public PointToMultipointAvailability FindAndUseAvailableChannels(
      RACommNode source,
      IList<RACommNode> destinations,
      double latency_limit,
      double data_rate,
      out Channel[] channels) {
    PointToMultipointAvailability availability = FindChannels(
        source,
        destinations,
        latency_limit,
        data_rate,
        current_network_usage_,
        out channels);
    if (availability != Unavailable) {
      var links_by_tx_antenna = from channel in channels where channel != null
                                from link in channel.links
                                group link by link.tx_antenna;
      foreach (var links in links_by_tx_antenna) {
        current_network_usage_.UseLinks(links, data_rate);
      }
    }
    return availability;
  }

  private Circuit FindCircuit(RACommNode source,
                              RACommNode destination,
                              double round_trip_latency_limit,
                              double one_way_data_rate,
                              NetworkUsage usage) {
    if (FindChannels(source,
                     new[]{destination},
                     round_trip_latency_limit,
                     one_way_data_rate,
                     usage,
                     out Channel[] forward) == Unavailable) {
      return null;
    }
    var usage_with_forward_channel = new RoutingNetworkUsage(this, usage);
    foreach (var link in forward[0].links) {
      usage_with_forward_channel.UseLinks(new[]{link}, one_way_data_rate);
    }
    if (FindChannels(destination,
                     new[]{source},
                     round_trip_latency_limit - forward[0].latency,
                     one_way_data_rate,
                     usage_with_forward_channel,
                     out Channel[] backward) == Unavailable) {
      return null;
    }
    return new Circuit(forward[0], backward[0]);
  }

  private PointToMultipointAvailability FindChannels(
      RACommNode source,
      IList<RACommNode> destinations,
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
    channels = new Channel[destinations.Count()];
    bool is_point_to_multipoint = destinations.Count() > 1;

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

        if (tx_only_.Contains(rx) || interior.Contains(rx)) {
          continue;
        }

        var link = OrientedLink.Get(this, from: tx, to: rx);

        if (is_point_to_multipoint &&
            !multiple_tracking_.Contains(tx) &&
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
    public readonly List<PointToMultipointConnection> connections = new List<PointToMultipointConnection>();
  }

  private class RoutingNetworkUsage : NetworkUsage {
    public RoutingNetworkUsage(Routing routing) {
      routing_ = routing;
    }

    public RoutingNetworkUsage(Routing routing, NetworkUsage other)
        : this(routing) {
      if (other is RoutingNetworkUsage nontrival) {
        tx_power_usage_ =
            new Dictionary<RealAntenna, double>(nontrival.tx_power_usage_);
        spectrum_usage_ =
            new Dictionary<RealAntenna, double>(nontrival.spectrum_usage_);
      }
    }

    public void Clear() {
      tx_power_usage_.Clear();
      spectrum_usage_.Clear();
    }

    public override double TxPowerUsage(RealAntennaDigital tx) {
      tx_power_usage_.TryGetValue(tx, out double usage);
      return usage;
    }

    public override double SpectrumUsage(RealAntennaDigital rx) { 
      spectrum_usage_.TryGetValue(rx, out double usage);
      return usage;
    }

    // The links must all share the same tx antenna and tech level.
    // Uses tx power corresponding to broadcast at the given data rate along
    // all of these links (thus at the power needed for the weakest link).
    // Also uses the necessary spectrum on all antennas involved.
    public void UseLinks(IEnumerable<OrientedLink> links, double data_rate) {
      EnsureSameTxAntennaAndTL(links);
      UseTxPower(links, data_rate);
      UseSpectrum(links, data_rate);
    }

    private void UseTxPower(IEnumerable<OrientedLink> links, double data_rate) {
      if (routing_.multiple_tracking_.Contains(links.First().tx)) {
        return;
      }
      RealAntennaDigital tx_antenna = links.First().tx_antenna;
      if (!tx_power_usage_.ContainsKey(tx_antenna)) {
        tx_power_usage_.Add(tx_antenna, 0);
      }
      double usage = (from link in links 
                      select link.TxPowerUsageFromDataRate(data_rate)).Max();
      tx_power_usage_[tx_antenna] += usage;
    }

    private void UseSpectrum(IEnumerable<OrientedLink> links, double data_rate) {
      double usage = links.First().SpectrumUsageFromDataRate(data_rate);
      foreach (OrientedLink link in links) {
        if (routing_.multiple_tracking_.Contains(link.rx)) {
          continue;
        }
        if (!spectrum_usage_.ContainsKey(link.rx_antenna)) {
          spectrum_usage_.Add(link.rx_antenna, 0);
        }
        spectrum_usage_[link.rx_antenna] += usage;
      }
      RealAntennaDigital tx_antenna = links.First().tx_antenna;
      if (routing_.multiple_tracking_.Contains(links.First().tx)) {
        return;
      }
      if (!spectrum_usage_.ContainsKey(tx_antenna)) {
        spectrum_usage_.Add(tx_antenna, 0);
      }
      spectrum_usage_[tx_antenna] += usage;
    }

    private void EnsureSameTxAntennaAndTL(IEnumerable<OrientedLink> links) {
#if DEBUG
      RealAntennaDigital tx_antenna = links.First().tx_antenna;
      var antennas = from link in links select link.tx_antenna;
      if (antennas.Any(tx => tx != tx_antenna)) {
        throw new ArgumentException("Broadcast from multiple antennas");
      }
      int tech_level = links.First().tech_level;
      var tech_levels = from link in links select link.tech_level;
      if (tech_levels.Any(tl => tl != tech_level)) {
        throw new ArgumentException("Broadcast at multiple tech levels");
      }
#endif
    }

    private readonly Dictionary<RealAntenna, double> tx_power_usage_ =
        new Dictionary<RealAntenna, double>();
    private readonly Dictionary<RealAntenna, double> spectrum_usage_ =
        new Dictionary<RealAntenna, double>();
    private Routing routing_;
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
    public RealAntennas.Antenna.BandInfo band => tx_antenna.RFBand;
    // TODO(egg): we only care about encoding and modulation; but while TL 3 and
    // 4 have the same encoder, they differ in modulation (QPSK vs. 8PSK), so it
    // doesn’t matter that much.
    public bool is_at_tx_tech_level =>
        tech_level == tx_antenna.TechLevelInfo.Level;
    public RealAntennaDigital lowest_tech_antenna => 
        is_at_tx_tech_level ? tx_antenna : rx_antenna;
    public RAModulator modulator => lowest_tech_antenna.modulator;
    public RealAntennas.Antenna.Encoder encoder => lowest_tech_antenna.Encoder;
    // TODO(egg): this needs to be adapted once we have support for landlines.
    public double length => (tx.precisePosition - rx.precisePosition).magnitude;

    public double CapacityWithUsage(NetworkUsage usage) {
      double available_spectrum =
          band.ChannelWidth - Math.Max(usage.SpectrumUsage(tx_antenna),
                                       usage.SpectrumUsage(rx_antenna));
      double bandwidth_limited_data_rate =
          Math.Min(max_symbol_rate, available_spectrum) * bits_per_symbol;
      double power_limited_data_rate =
          max_data_rate * (1 - usage.TxPowerUsage(tx_antenna));
      return Math.Min(bandwidth_limited_data_rate, power_limited_data_rate);
    }

    public double TxPowerUsageFromDataRate(double data_rate) {
      return data_rate / max_data_rate;
    }

    public double SpectrumUsageFromDataRate(double data_rate) {
      return data_rate / (encoder.CodingRate * modulator.ModulationBits);
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

    private double max_symbol_rate => max_data_rate / bits_per_symbol;
    private double bits_per_symbol =>
        encoder.CodingRate * modulator.ModulationBits;

    private readonly Routing routing_;
  }

  private readonly RoutingNetworkUsage current_network_usage_;

  private readonly Dictionary<(RACommNode, RACommNode), OrientedLink> links_ =
      new Dictionary<(RACommNode, RACommNode), OrientedLink>();

  // Stations only capable of transmitting.
  private HashSet<RACommNode> tx_only_ = new HashSet<RACommNode>();
  // Station only capable of receiving.
  private HashSet<RACommNode> rx_only_ = new HashSet<RACommNode>();
  // Station modelled as capable of tracking multiple targets simultaneously,
  // so that each of its antennas really represents multiple independent
  // antennas.  Neither their transmitted power nor their spectrum get used up.
  private HashSet<RACommNode> multiple_tracking_ = new HashSet<RACommNode>();
}

}
